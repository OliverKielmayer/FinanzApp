using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Vertragsstände aus einer Vermögensübersicht — Vorschau und Übernahme.
/// </summary>
/// <remarks>
/// <para>Der Anlass sind zwei Dateien, die dasselbe enthalten: die Monatsübersicht des Maklers
/// und eine über Jahre von Hand geführte Tabellenkalkulation. Die zweite ist in das Format der
/// ersten umgewandelt worden, damit es <b>eine</b> Strecke gibt statt einer für das Einmalige
/// und einer für das Wiederkehrende. Eine Datei mit 264 Ständen unterscheidet sich hier von
/// einer mit acht nur in der Anzahl.</para>
/// <para><b>Nichts hier verändert eine Vermögenszahl ohne ausdrückliche Bestätigung.</b> Die
/// Datei kommt monatlich unbeaufsichtigt über den überwachten Ordner herein; sie wird abgelegt
/// und wartet dann im Scaneingang. Das ist dieselbe Schwelle wie beim Statusreport und aus
/// demselben Grund — ein Dienst, dem niemand zusieht, kann keinen Menschen ersetzen, der die
/// gelesenen Werte neben ihrer Herkunft gesehen hat.</para>
/// <para>Gelesen wird bei jedem Aufruf die <em>abgelegte Datei</em>. Sie liegt seit der
/// Einlieferung und ändert sich nicht; ein zwischengespeicherter Auszug liefe ihr irgendwann
/// hinterher, und dann bestätigte der Benutzer eine Zahl und übernähme eine andere.</para>
/// </remarks>
public sealed class PolicyValueImportService(
    FinanzAppDbContext db,
    DocumentPathService paths,
    AssetSheetParser parser,
    IClock clock)
{
    /// <summary>Woher ein so entstandener Stand kommt. Steht später am Bericht.</summary>
    public const string Source = "Vermögensübersicht";

    // ── Vorschau ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liest die abgelegte Datei und stellt daneben, was im Bestand steht.
    /// </summary>
    public async Task<PolicyValueImportDto> PreviewAsync(int documentId, CancellationToken ct = default)
    {
        var (dokument, zeilen) = await ReadAsync(documentId, ct);

        var vertraege = await db.Policies.AsNoTracking()
            .Where(p => p.PolicyNumber != null && p.PolicyNumber != "")
            .ToListAsync(ct);

        var bekannt = vertraege
            .GroupBy(p => Key(p.PolicyNumber!))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Die vorhandenen Stände der betroffenen Verträge, damit jede Zeile sagen kann, ob sie
        // etwas ändert. In einem Zug geladen: bei 264 Zeilen wäre eine Abfrage je Stichtag eine
        // Abfrage zu viel.
        var vertragIds = bekannt.Values.Select(p => p.Id).ToList();

        var stände = await db.PolicyReports.AsNoTracking()
            .Where(r => vertragIds.Contains(r.PolicyId))
            .ToListAsync(ct);

        var vorhanden = stände.ToDictionary(r => (r.PolicyId, r.AsOf), r => r.Value);

        var gruppen = new List<PolicyValueContractDto>();
        var übersprungen = new List<PolicyValueSkippedDto>();

        foreach (var zeile in zeilen)
        {
            if (zeile.Problem is { Length: > 0 } grund)
            {
                übersprungen.Add(Skipped(zeile, grund));
            }
        }

        foreach (var gruppe in Groups(zeilen))
        {
            var erste = gruppe.First();
            bekannt.TryGetValue(Key(erste.ContractNumber), out var vertrag);

            // Was übernommen wird, entscheidet nicht das Dateiformat allein: eine Zeile, deren
            // Nummer im Bestand steht, gehört dorthin — auch wenn die Datei sie unter
            // „Sonstiges“ führt. Nur wo beides fehlt, ist wirklich nichts zu tun.
            if (vertrag is null && !IsPolicyRow(erste))
            {
                übersprungen.AddRange(gruppe.Select(z => Skipped(z, Unassignable(z))));
                continue;
            }

            if (vertrag is not null && !vertrag.IsCapitalForming)
            {
                übersprungen.AddRange(gruppe.Select(z => Skipped(
                    z, $"„{vertrag.Name}“ ist eine Absicherung und führt keinen Wert")));
                continue;
            }

            gruppen.Add(Contract(gruppe, vertrag, vorhanden));
        }

        var stichtage = gruppen.SelectMany(g => g.Stands).Select(s => s.AsOf).ToList();

        return new PolicyValueImportDto
        {
            DocumentId = dokument.Id,
            FileName = dokument.FileName,
            Source = dokument.Description,
            From = stichtage.Count == 0 ? null : stichtage.Min(),
            To = stichtage.Count == 0 ? null : stichtage.Max(),
            LineCount = zeilen.Count,
            NewCount = gruppen.Sum(g => g.NewCount),
            ChangedCount = gruppen.Sum(g => g.ChangedCount),
            UnchangedCount = gruppen.Sum(g => g.UnchangedCount),
            Contracts = [.. gruppen.OrderByDescending(g => g.LatestValue).ThenBy(g => g.ContractNumber)],
            Skipped = [.. übersprungen.OrderBy(s => s.Line)],
            Blocker = gruppen.Count == 0
                ? "In dieser Datei steht kein Vertragsstand, der sich übernehmen ließe."
                : null,
        };
    }

    /// <summary>
    /// Die verwertbaren Zeilen, je Vertrag gebündelt und je Stichtag eindeutig.
    /// </summary>
    /// <remarks>
    /// <para>Ein Stichtag, ein Stand: steht derselbe Vertrag zweimal zum selben Tag in der Datei
    /// — eine berichtigte Zeile unter der ursprünglichen —, gilt die spätere. Ohne diesen Schritt
    /// liefe die Übernahme in den eindeutigen Schlüssel über Vertrag und Stichtag, und zwar erst
    /// beim Speichern, mit einer Meldung, die niemandem sagt, welche Zeile gemeint ist.</para>
    /// <para>Dieselbe Reihenfolge für Vorschau und Übernahme, damit beide dieselbe Zeile
    /// gewinnen lassen.</para>
    /// </remarks>
    private static List<List<AssetSheetRow>> Groups(IReadOnlyList<AssetSheetRow> zeilen)
        => [.. zeilen
            .Where(z => z.Problem is null)
            .GroupBy(z => Key(z.ContractNumber), StringComparer.Ordinal)
            .Select(g => g
                .GroupBy(z => z.AsOf!.Value)
                .Select(t => t.Last())
                .OrderBy(z => z.AsOf!.Value)
                .ToList())];

    /// <summary>Ein Vertrag der Datei mit allen seinen Ständen.</summary>
    private static PolicyValueContractDto Contract(
        IReadOnlyList<AssetSheetRow> zeilen,
        Policy? vertrag,
        Dictionary<(int, DateOnly), decimal> vorhanden)
    {
        // Der jüngste Satz der Datei gibt die Stammdaten. Ältere Zeilen der Historie tragen
        // einen Beitrag von damals; als aktueller Vorschlag wäre er falsch.
        var neueste = zeilen.MaxBy(z => z.AsOf!.Value)!;

        var stände = zeilen
            .Select(z =>
            {
                var alt = vertrag is not null
                          && vorhanden.TryGetValue((vertrag.Id, z.AsOf!.Value), out var wert)
                    ? wert
                    : (decimal?)null;

                return new PolicyValueStandDto
                {
                    AsOf = z.AsOf!.Value,
                    Value = z.Value!.Value,
                    State = alt is null
                        ? PolicyValueState.New
                        : alt == z.Value ? PolicyValueState.Unchanged : PolicyValueState.Changed,
                    Previous = alt,
                    Note = z.Note,
                };
            })
            .OrderByDescending(s => s.AsOf)
            .ToList();

        var neu = stände.Count(s => s.State == PolicyValueState.New);
        var geändert = stände.Count(s => s.State == PolicyValueState.Changed);

        // Auch ein nachzutragender Beitrag ist etwas, das dabei herauskommt. Ohne ihn stünde
        // nach dem Weg „erst die Historie, dann die Monatsdatei“ an sieben Verträgen ein Beitrag
        // von null: die Historie nennt keinen, und die Monatsdatei wäre abgewählt, weil ihre
        // Stände schon dastehen.
        var beitragFehlt = vertrag is { Premium: 0m } && neueste.MonthlyRate is > 0m;

        return new PolicyValueContractDto
        {
            ContractNumber = neueste.ContractNumber,
            Company = neueste.Company,
            ContractKind = neueste.ContractKind,
            Action = vertrag is null ? PolicyValueAction.Create : PolicyValueAction.Match,
            PolicyId = vertrag?.Id,
            PolicyName = vertrag?.Name,
            SuggestedKind = vertrag is null ? KindOf(neueste.ContractKind) : null,
            SuggestedName = vertrag is null ? NameFor(neueste) : null,
            MonthlyRate = neueste.MonthlyRate,
            CurrentPremium = vertrag?.Premium,
            CurrentValue = vertrag?.CurrentValue,
            CurrentAsOf = vertrag?.ValuationDate,
            LatestValue = neueste.Value!.Value,
            LatestAsOf = neueste.AsOf!.Value,
            NewCount = neu,
            ChangedCount = geändert,
            UnchangedCount = stände.Count - neu - geändert,

            // Angehakt, solange etwas dabei herauskommt. Eine Datei, die zum zweiten Mal
            // hereinkommt, schlägt nichts mehr vor — und der Kopf sagt dann auch, warum.
            PreSelected = neu + geändert > 0 || beitragFehlt,
            Stands = stände,
        };
    }

    private static PolicyValueSkippedDto Skipped(AssetSheetRow zeile, string grund)
        => new()
        {
            Line = zeile.Line,
            Label = Label(zeile),
            Reason = grund,
        };

    /// <summary>Wie eine übersprungene Zeile heißt, damit sie sich in der Datei wiederfindet.</summary>
    private static string Label(AssetSheetRow zeile)
    {
        var teile = new[] { zeile.ContractKind, zeile.Company, zeile.ContractNumber }
            .Where(t => t.Length > 0)
            .ToList();

        return teile.Count == 0 ? $"Zeile {zeile.Line}" : string.Join(" · ", teile);
    }

    /// <summary>Warum aus einer sonst vollständigen Zeile kein Stand wird.</summary>
    private static string Unassignable(AssetSheetRow zeile)
        => zeile.Category == AssetSheetParser.PolicyCategory
            ? $"Vertragsart „{zeile.ContractKind}“ ist keine Vorsorgeart"
            : $"Kategorie „{zeile.Category}“ — kein Vorsorgevertrag. Steht die Nummer an einem "
              + "kapitalbildenden Vertrag, kommt die Zeile beim nächsten Mal mit.";

    /// <summary>Ob die Datei die Zeile selbst als Vorsorgevertrag ausweist.</summary>
    /// <remarks>
    /// Nur die Kategorie zählt, nicht der Name der Gesellschaft. Ein Bausparvertrag steht in
    /// dieser Datei unter „Sonstiges“ mit der Vertragsart „unbekannt“ — daraus eine Art zu raten
    /// hieße, eine Angabe zu erfinden, die die Datei nicht macht.
    /// </remarks>
    private static bool IsPolicyRow(AssetSheetRow zeile)
        => zeile.Category == AssetSheetParser.PolicyCategory && KindOf(zeile.ContractKind) is not null;

    /// <summary>Die Vertragsart der Datei in der Sprache der Anwendung.</summary>
    private static PolicyKind? KindOf(string art) => art.Trim().ToLowerInvariant() switch
    {
        "lebensversicherung" => PolicyKind.CapitalLife,
        "rentenversicherung" => PolicyKind.Pension,
        "riesterrente" or "riester" => PolicyKind.Riester,
        "bausparvertrag" or "bausparen" => PolicyKind.BuildingSociety,
        "direktversicherung" or "betriebliche altersvorsorge" => PolicyKind.OccupationalPension,
        _ => null,
    };

    /// <summary>
    /// Wie ein neu angelegter Vertrag heißt.
    /// </summary>
    /// <remarks>
    /// Gesellschaft und Nummer, weil die Datei die Spalte „Bezeichnung“ leer lässt. Sieben
    /// Verträge desselben Versicherers heißen sonst siebenmal gleich, und in der Liste wäre
    /// keiner davon wiederzufinden. Umbenennen kann man ihn danach.
    /// </remarks>
    private static string NameFor(AssetSheetRow zeile)
        => zeile.Company.Length == 0
            ? zeile.ContractNumber
            : $"{zeile.Company} {zeile.ContractNumber}".Trim();

    // ── Übernahme ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Übernimmt die <b>gewählten</b> Verträge in einer Transaktion.
    /// </summary>
    /// <remarks>
    /// <para>Entweder stehen danach alle Stände im Bestand oder keiner. Ein halb übernommener
    /// Verlauf wäre schlimmer als gar keiner: er sähe vollständig aus.</para>
    /// <para>Geschrieben werden <b>Stände</b>. Der Beitrag wird ergänzt, wo keiner steht, und nie
    /// überschrieben — siehe <see cref="FillPremium"/>.</para>
    /// </remarks>
    public async Task<PolicyValueResultDto> ConfirmAsync(
        ConfirmPolicyValuesRequest request, CancellationToken ct = default)
    {
        var (dokument, zeilen) = await ReadAsync(request.DocumentId, ct);

        var gewählt = request.Contracts
            .GroupBy(c => Key(c.ContractNumber), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        if (gewählt.Count == 0)
        {
            throw new RuleViolationException("Es ist kein Vertrag zur Übernahme gewählt.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var wirkungen = new List<PolicyValueEffectDto>();
        var angelegt = 0;
        var neu = 0;
        var geändert = 0;

        foreach (var sätze in Groups(zeilen))
        {
            if (!gewählt.TryGetValue(Key(sätze[0].ContractNumber), out var wahl))
            {
                continue;
            }

            var neueste = sätze[^1];

            var (vertrag, neuAngelegt) = await TargetAsync(wahl, neueste, ct);
            var vorher = vertrag.CurrentValue;
            var vorherStichtag = vertrag.ValuationDate;

            if (neuAngelegt)
            {
                angelegt++;

                // Erst speichern, dann Stände schreiben: ein Bericht braucht die Kennung seines
                // Vertrags, und die vergibt die Datenbank.
                await db.SaveChangesAsync(ct);
            }

            var alt = await db.PolicyReports
                .Where(r => r.PolicyId == vertrag.Id)
                .ToDictionaryAsync(r => r.AsOf, r => r.Value, ct);

            foreach (var satz in sätze)
            {
                var stichtag = satz.AsOf!.Value;

                if (alt.TryGetValue(stichtag, out var bestand))
                {
                    if (bestand == satz.Value)
                    {
                        continue;
                    }

                    geändert++;
                }
                else
                {
                    neu++;
                }

                // Über dieselbe Stelle wie der eingelesene Statusreport und die Maske: ein
                // Stichtag, ein Stand, und der Vertrag führt danach den jüngsten. Zwei Wege mit
                // zwei Regeln liefen auseinander.
                await PolicyService.RecordReportAsync(
                    db, clock, vertrag.Id, stichtag, satz.Value!.Value, Source, ct,
                    documentId: dokument.Id);
            }

            await db.SaveChangesAsync(ct);

            wirkungen.Add(new PolicyValueEffectDto
            {
                PolicyId = vertrag.Id,
                PolicyName = vertrag.Name,
                Created = neuAngelegt,
                StandCount = sätze.Count,
                Value = vertrag.CurrentValue ?? neueste.Value!.Value,
                AsOf = vertrag.ValuationDate ?? neueste.AsOf!.Value,
                Delta = vorher is null ? null : (vertrag.CurrentValue ?? 0m) - vorher.Value,
                PreviousAsOf = vorherStichtag,
            });
        }

        // Übernommen ist mehr als weggeräumt: wartet die Übersicht noch im Scaneingang, ist sie
        // damit erledigt. Sie dort stehen zu lassen hieße, nach der Bestätigung weiter „wartet
        // auf Prüfung“ zu behaupten — über eine Datei, deren Werte schon in den Verträgen
        // stehen. Über das Feld und nicht über ScanInboxService.FileAsync: dessen Schwelle ist
        // Typ *und* Verknüpfung, und eine Übersicht gehört zu allen ihren Verträgen, nicht zu
        // einem.
        var eingang = await db.ScanInbox
            .FirstOrDefaultAsync(x => x.DocumentId == dokument.Id && x.FiledAt == null, ct);

        if (eingang is not null && wirkungen.Count > 0)
        {
            eingang.FiledAt = clock.Now;
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new PolicyValueResultDto
        {
            PolicyCount = wirkungen.Count,
            CreatedCount = angelegt,
            NewCount = neu,
            ChangedCount = geändert,
            Effects = [.. wirkungen.OrderByDescending(w => w.Value)],
        };
    }

    /// <summary>
    /// Der Vertrag, in den die Stände gehen — vorhanden oder frisch angelegt.
    /// </summary>
    /// <remarks>
    /// Die Wahl des Nutzers hat Vorrang vor der Nummer: so lässt sich eine Nummer, die die
    /// Anwendung nicht kennt, von Hand einem vorhandenen Vertrag zuordnen. Nur wo niemand etwas
    /// gewählt hat, entscheidet die Nummer.
    /// </remarks>
    private async Task<(Policy Policy, bool Created)> TargetAsync(
        PolicyValueChoice wahl, AssetSheetRow neueste, CancellationToken ct)
    {
        if (wahl.PolicyId is { } id)
        {
            var gewählt = await db.Policies.FirstOrDefaultAsync(p => p.Id == id, ct)
                          ?? throw new RuleViolationException(
                              $"Den Vertrag zu „{wahl.ContractNumber}“ gibt es nicht mehr.");

            if (!gewählt.IsCapitalForming)
            {
                throw new RuleViolationException(
                    $"„{gewählt.Name}“ ist eine Absicherung und führt keinen erreichten Wert.");
            }

            // Die Nummer nachtragen, wenn sie fehlt: der Vertrag wird beim nächsten Import sonst
            // wieder nicht gefunden, und jemand ordnet ihn Monat für Monat von Hand zu.
            if (string.IsNullOrWhiteSpace(gewählt.PolicyNumber))
            {
                gewählt.PolicyNumber = neueste.ContractNumber;
            }

            FillPremium(gewählt, neueste);
            return (gewählt, false);
        }

        var vorhanden = await db.Policies
            .FirstOrDefaultAsync(p => p.PolicyNumber == neueste.ContractNumber, ct);

        if (vorhanden is not null)
        {
            FillPremium(vorhanden, neueste);
            return (vorhanden, false);
        }

        if (!wahl.Create)
        {
            throw new RuleViolationException(
                $"Zu „{wahl.ContractNumber}“ gibt es keinen Vertrag, und angelegt werden soll "
                + "auch keiner.");
        }

        var art = KindOf(neueste.ContractKind)
                  ?? throw new RuleViolationException(
                      $"Die Vertragsart „{neueste.ContractKind}“ zu „{wahl.ContractNumber}“ sagt "
                      + "nicht, was für ein Vertrag das ist. Bitte von Hand anlegen und "
                      + "zuordnen.");

        var neuer = new Policy
        {
            Kind = art,
            IsCapitalForming = true,
            Name = NameFor(neueste),
            Provider = neueste.Company.Length == 0 ? "unbekannt" : neueste.Company,
            PolicyNumber = neueste.ContractNumber,
            Premium = neueste.MonthlyRate ?? 0m,
            PremiumInterval = PremiumInterval.Monthly,
        };

        db.Policies.Add(neuer);
        return (neuer, true);
    }

    /// <summary>
    /// Trägt den Beitrag nach — aber nur, wenn keiner dasteht.
    /// </summary>
    /// <remarks>
    /// <para>Eine Datei, die monatlich unbeaufsichtigt hereinkommt, schreibt keinen Beitrag um.
    /// Was ein Vertrag kostet, hat jemand eingetragen oder aus der Police gelesen; ein Import,
    /// der das überschreibt, ändert eine Angabe, um die er nicht gebeten wurde.</para>
    /// <para>Eine <em>fehlende</em> Angabe zu ergänzen ist etwas anderes. Sie ist der Grund
    /// dieser Zeilen: die umgewandelte Historie nennt keinen Beitrag — der Wert der
    /// Tabellenkalkulation war ein gerundeter Jahreswert und für manche Jahre gar keiner —, und
    /// ohne diesen Schritt bliebe der Beitrag auf null stehen, egal in welcher Reihenfolge die
    /// beiden Dateien hereinkommen.</para>
    /// <para>Weicht ein gemeldeter Beitrag von einem vorhandenen ab — eine Dynamik etwa —, bleibt
    /// der vorhandene stehen. Die Vorschau nennt den der Datei daneben, und geändert wird er am
    /// Vertrag.</para>
    /// </remarks>
    private static void FillPremium(Policy policy, AssetSheetRow neueste)
    {
        if (policy.Premium == 0m && neueste.MonthlyRate is { } beitrag && beitrag > 0m)
        {
            policy.Premium = beitrag;
            policy.PremiumInterval = PremiumInterval.Monthly;
        }
    }

    // ── Gemeinsames ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Holt das abgelegte Dokument und liest es.
    /// </summary>
    /// <remarks>
    /// Eine Stelle für Vorschau und Übernahme. Zwei Fassungen liefen auseinander, sobald eine
    /// Leseregel dazukäme — und der Unterschied fiele erst auf, wenn im Vertrag eine Zahl steht,
    /// die in der Vorschau anders aussah.
    /// </remarks>
    private async Task<(Document Document, IReadOnlyList<AssetSheetRow> Rows)> ReadAsync(
        int documentId, CancellationToken ct)
    {
        var dokument = await db.Documents.AsNoTracking()
                           .FirstOrDefaultAsync(d => d.Id == documentId, ct)
                       ?? throw new RuleViolationException("Dieses Dokument gibt es nicht.");

        if (paths.Resolve(dokument.RelativePath) is not { } absolut || !File.Exists(absolut))
        {
            throw new RuleViolationException(
                $"Die abgelegte Datei fehlt: {dokument.RelativePath}. Ohne sie lässt sich nichts prüfen.");
        }

        await using var datei = File.OpenRead(absolut);

        try
        {
            return (dokument, await parser.ParseAsync(datei, dokument.FileName, ct));
        }
        catch (StatementFormatException ex)
        {
            throw new RuleViolationException(ex.Message);
        }
    }

    /// <summary>
    /// Wie zwei Vertragsnummern verglichen werden.
    /// </summary>
    /// <remarks>
    /// Ohne Leerzeichen und ohne Rücksicht auf Groß- und Kleinschreibung: dieselbe Nummer steht
    /// in der Datei als „80 511623094“ und im Vertrag vielleicht als „80511623094“. Der
    /// Bindestrich bleibt — er trennt bei „01511104-01“ sieben Verträge voneinander, und ohne
    /// ihn fielen sie zusammen.
    /// </remarks>
    private static string Key(string number)
        => new string([.. number.Where(c => !char.IsWhiteSpace(c))]).ToUpperInvariant();
}
