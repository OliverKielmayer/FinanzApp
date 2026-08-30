using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Liest ein PDF ein, schlägt Typ, Zielobjekt und Ablage vor und übernimmt die Werte —
/// Abschnitt 14 des v5-Handoffs.
/// </summary>
/// <remarks>
/// <para>Der Dienst kennt keinen einzigen Feldnamen. Was ein Statusreport ist und was daraus ins
/// Vermögen zählt, steht in <see cref="DocumentKindLibrary"/>; hier steht nur der Weg: lesen,
/// Typ bestimmen, Ziel finden, ablegen, vorschlagen — und erst auf ausdrückliche Bestätigung
/// speichern.</para>
/// <para>Die Trennung ist der Punkt der ganzen Übung. <b>Nichts Unbestätigtes verändert eine
/// Vermögenszahl.</b> Zwischen Analyse und Übernahme liegt ein Mensch, der die Werte mit ihrer
/// Herkunftsseite gesehen hat.</para>
/// </remarks>
public sealed class DocumentScanService(
    FinanzAppDbContext db,
    DocumentService documents,
    DocumentPathService paths,
    DepotStatementService statements,
    IPdfTextReader reader,
    IClock clock)
{
    private readonly DocumentFieldExtractor extractor = new();

    // ── Analyse ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liest die Datei, legt sie ab und gibt den Vorschlag zurück.
    /// </summary>
    /// <remarks>
    /// Die Ablage passiert sofort und nicht erst bei der Übernahme: eine eingescannte Seite, die
    /// erst nach einer Bestätigung entsteht, ist verloren, sobald jemand abbricht. Die Werte
    /// dagegen warten.
    /// </remarks>
    public async Task<ScanAnalysisDto> AnalyseAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        var inhalt = reader.Read(buffer);
        var art = DocumentKindLibrary.Detect(inhalt);

        if (art is null)
        {
            return await UnknownAsync(buffer, fileName, inhalt, ct);
        }

        var gelesen = extractor.Read(art, inhalt);
        var werte = gelesen.Values;
        var stichtag = Date(werte, art.AsOfField);
        var schreiben = Date(werte, art.DocumentDateField);
        var ziel = await TargetAsync(art, Text(werte, art.TargetNumberField), ct);

        buffer.Position = 0;
        var ablage = await documents.UploadAsync(
            buffer, fileName, art.Area,
            title: art.Label,
            documentTypeId: null,
            documentDate: schreiben,
            subFolder: Folder(art, ziel, stichtag),
            preferredName: FileName(art, stichtag),
            ct: ct);

        var dokument = await db.Documents.FirstAsync(d => d.Id == ablage.DocumentId, ct);
        dokument.ScanKind = art.Key;

        foreach (var wert in werte)
        {
            db.DocumentExtractions.Add(new DocumentExtraction
            {
                DocumentId = ablage.DocumentId,
                FieldKey = wert.Rule.Key,
                Label = wert.Rule.Label,
                Value = wert.Raw,
                SourcePage = wert.Page,
                Confidence = wert.Confidence,
                Confirmed = false,
                CreatedAt = clock.Now,
            });
        }

        await db.SaveChangesAsync(ct);

        var zeilen = Rows(art, werte);

        return new ScanAnalysisDto
        {
            DocumentId = ablage.DocumentId,
            FileName = fileName,
            RelativePath = ablage.RelativePath,
            PageCount = inhalt.PageCount,

            KindKey = art.Key,
            KindLabel = art.Label,
            TextNote = inhalt.Note,
            HasTextLayer = inhalt.HasTextLayer,

            TargetName = ziel?.Name,
            TargetSub = ziel?.Sub,
            TargetId = ziel?.Id,
            TargetNoun = art.TargetNoun,
            TargetLink = art.TargetLink,
            TargetHref = ziel?.Href,

            DocumentDate = schreiben,
            AsOf = stichtag,

            Steps = Steps(art, inhalt, werte, ziel, zeilen.Count, gelesen.Proofs),
            Fields = zeilen,
            Repeat = Repeat(art, gelesen, werte),
            Proofs =
            [
                .. gelesen.Proofs.Select(p => new ScanProofDto
                {
                    Line = p.Line, Why = p.Why, Passed = p.Passed,
                }),
            ],
            Blocker = Blocker(art, ziel, stichtag, werte),
        };
    }

    /// <summary>
    /// Ein Dokument, dessen Typ nicht erkannt wurde.
    /// </summary>
    /// <remarks>
    /// Es wird trotzdem abgelegt — im Bereich „Sonstiges“, ohne Vorschlag. Eine Datei, die die
    /// Analyse nicht versteht, ist kein Fehler des Nutzers und darf nicht verloren gehen.
    /// </remarks>
    private async Task<ScanAnalysisDto> UnknownAsync(
        MemoryStream buffer, string fileName, PdfContent inhalt, CancellationToken ct)
    {
        buffer.Position = 0;
        var ablage = await documents.UploadAsync(
            buffer, fileName, DocumentArea.Other,
            title: null, documentTypeId: null, documentDate: null, ct: ct);

        return new ScanAnalysisDto
        {
            DocumentId = ablage.DocumentId,
            FileName = fileName,
            RelativePath = ablage.RelativePath,
            PageCount = inhalt.PageCount,
            TextNote = inhalt.Note,
            HasTextLayer = inhalt.HasTextLayer,
            Steps = ["Text gelesen (" + inhalt.PageCount + " Seiten)", "Typ nicht erkannt"],
            Fields = [],
            Proofs = [],
            Note = inhalt.HasTextLayer
                ? "Die Art des Dokuments ist nicht erkennbar. Die Datei ist abgelegt; die Werte "
                  + "bitte am Zielobjekt von Hand eintragen."
                : "Aus dieser Datei ist kein Text zu holen — vermutlich ein reiner Scan. Die Datei "
                  + "ist abgelegt; die Werte bitte von Hand eintragen.",
        };
    }

    // ── Übernahme ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Übernimmt die bestätigten Werte ins Zielobjekt.
    /// </summary>
    /// <remarks>
    /// Grundlage sind die bei der Analyse gespeicherten Werte, nicht ein zweites Auslesen: was
    /// der Mensch gesehen hat, ist das, was gilt. Korrekturen aus der Maske überschreiben sie
    /// einzeln.
    /// </remarks>
    public async Task<ScanResultDto> ConfirmAsync(
        ConfirmScanRequest request, CancellationToken ct = default)
    {
        var dokument = await db.Documents.FirstOrDefaultAsync(d => d.Id == request.DocumentId, ct)
                       ?? throw new RuleViolationException("Dieses Dokument gibt es nicht.");

        var art = DocumentKindLibrary.All.FirstOrDefault(a => a.Key == dokument.ScanKind)
                  ?? throw new RuleViolationException(
                      "Für dieses Dokument ist kein Typ erkannt worden — es lässt sich nicht übernehmen.");

        var rohwerte = await db.DocumentExtractions
            .Where(x => x.DocumentId == dokument.Id)
            .ToListAsync(ct);

        var werte = Values(art, rohwerte, request.Values);

        // Die Zeilen einer Wiederholgruppe stehen nicht in der Werteliste — sie sind viele
        // gleichnamige Felder. Für sie wird die abgelegte Datei noch einmal gelesen; sie liegt
        // seit der Analyse und ändert sich nicht.
        var gruppen = art.Repeat is null ? [] : await RowsAsync(art, dokument, ct);

        var ergebnis = art.Target switch
        {
            DocumentTargetKind.Policy => await ApplyPolicyAsync(art, dokument, werte, ct),
            DocumentTargetKind.Depot => await ApplyDepotAsync(art, dokument, werte, gruppen, ct),
            _ => throw new RuleViolationException("Für diesen Typ gibt es kein Ziel."),
        };

        foreach (var zeile in rohwerte)
        {
            zeile.Confirmed = true;
        }

        await db.SaveChangesAsync(ct);
        return ergebnis;
    }

    /// <summary>
    /// Trägt den erreichten Wert samt Stichtag in den Vertrag ein.
    /// </summary>
    /// <remarks>
    /// Wert <em>und</em> Stichtag, nie nur der Wert: ein Jahresstand ohne Datum sähe im Vermögen
    /// aus wie ein Tageskurs. Gemeldet wird die Veränderung gegenüber dem bisherigen Stand — die
    /// Zahl allein sagt niemandem, ob der Vertrag gewachsen ist.
    /// </remarks>
    private async Task<ScanResultDto> ApplyPolicyAsync(
        DocumentKind art, Document dokument, Dictionary<string, ScanValue> werte, CancellationToken ct)
    {
        var nummer = werte.GetValueOrDefault(art.TargetNumberField)?.Text;
        var vertrag = await FindPolicyAsync(nummer, ct)
                      ?? throw new RuleViolationException(
                          nummer is { Length: > 0 }
                              ? $"Zur {Label(art, art.TargetNumberField)} {nummer} gibt es keinen Vertrag."
                              : $"Das Dokument nennt keine {Label(art, art.TargetNumberField)}.");

        var leitfeld = art.Fields.First(f => f.Lead);
        var wert = werte.GetValueOrDefault(leitfeld.Key)?.Number
                   ?? throw new RuleViolationException(
                       $"Ohne {leitfeld.Label} lässt sich nichts übernehmen.");

        var stichtag = werte.GetValueOrDefault(art.AsOfField)?.Date
                       ?? throw new RuleViolationException(
                           "Ohne Stichtag wird der Wert nicht übernommen — ein Jahresstand ohne "
                           + "Datum sieht aus wie ein Tageskurs.");

        var vorher = vertrag.CurrentValue;
        var vorherStichtag = vertrag.ValuationDate;

        vertrag.CurrentValue = wert;
        vertrag.ValuationDate = stichtag;

        // Die Bestandteile mit übernehmen: sie tragen den Block „So entsteht der Wert" am
        // Vertrag (Abschnitt 19.5), und der Bericht nennt sie ohnehin einzeln.
        vertrag.BaseValue = werte.GetValueOrDefault("rueckkauf")?.Number ?? vertrag.BaseValue;
        vertrag.AccruedBonus = werte.GetValueOrDefault("ansammlung")?.Number ?? vertrag.AccruedBonus;

        // Und den Stand in die Berichtsreihe — Abschnitt 19.6. Ein neuer Bericht setzt den Wert
        // **seines** Stichtags; ein zweiter zum selben Tag aktualisiert, statt zu verdoppeln.
        await PolicyService.RecordReportAsync(db, clock, vertrag.Id, stichtag, wert, art.Label, ct);

        // Das Ablaufdatum steht im Bericht; im Vertrag fehlt es oft. Ergänzt, nie überschrieben:
        // was der Nutzer gepflegt hat, weiß er besser als ein Leseergebnis.
        if (werte.GetValueOrDefault("ablauf")?.Date is { } ablauf)
        {
            vertrag.MaturesOn ??= ablauf;
        }

        await documents.LinkAsync(dokument.Id, LinkTargetType.Policy, vertrag.Id, ct);

        return new ScanResultDto
        {
            Saved = true,
            Title = art.Label + " abgelegt",
            Subtitle = $"bei {vertrag.Provider} · {GermanFormat.Date(stichtag)}",
            LeadLabel = leitfeld.Label,
            LeadNumber = wert,
            LeadIsMoney = true,
            Effect = Effect(wert, vorher, vorherStichtag),
            Rule = $"Absender „{vertrag.Provider}“ + „{art.Label.Split(' ')[0]}“ → künftig automatisch hierher",
            TargetLink = art.TargetLink,
            TargetHref = $"/police/{vertrag.Id}",
        };
    }

    /// <summary>
    /// Die Wiederholgruppe für den Prüfschritt.
    /// </summary>
    /// <remarks>
    /// Die Summe kommt aus den Zeilen und nicht aus dem Kopf: sie ist die Aussage, die geprüft
    /// wird. Ob sie zum ausgewiesenen Gesamtwert passt, steht daneben.
    /// </remarks>
    private static ScanRepeatDto? Repeat(
        DocumentKind art, ExtractionResult gelesen, IReadOnlyList<ReadValue> werte)
    {
        if (art.Repeat is not { } gruppe || gelesen.Rows.Count == 0)
        {
            return null;
        }

        var zeilen = gelesen.Rows
            .Select(z => new ScanRowDto
            {
                Name = z[gruppe.NameField]?.Raw ?? z["isin"]?.Raw ?? "Position",
                Meta = RowMeta(z),
                Value = z[gruppe.ValueField]?.Number,
            })
            .ToList();

        var summe = decimal.Round(zeilen.Sum(z => z.Value ?? 0m), 2);

        var ausgewiesen = gruppe.TotalField is { } schluessel
            ? werte.FirstOrDefault(w => w.Rule.Key == schluessel)?.Number
            : null;

        var passt = ausgewiesen is not { } gesamt || Math.Abs(gesamt - summe) <= 0.01m;

        return new ScanRepeatDto
        {
            Title = gruppe.Title,
            Rows = zeilen,
            Total = summe,
            Matches = passt,
            Note = ausgewiesen is null
                ? "Das Dokument weist keinen Gesamtwert aus — geprüft ist jede Zeile für sich."
                : passt
                    ? "Die Summe entspricht dem ausgewiesenen Gesamtwert."
                    : "Die Summe weicht vom ausgewiesenen Gesamtwert ab — bitte prüfen.",
        };
    }

    /// <summary>Eine Zeile in Worten.</summary>
    private static string RowMeta(ReadRow zeile)
    {
        var teile = new List<string>();

        if (zeile["nominale"]?.Number is { } menge)
        {
            teile.Add($"{GermanFormat.Quantity(menge)} Stück");
        }

        if (zeile["kurs"]?.Number is { } kurs)
        {
            teile.Add("zu " + GermanFormat.Price(kurs));
        }

        if (zeile["isin"]?.Raw is { Length: > 0 } isin)
        {
            teile.Add(isin);
        }

        return string.Join(" · ", teile);
    }

    /// <summary>
    /// Legt die Aufstellung als Bestandsnachweis zum Stichtag an.
    /// </summary>
    /// <remarks>
    /// Sie <em>belegt</em> den Depotwert und ersetzt ihn nicht: gerechnet wird weiter aus den
    /// importierten Ausführungen. Was hier entsteht, ist die Gegenseite des Bestandsabgleichs
    /// aus Abschnitt 11.3.
    /// </remarks>
    /// <summary>Liest die Zeilen der Wiederholgruppe erneut aus der abgelegten Datei.</summary>
    /// <remarks>
    /// Kein zweiter Vorschlag: nur die Zeilen. Sie unverändert wiederzufinden ist der Sinn der
    /// sofortigen Ablage — die Datei ist seit der Analyse dieselbe.
    /// </remarks>
    private async Task<IReadOnlyList<ReadRow>> RowsAsync(
        DocumentKind art, Document dokument, CancellationToken ct)
    {
        var absolut = paths.Resolve(dokument.RelativePath);

        if (absolut is null || !File.Exists(absolut))
        {
            return [];
        }

        await using var datei = File.OpenRead(absolut);
        return extractor.Read(art, reader.Read(datei)).Rows;
    }

    /// <summary>
    /// Die Positionen einer Aufstellung.
    /// </summary>
    /// <remarks>
    /// Aus der Wiederholgruppe, wenn der Typ eine hat. Zeilen ohne Nominale, Kurs oder ISIN
    /// fallen heraus statt mit Null anzukommen — eine Position ohne Stückzahl ist keine.
    /// </remarks>
    private static List<CreateDepotStatementPosition> Positions(
        DocumentKind art,
        Dictionary<string, ScanValue> werte,
        IReadOnlyList<ReadRow> zeilen)
    {
        if (art.Repeat is not null)
        {
            return
            [
                .. zeilen
                    .Select(z => Position(
                        z["isin"]?.Raw,
                        z["papier"]?.Raw,
                        z["wkn"]?.Raw,
                        z["nominale"]?.Number,
                        z["kurs"]?.Number,
                        z["kurswert"]?.Number,
                        z["verwahrart"]?.Raw,
                        z["lagerland"]?.Raw,
                        z["lagerstelle"]?.Raw))
                    .OfType<CreateDepotStatementPosition>(),
            ];
        }

        var einzeln = Position(
            werte.GetValueOrDefault("isin")?.Text,
            werte.GetValueOrDefault("papier")?.Text,
            werte.GetValueOrDefault("wkn")?.Text,
            werte.GetValueOrDefault("nominale")?.Number,
            werte.GetValueOrDefault("kurs")?.Number,
            werte.GetValueOrDefault("kurswert")?.Number,
            werte.GetValueOrDefault("verwahrart")?.Text,
            werte.GetValueOrDefault("lagerland")?.Text,
            werte.GetValueOrDefault("lagerstelle")?.Text);

        return einzeln is null ? [] : [einzeln];
    }

    private static CreateDepotStatementPosition? Position(
        string? isin, string? name, string? wkn,
        decimal? menge, decimal? kurs, decimal? wert,
        string? verwahrart, string? land, string? stelle)
        => isin is { Length: > 0 } kennung && menge is { } stueck && kurs is { } preis
            ? new CreateDepotStatementPosition
            {
                SecurityName = name is { Length: > 0 } ? name : kennung,
                Isin = kennung,
                Wkn = wkn,
                Quantity = stueck,
                Price = preis,
                Value = wert,
                SafeCustody = verwahrart,
                Country = land,
                Depository = stelle,
            }
            : null;

    private async Task<ScanResultDto> ApplyDepotAsync(
        DocumentKind art,
        Document dokument,
        Dictionary<string, ScanValue> werte,
        IReadOnlyList<ReadRow> gruppen,
        CancellationToken ct)
    {
        var nummer = werte.GetValueOrDefault(art.TargetNumberField)?.Text;
        var depot = await FindDepotAsync(nummer, ct)
                    ?? throw new RuleViolationException(
                        nummer is { Length: > 0 }
                            ? $"Zur {Label(art, art.TargetNumberField)} {nummer} gibt es kein Depot."
                            : $"Das Dokument nennt keine {Label(art, art.TargetNumberField)}.");

        var stichtag = werte.GetValueOrDefault(art.AsOfField)?.Date
                       ?? throw new RuleViolationException("Ohne Stichtag belegt die Aufstellung nichts.");

        // Eine Aufstellung mit N Positionen, nicht N Aufstellungen — Abschnitt 17.2. Die
        // Zuordnung zum Depot geht über die Depotnummer, die der Zeilen über die ISIN.
        var positionen = Positions(art, werte, gruppen);

        if (positionen.Count == 0)
        {
            throw new RuleViolationException(
                "Nominale, Kurs und ISIN müssen stehen — sonst ist es kein Bestandsnachweis.");
        }

        var aufstellung = await statements.CreateAsync(depot.Id, new CreateDepotStatementRequest
        {
            AsOf = stichtag,
            IssuedOn = werte.GetValueOrDefault(art.DocumentDateField)?.Date,
            DepotNumber = nummer,
            Reference = werte.GetValueOrDefault("referenz")?.Text,
            Custodian = werte.GetValueOrDefault("absender")?.Text,
            DocumentId = dokument.Id,
            Positions = positionen,
        }, ct);

        await documents.LinkAsync(dokument.Id, LinkTargetType.Portfolio, depot.Id, ct);

        var wert = decimal.Round(positionen.Sum(p => p.Value ?? p.Quantity * p.Price), 2);

        return new ScanResultDto
        {
            Saved = true,
            Title = art.Label + " abgelegt",
            Subtitle = $"bei {depot.Name} · {GermanFormat.Date(stichtag)}",
            LeadLabel = "Depotwert zum Stichtag",
            LeadNumber = wert,
            LeadIsMoney = true,
            Effect = positionen.Count == 1
                ?
                [
                    new() { Quantity = positionen[0].Quantity },
                    new() { Text = "Stück zu" },
                    new() { Price = positionen[0].Price },
                    new() { Text = "· Kurswert" },
                    new() { Money = wert },
                    new() { Text = "zum " + GermanFormat.Date(aufstellung.AsOf) },
                ]
                :
                [
                    new() { Text = $"{positionen.Count} Positionen · Depotwert" },
                    new() { Money = wert },
                    new() { Text = "zum " + GermanFormat.Date(aufstellung.AsOf) },
                ],
            Rule = $"Absender „{depot.Broker ?? depot.Name}“ + „Quartalsaufstellung“ → künftig automatisch hierher",
            TargetLink = art.TargetLink,
            TargetHref = "/depot",
        };
    }

    // ── Zielobjekt ─────────────────────────────────────────────────────────────────────────

    private sealed record Target(int Id, string Name, string? Sub, string Href);

    /// <summary>
    /// Sucht das Objekt, zu dem das Dokument gehört — über seine Nummer.
    /// </summary>
    /// <remarks>
    /// Die Nummer und nicht der Name: Absenderbezeichnungen wechseln („finanzen.net zero GmbH“
    /// steht auf dem Papier, „finanzen.net ZERO“ im Depot), eine Vertragsnummer nicht.
    /// </remarks>
    private async Task<Target?> TargetAsync(DocumentKind art, string? nummer, CancellationToken ct)
    {
        if (art.Target == DocumentTargetKind.Policy)
        {
            var vertrag = await FindPolicyAsync(nummer, ct);
            return vertrag is null
                ? null
                : new Target(vertrag.Id, vertrag.Provider,
                    Join(vertrag.Name, vertrag.PolicyNumber is { Length: > 0 } n ? "Nr. " + n : null),
                    $"/police/{vertrag.Id}");
        }

        var depot = await FindDepotAsync(nummer, ct);
        return depot is null
            ? null
            : new Target(depot.Id, depot.Name,
                Join(depot.Number is { Length: > 0 } d ? "Depot " + d : null, depot.Broker),
                "/depot");
    }

    private async Task<Policy?> FindPolicyAsync(string? nummer, CancellationToken ct)
        => nummer is not { Length: > 0 }
            ? null
            : await db.Policies.FirstOrDefaultAsync(p => p.PolicyNumber == nummer, ct);

    private async Task<Depot?> FindDepotAsync(string? nummer, CancellationToken ct)
        => nummer is not { Length: > 0 }
            ? null
            : await db.Depots.FirstOrDefaultAsync(d => d.Number == nummer, ct);

    // ── Zusammenbauen ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Ablageschublade: Bereich, Objekt, Jahr.
    /// </summary>
    /// <remarks>
    /// Kein Zielobjekt gefunden? Dann steht dort der Absender, und wenn auch der fehlt,
    /// „Unbekannt“. Ein Dokument in einem Ordner namens „Unbekannt“ ist auffindbar; eins ohne
    /// Ordner ist es nicht.
    /// </remarks>
    private static string Folder(DocumentKind art, Target? ziel, DateOnly? stichtag)
        => art.FolderTemplate
            .Replace("{ziel}", ziel?.Name ?? "Unbekannt", StringComparison.Ordinal)
            .Replace("{jahr}", (stichtag?.Year)?.ToString() ?? "Ohne Jahr", StringComparison.Ordinal);

    private static string FileName(DocumentKind art, DateOnly? stichtag)
        => art.FileTemplate.Replace(
            "{stichtag}",
            stichtag?.ToString("yyyy-MM-dd") ?? "ohne-Stichtag",
            StringComparison.Ordinal);

    /// <summary>Die Analyseschritte mit dem, was tatsächlich gefunden wurde.</summary>
    private static List<string> Steps(
        DocumentKind art,
        PdfContent inhalt,
        IReadOnlyList<ReadValue> werte,
        Target? ziel,
        int gezeigt,
        IReadOnlyList<ProofResult> proben)
    {
        var absender = werte.FirstOrDefault(w => w.Rule.Key == "absender")?.Raw;

        // Die Kette endet mit der Probe: sie ist der letzte Schritt, der über die Werte
        // entscheidet, und ohne sie sähe die Analyse fertiger aus, als sie ist.
        var abschluss = proben.Count == 0
            ? []
            : proben.All(p => p.Passed)
                ? new[] { "Rechenprobe bestanden" }
                : ["Rechenprobe nicht aufgegangen — Werte prüfen"];

        return
        [
            .. art.Steps.Select(s => s
                .Replace("{seiten}", inhalt.PageCount.ToString(), StringComparison.Ordinal)
                .Replace("{absender}", absender ?? "nicht erkannt", StringComparison.Ordinal)
                .Replace("{werte}", gezeigt.ToString(), StringComparison.Ordinal)
                .Replace("{ziel}",
                    ziel is null
                        ? $"kein {art.TargetNoun} zugeordnet"
                        : $"{art.TargetNoun} {ziel.Name} zugeordnet",
                    StringComparison.Ordinal)),
            .. abschluss,
        ];
    }

    /// <summary>
    /// Die Werteliste, wie sie der Mensch sieht.
    /// </summary>
    /// <remarks>
    /// Kopfdaten — Stichtag, Absender, Vertragsnummer — stehen nicht darin: sie tragen den
    /// Vorschlag darüber und stünden hier ein zweites Mal. Gepaarte Felder werden zu einer Zeile
    /// zusammengezogen.
    /// </remarks>
    private static List<ScanFieldDto> Rows(DocumentKind art, IReadOnlyList<ReadValue> werte)
    {
        var kopf = Head(art);
        var paare = art.Fields.Where(f => f.PairedWith is not null)
            .Select(f => f.PairedWith!)
            .ToHashSet(StringComparer.Ordinal);

        var zeilen = new List<ScanFieldDto>();

        foreach (var wert in werte)
        {
            if (kopf.Contains(wert.Rule.Key) || paare.Contains(wert.Rule.Key))
            {
                continue;
            }

            var anzeige = wert.Raw;
            if (wert.Rule.PairedWith is { } zweit
                && werte.FirstOrDefault(w => w.Rule.Key == zweit) is { } dazu)
            {
                anzeige += " · " + dazu.Raw;
            }

            var geld = wert.Rule.Kind == DocumentValueKind.Money;

            zeilen.Add(new ScanFieldDto
            {
                Key = wert.Rule.Key,
                Label = wert.Rule.Label,

                // Beträge gehen als Zahl hinaus, alles andere als Text: nur so greift die Maske.
                Display = geld ? string.Empty : anzeige,
                Number = wert.Number,
                IsMoney = geld,
                SourcePage = wert.Page,
                Confidence = wert.Confidence,
                Lead = wert.Rule.Lead,
                Soft = wert.Rule.Soft,
                Warning = wert.Warning,
            });
        }

        return zeilen;
    }

    /// <summary>Felder, die den Vorschlag tragen und deshalb nicht in der Liste stehen.</summary>
    private static HashSet<string> Head(DocumentKind art)
        => [art.AsOfField, art.DocumentDateField, art.TargetNumberField, "absender"];

    /// <summary>
    /// Was die Übernahme verhindert.
    /// </summary>
    /// <remarks>
    /// Vorher gesagt statt hinterher: ein Vorschlag mit „Übernehmen“-Knopf, der beim Drücken
    /// scheitert, hat den Nutzer zweimal Zeit gekostet.
    /// </remarks>
    private static string? Blocker(
        DocumentKind art, Target? ziel, DateOnly? stichtag, IReadOnlyList<ReadValue> werte)
    {
        if (ziel is null)
        {
            // Die gesuchte Nummer gehört in die Meldung: mit ihr weiß der Nutzer, was er am
            // Objekt nachtragen muss. Ohne sie bleibt nur „geht nicht“.
            var nummer = Text(werte, art.TargetNumberField);
            var gesucht = nummer is { Length: > 0 }
                ? $" Gesucht wurde nach {Label(art, art.TargetNumberField)} {nummer}."
                : $" Das Dokument nennt auch keine {Label(art, art.TargetNumberField)}.";

            return $"Kein {art.TargetNoun} gefunden, zu dem das Dokument passt.{gesucht} Die Datei "
                   + "ist abgelegt; übernehmen lässt sich erst, wenn das Ziel dasteht.";
        }

        if (stichtag is null)
        {
            return "Ohne Stichtag wird nichts übernommen — ein Stand ohne Datum ist keiner.";
        }

        var leit = art.Fields.Where(f => f.Lead).Select(f => f.Key).ToList();
        var fehlt = leit.Where(k => werte.All(w => w.Rule.Key != k)).ToList();

        return fehlt.Count == 0
            ? null
            : "Es fehlt: " + string.Join(", ",
                fehlt.Select(k => art.Fields.First(f => f.Key == k).Label));
    }

    /// <summary>
    /// Die Wirkung: was übernommen wurde und was sich dadurch ändert.
    /// </summary>
    /// <remarks>
    /// Die Veränderung gehört dazu, weil der Betrag allein nichts sagt. Ob ein Vertrag mit
    /// 20.481,52 € gut dasteht, weiß nur, wer den Vorjahresstand daneben sieht.
    /// </remarks>
    private static List<ScanEffectPart> Effect(decimal wert, decimal? vorher, DateOnly? vorherStichtag)
    {
        List<ScanEffectPart> satz = [new() { Money = wert }, new() { Text = "übernommen ·" }];

        if (vorher is not { } alt)
        {
            satz.Add(new() { Text = "erster erfasster Stand" });
            return satz;
        }

        var vergleich = vorherStichtag is { } tag
            ? "gegenüber dem Stand vom " + GermanFormat.Date(tag)
            : "gegenüber dem bisherigen Stand";

        var differenz = wert - alt;
        if (differenz == 0m)
        {
            satz.Add(new() { Text = "unverändert " + vergleich });
            return satz;
        }

        satz.Add(new() { Money = differenz, Signed = true });
        satz.Add(new() { Text = vergleich });
        return satz;
    }

    // ── Gespeicherte Werte zurücklesen ─────────────────────────────────────────────────────

    private sealed record ScanValue(string Text, decimal? Number, DateOnly? Date);

    /// <summary>
    /// Bringt gespeicherte Werte und Korrekturen aus der Maske zusammen.
    /// </summary>
    /// <remarks>
    /// Beide werden mit derselben Regel gelesen wie beim ersten Mal. Eine von Hand eingetippte
    /// „18.373,87 EUR“ muss dieselbe Zahl ergeben wie die gelesene — sonst hinge der
    /// Vermögenswert daran, wer ihn eingetragen hat.
    /// </remarks>
    private static Dictionary<string, ScanValue> Values(
        DocumentKind art,
        List<DocumentExtraction> gespeichert,
        IReadOnlyDictionary<string, string>? korrekturen)
    {
        var werte = new Dictionary<string, ScanValue>(StringComparer.Ordinal);

        foreach (var regel in art.Fields)
        {
            var roh = korrekturen?.GetValueOrDefault(regel.Key)
                      ?? gespeichert.FirstOrDefault(g => g.FieldKey == regel.Key)?.Value;

            if (roh is not { Length: > 0 })
            {
                continue;
            }

            var gelesen = DocumentFieldExtractor.Read(regel, roh);
            werte[regel.Key] = new ScanValue(gelesen?.Raw ?? roh.Trim(), gelesen?.Number, gelesen?.Date);
        }

        return werte;
    }

    /// <summary>Wie der Typ ein Feld nennt.</summary>
    private static string Label(DocumentKind art, string key)
        => art.Fields.FirstOrDefault(f => f.Key == key)?.Label ?? key;

    private static string? Join(string? a, string? b)
    {
        var teile = new[] { a, b }.Where(t => t is { Length: > 0 });
        var text = string.Join(" · ", teile);
        return text.Length == 0 ? null : text;
    }

    private static DateOnly? Date(IReadOnlyList<ReadValue> werte, string key)
        => werte.FirstOrDefault(w => w.Rule.Key == key)?.Date;

    private static string? Text(IReadOnlyList<ReadValue> werte, string key)
        => werte.FirstOrDefault(w => w.Rule.Key == key)?.Raw;
}
