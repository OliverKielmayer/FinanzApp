using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class BudgetService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>
    /// Budgetauslastung für den gewählten Zeitraum. Der geplante Betrag ist je Monat hinterlegt
    /// und wird auf Quartal beziehungsweise Jahr hochgerechnet; verbraucht ist die Summe der
    /// Ausgaben der zugeordneten Kategorie im Zeitraum. Umbuchungen zählen nicht mit.
    /// </summary>
    public async Task<BudgetOverviewDto> GetOverviewAsync(
        BudgetPeriod period, CancellationToken ct = default)
    {
        var (from, to, months, label) = ResolvePeriod(period, clock.Today);

        var budgets = await db.Budgets.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync(ct);

        var spendByCategory = (await db.Transactions.AsNoTracking()
                .Where(t => t.Kind == TransactionKind.Expense
                            && t.CategoryId != null
                            && t.BookingDate >= from && t.BookingDate <= to)
                .Select(t => new { CategoryId = t.CategoryId!.Value, t.Amount })
                .ToListAsync(ct))
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(t => t.Amount)));

        var items = budgets
            .Select(b => new BudgetDto
            {
                Id = b.Id,
                Name = b.Name,
                Planned = b.PlannedPerMonth * months,
                Spent = spendByCategory.TryGetValue(b.CategoryId, out var spent) ? spent : 0m,
            })
            .ToList();

        return new BudgetOverviewDto
        {
            Period = period,
            PeriodLabel = label,
            Planned = items.Sum(i => i.Planned),
            Spent = items.Sum(i => i.Spent),
            OverspentCount = items.Count(i => i.IsOverspent),
            Items = items,
        };
    }

    /// <summary>Die ersten Budgets der Liste — das Dashboard zeigt davon drei.</summary>
    public async Task<IReadOnlyList<BudgetDto>> GetTopAsync(int count, CancellationToken ct = default)
        => [.. (await GetOverviewAsync(BudgetPeriod.Month, ct)).Items.Take(count)];

    /// <summary>Zeitraumgrenzen, Monatszahl für die Hochrechnung und Beschriftung.</summary>
    public static (DateOnly From, DateOnly To, int Months, string Label) ResolvePeriod(
        BudgetPeriod period, DateOnly today) => period switch
    {
        BudgetPeriod.Month => (
            new DateOnly(today.Year, today.Month, 1),
            new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            1,
            GermanFormat.MonthName(today.Month) + " " + today.Year),

        BudgetPeriod.Quarter => QuarterOf(today),

        BudgetPeriod.Year => (
            new DateOnly(today.Year, 1, 1),
            new DateOnly(today.Year, 12, 31),
            12,
            today.Year.ToString()),

        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    private static (DateOnly, DateOnly, int, string) QuarterOf(DateOnly today)
    {
        var quarter = (today.Month - 1) / 3 + 1;
        var firstMonth = (quarter - 1) * 3 + 1;
        var from = new DateOnly(today.Year, firstMonth, 1);
        var to = from.AddMonths(3).AddDays(-1);
        return (from, to, 3, "Q" + quarter + " " + today.Year);
    }
}
