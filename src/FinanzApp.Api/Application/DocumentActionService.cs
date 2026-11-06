using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Sieht Schreiben daraufhin durch, ob sie etwas verlangen.
/// </summary>
/// <remarks>
/// <para>Die zweite Schicht hinter den Leseregeln. Jene erkennen eine Forderung an einem Wort
/// — „fällig“, „zahlbar bis“ — und finden damit Bescheide und Rechnungen. Was frei formuliert
/// ist, finden sie nicht: „Bitte stellen Sie Ihren Lastschrifteinzug auf das neue Konto um“
/// enthält keines ihrer Wörter und verlangt trotzdem etwas.</para>
/// <para><b>Das Modell findet, es entscheidet nicht.</b> Es gibt eine Zeile zurück, nie einen
/// Wert. Ob die Zeile echt ist, prüft <see cref="ActionEvidence"/> am gespeicherten Text; was
/// dort nicht vorkommt, wird verworfen, ohne dass jemand abwägen müsste, wie glaubwürdig es
/// klingt. Das Datum aus der Zeile liest derselbe Leser wie überall sonst.</para>
/// <para>Gefundenes wird als <see cref="Data.Entities.Document.ActionFromModel"/> gekennzeichnet.
/// Die Zeile ist so geprüft wie jede andere — schwächer ist nur der Grund, sie für eine
/// Forderung zu halten, und wer den Vorgang liest, soll das wissen.</para>
/// </remarks>
public sealed class DocumentActionService(
    FinanzAppDbContext db,
    IDocumentActionFinder finder,
    IClock clock,
    ILogger<DocumentActionService> log)
{
    /// <summary>
    /// Sieht einen einzelnen Beleg durch. <c>true</c>, wenn er etwas verlangt.
    /// </summary>
    /// <remarks>
    /// Auch ein Beleg, der nichts verlangt, wird vermerkt — mit einer leeren Zeile. Sonst ginge
    /// er bei jedem Durchlauf erneut durch das Modell, und die Zahl der offenen Belege stünde
    /// für immer still.
    /// </remarks>
    public async Task<bool> ExamineAsync(int documentId, CancellationToken ct = default)
    {
        if (!finder.Available)
        {
            return false;
        }

        var dokument = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (dokument is null
            || dokument.ActionLine is not null
            || dokument.ContentText is not { Length: > 0 } text)
        {
            return false;
        }

        var behauptet = await finder.FindActionLineAsync(text, ct);

        // Nichts gefunden und nichts erreichbar sehen hier gleich aus. Der Unterschied steht
        // im Protokoll der Anbindung; für den Beleg ist beides dasselbe Ergebnis.
        if (behauptet is not { Length: > 0 })
        {
            await VermerkeAsync(dokument, string.Empty, due: null, ct);
            return false;
        }

        if (ActionEvidence.Verify(text, behauptet, clock.Today) is not { } gefunden)
        {
            log.LogInformation(
                "Beleg {Id}: Das Modell nannte einen Satz, der so nicht im Beleg steht. "
                    + "Verworfen: {Satz}",
                documentId, ActionEvidence.ForLog(behauptet));

            await VermerkeAsync(dokument, string.Empty, due: null, ct);
            return false;
        }

        log.LogInformation(
            "Beleg {Id} verlangt etwas ({Termin}): {Satz}",
            documentId, ActionEvidence.Format(gefunden.Due), ActionEvidence.ForLog(gefunden.Line));

        await VermerkeAsync(dokument, gefunden.Line, gefunden.Due, ct);
        return true;
    }

    /// <summary>
    /// Sieht den Bestand durch, in Schüben.
    /// </summary>
    /// <remarks>
    /// <para>Mit Obergrenze, wie das Nachholen des Textes: Jeder Beleg kostet das Modell ein
    /// bis zwei Sekunden, und eine Anfrage, die Minuten braucht, bricht unterwegs ab.</para>
    /// <para>Die neuesten zuerst. Ein Schreiben von gestern kann noch eine Frist haben; eines
    /// von 2019 hat seine längst hinter sich, und wer abbricht, hat dann wenigstens das
    /// Wichtige gesehen.</para>
    /// </remarks>
    public async Task<ActionScanResultDto> ScanAsync(int limit = 20, CancellationToken ct = default)
    {
        if (!finder.Available)
        {
            return new ActionScanResultDto
            {
                Verfuegbar = false,
                Durchgesehen = 0,
                Gefunden = 0,
                Offen = await OpenCountAsync(ct),
            };
        }

        var offen = await db.Documents.AsNoTracking()
            .Where(d => d.ActionLine == null && d.ContentText != null && d.ContentText != "")
            .OrderByDescending(d => d.DocumentDate)
            .ThenByDescending(d => d.Id)
            .Select(d => d.Id)
            .Take(limit)
            .ToListAsync(ct);

        var gefunden = 0;
        foreach (var id in offen)
        {
            if (await ExamineAsync(id, ct))
            {
                gefunden++;
            }
        }

        return new ActionScanResultDto
        {
            Verfuegbar = true,
            Durchgesehen = offen.Count,
            Gefunden = gefunden,
            Offen = await OpenCountAsync(ct),
        };
    }

    /// <summary>Wie viele Belege noch niemand durchgesehen hat.</summary>
    public Task<int> OpenCountAsync(CancellationToken ct = default)
        => db.Documents.AsNoTracking()
            .CountAsync(d => d.ActionLine == null && d.ContentText != null && d.ContentText != "", ct);

    private async Task VermerkeAsync(
        Data.Entities.Document dokument, string zeile, DateOnly? due, CancellationToken ct)
    {
        dokument.ActionLine = zeile;
        dokument.DueDate = due;
        dokument.ActionFromModel = zeile.Length > 0;
        dokument.UpdatedAt = clock.Now;

        await db.SaveChangesAsync(ct);
    }
}
