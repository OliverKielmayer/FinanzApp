namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Ein Arbeitsverhältnis, so wie die Anzeige es braucht.
/// </summary>
/// <remarks>
/// <see cref="YearlyGross"/> ist <c>null</c>, sobald das Verhältnis beendet ist. Ein beendeter
/// Vertrag trägt keine Jahreslast mehr; eine Zahl an dieser Stelle stünde in jeder Summe, in
/// die sie nicht gehört.
/// </remarks>
public sealed record EmploymentRowDto
{
    public required int Id { get; init; }
    public required string Employer { get; init; }

    /// <summary>Position · Art · seit · Std./Woche · Kündigungsfrist, aus den Rohfeldern.</summary>
    public required string Meta { get; init; }

    public required EmploymentKind Kind { get; init; }
    public required string KindLabel { get; init; }
    public required DateOnly StartsOn { get; init; }
    public required DateOnly? EndsOn { get; init; }
    public required decimal GrossMonthly { get; init; }

    /// <summary>Netto pro Monat — erfasst oder geschätzt, siehe <see cref="NetIsEstimated"/>.</summary>
    public required decimal NetMonthly { get; init; }

    /// <summary>Ob das Netto geschätzt ist. Eine Schätzung, die sich nicht zu erkennen gibt, ist eine Behauptung.</summary>
    public required bool NetIsEstimated { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>Bruttogehalt pro Jahr. <c>null</c> bei beendeten Verhältnissen.</summary>
    public required decimal? YearlyGross { get; init; }

    public required int PayslipCount { get; init; }
}

/// <summary>
/// Eine Lohnabrechnung mit ihrem Zustand.
/// </summary>
/// <remarks>
/// Der Zustand steht nicht als fertiger Satz hier: „Zahlung zugeordnet · 21.08.“ setzt die
/// Anzeige zusammen, weil sie das Datum ohnehin formatiert und den Betrag maskieren können muss.
/// </remarks>
public sealed record PayslipRowDto
{
    public required int Id { get; init; }

    /// <summary>Das Arbeitsverhältnis. <c>null</c>, wenn es gelöscht wurde.</summary>
    public required int? EmploymentId { get; init; }

    public required string? Employer { get; init; }

    /// <summary>Der Abrechnungsmonat, immer der Erste.</summary>
    public required DateOnly Month { get; init; }

    public required decimal Gross { get; init; }

    /// <summary>
    /// Das Netto — eingetragen oder geschätzt, und <see cref="NetIsEstimated"/> sagt welches.
    /// </summary>
    /// <remarks>
    /// Die Schätzung greift nur, wo nichts eingetragen ist, und sie steht nie unbeschriftet da:
    /// die Zeile liest sich dann „Netto 5.240 € (geschätzt)“. Ein Faktor, der niemandes
    /// Steuerklasse kennt, darf nicht unsichtbar in Auswertungen wirken.
    /// </remarks>
    public required decimal Net { get; init; }

    /// <summary>Das Netto ist gerechnet, nicht abgeschrieben.</summary>
    public required bool NetIsEstimated { get; init; }

    public required decimal Payout { get; init; }

    /// <summary>Brutto minus Netto — gerechnet, nicht gespeichert.</summary>
    public decimal Deductions => Gross - Net;

    public required int? DocumentId { get; init; }
    public required string? DocumentTitle { get; init; }

    public required int? TransactionId { get; init; }
    public required DateOnly? PaidOn { get; init; }
    public required string? PaidFrom { get; init; }

    /// <summary>
    /// Der Betrag der zugeordneten Buchung.
    /// </summary>
    /// <remarks>
    /// Er steht getrennt vom Auszahlungsbetrag, weil beide auseinandergehen können und die
    /// Anzeige das dann sagen soll, statt eine der beiden Zahlen verschwinden zu lassen.
    /// </remarks>
    public required decimal? PaidAmount { get; init; }
}

/// <summary>Eine Vereinbarung: Gehaltsänderung, Bonus, betriebliche Altersvorsorge.</summary>
public sealed record WorkAgreementRowDto
{
    public required int Id { get; init; }
    public required int EmploymentId { get; init; }
    public required string Name { get; init; }
    public required DateOnly SignedOn { get; init; }
    public required WorkAgreementKind Kind { get; init; }
    public required string KindLabel { get; init; }
    public required int? DocumentId { get; init; }
    public required string? DocumentTitle { get; init; }
}

/// <summary>
/// Der Kopf des Bereichs. Zahlen, keine Sätze.
/// </summary>
/// <remarks>
/// Alle Summen laufen <b>nur über laufende</b> Verhältnisse. Der Prototyp addierte beide und kam
/// auf 127.200 € Bruttogehalt pro Jahr, während der Bereich selbst 77.760 € nannte — 49.440 €
/// Unterschied für dieselbe Größe.
/// </remarks>
public sealed record EmploymentHeadDto
{
    /// <summary>Arbeitgeber des laufenden Verhältnisses. <c>null</c>, wenn keines läuft.</summary>
    public required string? Employer { get; init; }

    public required decimal YearlyGross { get; init; }
    public required decimal MonthlyGross { get; init; }
    public required decimal MonthlyNet { get; init; }

    /// <summary>Ob in <see cref="MonthlyNet"/> eine Schätzung steckt.</summary>
    public required bool NetIsEstimated { get; init; }

    /// <summary>Abgabenquote in Prozent. <c>null</c> ohne Brutto — dann gibt es nichts zu teilen.</summary>
    public required decimal? DeductionRatePercent { get; init; }

    public required int ActiveCount { get; init; }
    public required int TotalCount { get; init; }
}

public sealed record EmploymentOverviewDto
{
    public required EmploymentHeadDto Head { get; init; }
    public required IReadOnlyList<EmploymentRowDto> Employments { get; init; }
    public required IReadOnlyList<PayslipRowDto> Payslips { get; init; }
    public required IReadOnlyList<WorkAgreementRowDto> Agreements { get; init; }

    /// <summary>Abrechnungen ohne Beleg.</summary>
    public required int WithoutDocumentCount { get; init; }

    /// <summary>Abrechnungen ohne zugeordnete Zahlung.</summary>
    public required int WithoutPaymentCount { get; init; }
}

public sealed record CreatePayslipRequest
{
    public required int EmploymentId { get; init; }

    /// <summary>Irgendein Tag des Abrechnungsmonats; gespeichert wird der Erste.</summary>
    public required DateOnly Month { get; init; }

    public required decimal Gross { get; init; }

    /// <summary>
    /// Nettogehalt laut Abrechnung. Ohne Angabe schätzt die Anwendung — und schreibt dran, dass
    /// sie schätzt.
    /// </summary>
    public decimal? Net { get; init; }

    /// <summary>Auszahlungsbetrag. Ohne Angabe gilt der Nettobetrag.</summary>
    public decimal? Payout { get; init; }
}

public sealed record LinkPayslipPaymentRequest
{
    public required int TransactionId { get; init; }
}
