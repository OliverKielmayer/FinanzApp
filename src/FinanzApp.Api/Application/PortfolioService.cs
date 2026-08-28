using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Depotwert und seine Positionen.
/// </summary>
/// <remarks>
/// <para>Er hat <b>eine</b> Quelle — v5-Handoff, Abschnitt 11.4. Liegen ausgeführte Orders vor,
/// entstehen die Positionen daraus und nicht aus gepflegten Zeilen: Stück ist die Summe der
/// Ausführungen, Einstand die Summe der Werte samt Gebühren, der Wert Stück mal letztem
/// belegbaren Kurs. Der Prototyp führte beides nebeneinander und wies dieselbe ISIN mit drei
/// Stückzahlen aus; der falsche Depotwert lief über das Finanzvermögen bis ins Gesamtvermögen
/// netto.</para>
/// <para>Ohne Ausführungen bleibt es bei den gepflegten Positionen — wer sein Depot von Hand
/// führt, soll nicht erst importieren müssen. Sobald welche da sind, gewinnen sie.</para>
/// </remarks>
public sealed class PortfolioService(FinanzAppDbContext db)
{
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

        var bestand = await GetHoldingsAsync(depot.Id, ct);

        var history = await db.PortfolioSnapshots.AsNoTracking()
            .OrderBy(s => s.Month)
            .Select(s => new TimeSeriesPointDto { Month = s.Month, Value = s.Value })
            .ToListAsync(ct);

