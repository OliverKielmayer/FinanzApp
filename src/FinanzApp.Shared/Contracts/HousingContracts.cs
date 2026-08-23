namespace FinanzApp.Shared.Contracts;

// ── Versicherungen ─────────────────────────────────────────────────────────────────────────

public sealed record InsuranceListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Insurer { get; init; }
    public required decimal Premium { get; init; }
    public required PremiumInterval PremiumInterval { get; init; }
    public DateOnly? EndsOn { get; init; }
    public DateOnly? NoticeDeadline { get; init; }

    /// <summary>Tage bis zum letzten Kündigungstag. Negativ heißt: Frist ist verstrichen.</summary>
    public int? DaysUntilNotice { get; init; }

    /// <summary>Frist läuft und ist noch erreichbar — Zeile im Akzentmuster.</summary>
    public required bool NoticeIsDue { get; init; }
}

public sealed record InsuranceDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Insurer { get; init; }
    public string? PolicyNumber { get; init; }
    public required decimal Premium { get; init; }
    public required PremiumInterval PremiumInterval { get; init; }
    public required decimal MonthlyPremium { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsOn { get; init; }
    public required int NoticePeriodMonths { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public int? DaysUntilNotice { get; init; }
    public required bool NoticeIsDue { get; init; }
    public string? AccountName { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }

    /// <summary>Beitragszahlungen als Verweis auf echte Buchungen.</summary>
    public required IReadOnlyList<LinkedPaymentDto> Payments { get; init; }
}

/// <summary>Eine Zahlung, die zu einem Fachobjekt gehört — immer ein Verweis, nie eine Kopie.</summary>
public sealed record LinkedPaymentDto
{
    public required int TransactionId { get; init; }
    public required DateOnly BookingDate { get; init; }
    public required decimal Amount { get; init; }
    public required string Payee { get; init; }
    public required string AccountName { get; init; }
}

// ── Wohnen & Immobilien ────────────────────────────────────────────────────────────────────

public sealed record PropertyListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public required decimal MarketValue { get; init; }
    public required int ContractCount { get; init; }
}

public sealed record PropertyDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public required decimal MarketValue { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }

    /// <summary>Das bestehende Darlehen, als Verweiszeile. Nicht kopiert.</summary>
    public PropertyLoanRefDto? Loan { get; init; }

    /// <summary>Summe der Kosten der letzten zwölf Monate aus echten Buchungen.</summary>
    public required decimal CostsLastTwelveMonths { get; init; }

    /// <summary>Woraus sich die Kosten zusammensetzen, für die Untertitelzeile.</summary>
    public required IReadOnlyList<string> CostParts { get; init; }

    public required IReadOnlyList<ContractListItemDto> Contracts { get; init; }
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
}

public sealed record PropertyLoanRefDto
{
    public required int LoanId { get; init; }
    public required decimal RemainingDebt { get; init; }
    public required decimal Installment { get; init; }
    public required decimal InterestRatePercent { get; init; }
}

public sealed record ContractListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required decimal MonthlyAmount { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public required bool NoticeIsDue { get; init; }
    public required int OpenInvoiceCount { get; init; }
}

public sealed record ContractDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public string? ContractNumber { get; init; }
    public required decimal MonthlyAmount { get; init; }
    public string? AccountName { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsOn { get; init; }
    public required int NoticePeriodWeeks { get; init; }
    public DateOnly? NoticeToDate { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public required bool NoticeIsDue { get; init; }
    public int? PropertyId { get; init; }
    public string? PropertyName { get; init; }
    public required IReadOnlyList<InvoiceListItemDto> Invoices { get; init; }
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
}

public sealed record InvoiceListItemDto
{
    public required int Id { get; init; }
    public required string Subject { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly DueOn { get; init; }
    public required InvoiceStatus Status { get; init; }

    /// <summary>Tage bis zur Fälligkeit. Negativ heißt: überfällig.</summary>
    public required int DaysUntilDue { get; init; }

    public bool IsOverdue => Status == InvoiceStatus.Open && DaysUntilDue < 0;
}

public sealed record InvoiceDetailDto
{
    public required int Id { get; init; }
    public required string Subject { get; init; }
    public string? Number { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly IssuedOn { get; init; }
    public required DateOnly DueOn { get; init; }
    public required InvoiceStatus Status { get; init; }
    public required int DaysUntilDue { get; init; }
    public int? ContractId { get; init; }
    public string? ContractName { get; init; }
    public int? PropertyId { get; init; }
    public string? PropertyName { get; init; }
    public int? TransactionId { get; init; }
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }

    public bool IsOverdue => Status == InvoiceStatus.Open && DaysUntilDue < 0;
}

public sealed record PayInvoiceRequest
{
    /// <summary>Zugeordnete Buchung. Ohne Angabe gilt die Rechnung als bezahlt ohne Beleg.</summary>
    public int? TransactionId { get; init; }
}
