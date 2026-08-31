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
    ScanInboxService inbox)
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
        var analyse = await scans.AnalyseAsync(content, fileName, ct);
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

        var eingang = await inbox.AddAsync(
            analyse.DocumentId, analyse.TargetName, analyse.PageCount, erkannt, ct);

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
            Summary = Summarise(analyse, art, typ?.Name, verknuepft, fehlt),
        };
    }

    /// <summary>
    /// Der Dokumenttyp des Haushalts, der zur erkannten Art passt — über den Namen.
    /// </summary>
    /// <remarks>
    /// <para>Welche Typen es gibt, entscheidet der Haushalt und nicht der Quelltext; die
    /// Einlieferung legt deshalb keinen an. Sie nimmt den gleichnamigen, wenn es ihn gibt, und
    /// lässt das Feld sonst leer — dann trägt ihn der Nutzer im Eingang nach.</para>
    /// <para>Stillgelegte Typen bleiben außen vor: sie stehen in keiner Auswahlliste mehr, und
    /// ein Dienst soll nicht vergeben, was ein Mensch nicht mehr vergeben kann.</para>
    /// </remarks>
    private async Task<DocumentType?> MatchTypeAsync(DocumentKind art, CancellationToken ct)
        => await db.DocumentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsRetired && t.Name == art.Label, ct);

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
            fehlt.Add($"ein Dokumenttyp namens „{art.Label}“");
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
