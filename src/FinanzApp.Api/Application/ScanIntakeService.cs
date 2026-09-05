using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Nimmt eine Datei entgegen, die ein überwachter Ordner hereinreicht, und ordnet sie so weit
/// ein, wie es ohne Rückfrage geht.
/// </summary>
/// <remarks>
/// <para>Der unbeaufsichtigte Weg neben dem beaufsichtigten. Beide benutzen dieselbe Analyse —
/// hier steht nur, was ein Dienst darf, wenn niemand zusieht: <b>ablegen und zuordnen, aber
/// nichts übernehmen.</b> Bereich, Ablageordner, Dokumenttyp und die Verknüpfung zum Objekt
/// sind Einordnung; sie lassen sich ansehen und mit einem Griff ändern. Ein erreichter Wert im
/// Vertrag ist das nicht — er wäre danach eine Vermögenszahl, die nie jemand gesehen hat.
/// Deshalb endet dieser Weg im Scaneingang und nicht in
/// <see cref="DocumentScanService.ConfirmAsync"/>.</para>
/// <para>Jede Einlieferung landet im Eingang, auch die vollständig zugeordnete. Sonst käme ein
/// Beleg an, ohne dass irgendwo stünde, dass er angekommen ist — und ein Ordnerdienst, dessen
/// Ergebnis man nicht sieht, ist einer, dem man nicht trauen kann.</para>
/// </remarks>
public sealed class ScanIntakeService(
    FinanzAppDbContext db,
    DocumentScanService scans,
    DocumentService documents,
    ScanInboxService inbox,
    AssetSheetParser sheets,
    IClock clock)
{
    /// <summary>Höchstlänge der mitgelieferten Herkunft. Sie steht am Dokument, nicht im Protokoll.</summary>
    private const int MaxSourceLength = 200;

    /// <summary>
    /// Liest die Datei, legt sie im passenden Bereich ab, verknüpft sie mit ihrem Objekt und
    /// stellt sie in den Scaneingang.
    /// </summary>
    /// <param name="source">
    /// Woher die Datei kam — der Pfad im überwachten Ordner. Steht danach als Beschreibung am
    /// Dokument, damit sich ein Beleg bis zu seiner Quelle zurückverfolgen lässt.
    /// </param>
    public async Task<ScanIntakeResultDto> TakeInAsync(
        Stream content, string fileName, string? source, CancellationToken ct = default)
    {
        // Nicht jede eingelieferte Datei ist ein Beleg. Die Vermögensübersicht des Maklers ist
        // eine Tabelle mit einer Zeile je Vertrag; sie durch die Belegerkennung zu schicken
        // hieße, ein PDF in ihr zu suchen. Entschieden wird an der Kopfzeile und nicht am
        // Dateinamen — der sagt über den Inhalt nichts.
        using var puffer = new MemoryStream();
        await content.CopyToAsync(puffer, ct);
        puffer.Position = 0;

        // Vor allem anderen: liegt genau diese Datei schon da? Geprüft wird, bevor irgendetwas
        // geschrieben ist — danach wäre die zweite Kopie schon abgelegt, und sie wieder
        // wegzuräumen hieße, einen Fehler zu beheben, den man nicht hätte machen müssen.
        var abdruck = await DocumentService.FingerprintAsync(puffer, ct);
        puffer.Position = 0;

        if (await documents.FindByContentAsync(abdruck, ct) is { } schonDa)
        {
            return Duplicate(schonDa, fileName);
        }

        if (AssetSheetParser.CanRead(fileName) && AssetSheetParser.LooksLikeSheet(puffer))
        {
            return await TakeInSheetAsync(puffer, fileName, source, ct);
        }

        var analyse = await scans.AnalyseAsync(puffer, fileName, ct);
        var art = DocumentKindLibrary.All.FirstOrDefault(a => a.Key == analyse.KindKey);

        var typ = art is null ? null : await MatchTypeAsync(art, ct);
        var verknuepft = art is not null
                         && analyse.TargetId is { } zielId
                         && await LinkAsync(analyse.DocumentId, art.Target, zielId, ct);

        await StampAsync(analyse.DocumentId, typ?.Id, source, ct);

        // Erkannt heißt: die Analyse weiß, was das Dokument ist *und* wozu es gehört. Nur der
        // Typ ohne Ziel wäre im Eingang die Zusage „erkannt“ für einen Beleg, den danach doch
        // niemand wegräumen kann.
        var erkannt = art is not null && verknuepft;

        var doppelt = await SameFilingAsync(analyse.DocumentId, ct);

        var eingang = await inbox.AddAsync(
            analyse.DocumentId, analyse.TargetName, analyse.PageCount, erkannt, doppelt?.Id, ct);

        var fehlt = Missing(art, verknuepft, typ is not null, analyse);

        return new ScanIntakeResultDto
        {
            DocumentId = analyse.DocumentId,
            InboxId = eingang,
            FileName = analyse.FileName,
            RelativePath = analyse.RelativePath,
            Area = art?.Area ?? DocumentArea.Other,
            Outcome = fehlt is null ? ScanIntakeOutcome.Assigned : ScanIntakeOutcome.Waiting,
            PageCount = analyse.PageCount,
            KindKey = analyse.KindKey,
            KindLabel = analyse.KindLabel,
            TypeName = typ?.Name,
            TargetName = verknuepft ? analyse.TargetName : null,
            TargetNoun = verknuepft ? analyse.TargetNoun : null,
            Missing = fehlt,
            DuplicateOfDocumentId = doppelt?.Id,
            Summary = Summarise(analyse, art, typ?.Name, verknuepft, fehlt)
                      + (doppelt is null
                          ? string.Empty
                          : $" · Achtung: „{doppelt.Title}“ liegt schon unter demselben "
                            + $"Ablagenamen ({doppelt.RelativePath}) — womöglich derselbe Beleg."),
        };
    }

    /// <summary>
    /// Die Antwort auf eine Datei, die schon in der Ablage liegt.
    /// </summary>
    /// <remarks>
    /// Sie ist nicht abgelegt worden, also gibt es kein neues Dokument und keinen Eintrag im
    /// Eingang. <see cref="ScanIntakeResultDto.DocumentId"/> nennt darum das <em>vorhandene</em>
    /// — die Antwort soll den Weg zum Beleg weisen und nicht ins Leere zeigen.
    /// </remarks>
    private static ScanIntakeResultDto Duplicate(Document vorhanden, string fileName)
        => new()
        {
            DocumentId = vorhanden.Id,
            InboxId = null,
            FileName = vorhanden.FileName,
            RelativePath = vorhanden.RelativePath,
            Area = vorhanden.Area,
            Outcome = ScanIntakeOutcome.Duplicate,

            // Nichts gelesen, nichts gezählt. Eine Seitenzahl hier wäre die des vorhandenen
            // Belegs und stünde in einer Antwort über die eingereichte Datei.
            PageCount = 0,

            KindKey = vorhanden.ScanKind,
            DuplicateOfDocumentId = vorhanden.Id,

            // Ohne Datum: „DocumentDate“ trägt bei einem Beleg, dessen Schreibdatum nicht
            // gelesen wurde, den Ablagetag — es stünde hier als Datum des Schreibens und wäre
            // das Datum von gestern. Der Pfad sagt ohnehin mehr; in ihm steht der Stichtag.
            Summary = $"„{fileName}“ ist Byte für Byte dieselbe Datei wie „{vorhanden.Title}“, "
                      + $"abgelegt unter {vorhanden.RelativePath} — nicht ein zweites Mal abgelegt.",
        };

    /// <summary>
    /// Ein anderer Beleg, den die Ablage unter denselben Namen gelegt hätte.
    /// </summary>
    /// <remarks>
    /// <para>Die zweite Stufe, für den Fall, den der Abdruck nicht trifft: dasselbe Schreiben ein
    /// zweites Mal eingescannt ergibt eine andere Datei — anderes Bild, andere Bytes —, und
    /// derselbe Beleg ist es trotzdem.</para>
    /// <para><b>Gemessen wird am Ablagenamen</b>, denn den bildet die Analyse aus genau den
    /// Merkmalen, die einen Beleg ausmachen: Art, Objekt und Stichtag stehen in Ordner und
    /// Dateiname. Am <see cref="Document.DocumentDate"/> hinge der Vergleich schief — es trägt
    /// den Ablagetag, wo kein Schreibdatum gelesen wurde, und dann wären zwei Berichte aus
    /// verschiedenen Jahren, am selben Tag eingeliefert, plötzlich derselbe Beleg.</para>
    /// <para>Aufgehalten wird nichts: eine berichtigte Fassung desselben Schreibens bekäme
    /// denselben Namen. Der Verdacht steht im Eingang, wo ein Mensch ihn sieht.</para>
    /// </remarks>
    private async Task<Document?> SameFilingAsync(int documentId, CancellationToken ct)
    {
        var abgelegt = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId, ct);

        // Nur bei erkannter Art. Sonst ist der Ablagename der eingereichte Dateiname, und der
        // sagt über den Inhalt nichts: ein Scanner, der jedes Blatt „scan_0001.pdf“ nennt, ließe
        // den Verdacht bei jedem zweiten Zettel aufleuchten — und ein Hinweis, der immer
        // dasteht, wird nicht mehr gelesen.
        if (abgelegt.ScanKind is not { Length: > 0 } art)
        {
            return null;
        }

        var stamm = FilingStem(abgelegt.RelativePath);
        var ordner = FilingFolder(abgelegt.RelativePath);

        // Vorgesiebt über Art und Ordner: beides kann die Datenbank. Den Namen selbst vergleicht
        // der Dienst — die Ausweichziffer abzuschneiden ist keine Abfrage, die SQLite kennt.
        var kandidaten = await db.Documents.AsNoTracking()
            .Where(d => d.Id != documentId && d.ScanKind == art && d.RelativePath.StartsWith(ordner))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        return kandidaten.FirstOrDefault(d => FilingStem(d.RelativePath) == stamm);
    }

    /// <summary>Der Ordner eines Ablagepfads, mit Schrägstrich am Ende.</summary>
    private static string FilingFolder(string relativePath)
    {
        var strich = relativePath.LastIndexOf('/');
        return strich < 0 ? string.Empty : relativePath[..(strich + 1)];
    }

    /// <summary>
    /// Der Ablagepfad ohne die Ziffer, mit der die Ablage einem besetzten Namen ausweicht.
    /// </summary>
    /// <remarks>
    /// <c>Statusreport_2016-07-31_1.pdf</c> und <c>Statusreport_2016-07-31.pdf</c> sind derselbe
    /// Name. Abgeschnitten wird nur eine <em>kurze</em> Zahl: Ausweichziffern zählen bei eins los
    /// und bleiben klein, während <c>_2016</c> zum Namen gehört. Vier Stellen sind eine
    /// Jahreszahl und bleiben stehen.
    /// </remarks>
    private static string FilingStem(string relativePath)
    {
        var ohneEndung = relativePath[..^Path.GetExtension(relativePath).Length];
        var strich = ohneEndung.LastIndexOf('_');

        if (strich <= 0)
        {
            return ohneEndung;
        }

        var hinten = ohneEndung[(strich + 1)..];

        return hinten is { Length: > 0 and < 4 } && hinten.All(char.IsAsciiDigit)
            ? ohneEndung[..strich]
            : ohneEndung;
    }

    /// <summary>
    /// Eine Vermögensübersicht: ablegen, in den Eingang stellen — und nichts übernehmen.
    /// </summary>
    /// <remarks>
    /// <para>Dieselbe Schwelle wie beim Beleg, aus demselben Grund. Diese Datei kommt monatlich
    /// und trägt für jeden Vertrag eine Zahl; ließe der Dienst sie durch, stünden acht
    /// Vermögenswerte in der Anwendung, die nie jemand gesehen hat. Was daraus wird, entscheidet
    /// der Prüfschritt unter <c>/vorsorge/werte/{id}</c>.</para>
    /// <para>Zugeordnet wird hier nichts, und das ist keine Lücke: die Datei gehört zu <em>allen</em>
    /// Verträgen darin, nicht zu einem. Welcher Vertrag welche Zeile bekommt, steht in der
    /// Vorschau — dort wird die Nummer gegen den Bestand gehalten, und dort fällt auch auf,
    /// wenn einer fehlt.</para>
    /// </remarks>
    private async Task<ScanIntakeResultDto> TakeInSheetAsync(
        MemoryStream content, string fileName, string? source, CancellationToken ct)
    {
        var zeilen = await sheets.ParseAsync(content, fileName, ct);

        // Der jüngste Stichtag der Datei benennt sie. Der Tag der Einlieferung täte es nicht:
        // die Übersicht vom Juli kommt im August, und eine nachgereichte käme unter dem Namen
        // der neueren an.
        var stichtag = zeilen.Select(z => z.AsOf).Where(d => d is not null).Max()
                       ?? DateOnly.FromDateTime(clock.Now);

        content.Position = 0;

        var ablage = await documents.UploadAsync(
            content, fileName, DocumentArea.Insurance,
            title: AssetSheetKind.Label,
            documentTypeId: null,
            documentDate: stichtag,
            subFolder: $"Vermögensübersicht/{stichtag:yyyy}",
            preferredName: $"Vermoegensuebersicht_{stichtag:yyyy-MM-dd}",
            ct: ct);

        // Das Kennzeichen, an dem der Eingang später den Prüfschritt findet. Ohne es wäre die
        // Datei ein abgelegtes Dokument wie jedes andere, und die gelesenen Stände kämen nie an
        // ihren Verträgen an.
        var dokument = await db.Documents.FirstAsync(d => d.Id == ablage.DocumentId, ct);
        dokument.ScanKind = AssetSheetKind.Key;

        if (Clean(source) is { Length: > 0 } herkunft)
        {
            dokument.Description = "Aus dem überwachten Ordner übernommen: " + herkunft;
        }

        await db.SaveChangesAsync(ct);

        // Wie viele Zeilen überhaupt lesbar sind — nicht, wie viele davon Verträge werden. Das
        // hängt am Bestand, und der Dienst schlägt hier nichts nach: die Datei führt auch Konten
        // und Depots, und eine Zahl, die sie als Verträge ausgäbe, stünde falsch im Protokoll.
        var lesbar = zeilen.Count(z => z.Problem is null);

        // „prüfen“ und nicht „erkannt“: erkannt hieße im Eingang, dass nur noch das Ja fehlt.
        // Hier fehlt mehr — welche Nummer zu welchem Vertrag gehört, sagt erst die Vorschau.
        // Auch hier: zwei Übersichten zum selben Stichtag bekämen denselben Ablagenamen.
        var doppelt = await SameFilingAsync(ablage.DocumentId, ct);

        var eingang = await inbox.AddAsync(
            ablage.DocumentId, sender: AssetSheetKind.Label, pageCount: null, recognised: false,
            doppelt?.Id, ct);

        return new ScanIntakeResultDto
        {
            DocumentId = ablage.DocumentId,
            InboxId = eingang,
            FileName = dokument.FileName,
            RelativePath = ablage.RelativePath,
            Area = DocumentArea.Insurance,
            Outcome = ScanIntakeOutcome.Waiting,

            // Eine Tabelle hat keine Seiten. Eine Null hier wäre eine Behauptung über eine
            // Größe, die es an dieser Datei nicht gibt; die Zeilen stehen im Protokollsatz.
            PageCount = 1,
            KindKey = AssetSheetKind.Key,
            KindLabel = AssetSheetKind.Label,
            DuplicateOfDocumentId = doppelt?.Id,
            Missing = "Es fehlt: die Zuordnung der Zeilen zu Verträgen und die Bestätigung "
                      + "ihrer Werte.",
            Summary = $"{AssetSheetKind.Label} zum {stichtag:dd.MM.yyyy} · {zeilen.Count} Zeilen, davon "
                      + $"{lesbar} mit Vertragsnummer, Wert und Stichtag · abgelegt unter "
                      + $"{ablage.RelativePath} · wartet im Scaneingang auf die Prüfung der Werte",
        };
    }

    /// <summary>
    /// Der Dokumenttyp des Haushalts, der zur erkannten Art passt — über den Namen.
    /// </summary>
    /// <remarks>
    /// <para>Welche Typen es gibt, entscheidet der Haushalt und nicht der Quelltext; die
    /// Einlieferung legt deshalb keinen an. Sie nimmt den passenden, wenn es ihn gibt, und lässt
    /// das Feld sonst leer — dann trägt ihn der Nutzer im Eingang nach.</para>
    /// <para><b>Zwei Namen, in dieser Reihenfolge:</b> zuerst der Typ, den die Art nennt
    /// (<see cref="DocumentKind.TypeName"/>) — er ist die Schublade und darf für mehrere Arten
    /// dieselbe sein. Dann die Bezeichnung der Art selbst: wer seine Ablage früher danach benannt
    /// hat, behält sie. Ohne diesen zweiten Blick hieße die Neuerung für einen gewachsenen
    /// Bestand, dass er seine Typen umbenennen muss.</para>
    /// <para>Stillgelegte Typen bleiben außen vor: sie stehen in keiner Auswahlliste mehr, und
    /// ein Dienst soll nicht vergeben, was ein Mensch nicht mehr vergeben kann.</para>
    /// </remarks>
    private async Task<DocumentType?> MatchTypeAsync(DocumentKind art, CancellationToken ct)
    {
        var offen = db.DocumentTypes.AsNoTracking().Where(t => !t.IsRetired);

        return await offen.FirstOrDefaultAsync(t => t.Name == art.TypeName, ct)
               ?? await offen.FirstOrDefaultAsync(t => t.Name == art.Label, ct);
    }

    /// <summary>Hängt das Dokument an das Objekt, zu dem es gehört.</summary>
    /// <remarks>
    /// Über <see cref="DocumentService.LinkAsync"/> und nicht über einen eigenen Einfügebefehl:
    /// dort wird geprüft, ob das Ziel im eigenen Haushalt überhaupt existiert, und eine zweite
    /// Einlieferung desselben Belegs verdoppelt die Verknüpfung nicht.
    /// </remarks>
    private async Task<bool> LinkAsync(
        int documentId, DocumentTargetKind target, int targetId, CancellationToken ct)
    {
        try
        {
            var link = await documents.LinkAsync(documentId, LinkTypeFor(target), targetId, ct);
            return link is not null;
        }
        catch (ArgumentException)
        {
            // Das Ziel gibt es nicht (mehr). Kein Grund, die Einlieferung scheitern zu lassen —
            // die Datei ist abgelegt, und der Eingang fragt nach dem Objekt.
            return false;
        }
    }

    /// <summary>Setzt Dokumenttyp und Herkunft am frisch abgelegten Dokument.</summary>
    /// <remarks>
    /// Die Herkunft steht als Beschreibung dort, wo sie hingehört: am Dokument. Wer im Eingang
    /// eine Datei findet, die er nicht erwartet hat, sieht damit, aus welchem Ordner sie kam.
    /// </remarks>
    private async Task StampAsync(int documentId, int? typeId, string? source, CancellationToken ct)
    {
        var dokument = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (dokument is null)
        {
            return;
        }

        if (typeId is { } id)
        {
            dokument.DocumentTypeId = id;
        }

        if (Clean(source) is { Length: > 0 } herkunft)
        {
            dokument.Description = "Aus dem überwachten Ordner übernommen: " + herkunft;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Was zur vollständigen Zuordnung fehlt — <c>null</c>, wenn nichts fehlt.</summary>
    /// <remarks>
    /// In der Reihenfolge, in der es einen Menschen interessiert: was das Dokument ist, wozu es
    /// gehört, unter welchem Typ es abgelegt wird. Ein Satz, der die Arbeit benennt, ist im
    /// Protokoll eines Dienstes mehr wert als ein Zustandswort.
    /// </remarks>
    private static string? Missing(
        DocumentKind? art, bool verknuepft, bool hatTyp, ScanAnalysisDto analyse)
    {
        if (art is null)
        {
            return analyse.HasTextLayer
                ? "Die Art des Dokuments ist nicht erkennbar — Typ und Objekt fehlen."
                : "Aus der Datei ist kein Text zu holen — Typ und Objekt fehlen.";
        }

        List<string> fehlt = [];

        if (!verknuepft)
        {
            fehlt.Add(analyse.TargetName is { Length: > 0 }
                ? $"das Objekt ({art.TargetNoun})"
                : $"ein {art.TargetNoun}, zu dem das Dokument passt");
        }

        if (!hatTyp)
        {
            // Der kurze Name: er ist der, den anzulegen hilft, und er passt gleich für mehrere
            // Arten. Die lange Bezeichnung stünde hier als Aufforderung, das Falsche zu tun.
            fehlt.Add($"ein Dokumenttyp namens „{art.TypeName}“");
        }

        return fehlt.Count == 0 ? null : "Es fehlt: " + string.Join(" und ", fehlt) + ".";
    }

    /// <summary>Die Zeile, die im Protokoll des einliefernden Dienstes steht.</summary>
    private static string Summarise(
        ScanAnalysisDto analyse, DocumentKind? art, string? typ, bool verknuepft, string? fehlt)
    {
        List<string> teile = [art?.Label ?? "Unbekannte Art"];

        if (verknuepft && analyse.TargetName is { Length: > 0 } ziel)
        {
            teile.Add($"{art?.TargetNoun ?? "Objekt"} {ziel}");
        }

        if (typ is { Length: > 0 })
        {
            teile.Add("Typ " + typ);
        }

        teile.Add("abgelegt unter " + analyse.RelativePath);
        teile.Add(fehlt is null
            ? "wartet im Scaneingang auf Bestätigung"
            : "wartet im Scaneingang · " + fehlt);

        return string.Join(" · ", teile);
    }

    /// <summary>Worauf ein Dokumenttyp zeigt, in der Sprache der Verknüpfungen.</summary>
    private static LinkTargetType LinkTypeFor(DocumentTargetKind target) => target switch
    {
        DocumentTargetKind.Depot => LinkTargetType.Portfolio,
        _ => LinkTargetType.Policy,
    };

    /// <summary>
    /// Die Herkunft, gekürzt und ohne Zeilenumbrüche.
    /// </summary>
    /// <remarks>
    /// Sie kommt von außen und wird angezeigt. Umbrüche darin zerrissen die Beschreibung, und
    /// ein ganzer Pfadbaum darin machte sie unlesbar.
    /// </remarks>
    private static string? Clean(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var line = string.Concat(source.Where(c => !char.IsControl(c))).Trim();
        return line.Length <= MaxSourceLength ? line : line[..MaxSourceLength] + "…";
    }
}
