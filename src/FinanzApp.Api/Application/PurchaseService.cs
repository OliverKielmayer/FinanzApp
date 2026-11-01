using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Anschaffungen — gekaufte Gegenstände, an denen ihre Papiere hängen.
/// </summary>
/// <remarks>
/// <para>Der Platz, der zwischen den Objekten fehlte. Ein Lieferschein für eine Einbauküche
/// oder ein medizinisches Gerät gehört an keinen Vertrag, an kein Konto und an keine
/// Immobilie. Ohne dieses Objekt landete er im Nirgendwo oder, schlimmer, an irgendetwas
/// Naheliegendem — und eine falsche Verknüpfung sieht aus wie eine Auskunft.</para>
/// <para>Bewusst ohne eigene Auswertung. Was eine Anschaffung kostet, steht auf ihrer
/// Rechnung; sie in eine Vermögensrechnung zu ziehen hieße, über Abschreibung und Restwert zu
/// entscheiden, und das hat niemand verlangt.</para>
/// </remarks>
public sealed class PurchaseService(
    FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    public async Task<IReadOnlyList<PurchaseListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var heute = DateOnly.FromDateTime(clock.Now);

        var zeilen = await db.Purchases.AsNoTracking()
            .OrderByDescending(p => p.PurchaseDate)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.PurchaseDate,
                p.Vendor,
                p.Price,
                p.WarrantyUntil,
            })
            .ToListAsync(ct);

        // Eine Abfrage für alle Zähler statt einer je Zeile: Bei dreißig Anschaffungen wären
        // das sonst einunddreißig Abfragen für eine Liste.
        var belege = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.TargetType == LinkTargetType.Purchase)
            .GroupBy(l => l.TargetId)
            .Select(g => new { TargetId = g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.TargetId, x => x.Anzahl, ct);

        return
        [
            .. zeilen.Select(p => new PurchaseListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                PurchaseDate = p.PurchaseDate,
                Vendor = p.Vendor,
                Price = p.Price,
                WarrantyUntil = p.WarrantyUntil,
                DocumentCount = belege.TryGetValue(p.Id, out var anzahl) ? anzahl : 0,
                WarrantyRunning = Laeuft(p.WarrantyUntil, heute),
            }),
        ];
    }

    public async Task<PurchaseDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var purchase = await db.Purchases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (purchase is null)
        {
            return null;
        }

        return new PurchaseDetailDto
        {
            Id = purchase.Id,
            Name = purchase.Name,
            PurchaseDate = purchase.PurchaseDate,
            Vendor = purchase.Vendor,
            Price = purchase.Price,
            WarrantyUntil = purchase.WarrantyUntil,
            WarrantyRunning = Laeuft(purchase.WarrantyUntil, DateOnly.FromDateTime(clock.Now)),
            Note = purchase.Note,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Purchase, purchase.Id, ct),
        };
    }

    /// <summary>
    /// Ob die Garantie am Stichtag noch läuft.
    /// </summary>
    /// <remarks>
    /// <c>null</c> heißt: Es steht keine Frist da. Das ist nicht dasselbe wie „abgelaufen“ —
    /// eine fehlende Angabe als Ablauf auszugeben, hieße eine Auskunft zu erfinden, nach der
    /// sich jemand richtet. Der Stichtag selbst zählt mit: An dem Tag gilt sie noch.
    /// </remarks>
    private static bool? Laeuft(DateOnly? bis, DateOnly stichtag)
        => bis is { } frist ? frist >= stichtag : null;
}
