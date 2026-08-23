namespace FinanzApp.Shared.Contracts;

/// <summary>Art einer Buchung. Umbuchungen sind bewusst eine eigene Art und dürfen in
/// Auswertungen weder als Einnahme noch als Ausgabe gezählt werden.</summary>
public enum TransactionKind
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
}

/// <summary>Für welche Buchungsrichtung eine Kategorie angeboten wird.</summary>
public enum CategoryDirection
{
    Expense = 0,
    Income = 1,
}

/// <summary>Zeitraum eines Budgets.</summary>
public enum BudgetPeriod
{
    Month = 0,
    Quarter = 1,
    Year = 2,
}
