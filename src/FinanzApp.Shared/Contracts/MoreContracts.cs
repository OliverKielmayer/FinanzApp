namespace FinanzApp.Shared.Contracts;

/// <summary>Kennzahlen für die Sammelseite „Mehr“.</summary>
public sealed record MoreOverviewDto
{
    public required InsuranceSummaryDto Insurance { get; init; }
    public required LoanSummaryDto Loan { get; init; }
    public required ImportSummaryDto Import { get; init; }
    public required int CategoryCount { get; init; }
    public required int RuleCount { get; init; }

    /// <summary>Benutzer im Haushalt — Untertitel der Zeile „Benutzer &amp; Anmeldung“.</summary>
    public required int HouseholdMemberCount { get; init; }
    public required SecuritySummaryDto Security { get; init; }
}

public sealed record InsuranceSummaryDto
{
    public required string Provider { get; init; }

    /// <summary>Summe der Rückkaufswerte.</summary>
    public required decimal SurrenderValue { get; init; }

    public required DateOnly ValuationDate { get; init; }
}

public sealed record LoanSummaryDto
{
    public required int LoanId { get; init; }
    public required string Lender { get; init; }
    public required decimal Installment { get; init; }

    /// <summary>Restschuld, positiv geführt.</summary>
    public required decimal RemainingDebt { get; init; }
}

public sealed record ImportSummaryDto
{
    public required int ProfileCount { get; init; }
    public required string Formats { get; init; }
}

public sealed record SecuritySummaryDto
{
    public required bool TwoFactorEnabled { get; init; }
    public required DateTime LastBackup { get; init; }
}
