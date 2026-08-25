namespace FinanzApp.Shared.Contracts;

// ── Vorsorge & Kapital, Absicherung ────────────────────────────────────────

/// <summary>
/// Kopfzahlen eines der beiden Bereiche. Welche Zahl oben steht, hängt am Bereich: Vorsorge
/// zeigt einen <em>Wert</em>, Absicherung einen <em>Jahresbeitrag</em>. Eine Absicherung hat
/// keinen Wert — dort eine Summe zu zeigen, wäre schlicht falsch.
/// </summary>
public sealed record PolicyOverviewDto
{
    public required bool CapitalForming { get; init; }
    public required string Title { get; init; }

    /// <summary>Summe der erreichten Werte. Nur bei Vorsorge gesetzt.</summary>
    public decimal? TotalValue { get; init; }

    /// <summary>Ältester Stichtag der Werte — so alt ist die Summe mindestens.</summary>
    public DateOnly? OldestValuationDate { get; init; }

    /// <summary>Summe der Jahresbeiträge. Nur bei Absicherung gesetzt.</summary>
    public decimal? TotalAnnualPremium { get; init; }

    public required IReadOnlyList<PolicyListItemDto> Items { get; init; }
}

public sealed record PolicyListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required PolicyKind Kind { get; init; }
    public required bool IsCapitalForming { get; init; }

    /// <summary>Zweite Zeile: Vertragsart plus das, was den Vertrag ausmacht.</summary>
    public required string Meta { get; init; }

    public required decimal Premium { get; init; }
    public required PremiumInterval PremiumInterval { get; init; }
    public required decimal AnnualPremium { get; init; }

    /// <summary>Erreichter Wert — nur kapitalbildend, sonst <c>null</c>.</summary>
    public decimal? Value { get; init; }

    /// <summary>Stichtag dazu. Ohne ihn wird der Wert nicht gezeigt.</summary>
    public DateOnly? ValuationDate { get; init; }

    public DateOnly? EndsOn { get; init; }
    public DateOnly? NoticeDeadline { get; init; }

    /// <summary>Tage bis zum letzten Kündigungstag. Negativ heißt: Frist ist verstrichen.</summary>
    public int? DaysUntilNotice { get; init; }

    /// <summary>Tage bis zur gesetzten Erinnerung. <c>null</c>, wenn keine gesetzt ist.</summary>
    public int? DaysUntilReminder { get; init; }

    /// <summary>Frist läuft und ist noch erreichbar — Zeile im Akzentmuster.</summary>
    public required bool NoticeIsDue { get; init; }
}

public sealed record PolicyDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required PolicyKind Kind { get; init; }
    public required string KindLabel { get; init; }
    public required bool IsCapitalForming { get; init; }
    public string? PolicyNumber { get; init; }
    public required decimal Premium { get; init; }
    public required PremiumInterval PremiumInterval { get; init; }
    public required decimal MonthlyPremium { get; init; }
    public required decimal AnnualPremium { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsOn { get; init; }
    public required int NoticePeriodMonths { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public int? DaysUntilNotice { get; init; }
    public DateOnly? NoticeReminderOn { get; init; }
    public int? DaysUntilReminder { get; init; }
    public required bool NoticeIsDue { get; init; }

    public decimal? CurrentValue { get; init; }
    public DateOnly? ValuationDate { get; init; }
    public decimal? MaturityValue { get; init; }
    public DateOnly? MaturesOn { get; init; }
    public decimal? SumInsured { get; init; }
    public decimal? Deductible { get; init; }

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
