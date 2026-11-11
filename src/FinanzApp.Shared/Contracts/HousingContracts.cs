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

    /// <summary>Selbstbeteiligung in Prozent des Rechnungsbetrags.</summary>
    public decimal? OwnSharePercent { get; init; }

    /// <summary>Höchstbetrag der Selbstbeteiligung im Kalenderjahr.</summary>
    public decimal? OwnShareAnnualCap { get; init; }

    /// <summary>Satz für Zahnbehandlung, ohne Deckel.</summary>
    public decimal? DentalOwnSharePercent { get; init; }

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

    /// <summary>
    /// Was in das Objekt gesteckt wurde: die Summe der zugeordneten Anschaffungen.
    /// </summary>
    /// <remarks>
    /// <para>Bewusst <b>neben</b> <see cref="CostsLastTwelveMonths"/> und nicht darin. Die
    /// Kostenzeile rechnet Hochrechenbares — Darlehensrate mal zwölf, Vertragsjahresbeträge,
    /// Versicherung. Eine Einbauküche ist keine Jahreslast; in dieselbe Zahl gemischt stünde
    /// sie als Dauerkosten da und fiele im Jahr darauf ohne erkennbaren Grund wieder weg.</para>
    /// <para>Ohne Zeitfenster: Was zugeordnet ist, zählt. Ein Stichtag — etwa „seit dem
    /// Kaufdatum“ — entschiede es für den Benutzer, und zwar falsch, sobald etwas kurz vor
    /// dem Kauf für genau dieses Objekt angeschafft wurde.</para>
    /// </remarks>
    public required decimal Investments { get; init; }

    /// <summary>Wie viele Anschaffungen die Summe trägt. Ohne sie wäre sie nicht nachzählbar.</summary>
    public required int InvestmentCount { get; init; }

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

    /// <summary>
    /// Buchungen, die zu diesem Objekt gehören — jede mit dem Grund, warum sie hier steht.
    /// </summary>
    /// <remarks>
    /// <para>Eine Buchung trägt kein Objekt. Was zum Haus gehört, wird deshalb hergeleitet:
    /// aus Einlagen auf dieses Objekt, aus den Nummern seiner Verträge und der Gebäude- und
    /// Hausratversicherung, aus dem Anbieternamen — nur wo er eindeutig ist — und aus der
    /// Darlehensrate.</para>
    /// <para>Der Grund steht an jeder Zeile, weil die Herleitung unterschiedlich fest ist:
    /// Eine Vertragsnummer im Verwendungszweck ist ein Beleg, ein passender Betrag ohne
    /// alles andere ein Schluss. Wer das nicht sieht, hält beides für dasselbe.</para>
    /// </remarks>
    public required IReadOnlyList<LinkedPaymentDto> Bookings { get; init; }
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

    /// <summary>Der Betrag je Zahlung, im Takt von <see cref="PaymentInterval"/>.</summary>
    public required decimal Amount { get; init; }

    public required PremiumInterval PaymentInterval { get; init; }

    /// <summary>Derselbe Betrag auf den Monat — die Größe, in der Berichte vergleichen.</summary>
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

    /// <summary>
    /// Ob der Abschlag zu den Objektkosten zählt — Handoff „Gemeinsame Immobilie“, 3.4.
    /// </summary>
    /// <remarks>
    /// Nicht dasselbe wie die Zuordnung zum Objekt: der Internetanschluss hängt am Haus und zieht
    /// doch mit den Leuten um. Die Zuordnung sagt wohin, das Kennzeichen sagt, wessen Kosten.
    /// </remarks>
    public required bool PropertyRelated { get; init; }

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

    /// <summary>Wer die Zahlung erwartet — bei einer Rechnung ohne Vertrag die einzige Quelle.</summary>
    public string? Payee { get; init; }

    /// <summary>Wann die Zahlung als erfolgt gilt.</summary>
    public DateOnly? PaidOn { get; init; }

    /// <summary>Was zur Zahlung zu sagen war — „bar bezahlt“.</summary>
    public string? PaymentNote { get; init; }

    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }

    public bool IsOverdue => Status == InvoiceStatus.Open && DaysUntilDue < 0;

    /// <summary>
    /// Ob die Zahlung durch eine Buchung belegt ist — oder nur bestätigt.
    /// </summary>
    /// <remarks>
    /// Eine Buchung ist ein Beleg, eine bestätigte Barzahlung eine Aussage. Beides heißt
    /// „bezahlt“, und nur eines lässt sich nachprüfen — das gehört an die Oberfläche.
    /// </remarks>
    public bool IsProven => Status == InvoiceStatus.Paid && TransactionId is not null;
}

public sealed record PayInvoiceRequest
{
    /// <summary>Zugeordnete Buchung. Ohne Angabe gilt die Rechnung als bezahlt ohne Beleg.</summary>
    public int? TransactionId { get; init; }