        return new PortfolioDto
        {
            DepotId = depot.Id,
            DepotName = depot.Name,
            TotalValue = bestand.Value,
            Gain = bestand.Value - bestand.Cost,
            GainPercent = bestand.Cost == 0 ? 0 : ((bestand.Value / bestand.Cost) - 1) * 100m,
            TwrorPercent = depot.TwrorPercent,
            PricesAsOf = bestand.PricedAt,
            PricesFromTrades = bestand.FromTrades,
            History = history,
            Positions = bestand.Positions,
        };
    }

    /// <summary>
    /// Der Bestand eines Depots — die eine Quelle für alle, die ihn brauchen.
    /// </summary>
    /// <remarks>
    /// Depot-Hero, Bestand-Zeile, Vermögensaufstellung und der Bericht „Depot G/V“ lesen hier.
    /// Vorher fragte jede Ansicht selbst die Positionstabelle ab — und als die Ausführungen
    /// dazukamen, zeigten drei Stellen drei Zahlen für dasselbe Depot.
    /// </remarks>
    public async Task<DepotHoldings> GetHoldingsAsync(int depotId, CancellationToken ct = default)
    {
        var trades = await TradesAsync(depotId, ct);

        if (trades.Count > 0)
        {
            var bestand = Holdings(trades).Where(x => x.Value.Quantity > 0m).ToList();
            var abgeleitet = FromTrades(trades);

            return new DepotHoldings(
                abgeleitet,
                abgeleitet.Sum(p => p.Value),
                abgeleitet.Sum(p => p.CostBasis),
                Oldest(bestand.Select(x => x.Value.PricedAt)),
                FromTrades: true);
        }

        var gepflegt = await db.PortfolioPositions.AsNoTracking()
            .Where(p => p.DepotId == depotId)
            .ToListAsync(ct);

        var zeilen = FromRows(gepflegt);

        return new DepotHoldings(
            zeilen,
            zeilen.Sum(p => p.Value),
            zeilen.Sum(p => p.CostBasis),
            Oldest(gepflegt.Select(p => p.PriceAsOf)),
            FromTrades: false);
    }

    /// <summary>Depotwert für die Vermögensaufstellung — dieselbe Zahl wie im Depot-Hero.</summary>
    public async Task<decimal> GetTotalValueAsync(CancellationToken ct = default)
    {
        var depots = await db.Depots.AsNoTracking().Select(d => d.Id).ToListAsync(ct);
        var summe = 0m;

        foreach (var id in depots)
        {
            summe += (await GetHoldingsAsync(id, ct)).Value;
        }

        return summe;
    }

    // ── Aus den Ausführungen ───────────────────────────────────────────────────────────────

    private Task<List<DepotTrade>> TradesAsync(int depotId, CancellationToken ct)
        => db.DepotTrades.AsNoTracking()
            .Where(t => t.DepotId == depotId)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

    private sealed record Holding(
        string Name, decimal Quantity, decimal Cost, decimal Price, DateTime PricedAt);

    /// <summary>
    /// Je Wertpapier der Bestand aus seinen Ausführungen.
    /// </summary>
    /// <remarks>
    /// Verkäufe mindern den Einstand anteilig zum durchschnittlichen Anschaffungspreis. Sie zum
    /// Verkaufskurs abzuziehen verschöbe den Einstand des Rests und damit jeden Gewinn danach.
    /// </remarks>
    private static Dictionary<string, Holding> Holdings(List<DepotTrade> trades)
    {
        var bestand = new Dictionary<string, Holding>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in trades)
        {
            var vorher = bestand.TryGetValue(t.Isin, out var h)
                ? h
                : new Holding(t.SecurityName, 0m, 0m, t.Price, t.ExecutedAt);

            if (t.Kind == DepotTradeKind.Buy)
            {
                bestand[t.Isin] = vorher with
                {
                    Name = t.SecurityName,
                    Quantity = vorher.Quantity + t.Quantity,
                    Cost = vorher.Cost + t.Value + t.Fee,
                    Price = t.Price,
                    PricedAt = t.ExecutedAt,
                };

                continue;
            }

            var menge = Math.Min(t.Quantity, vorher.Quantity);
            var anteil = vorher.Quantity == 0m ? 0m : menge / vorher.Quantity;

            bestand[t.Isin] = vorher with
            {
                Quantity = vorher.Quantity - menge,
                Cost = vorher.Cost - (vorher.Cost * anteil),
                Price = t.Price,
                PricedAt = t.ExecutedAt,
            };
        }

        return bestand;
    }

    private static List<PositionDto> FromTrades(List<DepotTrade> trades)
        => [.. Holdings(trades)
            .Where(x => x.Value.Quantity > 0m)
            .Select(x => new PositionDto
            {
                // Abgeleitete Positionen haben keine eigene Zeile in der Ablage. Eine erfundene
                // Id führte auf einen Bearbeiten-Pfad, hinter dem nichts steht.
                Id = 0,
                Name = x.Value.Name,
                Isin = x.Key,
                Quantity = x.Value.Quantity,
                Price = x.Value.Price,
                Value = x.Value.Quantity * x.Value.Price,
                CostBasis = x.Value.Cost,
                GainPercent = x.Value.Cost == 0m
                    ? 0m
                    : ((x.Value.Quantity * x.Value.Price / x.Value.Cost) - 1m) * 100m,
            })
            .OrderByDescending(p => p.Value)];

    /// <summary>
    /// Der älteste Kursstichtag, nicht der jüngste.
    /// </summary>
    /// <remarks>
    /// Der Wert eines Depots ist eine Summe, und eine Summe ist nur so frisch wie ihr ältester
    /// Bestandteil. Den jüngsten zu nennen ließe eine Zahl aktueller aussehen, als sie ist.
    /// </remarks>
    private static DateTime Oldest(IEnumerable<DateTime> stichtage)
    {
        var alle = stichtage.ToList();

        return alle.Count == 0 ? DateTime.MinValue : alle.Min();
    }

    private static List<PositionDto> FromRows(IReadOnlyCollection<PortfolioPosition> rows)
        => [.. rows
            .OrderByDescending(p => p.Quantity * p.Price)
            .Select(p => new PositionDto
            {
                Id = p.Id,
                Name = p.Name,
                Isin = p.Isin,
                Quantity = p.Quantity,
                Price = p.Price,
                Value = p.Quantity * p.Price,
                CostBasis = p.CostBasis,
                GainPercent = p.CostBasis == 0 ? 0 : ((p.Quantity * p.Price / p.CostBasis) - 1) * 100m,
            })];

}

/// <summary>
/// Der Bestand eines Depots, aus einer Quelle.
/// </summary>
/// <remarks>
/// <paramref name="PricedAt"/> ist bei abgeleiteten Positionen der Zeitpunkt der letzten
/// Ausführung — ein belegbarer Kurs, kein Live-Kurs. <paramref name="FromTrades"/> sagt, welcher
/// Fall vorliegt, damit die Anzeige es dazuschreiben kann.
/// </remarks>
public sealed record DepotHoldings(
    IReadOnlyList<PositionDto> Positions,
    decimal Value,
    decimal Cost,
    DateTime PricedAt,
    bool FromTrades);
