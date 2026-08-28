using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Ausgeführte Orders: einlesen, auswerten, Positionen daraus ableiten — v5-Handoff, Abschnitt 11.
/// </summary>
/// <remarks>
/// <para>Der Depotwert hat <b>eine</b> Quelle. Liegen Ausführungen vor, gewinnen sie gegen jeden
/// gepflegten Wert und gegen jede von Hand erfasste Position: Stück ist die Summe der
/// Ausführungen, Einstand die Summe der Werte samt Gebühren, der aktuelle Wert Stück mal
/// letztem belegbaren Kurs.</para>
/// <para>Gerundet wird erst bei der Ausgabe. Der Prototyp rundete die Summe vor der
/// Differenzbildung und wich dadurch um einen Euro von der einzigen Position darunter ab.</para>
/// </remarks>
public sealed class DepotTradeService(FinanzAppDbContext db, OrderCsvParser parser)
{
    /// <summary>
    /// Liest eine Orderdatei ein und meldet, was dabei herauskam.
    /// </summary>
    /// <remarks>
    /// Wiedererkannt wird über Ausführungszeitpunkt, Stück und Kurs — die Datei führt keine
    /// Ordernummer. Bereits vorhandene Sätze werden gezählt und nicht erneut gebucht; wer
    /// dieselbe Datei zweimal einliest, verdoppelt sein Depot sonst lautlos.
    /// </remarks>
    public async Task<DepotImportResultDto> ImportAsync(
        int depotId, Stream content, string fileName, CancellationToken ct = default)
    {
        var depot = await db.Depots.FirstOrDefaultAsync(d => d.Id == depotId, ct)
                    ?? throw new RuleViolationException("Dieses Depot gibt es nicht.");

        var gelesen = await parser.ParseAsync(content, fileName, ct);

        var bekannt = await db.DepotTrades.AsNoTracking()
            .Where(t => t.DepotId == depot.Id)
            .Select(t => t.ImportReference)
            .ToListAsync(ct);

        var referenzen = bekannt.ToHashSet(StringComparer.Ordinal);

        var uebersprungen = new List<DepotImportSkipDto>();
        var doppelt = 0;
        var neu = 0;

        foreach (var satz in gelesen)
        {
            if (satz.Problem is { } grund)
            {
                uebersprungen.Add(new DepotImportSkipDto(satz.SecurityName, null, grund));
                continue;
            }

            // Auch innerhalb einer Datei: derselbe Satz zweimal bleibt einer.
            if (!referenzen.Add(satz.Reference))
            {
                doppelt++;
                continue;
            }

            db.DepotTrades.Add(new DepotTrade
            {
                DepotId = depot.Id,
                SecurityName = satz.SecurityName,
                Isin = satz.Isin,
                Wkn = satz.Wkn,
                Kind = satz.IsSell ? DepotTradeKind.Sell : DepotTradeKind.Buy,
                OrderType = satz.IsLimit ? DepotOrderType.Limit : DepotOrderType.Market,
                LimitPrice = satz.LimitPrice,
                ExecutedAt = satz.ExecutedAt,
                Quantity = satz.Quantity,
                Price = satz.Price,
                Value = satz.Value,
                Fee = satz.Fee,
                ImportReference = satz.Reference,
            });

            neu++;
        }

        await db.SaveChangesAsync(ct);

        return new DepotImportResultDto
        {
            FileName = fileName,
            ReadCount = gelesen.Count,
            ImportedCount = neu,
            DuplicateCount = doppelt,
            Skipped = uebersprungen,
        };
    }

    // ── Auswertung ─────────────────────────────────────────────────────────────────────────