    /// <summary>
    /// Der Tag der Zahlung. Ohne Angabe nimmt der Dienst den Buchungstag der zugeordneten
    /// Buchung — und ohne Buchung den heutigen.
    /// </summary>
    public DateOnly? PaidOn { get; init; }

    /// <summary>Freitext zur Zahlung, etwa „bar bezahlt“.</summary>
    public string? Note { get; init; }
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

    /// <summary>Die 1-%-Regelung, wenn sie sich rechnen lässt — sonst <c>null</c>.</summary>
    public CompanyCarBenefitDto? Benefit { get; init; }

    /// <summary>Die verknüpfte Kfz-Versicherung, als Verweiszeile. Nicht kopiert.</summary>
    public VehiclePolicyRefDto? Policy { get; init; }

    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
}

/// <summary>
/// Der geldwerte Vorteil eines Dienstwagens, in seinen Bestandteilen.
/// </summary>
/// <remarks>
/// Nicht nur die Endsumme: Wer 841,08 € im Monat liest, will wissen, woraus sie entsteht — und
/// die beiden Teile haben verschiedene Gründe. Die private Nutzung fällt an, solange der Wagen
/// da ist; der Wegzuschlag hängt an einer Entfernung, die sich ändert, wenn jemand umzieht.
/// </remarks>
public sealed record CompanyCarBenefitDto
{
    /// <summary>Der erfasste Bruttolistenpreis, ungerundet.</summary>
    public required decimal GrossListPrice { get; init; }

    /// <summary>Anteil, mit dem er angesetzt wird: 100, 50 oder 25.</summary>
    public required decimal TaxableSharePercent { get; init; }

    /// <summary>Bemessungsgrundlage: abgerundet auf volle hundert Euro, dann gemindert.</summary>
    public required decimal Basis { get; init; }

    /// <summary>Ein Prozent der Bemessungsgrundlage, je Monat.</summary>
    public required decimal UsePerMonth { get; init; }

    /// <summary>Einfache Entfernung zur Arbeitsstätte. <c>null</c>, wenn keine erfasst ist.</summary>
    public decimal? CommuteKilometres { get; init; }

    /// <summary>0,03 % je Kilometer, je Monat. Null ohne erfasste Entfernung.</summary>
    public required decimal CommutePerMonth { get; init; }

    /// <summary>Der Arbeitgeber, an dessen Verhältnis der Wagen hängt.</summary>
    public string? EmployerName { get; init; }

    public required decimal PerMonth { get; init; }
    public required decimal PerYear { get; init; }
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

/// <summary>
/// Ein Objekt, das eine gelernte Regel vorschlägt — samt der Herkunft des Vorschlags.
/// </summary>
/// <remarks>
/// Das Lerndatum steht bewusst dabei. Ein Vorschlag ohne Begründung ist Magie, und Magie wird
/// entweder blind befolgt oder misstrauisch ignoriert; beides ist schlechter als „weil Post von
/// diesem Absender am 6.11. dorthin ging“.
/// </remarks>
public sealed record SuggestedTargetDto
{
    public required LinkTargetType TargetType { get; init; }
    public required int TargetId { get; init; }

    /// <summary>Der Name des Objekts, etwa „Haus Musterweg 3“.</summary>
    public required string Label { get; init; }

    /// <summary>Die Art des Objekts im Klartext, etwa „Immobilie“.</summary>
    public required string TargetLabel { get; init; }

    /// <summary>Wann die Regel gelernt wurde.</summary>
    public required DateOnly LearnedOn { get; init; }
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

    /// <summary>
    /// Ob sich aus diesem Beleg Werte übernehmen lassen.
    /// </summary>
    /// <remarks>
    /// Wahr, sobald beim Einlesen eine Dokumentart erkannt wurde — dann gibt es gelesene Werte,
    /// einen Prüfschritt und eine Rechenprobe. Ohne erkannte Art ist der Beleg abgelegt und
    /// verknüpfbar, aber es steht nichts darin, das in ein Objekt gehörte.
    /// </remarks>
    public required bool CanTakeValues { get; init; }

    /// <summary>
    /// Wohin der Schalter „Werte übernehmen“ führt.
    /// </summary>
    /// <remarks>
    /// Nicht jeder Prüfschritt sieht gleich aus: ein Statusreport trägt einen Wert für einen
    /// Vertrag, eine Vermögensübersicht acht Werte für acht Verträge. Welcher Schirm der richtige
    /// ist, weiß der Server aus der erkannten Art — die Oberfläche folgt dem Weg, statt ihn aus
    /// einem Kennzeichen selbst zusammenzusetzen.
    /// </remarks>
    public string? ValuesHref { get; init; }

    /// <summary>Wie der Schalter heißt, der dorthin führt.</summary>
    /// <remarks>
    /// Aus derselben Hand wie der Weg, und aus demselben Grund: welcher Schirm folgt, weiß der
    /// Server aus der erkannten Art. Aus einem Kontoauszug werden Buchungen und keine Werte.
    /// </remarks>
    public string? ValuesLabel { get; init; }

