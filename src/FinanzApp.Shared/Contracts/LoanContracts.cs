namespace FinanzApp.Shared.Contracts;

public sealed record LoanDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Lender { get; init; }

    /// <summary>Restschuld, positiv geführt.</summary>
    public required decimal RemainingDebt { get; init; }

    public required decimal InterestRatePercent { get; init; }
    public required decimal Installment { get; init; }
    public required DateOnly NextPaymentDate { get; init; }
    public required IReadOnlyList<AmortizationEntryDto> Schedule { get; init; }
}

public sealed record AmortizationEntryDto
{
    public required DateOnly Month { get; init; }
    public required decimal Interest { get; init; }
    public required decimal Principal { get; init; }
    public required decimal RemainingDebt { get; init; }
}