    public async Task<DepotTradesDto> GetAsync(
        int depotId, int? year = null, CancellationToken ct = default)
    {
        var alle = await db.DepotTrades.AsNoTracking()
            .Where(t => t.DepotId == depotId)
            .OrderByDescending(t => t.ExecutedAt)
            .ToListAsync(ct);

        var gezeigt = year is { } jahr
            ? alle.Where(t => t.ExecutedAt.Year == jahr).ToList()
            : alle;

        var kopf = Head(alle);

        return new DepotTradesDto
        {
            Head = kopf,
            Years =
            [
                new(null, alle.Count, Menge(alle), alle.Sum(t => t.Value + t.Fee)),
                .. alle.Select(t => t.ExecutedAt.Year).Distinct().OrderByDescending(j => j)
                    .Select(j =>
                    {
                        var davon = alle.Where(t => t.ExecutedAt.Year == j).ToList();
                        return new DepotYearDto(j, davon.Count, Menge(davon), davon.Sum(t => t.Value + t.Fee));
                    }),
            ],
            Trades = [.. gezeigt.Select(Row)],

            // Der Anteil rechnet gegen die Anschaffungskosten aller Ausführungen, nicht gegen
            // die des Jahresausschnitts: sonst hätte jeder Filter seine eigenen Prozente.
            ShareOfCost = gezeigt.ToDictionary(
                t => t.Id,
                t => kopf.CostBasis == 0m ? 0m : (t.Value + t.Fee) / kopf.CostBasis),
        };
    }

    /// <summary>
    /// Die Kennzahlen über <em>alle</em> Ausführungen.
    /// </summary>
    /// <remarks>
    /// Verkäufe mindern den Einstand anteilig — zum durchschnittlichen Anschaffungspreis, nicht
    /// zum Verkaufskurs — und der Unterschied zum Verkaufserlös ist der realisierte Gewinn. Die
    /// heutige Datei enthält nur Käufe; das Modell muss beides tragen, sonst wird der erste
    /// Verkauf zu einer stillen Falschbuchung.
    /// </remarks>
    private static DepotTradesHeadDto Head(List<DepotTrade> alle)
    {
        var chronologisch = alle.OrderBy(t => t.ExecutedAt).ToList();

        var stueck = 0m;
        var einstand = 0m;
        var gebuehren = 0m;
        var realisiert = 0m;

        foreach (var satz in chronologisch)
        {
            if (satz.Kind == DepotTradeKind.Buy)
            {
                stueck += satz.Quantity;
                einstand += satz.Value + satz.Fee;
                gebuehren += satz.Fee;
                continue;
            }

            var anteil = stueck == 0m ? 0m : Math.Min(satz.Quantity, stueck) / stueck;
            var abgang = einstand * anteil;

            realisiert += satz.Value - satz.Fee - abgang;
            einstand -= abgang;
            stueck -= Math.Min(satz.Quantity, stueck);
        }

        var letzte = chronologisch.LastOrDefault();
        var kurs = letzte?.Price;
        var aktuell = stueck * (kurs ?? 0m);

        return new DepotTradesHeadDto
        {
            CostBasis = einstand,
            Fees = gebuehren,
            Quantity = stueck,
            ExecutionCount = alle.Count,
            BuyCount = alle.Count(t => t.Kind == DepotTradeKind.Buy),
            SellCount = alle.Count(t => t.Kind == DepotTradeKind.Sell),
            AverageCost = stueck == 0m ? null : einstand / stueck,
            LastPrice = kurs,
            LastPriceAt = letzte?.ExecutedAt,
            CurrentValue = aktuell,
            GainPercent = einstand == 0m ? null : (aktuell / einstand - 1m) * 100m,
            RealisedGain = realisiert,
        };
    }

    private static decimal Menge(List<DepotTrade> saetze)
        => saetze.Sum(t => t.Kind == DepotTradeKind.Buy ? t.Quantity : -t.Quantity);

    private static DepotTradeDto Row(DepotTrade t) => new()
    {
        Id = t.Id,
        SecurityName = t.SecurityName,
        Isin = t.Isin,
        Wkn = t.Wkn,
        Kind = t.Kind,
        OrderType = t.OrderType,
        LimitPrice = t.LimitPrice,
        ExecutedAt = t.ExecutedAt,
        Quantity = t.Quantity,
        Price = t.Price,
        Value = t.Value,
        Fee = t.Fee,
    };

    /// <summary>Wie viele Ausführungen ein Depot führt — die Frage „gibt es welche?“.</summary>
    public Task<int> CountAsync(int depotId, CancellationToken ct = default)
        => db.DepotTrades.CountAsync(t => t.DepotId == depotId, ct);
}