    /// <summary>
    /// Das Dokument, das denselben Beleg schon trägt — <c>null</c> im Normalfall.
    /// </summary>
    /// <remarks>
    /// Die Ablage hätte beiden denselben Namen gegeben — in ihm stehen Art, Zuordnung und
    /// Stichtag. Byteweise gleiche Dateien kommen gar nicht erst herein; was hier steht, ist der
    /// unsichere Fall, und der gehört einem Menschen vorgelegt statt vom Dienst entschieden.
    /// </remarks>
    public int? DuplicateOfDocumentId { get; init; }

    /// <summary>Wie das vorhandene Dokument heißt, damit der Verweis einen Namen trägt.</summary>
    public string? DuplicateOfTitle { get; init; }

    /// <summary>
    /// Was der Text vermuten lässt, wenn keine Leseregel gegriffen hat.
    /// </summary>
    /// <remarks>
    /// <para>Gerechnet und nicht gespeichert: Der Vorschlag entsteht bei jedem Aufruf neu aus
    /// dem gelesenen Text. Eine gespeicherte Vermutung liefe den Regeln davon, sobald jemand
    /// eine ändert — und sie stünde noch am Beleg, wenn sie längst widerlegt wäre.</para>
    /// <para><c>null</c> heißt: Der Text gibt nichts her, oder es gibt keinen.</para>
    /// </remarks>
    public string? SuggestedType { get; init; }

    /// <summary>Der vorgeschlagene Bereich, im Klartext.</summary>
    public string? SuggestedArea { get; init; }

    /// <summary>
    /// Das Objekt, an das Post von diesem Absender zuletzt ging. <c>null</c>, wenn nichts gelernt ist.
    /// </summary>
    /// <remarks>
    /// <para>Aus einer gelernten Ablageregel, nicht aus dem Text. Der Unterschied steht am
    /// Vorschlag: Die Art kommt aus Schlagwörtern im Schreiben, das Objekt aus einer früheren
    /// Entscheidung des Benutzers — und deshalb nennt der Vorschlag sein Lerndatum.</para>
    /// <para>Nur wo noch nichts verknüpft ist. Wo eine Leseregel das Ziel schon gefunden hat,
    /// steht eine Tatsache, und daneben gehört keine Vermutung.</para>
    /// </remarks>
    public SuggestedTargetDto? SuggestedTarget { get; init; }

    /// <summary>
    /// Ob es den vorgeschlagenen Dokumenttyp im Haushalt schon gibt.
    /// </summary>
    /// <remarks>
    /// Gibt es ihn nicht, bietet die Zeile an, ihn anzulegen. Den Benutzer stattdessen in die
    /// Typenverwaltung zu schicken hieße, ihn für eine Auskunft arbeiten zu lassen, die die
    /// Anwendung selbst hat.
    /// </remarks>
    public bool SuggestedTypeExists { get; init; }

    /// <summary>
    /// Gelesene Werte, die noch niemand übernommen hat.
    /// </summary>
    /// <remarks>
    /// „Wegräumen“ nimmt keine Werte — es räumt die Zeile weg. Wo welche offen sind, fragt der
    /// Eingang deshalb nach, statt sie stillschweigend liegen zu lassen: genau so sind dreizehn
    /// Statusreporte eines Vertrags verschwunden, und am Vertrag stand hinterher eine Null.
    /// </remarks>
    public int UnconfirmedValues { get; init; }

    public required DateOnly ArrivedOn { get; init; }
}

/// <summary>
/// Trägt Art und — wenn es sie schon gibt — das Objekt an einem wartenden Beleg nach.
/// </summary>
/// <remarks>
/// <para>Der Weg für alles, was die Erkennung nicht selbst hinbekommen hat. In einem Aufruf,
/// weil Art, Ablageordner und Dateiname zusammen gesetzt werden: nach einem gescheiterten
/// zweiten Aufruf stünde der Beleg sonst halb geändert da.</para>
/// <para><b>Das Objekt ist freiwillig.</b> Es gab Post, die vor ihrem Objekt ankommt — ein
/// Notarschreiben zum Hauskauf, bevor das Haus angelegt ist —, und Post, die nie eines bekommt.
/// Verlangte der Eingang beides, bliebe sie für immer im Eingang liegen; das war keine
/// Sorgfalt, sondern eine Sackgasse. Ohne Objekt wird der Beleg abgelegt und gilt als
/// bearbeitet, trägt aber sichtbar, dass er noch an nichts hängt.</para>
/// <para>Beide Felder gelten gemeinsam: Eine Art ohne Kennung — oder umgekehrt — ist kein
/// halbes Ziel, sondern ein Fehler.</para>
/// </remarks>
public sealed record AssignScanInboxRequest
{
    public required int DocumentTypeId { get; init; }
    public LinkTargetType? TargetType { get; init; }
    public int? TargetId { get; init; }
}
