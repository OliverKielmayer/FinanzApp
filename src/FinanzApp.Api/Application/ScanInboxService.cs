using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Scaneingang — ein Posteingang für Belege, die noch niemand eingeordnet hat.
/// </summary>
/// <remarks>
/// <para>Der Unterschied zum Einzelbeleg ist der Punkt: gescannt wird stapelweise, eingeordnet
/// wird später. Ein Beleg bleibt hier stehen, bis <b>Typ und Objekt</b> bestätigt sind. Ohne
/// diese Schwelle verschwände er in der Ablage, ohne dass jemand entschieden hätte, wozu er
/// gehört — und genau solche Dokumente findet später niemand wieder.</para>
/// <para>Die Datei liegt bereits im Dokumentordner; was fehlt, ist ihre Bedeutung.</para>
/// </remarks>
public sealed class ScanInboxService(FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    public async Task<ScanInboxDto> GetAsync(CancellationToken ct = default)
    {
        var rows = await db.ScanInbox.AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.FiledAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        // Wie viele gelesene Werte je Beleg noch niemand bestätigt hat — in einem Zug für
        // alle. „Wegräumen“ fragt danach, statt sie stillschweigend liegen zu lassen.
        var belege = rows.Select(x => x.DocumentId).Distinct().ToList();

        var offen = await db.DocumentExtractions
            .Where(x => belege.Contains(x.DocumentId) && !x.Confirmed)
            .GroupBy(x => x.DocumentId)
            .Select(g => new { g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Anzahl, ct);

        // Die Namen der vorhandenen Belege in einem Zug. Ein Verweis auf eine Nummer wäre für
        // den Leser nichts wert — er will wissen, welches Schreiben er schon hat.
        var verdächtig = rows.Select(x => x.DuplicateOfDocumentId).OfType<int>().Distinct().ToList();

        var namen = verdächtig.Count == 0
            ? new Dictionary<int, string>()
            : await db.Documents.AsNoTracking()
                .Where(d => verdächtig.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Title, ct);

        // Die gepflegten Typennamen, damit die Zeile sagen kann, ob der Vorschlag schon einen
        // hat. Einmal geladen statt je Zeile gefragt.
        var typen = (await db.DocumentTypes.AsNoTracking()
                .Where(t => !t.IsRetired)
                .Select(t => t.Name)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ziele = await AbgeleiteteZieleAsync(rows, ct);

        return new ScanInboxDto
        {
            WaitingCount = rows.Count,
            Items =
            [
                .. rows.Select(x => new ScanInboxItemDto
                {
                    Id = x.Id,
                    DocumentId = x.DocumentId,
                    FileName = x.Document?.FileName ?? "Beleg",
                    Sender = x.Sender,
                    PageCount = x.PageCount,
                    Recognised = x.Recognised,
                    CanTakeValues = x.Document != null && x.Document.ScanKind != null,
                    ValuesHref = ValuesHref(x.Document),
                    ValuesLabel = ScanReview.Label(x.Document?.ScanKind),
                    UnconfirmedValues = offen.GetValueOrDefault(x.DocumentId),
                    DuplicateOfDocumentId = x.DuplicateOfDocumentId,
                    DuplicateOfTitle = x.DuplicateOfDocumentId is { } vorhanden
                                       && namen.TryGetValue(vorhanden, out var name)
                        ? name
                        : null,

                    // Nur für das Unerkannte. Wo eine Leseregel gegriffen hat, steht der Typ
                    // schon fest, und ein zweiter Vorschlag daneben wäre ein Widerspruch.
                    SuggestedType = Vorschlag(x.Document)?.TypeName,
                    SuggestedArea = Vorschlag(x.Document) is { } v
                        ? DocumentPathService.FolderFor(v.Area)
                        : null,
                    SuggestedTypeExists = Vorschlag(x.Document) is { } w
                                          && typen.Contains(w.TypeName),
                    SuggestedTarget = ziele.GetValueOrDefault(x.Id),
                    ArrivedOn = DateOnly.FromDateTime(x.CreatedAt),
                }),
            ],
        };
    }

    /// <summary>
    /// Ob Post von diesem Absender nachweislich an mehr als ein Objekt gegangen ist.
    /// </summary>
    /// <remarks>
    /// <para>Gefragt wird mit <em>derselben</em> Regel, mit der der Vorschlag später greifen
    /// würde: Wen das Muster trifft, der zählt. Eine engere Frage ließe Regeln entstehen, die
    /// breiter wirken, als sie geprüft wurden.</para>
    /// <para>Ein einziger abweichender Beleg genügt. Das ist streng, und es soll streng sein:
    /// Der Schaden einer falschen Ablage ist nicht die falsche Zeile, sondern dass niemand sie
    /// sucht. Wer eine alte Fehlablage berichtigt, macht den Absender damit wieder eindeutig,
    /// und die Regel entsteht beim nächsten Beleg von selbst.</para>
    /// </remarks>
    private async Task<bool> UneinigAsync(
        string muster, LinkTargetType art, int ziel, CancellationToken ct)
    {
        var absender = await db.ScanInbox.AsNoTracking()
            .Where(x => x.Sender != null)
            .Select(x => new { x.DocumentId, x.Sender })
            .ToListAsync(ct);

        var belege = absender
            .Where(x => Filing.Matches(x.Sender!, muster))
            .Select(x => x.DocumentId)
            .Distinct()
            .ToList();

        if (belege.Count == 0)
        {
            return false;
        }

        return await db.DocumentLinks.AsNoTracking()
            .AnyAsync(l => belege.Contains(l.DocumentId)
                           && (l.TargetType != art || l.TargetId != ziel), ct);
    }

    /// <summary>
    /// Für welche Eingänge eine gelernte Regel ein Objekt vorschlägt.
    /// </summary>
    /// <remarks>
    /// <para>Vorgeschlagen wird nur, wo noch nichts hängt. Ein Beleg, den eine Leseregel bereits
    /// verknüpft hat, braucht keinen zweiten Vorschlag — der wäre ein Widerspruch neben einer
    /// Tatsache.</para>
    /// <para>Das Objekt wird aufgelöst, bevor der Vorschlag entsteht. Zeigt eine Regel auf
    /// etwas Gelöschtes, entfällt sie still: Ein Vorschlag mit dem Namen „Nicht mehr vorhanden“
    /// wäre schlechter als keiner.</para>
    /// <para>Die Regeln kommen in einem Zug, der Vergleich läuft im Speicher. Ein Präfix an der
    /// Wortgrenze lässt sich in SQL nicht ausdrücken, ohne die Normalisierung zu verdoppeln —
    /// und zwei Fassungen derselben Regel wären eine zu viel.</para>
    /// </remarks>
    private async Task<Dictionary<int, SuggestedTargetDto>> AbgeleiteteZieleAsync(
        List<ScanInboxItem> rows, CancellationToken ct)
    {
        var offen = rows.Where(x => x.Sender is { Length: > 0 }).ToList();
        if (offen.Count == 0)
        {
            return [];
        }

        var regeln = await db.FilingRules.AsNoTracking().ToListAsync(ct);
        if (regeln.Count == 0)
        {
            return [];
        }

        var belege = offen.Select(x => x.DocumentId).Distinct().ToList();
        var verknuepft = await db.DocumentLinks.AsNoTracking()
            .Where(l => belege.Contains(l.DocumentId))
            .Select(l => l.DocumentId)
            .ToListAsync(ct);

        var schonDa = verknuepft.ToHashSet();
        var namen = new Dictionary<(LinkTargetType, int), string?>();
        var ergebnis = new Dictionary<int, SuggestedTargetDto>();

        foreach (var eingang in offen)
        {
            if (schonDa.Contains(eingang.DocumentId))
            {
                continue;
            }

            var regel = regeln.FirstOrDefault(r => Filing.Matches(eingang.Sender!, r.SenderPattern));
            if (regel is null)
            {
                continue;
            }

            var schluessel = (regel.TargetType, regel.TargetId);
            if (!namen.TryGetValue(schluessel, out var name))
            {
                name = await documents.TargetLabelAsync(regel.TargetType, regel.TargetId, ct);
                namen[schluessel] = name;
            }

            if (name is not { Length: > 0 })
            {
                continue;
            }

            ergebnis[eingang.Id] = new SuggestedTargetDto
            {
                TargetType = regel.TargetType,
                TargetId = regel.TargetId,
                Label = name,
                TargetLabel = ObjectLabelService.TargetLabel(regel.TargetType),
                LearnedOn = DateOnly.FromDateTime(regel.LearnedAt),
            };
        }

        return ergebnis;
    }

    /// <summary>Der Prüfschritt, der zu diesem Dokument passt.</summary>
    /// <remarks>
    /// Welcher es ist, entscheidet <see cref="ScanReview"/> — dieselbe Weiche wie am Beleg
    /// selbst. Auch ohne Leseabdruck führt ein Weg dorthin: die Nachschau liest die Datei mit den
    /// Regeln von heute und erkennt die Art dabei neu. Sonst bliebe ein Beleg, der vor einer
    /// neuen Leseregel hereinkam, für immer stumm.
    /// </remarks>
    private static string? ValuesHref(Document? document)
        => document is null ? null : ScanReview.Href(document.ScanKind, document.Id);

    /// <summary>Nimmt einen bereits abgelegten Beleg in den Eingang auf.</summary>
    /// <param name="duplicateOf">
    /// Das Dokument, das denselben Beleg schon trägt — die Ablage hätte beiden denselben Namen
    /// gegeben. Der Eintrag entsteht trotzdem: es kann eine berichtigte Fassung sein, und was
    /// davon gilt, entscheidet kein Dienst.
    /// </param>
    public async Task<int> AddAsync(
        int documentId, string? sender, int? pageCount, bool recognised,
        int? duplicateOf = null, CancellationToken ct = default)
    {
        var item = new ScanInboxItem
        {
            DocumentId = documentId,
            Sender = sender,
            PageCount = pageCount,
            Recognised = recognised,
            DuplicateOfDocumentId = duplicateOf,
            CreatedAt = clock.Now,
        };

        db.ScanInbox.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }

    /// <summary>
    /// Trägt Typ und Objekt nach und räumt den Beleg damit weg.
    /// </summary>
    /// <remarks>
    /// <para>Der Weg für alles, was die Erkennung nicht selbst hinbekommen hat — ein Scan ohne
    /// Textebene, eine Rechnung, für die es keine Leseregel gibt, ein Beleg zu einem Vertrag,
    /// den es in der Anwendung noch nicht gibt. Er endet dort, wo der erkannte Beleg auch
    /// endet: eingeordnet und aus dem Eingang verschwunden.</para>
    /// <para>Der <see cref="Document.Area">Bereich</see> folgt dem gewählten Typ, und die Datei
    /// zieht mit: eine Einlieferung ohne erkanntes Objekt liegt unter „Unbekannt“, und bliebe sie
    /// dort, behauptete der Ordner nach dem Zuordnen etwas, das nicht mehr stimmt. Dass sich ein
    /// Pfad damit ändert, ist der Preis — wer im Dateimanager sucht, fände den Beleg sonst nicht
    /// bei seinem Vertrag.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Der Dokumenttyp oder das Zielobjekt gibt es im eigenen Haushalt nicht.
    /// </exception>
    /// <summary>
    /// Was der Text vermuten lässt — nur, wenn keine Leseregel gegriffen hat.
    /// </summary>
    /// <remarks>
    /// Gerechnet statt gespeichert. Der Text steht ohnehin am Dokument; eine zweite Spalte für
    /// die Vermutung liefe den Regeln davon, sobald jemand eine ändert.
    /// </remarks>
    private static FilingSuggestion? Vorschlag(Document? dokument)
        => dokument is { ScanKind: null }
            ? FilingSuggestions.For(dokument.ContentText)
            : null;

    public async Task<bool> AssignAsync(
        int id, AssignScanInboxRequest request, CancellationToken ct = default)
    {
        var item = await db.ScanInbox.FirstOrDefaultAsync(x => x.Id == id && x.FiledAt == null, ct);
        if (item is null)
        {
            return false;
        }

        // Art und Kennung gehören zusammen. Die halbe Angabe wäre ein Ziel, das es nicht gibt,
        // und sie fiele erst beim Verknüpfen auf — da stünde der Typ schon.
        if (request.TargetType is null != request.TargetId is null)
        {
            throw new ArgumentException("Ein Objekt braucht Art und Kennung — oder keines von beidem.");
        }

        var typ = await db.DocumentTypes.FirstOrDefaultAsync(
                      t => t.Id == request.DocumentTypeId && !t.IsRetired, ct)
                  ?? throw new ArgumentException("Diesen Dokumenttyp gibt es nicht.");

        var dokument = await db.Documents.FirstOrDefaultAsync(d => d.Id == item.DocumentId, ct);
        if (dokument is null)
        {
            return false;
        }

        // Erst verknüpfen, dann den Typ setzen: die Verknüpfung ist die Prüfung, ob es das Ziel
        // überhaupt gibt. Andersherum stünde nach einem falschen Ziel ein geänderter Typ da.
        var verknuepfung = request.TargetType is { } art && request.TargetId is { } ziel
            ? await documents.LinkAsync(item.DocumentId, art, ziel, ct)
            : null;

        dokument.DocumentTypeId = typ.Id;
        dokument.Area = typ.Area;
        dokument.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);

        // Und die Datei hinterher: eine Einlieferung ohne erkanntes Objekt legt sie unter
        // „Unbekannt“ ab, und die Art kann aus einem anderen Bereich kommen als die Erkennung
        // geraten hatte. Bliebe die Datei liegen, behauptete der Ordner etwas, das nicht mehr
        // stimmt — und wer im Dateimanager sucht, fände den Beleg nicht bei seinem Vertrag.
        //
        // Ohne Objekt bleibt „Unbekannt“ stehen und nur der Bereich wandert. Das ist keine
        // halbe Ablage: Der Beleg liegt dann sichtbar dort, wo noch etwas offen ist.
        var objekt = verknuepfung is { Label.Length: > 0 } gefunden ? gefunden.Label : null;

        await documents.RefileAsync(item.DocumentId, typ.Area, objekt, ct);

        // Und der Name hinterher. Ein Scanner nennt jede Seite „Scannen0007“; nach dem Umlegen
        // liegt sie zwar am richtigen Ort, heißt aber weiter so — und im Dateimanager
        // unterscheidet nichts die eine von der nächsten. Ohne Objekt trägt der Name immerhin
        // Art und Datum. Ein gepflegter Name bleibt unangetastet, dieselbe Regel wie beim
        // Umlegen.
        await documents.RenameIfScannedAsync(
            item.DocumentId, typ.Name, objekt ?? string.Empty, dokument.DocumentDate, ct);

        // Und hier lernt die App: Wohin Post von diesem Absender gehört, weiß sie erst, wenn es
        // ihr jemand einmal gezeigt hat. Genau das ist gerade passiert.
        await LerneAblageAsync(item.Sender, request.TargetType, request.TargetId, ct);

        // Über dieselbe Schwelle wie jeder andere Beleg. Die Regel steht einmal, und sie gilt
        // auch für den Weg, der sie gerade erfüllt hat.
        return await FileAsync(id, ct);
    }

    /// <summary>
    /// Merkt sich, wohin Post von diesem Absender gehört.
    /// </summary>
    /// <remarks>
    /// <para>Gelernt wird nur aus einer <b>vollständigen</b> Zuordnung: Wer kein Objekt gewählt
    /// hat, hat nichts über das Objekt gesagt, und „keines“ ist keine Regel — beim nächsten Mal
    /// soll wieder gefragt werden.</para>
    /// <para>Die zweite Zuordnung desselben Absenders an dasselbe Objekt bestätigt die Regel und
    /// frischt ihr Lerndatum auf. An ein <em>anderes</em> Objekt hebt sie die Regel auf, statt
    /// sie zu berichtigen — siehe <see cref="UneinigAsync"/>.</para>
    /// <para>Ohne Absender wird nichts gelernt. Ein Muster aus einer leeren Zeile träfe jeden
    /// Beleg, der ebenfalls keinen Absender trägt — und das sind die unsichersten von allen.</para>
    /// <para><b>Und nichts, wo der Absender uneins ist.</b> Ein Versicherer schickt die
    /// Statusreports für sieben Verträge unter demselben Briefkopf; unterschieden werden sie an
    /// der Vertragsnummer im Text, nicht am Absender. Eine Regel daraus wäre siebenmal von acht
    /// falsch — und zwar still, mit einem vorgewählten Objekt, das plausibel aussieht. Also
    /// vorher nachsehen, wohin Post von diesem Absender sonst noch ging.</para>
    /// </remarks>
    private async Task LerneAblageAsync(
        string? sender, LinkTargetType? targetType, int? targetId, CancellationToken ct)
    {
        if (sender is not { Length: > 0 } absender
            || targetType is not { } art || targetId is not { } ziel)
        {
            return;
        }

        var muster = Filing.RulePatternFor(absender);
        if (muster.Length == 0)
        {
            return;
        }

        if (await UneinigAsync(muster, art, ziel, ct))
        {
            // Eine vorhandene Regel ist damit widerlegt. Sie stehen zu lassen hieße, einen
            // Vorschlag weiterzugeben, von dem die Ablage bereits weiß, dass er nicht trägt.
            var ueberholt = await db.FilingRules.FirstOrDefaultAsync(r => r.SenderPattern == muster, ct);
            if (ueberholt is not null)
            {
                db.FilingRules.Remove(ueberholt);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        var regel = await db.FilingRules.FirstOrDefaultAsync(r => r.SenderPattern == muster, ct);
        if (regel is null)
        {
            db.FilingRules.Add(new FilingRule
            {
                SenderPattern = muster,
                TargetType = art,
                TargetId = ziel,
                LearnedAt = clock.Now,
            });
        }
        else
        {
            regel.TargetType = art;
            regel.TargetId = ziel;
            regel.LearnedAt = clock.Now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Nimmt einen Beleg aus dem Eingang — erst wenn er eine Art trägt.
    /// </summary>
    /// <remarks>
    /// <para>Die Prüfung liegt hier und nicht in der Oberfläche: ein Beleg ohne Art ist nicht
    /// abgelegt, sondern nur weggeklickt, egal über welchen Weg jemand ihn wegräumen will.</para>
    /// <para><b>Ein Objekt wird nicht verlangt.</b> Früher schon — und damit blieb jede Post
    /// liegen, deren Objekt es noch nicht gab oder nie geben wird. Was sie braucht, ist ein
    /// Ablageort und ein Name; woran sie hängt, kann nachgetragen werden und ist in der
    /// Dokumentliste als offener Punkt zu sehen.</para>
    /// </remarks>
    public async Task<bool> FileAsync(int id, CancellationToken ct = default)
    {
        var item = await db.ScanInbox.FirstOrDefaultAsync(x => x.Id == id && x.FiledAt == null, ct);
        if (item is null)
        {
            return false;
        }

        var hasType = await db.Documents.AnyAsync(
            d => d.Id == item.DocumentId && d.DocumentTypeId != null, ct);

        if (!hasType)
        {
            return false;
        }

        item.FiledAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
