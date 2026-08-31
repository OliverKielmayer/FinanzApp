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
                    ArrivedOn = DateOnly.FromDateTime(x.CreatedAt),
                }),
            ],
        };
    }

    /// <summary>Nimmt einen bereits abgelegten Beleg in den Eingang auf.</summary>
    public async Task<int> AddAsync(
        int documentId, string? sender, int? pageCount, bool recognised, CancellationToken ct = default)
    {
        var item = new ScanInboxItem
        {
            DocumentId = documentId,
            Sender = sender,
            PageCount = pageCount,
            Recognised = recognised,
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
    /// <para>Der <see cref="Document.Area">Bereich</see> folgt dem gewählten Typ. Die Datei
    /// selbst bleibt liegen, wo sie liegt: sie zu verschieben hieße, einen Pfad zu ändern, den
    /// jemand vielleicht schon woanders notiert hat — und der Bereich ist die Angabe, nach der
    /// gesucht und gefiltert wird, nicht der Ordnername.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Der Dokumenttyp oder das Zielobjekt gibt es im eigenen Haushalt nicht.
    /// </exception>
    public async Task<bool> AssignAsync(
        int id, AssignScanInboxRequest request, CancellationToken ct = default)
    {
        var item = await db.ScanInbox.FirstOrDefaultAsync(x => x.Id == id && x.FiledAt == null, ct);
        if (item is null)
        {
            return false;
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
        await documents.LinkAsync(item.DocumentId, request.TargetType, request.TargetId, ct);

        dokument.DocumentTypeId = typ.Id;
        dokument.Area = typ.Area;
        dokument.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);

        // Über dieselbe Schwelle wie jeder andere Beleg. Die Regel steht einmal, und sie gilt
        // auch für den Weg, der sie gerade erfüllt hat.
        return await FileAsync(id, ct);
    }

    /// <summary>
    /// Nimmt einen Beleg aus dem Eingang — erst wenn er einem Objekt zugeordnet ist.
    /// </summary>
    /// <remarks>
    /// Die Prüfung liegt hier und nicht in der Oberfläche: ein Beleg ohne Verknüpfung ist nicht
    /// eingeordnet, egal über welchen Weg jemand ihn wegräumen will.
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
        var hasLink = await db.DocumentLinks.AnyAsync(l => l.DocumentId == item.DocumentId, ct);

        if (!hasType || !hasLink)
        {
            return false;
        }

        item.FiledAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
