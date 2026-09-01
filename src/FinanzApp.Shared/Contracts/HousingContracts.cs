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

/// <summary>Ein Bestandteil des erreichten Werts.</summary>
public sealed record PolicyValuePartDto
{
    /// <summary>Wie er bei dieser Vertragsart heißt.</summary>
    public required string Label { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>Woher er stammt, etwa „Statusreport 31.07.2025“.</summary>
    public required string Origin { get; init; }
}

/// <summary>Ein gemeldeter Stand.</summary>
public sealed record PolicyReportDto
{
    public required int Id { get; init; }
    public required DateOnly AsOf { get; init; }
    public required decimal Value { get; init; }
    public required string Source { get; init; }

    /// <summary>Die Bestandteile, die dieser Bericht ausgewiesen hat.</summary>
    public decimal? BaseValue { get; init; }

    /// <inheritdoc cref="BaseValue"/>
    public decimal? AccruedBonus { get; init; }

    /// <summary>Das Dokument, aus dem er stammt — <c>null</c> bei einem erfassten Stand.</summary>
    public int? DocumentId { get; init; }

    public string? DocumentTitle { get; init; }

    /// <summary>
    /// Was beim Einlesen aus dem Dokument gelesen wurde.
    /// </summary>
    /// <remarks>
    /// Der ganze Satz, nicht nur die übernommenen Felder: erst daneben lässt sich sehen, warum
    /// im Vertrag steht, was dort steht — und ob der Bericht der richtige war.
    /// </remarks>
    public IReadOnlyList<PolicyReportValueDto> Values { get; init; } = [];
}

/// <summary>
/// Ein aus dem Dokument gelesener Wert.
/// </summary>
/// <remarks>
/// Beträge stehen in <see cref="Number"/> mit <see cref="IsMoney"/> und nicht als fertiger Text:
/// ein im Server formatierter Euro-Betrag ließe sich von „Beträge verbergen“ nicht mehr
/// maskieren. Dieselbe Aufteilung wie beim Belegweg.
/// </remarks>
public sealed record PolicyReportValueDto
{
    public required string Label { get; init; }

    /// <summary>Der Wert als Text — für alles, was kein Geld ist.</summary>
    public required string Display { get; init; }

    public decimal? Number { get; init; }

    public bool IsMoney { get; init; }

    /// <summary>Seite, auf der er stand.</summary>
    public int? SourcePage { get; init; }

    /// <summary>0 bis 1.</summary>
    public double Confidence { get; init; }
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

    /// <summary>
    /// Wie der erreichte Wert entsteht — Abschnitt 19.5.
    /// </summary>
    /// <remarks>
    /// Leer, wo kein Bestandteil erfasst ist: dann steht nur die Kopfzahl da, und der Block
    /// behauptet keine Herkunft, die niemand eingetragen hat.
    /// </remarks>
    public IReadOnlyList<PolicyValuePartDto> ValueParts { get; init; } = [];

    /// <summary>
    /// Die gemeldeten Stände. Nur aus ihnen entsteht ein Verlauf.
    /// </summary>
    /// <remarks>
    /// Bei einem einzigen Bericht wird <b>keine</b> Kurve gezeichnet — eine Linie durch einen
    /// Punkt ist eine Bewegung, die niemand gemessen hat.
    /// </remarks>
    public IReadOnlyList<PolicyReportDto> Reports { get; init; } = [];
}

/// <summary>Eine Zahlung, die zu einem Fachobjekt gehört — immer ein Verweis, nie eine Kopie.</summary>
public sealed record LinkedPaymentDto
{
    public required int TransactionId { get; init; }
    public required DateOnly BookingDate { get; init; }
    public required decimal Amount { get; init; }
    public required string Payee { get; init; }
    public required string AccountName { get; init; }

    /// <summary>
    /// Warum diese Buchung zu diesem Vertrag gehört.
    /// </summary>
    /// <remarks>
    /// Steht an jeder Zeile, weil eine Zuordnung ohne Begründung nur eine Behauptung ist. Der
    /// Anlass ist real: zugeordnet wurde einmal über den Anbieternamen, und bei vier Verträgen
    /// desselben Hauses hing damit jede Buchung an jedem Vertrag.
    /// </remarks>
    public string? MatchReason { get; init; }

