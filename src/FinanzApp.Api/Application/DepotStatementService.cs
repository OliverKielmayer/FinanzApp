using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Quartalsaufstellungen und der Bestandsabgleich — v5-Handoff, Abschnitte 11.2 und 11.3.
/// </summary>
/// <remarks>
/// <para>Der Abgleich ist die eigentliche Analyse: er summiert die importierten Ausführungen
/// <b>bis zum Stichtag</b> und stellt sie dem ausgewiesenen Bestand gegenüber. Stimmen die
/// Stückzahlen, ist der Depotwert belegt — der einzige Weg, eine Depotbewertung zu prüfen, ohne
/// dem Broker blind zu glauben.</para>
/// <para>Verglichen wird je Wertpapier. Stückzahlen verschiedener Papiere zu addieren ergäbe
/// eine Zahl, die nichts bedeutet — „321 Stück“ stimmt nur, solange es ein Papier ist.</para>
/// </remarks>
public sealed class DepotStatementService(FinanzAppDbContext db)
{
    public async Task<DepotStatementsDto> GetAsync(int depotId, CancellationToken ct = default)
    {
        var aufstellungen = await db.DepotStatements.AsNoTracking()
            .Include(s => s.Positions)
            .Include(s => s.Document)
            .Where(s => s.DepotId == depotId)
            .OrderByDescending(s => s.AsOf)
            .ToListAsync(ct);

        return new DepotStatementsDto
        {
            Statements = [.. aufstellungen.Select(Row)],

            // Die jüngste: sie sagt etwas über heute. Eine ältere abzugleichen ist auch möglich,
            // aber der Block über der Liste beantwortet die Frage „stimmt mein Depot jetzt“.
            Reconciliation = aufstellungen.Count == 0
                ? null
                : await ReconcileAsync(depotId, aufstellungen[0], ct),
        };
    }

