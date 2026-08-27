using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Auswertungen — Abschnitt 10b.
/// </summary>
/// <remarks>
/// <para>Die Aggregation liegt hier und nicht im Client. Der Client zeigt nur; er rechnet nichts
/// nach. Sonst gäbe es zwei Rechnungen für dieselbe Zahl, und die dritte Regel des Handoffs
/// berichtet, wie das ausgeht: zwei Angaben über dieselbe Menge, die verschieden zählen.</para>
/// <para>Der <b>Ausschluss einzelner Buchungen</b> kommt als Parameter herein und geht nirgends
/// in den Bestand: er ist eine Eigenschaft der Auswertung, keine der Buchung.</para>
/// <para>Die Kontosichtbarkeit wirkt vor jeder Aggregation, weil sie als Abfragefilter im
/// <c>DbContext</c> sitzt. Hier steht deshalb <b>kein</b> <c>IgnoreQueryFilters</c> — ein Bericht
/// darf keine Beträge aus nicht freigegebenen Konten enthalten, auch nicht summiert.</para>
/// </remarks>
public sealed class ReportService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>Ab hier gilt eine Kategorie als steigend oder sinkend.</summary>
    private const decimal Threshold = 5m;

    /// <summary>So viele Monate zeigt die Sparkline.</summary>
    private const int SparkMonths = 24;

    /// <summary>So viele Namen nennt die Riser-Zeile.</summary>
    private const int NamedRisers = 3;

    /// <summary>Ein Satz aus der Datenbank, auf das Nötige verkürzt.</summary>
    private sealed record Entry(int Id, DateOnly Day, int CategoryId, decimal Amount);

    public async Task<CostTrendDto> GetCostTrendAsync(
        CostTrendRequest request, CancellationToken ct = default)
    {
        var fenster = await ResolveAsync(request.Period, request.Comparison, ct);
        var excluded = request.ExcludedTransactionIds?.ToHashSet() ?? [];

        // Ein Zug für alles: Zeitraum, Vergleichszeitraum, Zwölfmonatsmittel und Sparkline lesen
        // dieselben Sätze, nur über verschiedene Ausschnitte. Vier Abfragen für vier Ausschnitte
        // derselben Menge wären vier Gelegenheiten, sie verschieden zu filtern.
        var alle = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId != null
                        && t.BookingDate >= fenster.WindowFrom
                        && t.BookingDate <= fenster.WindowTo)
            .Select(t => new Entry(t.Id, t.BookingDate, t.CategoryId!.Value, t.Amount))
            .ToListAsync(ct);

        var gezaehlt = alle.Where(e => !excluded.Contains(e.Id)).ToList();

        var namen = await db.Categories.AsNoTracking()
            .Where(c => c.Direction == CategoryDirection.Expense)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var budgets = (await db.Budgets.AsNoTracking().Select(b => b.CategoryId).ToListAsync(ct))
            .ToHashSet();

        var zeilen = new List<CostTrendRowDto>();

        foreach (var gruppe in gezaehlt
                     .Where(e => e.Day >= fenster.From && e.Day <= fenster.To)
                     .GroupBy(e => e.CategoryId))
        {
            if (!namen.TryGetValue(gruppe.Key, out var name))
            {
                // Eine Einnahmekategorie an einer Ausgabe: nicht vorgesehen, aber auch kein
                // Grund, den ganzen Bericht zu verweigern.
                continue;
            }

            var betrag = Sum(gruppe);
            var mittel = Average(gezaehlt, gruppe.Key, fenster.AverageFrom, fenster.AverageTo);

            var vergleich = fenster.Comparison == ComparisonBasis.TwelveMonthAverage
                ? mittel * fenster.Months
                : Sum(gezaehlt.Where(e => e.CategoryId == gruppe.Key
                                          && e.Day >= fenster.ComparisonFrom
                                          && e.Day <= fenster.ComparisonTo));

            var prozent = Change(betrag, vergleich);
            var imZeitraum = alle.Count(e => e.CategoryId == gruppe.Key
                                             && e.Day >= fenster.From && e.Day <= fenster.To);

            zeilen.Add(new CostTrendRowDto
            {
                CategoryId = gruppe.Key,
                Name = name,
                Amount = betrag,
                ComparisonAmount = vergleich,
                TwelveMonthAverage = mittel,
                ChangePercent = prozent,
                Status = Status(fenster.HasComparison, betrag, prozent),
                Spark = Spark(gezaehlt, gruppe.Key, fenster.SparkFrom),
                HasBudget = budgets.Contains(gruppe.Key),
                TransactionCount = imZeitraum,
                ExcludedCount = imZeitraum - gruppe.Count(),
                Payees = [],
                Entries = [],
            });
        }

        var sortiert = Sort(zeilen, request.Sort);

        if (request.OpenCategoryId is { } offen)
        {
            sortiert = await WithDrilldownAsync(sortiert, offen, fenster, excluded, ct);
        }

        var summe = sortiert.Sum(z => z.Amount);
        var steigend = sortiert.Where(z => z.Status == CostTrendStatus.Rising).ToList();
        var ohne = await UncategorisedAsync(fenster, excluded, ct);

        return new CostTrendDto
        {
            Range = await RangeAsync(fenster, summe, ct),
            Total = summe,
            ComparisonTotal = sortiert.Sum(z => z.ComparisonAmount),
            ChangePercent = Change(summe, sortiert.Sum(z => z.ComparisonAmount)),
            RisingCount = steigend.Count,
            RisingLine = RisingLine(fenster, steigend),
            ExcludedCount = sortiert.Sum(z => z.ExcludedCount),
            UncategorisedCount = ohne.Count,
            UncategorisedAmount = ohne.Amount,
            Rows = sortiert,
        };
    }

    // ── Zeitraum und Vergleichszeitraum ────────────────────────────────────────────────────

    /// <summary>Alle Grenzen, die ein Bericht braucht.</summary>
    /// <param name="To">
    /// Das Ende des Zeitraums, <b>gekappt auf heute</b>. Ohne die Kappung hielte der
    /// Jahreszeitraum acht gelaufene Monate gegen zwölf des Vorjahres, und jede Kategorie sänke.
    /// </param>
    private sealed record Window(
        PeriodScope Period, ComparisonBasis Comparison,
        DateOnly From, DateOnly To, int Months, string Label,
        DateOnly ComparisonFrom, DateOnly ComparisonTo, string ComparisonLabel,
        DateOnly AverageFrom, DateOnly AverageTo, DateOnly SparkFrom,
        DateOnly WindowFrom, DateOnly WindowTo, bool Truncated, bool HasComparison);

    private async Task<Window> ResolveAsync(
        PeriodScope period, ComparisonBasis comparison, CancellationToken ct)
    {
        var heute = clock.Today;
        var (from, to, months, label) = Periods.Resolve(period, heute);

        // Ein laufender Zeitraum reicht nur bis heute, und der Vergleich muss denselben
        // Ausschnitt treffen — sonst misst er die Länge und nicht die Kosten.
        var truncated = to > heute;
        var bis = truncated ? heute : to;

        // Die gelaufenen Monate, nicht die nominellen. Ein Quartal, von dem sechs Wochen um
        // sind, durch drei zu teilen ergaebe eine Monatsbasis, die es nie gab — und dieselbe
        // Zahl haelt beim Zwoelfmonatsmittel den Vergleich gerade.
        var monate = ((bis.Year - from.Year) * 12) + bis.Month - from.Month + 1;

        var (vonV, bisV, labelV) = comparison switch
        {
            ComparisonBasis.PreviousPeriod
                => (from.AddMonths(-months), bis.AddMonths(-months),
                    Periods.Resolve(period, from.AddDays(-1)).Label),

            ComparisonBasis.PreviousYear
                => (from.AddYears(-1), bis.AddYears(-1),
                    Periods.Resolve(period, from.AddYears(-1)).Label),

            _ => (Periods.FirstOfMonth(from).AddMonths(-12), from.AddDays(-1), "Ø 12 Monate"),
        };

        // Das Mittel nimmt die zwölf Monate vor dem Zeitraum. Ihn selbst mitzurechnen hieße,
        // ihn gegen sich selbst zu halten.
        var mittelVon = Periods.FirstOfMonth(from).AddMonths(-12);
        var mittelBis = Periods.FirstOfMonth(from).AddDays(-1);

        var sparkVon = Periods.FirstOfMonth(bis).AddMonths(-(SparkMonths - 1));

        // Liegt im Vergleichsfenster überhaupt etwas? Gefragt wird nach Buchungen jeder Art:
        // „damals nichts ausgegeben“ und „damals die Anwendung noch nicht benutzt“ sind
        // verschiedene Auskünfte, und nur die zweite verbietet einen Trend.
        var hat = await db.Transactions.AsNoTracking()
            .AnyAsync(t => t.BookingDate >= vonV && t.BookingDate <= bisV, ct);

        return new Window(
            period, comparison, from, bis, monate, label,
            vonV, bisV, labelV,
            mittelVon, mittelBis, sparkVon,
            new[] { sparkVon, vonV, mittelVon }.Min(), bis, truncated, hat);
    }

    private async Task<ReportRangeDto> RangeAsync(Window fenster, decimal summe, CancellationToken ct)
    {
        var konten = await db.Accounts.AsNoTracking().CountAsync(ct);

        var zeitraum = fenster.Truncated
            ? $"{fenster.Label} (bis {GermanFormat.Date(fenster.To)})"
            : fenster.Label;

        var gegen = fenster.Comparison == ComparisonBasis.TwelveMonthAverage || !fenster.Truncated
            ? fenster.ComparisonLabel
            : $"{fenster.ComparisonLabel} (bis {GermanFormat.Date(fenster.ComparisonTo)})";

        return new ReportRangeDto
        {
            Period = fenster.Period,
            Comparison = fenster.Comparison,
            From = fenster.From,
            To = fenster.To,
            Months = fenster.Months,
            PeriodLabel = zeitraum,
            ComparisonLabel = gegen,
            Line = $"{zeitraum} gegen {gegen} · {konten} "
                   + (konten == 1 ? "sichtbares Konto" : "sichtbare Konten"),
            VisibleAccountCount = konten,
            HasComparison = fenster.HasComparison,
            MonthlyExpenseBase = fenster.Months == 0 ? 0m : Math.Round(summe / fenster.Months, 2),
        };
    }

    // ── Rechnen ────────────────────────────────────────────────────────────────────────────

    private static decimal Sum(IEnumerable<Entry> entries) => Math.Abs(entries.Sum(e => e.Amount));

    /// <summary>Monatsmittel einer Kategorie über ein Fenster.</summary>
    private static decimal Average(List<Entry> entries, int categoryId, DateOnly from, DateOnly to)
    {
        var monate = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        if (monate <= 0)
        {
            return 0m;
        }

        return Math.Round(
            Sum(entries.Where(e => e.CategoryId == categoryId && e.Day >= from && e.Day <= to))
            / monate, 2);
    }

    /// <summary>
    /// Prozentuale Änderung, oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Ohne Vergleichswert gibt es keinen Prozentsatz. „+100 %“ wäre eine Zahl, die niemand
    /// gerechnet hat, und „0 %“ eine Behauptung von Gleichstand.
    /// </remarks>
    private static decimal? Change(decimal now, decimal before)
        => before == 0m ? null : Math.Round((now / before - 1m) * 100m, 1);

    private static CostTrendStatus Status(bool hasComparison, decimal now, decimal? change)
    {
        if (!hasComparison)
        {
            return CostTrendStatus.Unknown;
        }

        if (change is null)
        {
            // Das Vergleichsfenster hat Daten, diese Kategorie darin aber keine: sie ist neu.
            return now > 0m ? CostTrendStatus.Rising : CostTrendStatus.Stable;
        }

        return change >= Threshold ? CostTrendStatus.Rising
            : change <= -Threshold ? CostTrendStatus.Falling
            : CostTrendStatus.Stable;
    }

    /// <summary>24 Monatssummen, älteste zuerst — auch die leeren, sonst verrutscht die Kurve.</summary>
    private static List<decimal> Spark(List<Entry> entries, int categoryId, DateOnly from)
    {
        var werte = new List<decimal>(SparkMonths);

        for (var i = 0; i < SparkMonths; i++)
        {
            var monat = from.AddMonths(i);
            werte.Add(Sum(entries.Where(e => e.CategoryId == categoryId
                                             && e.Day.Year == monat.Year
                                             && e.Day.Month == monat.Month)));
        }

        return werte;
    }

    private static List<CostTrendRowDto> Sort(List<CostTrendRowDto> rows, CostTrendSort sort)
        => sort switch
        {
            // Ohne Vergleich gibt es keinen Anstieg — solche Zeilen stehen hinten, nicht oben.
            CostTrendSort.Increase =>
            [
                .. rows.OrderByDescending(r => r.ChangePercent.HasValue)
                    .ThenByDescending(r => r.ChangePercent ?? 0m)
                    .ThenByDescending(r => r.Amount),
            ],

            CostTrendSort.Amount => [.. rows.OrderByDescending(r => r.Amount)],

            _ => [.. rows.OrderBy(r => r.Name, StringComparer.CurrentCulture)],
        };

    private static string RisingLine(Window fenster, List<CostTrendRowDto> rising)
    {
        if (!fenster.HasComparison)
        {
            return $"Für {fenster.ComparisonLabel} liegen keine Buchungen vor — "
                   + "ohne Vergleichszeitraum kein Trend.";
        }

        if (rising.Count == 0)
        {
            return $"Keine Kategorie steigt um mehr als {Threshold:0} %.";
        }

        // Eine neue Kategorie hat keinen Prozentsatz und steht trotzdem vorn: sie ist der
        // stärkste denkbare Anstieg.
        var namen = rising
            .OrderByDescending(r => r.ChangePercent ?? decimal.MaxValue)
            .Take(NamedRisers)
            .Select(r => r.Name);

        return $"{rising.Count} "
               + (rising.Count == 1 ? "Kategorie steigt" : "Kategorien steigen")
               + $" um mehr als {Threshold:0} % — {string.Join(", ", namen)}";
    }

    // ── Drilldown ──────────────────────────────────────────────────────────────────────────

    private async Task<List<CostTrendRowDto>> WithDrilldownAsync(
        List<CostTrendRowDto> rows, int categoryId, Window fenster,
        HashSet<int> excluded, CancellationToken ct)
    {
        var index = rows.FindIndex(r => r.CategoryId == categoryId);
        if (index < 0)
        {
            return rows;
        }

        var saetze = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId == categoryId
                        && t.BookingDate >= fenster.From
                        && t.BookingDate <= fenster.To)
            .OrderByDescending(t => t.BookingDate).ThenBy(t => t.Id)
            .Select(t => new CostTrendEntryDto(
                t.Id, t.BookingDate, t.Payee, t.Amount, t.Account!.Name, false))
            .ToListAsync(ct);

        var mitFlagge = saetze
            .Select(e => e with { Excluded = excluded.Contains(e.Id) })
            .ToList();

        var empfaenger = mitFlagge
            .Where(e => !e.Excluded)
            .GroupBy(e => e.Payee, StringComparer.CurrentCulture)
            .Select(g => new CostTrendPayeeDto(g.Key, g.Count(), Math.Abs(g.Sum(e => e.Amount))))
            .OrderByDescending(p => p.Amount)
            .ToList();

        rows[index] = rows[index] with { Payees = empfaenger, Entries = mitFlagge };

        return rows;
    }

    private async Task<(int Count, decimal Amount)> UncategorisedAsync(
        Window fenster, HashSet<int> excluded, CancellationToken ct)
    {
        var offen = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId == null
                        && t.BookingDate >= fenster.From
                        && t.BookingDate <= fenster.To)
            .Select(t => new { t.Id, t.Amount })
            .ToListAsync(ct);

        var zaehlt = offen.Where(t => !excluded.Contains(t.Id)).ToList();

        return (zaehlt.Count, Math.Abs(zaehlt.Sum(t => t.Amount)));
    }
}
