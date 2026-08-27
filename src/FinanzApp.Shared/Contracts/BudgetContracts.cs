namespace FinanzApp.Shared.Contracts;

public sealed record BudgetDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required decimal Planned { get; init; }
    public required decimal Spent { get; init; }

    /// <summary>Auslastung 0…n. Über 1 bedeutet Überschreitung.</summary>
    public decimal Ratio => Planned == 0 ? 0 : Spent / Planned;

    public bool IsOverspent => Spent > Planned;

    /// <summary>Positiv: verbleibend. Negativ: über Budget.</summary>
    public decimal Remaining => Planned - Spent;
}

public sealed record BudgetOverviewDto
{
    public required PeriodScope Period { get; init; }

    /// <summary>Bezeichnung des Zeitraums, z. B. „August 2026“.</summary>
    public required string PeriodLabel { get; init; }

    public required decimal Planned { get; init; }
    public required decimal Spent { get; init; }
    public decimal Remaining => Planned - Spent;
    public required int OverspentCount { get; init; }
    public required IReadOnlyList<BudgetDto> Items { get; init; }
}