    /// <summary>
    /// Stellt eine Aufstellung den Ausführungen bis zu ihrem Stichtag gegenüber.
    /// </summary>
    /// <remarks>
    /// „Bis zum Stichtag“ heißt einschließlich: eine Ausführung am Stichtag selbst steht in
    /// diesem Bestand. Wer sie ausließe, fände jeden Quartalswechsel eine Differenz.
    /// </remarks>
    public async Task<DepotReconciliationDto> ReconcileAsync(
        int depotId, DepotStatement aufstellung, CancellationToken ct = default)
    {
        var grenze = aufstellung.AsOf.ToDateTime(TimeOnly.MaxValue);

        var ausfuehrungen = await db.DepotTrades.AsNoTracking()
            .Where(t => t.DepotId == depotId && t.ExecutedAt <= grenze)
            .OrderBy(t => t.ExecutedAt)
            .ToListAsync(ct);

        var ausOrders = Aggregate(ausfuehrungen);

        var isins = aufstellung.Positions.Select(p => p.Isin)
            .Concat(ausOrders.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var zeilen = isins.Select(isin =>
        {
            var ausweis = aufstellung.Positions
                .Where(p => string.Equals(p.Isin, isin, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var gehandelt = ausOrders.TryGetValue(isin, out var h) ? h : new Bestand(0m, 0m);

            return new ReconciliationRowDto
            {
                SecurityName = ausweis.FirstOrDefault()?.SecurityName
                               ?? ausfuehrungen.First(t => string.Equals(
                                   t.Isin, isin, StringComparison.OrdinalIgnoreCase)).SecurityName,
                Isin = isin,
                StatementQuantity = ausweis.Sum(p => p.Quantity),
                TradeQuantity = gehandelt.Quantity,
                TradeCost = gehandelt.Cost,
                StatementValue = ausweis.Sum(p => p.Value),
            };
        }).ToList();

        return new DepotReconciliationDto
        {
            AsOf = aufstellung.AsOf,
            StatementValue = aufstellung.Positions.Sum(p => p.Value),
            TradeCost = zeilen.Sum(z => z.TradeCost),
            Matches = zeilen.All(z => z.Difference == 0m),
            Rows = zeilen,
            TradeCount = ausfuehrungen.Count,
        };
    }

    private sealed record Bestand(decimal Quantity, decimal Cost);

    /// <summary>
    /// Stück und Anschaffungskosten je Wertpapier aus den Ausführungen.
    /// </summary>
    /// <remarks>
    /// Dieselbe Rechnung wie im Depotdienst: Verkäufe mindern den Einstand anteilig zum
    /// durchschnittlichen Anschaffungspreis. Sie hier anders zu rechnen hieße, dass der Abgleich
    /// gegen etwas anderes prüft als das, was der Depotwert behauptet.
    /// </remarks>
    private static Dictionary<string, Bestand> Aggregate(List<DepotTrade> trades)
    {
        var bestand = new Dictionary<string, Bestand>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in trades)
        {
            var vorher = bestand.TryGetValue(t.Isin, out var b) ? b : new Bestand(0m, 0m);

            if (t.Kind == DepotTradeKind.Buy)
            {
                bestand[t.Isin] = new Bestand(
                    vorher.Quantity + t.Quantity, vorher.Cost + t.Value + t.Fee);

                continue;
            }

            var menge = Math.Min(t.Quantity, vorher.Quantity);
            var anteil = vorher.Quantity == 0m ? 0m : menge / vorher.Quantity;

            bestand[t.Isin] = new Bestand(
                vorher.Quantity - menge, vorher.Cost - (vorher.Cost * anteil));
        }

        return bestand;
    }

    // ── Erfassen ───────────────────────────────────────────────────────────────────────────

    public async Task<DepotStatementDto> CreateAsync(
        int depotId, CreateDepotStatementRequest request, CancellationToken ct = default)
    {
        var depot = await db.Depots.FirstOrDefaultAsync(d => d.Id == depotId, ct)
                    ?? throw new RuleViolationException("Dieses Depot gibt es nicht.");

        if (request.Positions.Count == 0)
        {
            throw new RuleViolationException(
                "Eine Aufstellung ohne Position sagt nichts — mindestens ein Wertpapier.");
        }

        if (await db.DepotStatements.AnyAsync(
                s => s.DepotId == depot.Id && s.AsOf == request.AsOf, ct))
        {
            throw new RuleViolationException(
                $"Zum {GermanFormat.Date(request.AsOf)} gibt es schon eine Aufstellung.");
        }

        // Ein Schreiben kann nicht vor dem Bestand entstehen, den es ausweist.
        if (request.IssuedOn is { } erstellt && erstellt < request.AsOf)
        {
            throw new RuleViolationException(
                "Das Erstellungsdatum liegt vor dem Stichtag.");
        }

        var aufstellung = new DepotStatement
        {
            DepotId = depot.Id,
            AsOf = request.AsOf,
            IssuedOn = request.IssuedOn,
            DepotNumber = request.DepotNumber?.Trim(),
            Reference = request.Reference?.Trim(),
            Custodian = request.Custodian?.Trim(),
            DocumentId = request.DocumentId,
            Positions = [.. request.Positions.Select(p => new DepotStatementPosition
            {
                SecurityName = p.SecurityName.Trim(),
                Isin = p.Isin.Trim().ToUpperInvariant(),
                Wkn = p.Wkn?.Trim(),
                Quantity = p.Quantity,
                Price = p.Price,

                // Der Kurswert des Schreibens, nicht unsere Nachrechnung davon — die Bank
                // rundet auf ihre Weise, und abgeglichen wird gegen das, was dasteht.
                Value = p.Value ?? decimal.Round(p.Quantity * p.Price, 2),
                SafeCustody = p.SafeCustody?.Trim(),
                Country = p.Country?.Trim(),
                Depository = p.Depository?.Trim(),
            })],
        };

        db.DepotStatements.Add(aufstellung);
        await db.SaveChangesAsync(ct);

        return Row(aufstellung);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var aufstellung = await db.DepotStatements.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (aufstellung is null)
        {
            return false;
        }

        db.DepotStatements.Remove(aufstellung);
        await db.SaveChangesAsync(ct);

        return true;
    }

    private static DepotStatementDto Row(DepotStatement s) => new()
    {
        Id = s.Id,
        AsOf = s.AsOf,
        IssuedOn = s.IssuedOn,
        DepotNumber = s.DepotNumber,
        Reference = s.Reference,
        Custodian = s.Custodian,
        DocumentId = s.DocumentId,
        DocumentTitle = s.Document?.Title,
        Value = s.Positions.Sum(p => p.Value),
        Positions = [.. s.Positions.OrderBy(p => p.SecurityName).Select(p => new DepotStatementPositionDto
        {
            Id = p.Id,
            SecurityName = p.SecurityName,
            Isin = p.Isin,
            Wkn = p.Wkn,
            Quantity = p.Quantity,
            Price = p.Price,
            Value = p.Value,
            SafeCustody = p.SafeCustody,
            Country = p.Country,
            Depository = p.Depository,
        })],
    };
}
