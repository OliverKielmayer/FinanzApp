using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;

namespace FinanzApp.Api.Application;

/// <summary>
/// Zeitraumrechnung — Grenzen, Monatszahl und Beschriftung.
/// </summary>
/// <remarks>
/// Steht für sich, weil Budgets und Auswertungen dieselbe Rechnung brauchen. Zwei Fassungen
/// derselben Grenzen wären zwei Wahrheiten über denselben Monat, und der Handoff zu Abschnitt
/// 10b berichtet genau davon aus dem Prototypenbau.
/// </remarks>
public static class Periods
{
    /// <summary>Grenzen, Monatszahl für die Hochrechnung und Beschriftung.</summary>
    public static (DateOnly From, DateOnly To, int Months, string Label) Resolve(
        PeriodScope period, DateOnly today) => period switch
    {
        PeriodScope.Month => (
            new DateOnly(today.Year, today.Month, 1),
            new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)),
            1,
            GermanFormat.MonthName(today.Month) + " " + today.Year),

        PeriodScope.Quarter => QuarterOf(today),

        PeriodScope.Year => (
            new DateOnly(today.Year, 1, 1),
            new DateOnly(today.Year, 12, 31),
            12,
            today.Year.ToString()),

        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };

    /// <summary>Der Erste des Monats, in dem der Tag liegt.</summary>
    public static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    /// <summary>Der Letzte des Monats, in dem der Tag liegt.</summary>
    public static DateOnly LastOfMonth(DateOnly day)
        => new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    /// <summary>„August 2026“ — für einen Monat, der nicht der heutige ist.</summary>
    public static string MonthLabel(DateOnly day)
        => GermanFormat.MonthName(day.Month) + " " + day.Year;

    private static (DateOnly, DateOnly, int, string) QuarterOf(DateOnly today)
    {
        var quarter = (today.Month - 1) / 3 + 1;
        var firstMonth = (quarter - 1) * 3 + 1;
        var from = new DateOnly(today.Year, firstMonth, 1);
        var to = from.AddMonths(3).AddDays(-1);
        return (from, to, 3, "Q" + quarter + " " + today.Year);
    }
}
