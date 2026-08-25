namespace FinanzApp.Shared.Contracts;

/// <summary>Kennzahlen für die Sammelseite „Mehr“.</summary>
public sealed record MoreOverviewDto
{
    public required PensionSummaryDto Pension { get; init; }
    public required LoanSummaryDto Loan { get; init; }
    public required ImportSummaryDto Import { get; init; }
    public required int CategoryCount { get; init; }
    public required int RuleCount { get; init; }

    /// <summary>Benutzer im Haushalt — Untertitel der Zeile „Benutzer &amp; Anmeldung“.</summary>
    public required int HouseholdMemberCount { get; init; }

    /// <summary>Kennzahlen der Bereiche, die mit der Erweiterung dazugekommen sind.</summary>
    public required AreaCountsDto Areas { get; init; }
    public required SecuritySummaryDto Security { get; init; }
}

/// <summary>Rechte Spalte der Bereichsliste auf „Mehr“.</summary>
public sealed record AreaCountsDto
{
    public required int DocumentCount { get; init; }
    public required int MissingFileCount { get; init; }
    /// <summary>Kapitalbildende Verträge — Bereich „Vorsorge &amp; Kapital“.</summary>
    public required int PensionCount { get; init; }

    /// <summary>Verträge ohne Vermögenswert — Bereich „Absicherung“.</summary>
    public required int ProtectionCount { get; init; }
    public required int OpenMedicalBillCount { get; init; }
    public required int PropertyCount { get; init; }
    public required int ContractCount { get; init; }
    public required int OpenTaskCount { get; init; }
}

/// <summary>Kopfzahl der Vorsorge: Summe der erreichten Werte, immer mit Stichtag.</summary>
public sealed record PensionSummaryDto
{
    public required string Provider { get; init; }

    /// <summary>Summe der erreichten Werte — Rückkaufswert, Guthaben, Ansammlung.</summary>
    public required decimal TotalValue { get; init; }

    /// <summary>Ältester Stichtag. So alt ist die Summe mindestens.</summary>
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
