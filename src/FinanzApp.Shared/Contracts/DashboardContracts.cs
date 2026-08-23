namespace FinanzApp.Shared.Contracts;

/// <summary>Alles, was der Startscreen „Vermögen“ in einem Zug braucht.</summary>
public sealed record DashboardDto
{
    public required NetWorthDto NetWorth { get; init; }
    public required IReadOnlyList<AssetSliceDto> Assets { get; init; }
    public required IReadOnlyList<TimeSeriesPointDto> History { get; init; }
    public required MonthKpiDto Month { get; init; }
    public required LiabilityDto Liability { get; init; }
    public required IReadOnlyList<BudgetDto> TopBudgets { get; init; }
}

public sealed record NetWorthDto
{
    /// <summary>Summe aller Vermögenswerte.</summary>
    public required decimal Gross { get; init; }

    /// <summary>Summe aller Verbindlichkeiten, positiv geführt.</summary>
    public required decimal Liabilities { get; init; }

    public decimal Net => Gross - Liabilities;

    /// <summary>Veränderung gegenüber dem Vormonat, vorzeichenbehaftet.</summary>
    public required decimal DeltaPreviousMonth { get; init; }

    /// <summary>Veränderung im laufenden Jahr in Prozent, vorzeichenbehaftet.</summary>
    public required decimal DeltaYearPercent { get; init; }
}

/// <summary>Eine Kachel der Vermögensaufteilung.</summary>
public sealed record AssetSliceDto
{
    public required string Label { get; init; }
    public required string Subtitle { get; init; }
    public required decimal Value { get; init; }

    /// <summary>Anteil am Bruttovermögen, 0…1.</summary>
    public required decimal ShareOfGross { get; init; }

    /// <summary>Zielroute für den Tap auf die Kachel.</summary>
    public required string Route { get; init; }
}

public sealed record TimeSeriesPointDto
{
    public required DateOnly Month { get; init; }
    public required decimal Value { get; init; }
}

/// <summary>Monatssummen. Umbuchungen sind hier nicht enthalten.</summary>
public sealed record MonthKpiDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required decimal Income { get; init; }
    public required decimal Expenses { get; init; }
    public required decimal SavingsRatePercent { get; init; }
}

public sealed record LiabilityDto
{
    public required int LoanId { get; init; }
    public required string Label { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Restschuld, positiv geführt.</summary>
    public required decimal Amount { get; init; }
}