    /// <summary>Der Verwendungszweck im Original — die Grundlage der Zuordnung.</summary>
    public string? Reference { get; init; }
}

// ── Wohnen & Immobilien ────────────────────────────────────────────────────────────────────

public sealed record PropertyListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public required decimal MarketValue { get; init; }
    public required int ContractCount { get; init; }

    /// <summary>
    /// Die Beteiligten, falls Eigentumsanteile gepflegt sind.
    /// </summary>
    /// <remarks>
    /// Sie stehen in der Liste, weil die Erfassenmaske für eine Einlage Objekt <em>und</em>
    /// Person braucht. Ein zweiter Abruf je Objekt wäre ein Abruf ohne Ergebnis.
    /// </remarks>
    public IReadOnlyList<PropertyParticipantDto> Participants { get; init; } = [];

    /// <summary>Ob das Objekt Eigentumsanteile führt.</summary>
    public bool HasShares => Participants.Count > 0;
}

/// <summary>Ein Beteiligter, so weit ihn eine Liste braucht.</summary>
public sealed record PropertyParticipantDto
{
    public required int UserId { get; init; }
    public required string Name { get; init; }
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

    /// <summary>
    /// Die Beteiligung — <c>null</c>, solange das Objekt dem Haushalt allein gehört.
    /// </summary>
    /// <remarks>
    /// Sie kommt fertig gerechnet aus dem Dienst. Der Schirm zeigt Anteile, eigene Sicht und
    /// Ausgleichsstand, ohne selbst zu rechnen — die Zahl stünde sonst an vier Stellen desselben
    /// Schirms viermal gerechnet da.
    /// </remarks>
    public PropertyParticipationDto? Participation { get; init; }
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

// ── Fahrzeuge ─────────────────────────────────────────────────────────────

public sealed record VehicleListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Plate { get; init; }
    public required string Meta { get; init; }

    /// <summary>Kosten der letzten zwölf Monate — Beitrag, Steuer, Werkstatt.</summary>
    public required decimal CostsLastTwelveMonths { get; init; }

    /// <summary>Eine Frist läuft — Zeile im Akzentmuster.</summary>
    public required bool HasDeadline { get; init; }
}

public sealed record VehicleDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Plate { get; init; }
    public string? Usage { get; init; }
    public DateOnly? FirstRegistration { get; init; }
    public int? Mileage { get; init; }

    public required decimal CostsLastTwelveMonths { get; init; }
    public required IReadOnlyList<string> CostParts { get; init; }

    /// <summary>Die verknüpfte Kfz-Versicherung, als Verweiszeile. Nicht kopiert.</summary>
    public VehiclePolicyRefDto? Policy { get; init; }

    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
}

public sealed record VehiclePolicyRefDto
{
    public required int PolicyId { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required decimal AnnualPremium { get; init; }
    public DateOnly? NoticeDeadline { get; init; }
    public required bool NoticeIsDue { get; init; }
}

// ── Scaneingang ──────────────────────────────────────────────────────────

public sealed record ScanInboxDto
{
    public required int WaitingCount { get; init; }
    public required IReadOnlyList<ScanInboxItemDto> Items { get; init; }
}

public sealed record ScanInboxItemDto
{
    public required int Id { get; init; }
    public required int DocumentId { get; init; }
    public required string FileName { get; init; }
    public string? Sender { get; init; }
    public int? PageCount { get; init; }

    /// <summary>„erkannt“ oder „prüfen“.</summary>
    public required bool Recognised { get; init; }

    public required DateOnly ArrivedOn { get; init; }
}

/// <summary>
/// Trägt Typ und Objekt an einem wartenden Beleg nach.
/// </summary>
/// <remarks>
/// Der Weg für alles, was die Erkennung nicht selbst hinbekommen hat. Beides zusammen in einem
/// Aufruf, weil der Eingang beides zusammen verlangt: ein Beleg mit Typ, aber ohne Objekt wäre
/// nach dem halben Weg immer noch nicht eingeordnet — und stünde nach einem gescheiterten
/// zweiten Aufruf halb geändert da.
/// </remarks>
public sealed record AssignScanInboxRequest
{
    public required int DocumentTypeId { get; init; }
    public required LinkTargetType TargetType { get; init; }
    public required int TargetId { get; init; }
}
