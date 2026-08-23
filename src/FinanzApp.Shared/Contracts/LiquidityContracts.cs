namespace FinanzApp.Shared.Contracts;

/// <summary>
/// „Was bleibt übrig?“ — der laufende Monat mit dem, was noch kommt.
/// </summary>
/// <remarks>
/// Rechnet ausschließlich auf vorhandenen Daten: Buchungen, Budgets, Rechnungen, PKV-Vorgänge,
/// Vertragsfristen. Keine neue Eingabe, keine neue Tabelle.
/// Eigenanteile zählen als Ausgabe, erstattete Beträge nicht. Umbuchungen zählen weiterhin weder
/// als Einnahme noch als Ausgabe.
/// </remarks>
public sealed record LiquidityDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required decimal Income { get; init; }
    public required decimal Expenses { get; init; }
    public decimal Remaining => Income - Expenses;
    public required decimal SavingsRatePercent { get; init; }

    /// <summary>Bekannt, aber noch nicht gebucht — offene Rechnungen und Eigenanteile.</summary>
    public required IReadOnlyList<PendingAmountDto> StillDue { get; init; }

    /// <summary>Erwartete Eingänge, vor allem PKV-Erstattungen.</summary>
    public required IReadOnlyList<PendingAmountDto> Expected { get; init; }

    public decimal StillDueTotal => StillDue.Sum(x => x.Amount);

    public decimal ExpectedTotal => Expected.Sum(x => x.Amount);

    /// <summary>Was nach den bekannten Fixkosten des Monats übrig bleibt.</summary>
    public required decimal AvailableAfterFixedCosts { get; init; }

    /// <summary>Letzter Tag des betrachteten Monats.</summary>
    public required DateOnly PeriodEnd { get; init; }
}

public sealed record PendingAmountDto
{
    public required string Label { get; init; }
    public required decimal Amount { get; init; }
    public DateOnly? DueOn { get; init; }
    public LinkTargetType? SourceType { get; init; }
    public int? SourceId { get; init; }
}

/// <summary>„Wohin fließt es?“ — Kategorien über einen längeren Zeitraum.</summary>
public sealed record CashFlowDto
{
    public required int Months { get; init; }
    public required decimal FixedShare { get; init; }
    public required decimal VariableShare { get; init; }
    public required IReadOnlyList<CashFlowCategoryDto> Categories { get; init; }

    public decimal Total => FixedShare + VariableShare;
}

public sealed record CashFlowCategoryDto
{
    public required int CategoryId { get; init; }
    public required string Name { get; init; }

    /// <summary>Summe im Zeitraum, positiv.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Anteil an den Gesamtausgaben in Prozent.</summary>
    public required decimal SharePercent { get; init; }

    /// <summary>Monatsbudget, falls eines besteht.</summary>
    public decimal? BudgetPerMonth { get; init; }

    /// <summary>Im Schnitt über Budget.</summary>
    public required bool OverBudget { get; init; }

    /// <summary>Erklärzeile, etwa „nur Eigenanteile“ oder „36 % über Budget“.</summary>
    public string? Note { get; init; }
}

/// <summary>„Wo ist der Hebel?“ — speist sich aus Budgets und Vertragsfristen.</summary>
public sealed record SavingsPotentialDto
{
    public required IReadOnlyList<SavingsItemDto> Items { get; init; }

    /// <summary>Summe der bezifferbaren Positionen je Monat.</summary>
    public required decimal TotalPerMonth { get; init; }

    /// <summary>Positionen, deren Wirkung sich nicht beziffern lässt und die im Summenwert fehlen.</summary>
    public required int UnquantifiedCount { get; init; }
}

public sealed record SavingsItemDto
{
    public required string Title { get; init; }
    public required string Detail { get; init; }

    /// <summary>
    /// Monatliches Potential, wenn es sich beziffern lässt. <c>null</c> heißt: hier liegt eine
    /// Gelegenheit, aber wie viel sie bringt, weiß die Anwendung nicht — etwa bei einem
    /// Anbieterwechsel. Eine geschätzte Ersparnis wäre eine erfundene Zahl.
    /// </summary>
    public decimal? AmountPerMonth { get; init; }

    /// <summary>Was die Position heute im Monat kostet.</summary>
    public required decimal CurrentCostPerMonth { get; init; }

    /// <summary>Dringend, weil eine Frist läuft oder ein Budget dauerhaft reißt.</summary>
    public required bool IsUrgent { get; init; }

    public LinkTargetType? SourceType { get; init; }
    public int? SourceId { get; init; }
}
