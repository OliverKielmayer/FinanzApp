using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class PortfolioService(FinanzAppDbContext db)
{
    /// <summary>
    /// Depotwert, Gewinn und Positionen. Alles gerechnet: Wert ist Stück mal letzter Kurs,
    /// Gewinn ist Wert minus Einstand. Der Kurszeitstempel gehört ins Ergebnis und bleibt in der
    /// Oberfläche sichtbar — die Kurse stammen aus einem austauschbaren Provider und veralten.
    /// </summary>
    public async Task<PortfolioDto?> GetAsync(CancellationToken ct = default)
    {
        var depot = await db.Depots.AsNoTracking()
            .Include(d => d.Positions)
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync(ct);

        if (depot is null)
        {
            return null;
        }

        var positions = depot.Positions
            .OrderByDescending(p => p.Quantity * p.Price)
            .Select(p =>
            {
                var value = p.Quantity * p.Price;
                return new PositionDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Isin = p.Isin,
                    Quantity = p.Quantity,
                    Price = p.Price,
                    Value = value,
                    GainPercent = p.CostBasis == 0 ? 0 : (value / p.CostBasis - 1) * 100m,
                };
            })
            .ToList();

        var totalValue = positions.Sum(p => p.Value);
        var totalCost = depot.Positions.Sum(p => p.CostBasis);

        var history = await db.PortfolioSnapshots.AsNoTracking()
            .OrderBy(s => s.Month)
            .Select(s => new TimeSeriesPointDto { Month = s.Month, Value = s.Value })
            .ToListAsync(ct);

        return new PortfolioDto
        {
            DepotName = depot.Name,
            TotalValue = totalValue,
            Gain = totalValue - totalCost,
            GainPercent = totalCost == 0 ? 0 : (totalValue / totalCost - 1) * 100m,
            TwrorPercent = depot.TwrorPercent,
            PricesAsOf = depot.Positions.Count == 0
                ? DateTime.MinValue
                : depot.Positions.Max(p => p.PriceAsOf),
            History = history,
            Positions = positions,
        };
    }

    /// <summary>Depotwert für die Vermögensaufstellung.</summary>
    public async Task<decimal> GetTotalValueAsync(CancellationToken ct = default)
        => (await db.PortfolioPositions.AsNoTracking()
                .Select(p => new { p.Quantity, p.Price })
                .ToListAsync(ct))
            .Sum(p => p.Quantity * p.Price);
}
