using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Data.Entities;

/// <summary>
/// Ein PKV-Vorgang: die Arztrechnung mit ihrem Weg von „erfasst“ bis „erstattet“.
/// </summary>
/// <remarks>
/// <para>Die Dreiteilung ist der Kern: <see cref="GrossAmount"/> ist die Rechnung,
/// <see cref="OwnShare"/> der Eigenanteil, <see cref="ExpectedReimbursement"/> das, was die
/// Versicherung zahlen soll.</para>
/// <para><strong>Der Eigenanteil ist keine offene Forderung.</strong> Er ist eine gebuchte Ausgabe
/// und darf nirgends als „noch zu erstatten“ auftauchen. Offen ist ausschließlich die erwartete
/// Erstattung, solange keine Zahlung zugeordnet ist.</para>
/// <para>Geldbewegungen bleiben Buchungen: die Erstattung und der Eigenanteil verweisen auf
/// vorhandene <see cref="Transaction"/>-Sätze und werden nicht doppelt geführt.</para>
/// </remarks>
public class MedicalBill : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    /// <summary>Rechnungssteller, etwa „Dr. Meyer, Zahnarzt“.</summary>
    public required string Provider { get; set; }

    public DateOnly BillDate { get; set; }
    public string? BillNumber { get; set; }

    /// <summary>Rechnungsbetrag, positiv.</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>Eigenanteil, positiv. Gebuchte Ausgabe, keine Forderung.</summary>
    public decimal OwnShare { get; set; }

    /// <summary>Erwartete Erstattung, positiv. Das ist der offene Betrag.</summary>
    public decimal ExpectedReimbursement { get; set; }

    /// <summary>Tatsächlich erstattet. Erst gesetzt, wenn eine Zahlung zugeordnet wurde.</summary>
    public decimal? ActualReimbursement { get; set; }

    public MedicalBillStatus Status { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? SettlementReceivedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>Die Buchung, mit der die Erstattung einging.</summary>
    public int? ReimbursementTransactionId { get; set; }

    /// <summary>Die Buchung, mit der der Eigenanteil bezahlt wurde.</summary>
    public int? OwnShareTransactionId { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Offen ist nur die Erstattung — nie der Eigenanteil.</summary>
    public decimal OpenAmount
        => Status is MedicalBillStatus.Completed or MedicalBillStatus.Rejected
            ? 0m
            : ExpectedReimbursement - (ActualReimbursement ?? 0m);
}

/// <summary>
/// Ein Vorsorge- oder Absicherungsvertrag. <b>Ein</b> Modell für beide Bereiche, unterschieden
/// allein durch <see cref="IsCapitalForming"/>.
/// </summary>
/// <remarks>
/// <para>Das Entscheidungsmerkmal ist, <em>ob der Vertrag einen Wert hat, der ins Vermögen
/// zählt</em> (Handoff v4, Abschnitt 4):</para>
/// <list type="bullet">
///   <item><b>Vorsorge &amp; Kapital</b> (<c>IsCapitalForming = true</c>) — Kapital-LV,
///     Rentenversicherung, Riester, Bausparen, bAV. Sie tragen einen Rückkaufswert oder ein
///     Ansammlungsguthaben und erscheinen im Bruttovermögen, <b>immer mit Stichtag</b>.</item>
///   <item><b>Absicherung</b> (<c>false</c>) — Risikoleben, BU, Haftpflicht, Hausrat,
///     Wohngebäude, Kfz, Unfall, Rechtsschutz, Kranken. Sie haben Beitrag, Versicherungssumme
///     und Frist, aber <b>keinen</b> Vermögenswert.</item>
/// </list>
/// <para>Ein Risikoleben-Vertrag darf deshalb nie im Nettovermögen auftauchen — er zahlt im
/// Todesfall, er ist kein Guthaben. Genau daran ist die alte Sammelkategorie
/// „Versicherungen“ gescheitert.</para>
/// </remarks>
public class Policy : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    /// <summary>Vertragsart. Bestimmt zusammen mit ihr, unter welchem Bereich der Vertrag steht.</summary>
    public PolicyKind Kind { get; set; }

    /// <summary>
    /// Kapitalbildend — der Vertrag hat einen Wert, der ins Vermögen zählt. Redundant zu
    /// <see cref="Kind"/>, aber bewusst eigenständig: die Zuordnung einer Art kann sich ändern,
    /// die Vermögensrechnung soll davon nicht abhängen.
    /// </summary>
    public bool IsCapitalForming { get; set; }

    public required string Name { get; set; }

    /// <summary>Versicherer, Bank oder Kasse.</summary>
    public required string Provider { get; set; }

    public string? PolicyNumber { get; set; }

    public decimal Premium { get; set; }
    public PremiumInterval PremiumInterval { get; set; }

    public DateOnly? StartsOn { get; set; }

    /// <summary>Vertragsende. Aus ihm und der Frist entsteht die Kündigungsfrist.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>Kündigungsfrist in Monaten vor Vertragsende.</summary>
    public int NoticePeriodMonths { get; set; }

    /// <summary>
    /// Tag, ab dem an die Kündigung erinnert werden soll — unabhängig vom Termin selbst.
    /// </summary>
    /// <remarks>
    /// Termin und Erinnerung sind zweierlei: eine Frist kann ein Jahr entfernt liegen und trotzdem
    /// jetzt auf den Tisch gehören, weil ein Vergleich Zeit braucht. Ohne dieses Feld müsste man
    /// den Vertrag künstlich früher enden lassen, um ihn sichtbar zu machen.
    /// </remarks>
    public DateOnly? NoticeReminderOn { get; set; }

    /// <summary>Konto, von dem der Beitrag abgeht.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    // ── nur kapitalbildend ────────────────────────────────────────────────────────────────

    /// <summary>Erreichter Wert: Rückkaufswert, Ansammlungsguthaben, Bausparguthaben.</summary>
    public decimal? CurrentValue { get; set; }

    /// <summary>
    /// Stichtag des erreichten Werts. <b>Pflicht, sobald ein Wert steht</b> — ein Jahresstand ist
    /// kein Tageskurs und darf nicht wie einer aussehen.
    /// </summary>
    public DateOnly? ValuationDate { get; set; }

    /// <summary>Ablaufleistung, falls der Vertrag eine ausweist.</summary>
    public decimal? MaturityValue { get; set; }

    public DateOnly? MaturesOn { get; set; }

    // ── nur Absicherung ───────────────────────────────────────────────────────────────────

    /// <summary>Versicherungssumme.</summary>
    public decimal? SumInsured { get; set; }

    /// <summary>Selbstbeteiligung.</summary>
    public decimal? Deductible { get; set; }

    public string? Notes { get; set; }

    /// <summary>Beitrag auf einen Monat gerechnet — für die Kostenübersicht.</summary>
    public decimal MonthlyPremium => PremiumInterval switch
    {
        PremiumInterval.Monthly => Premium,
        PremiumInterval.Quarterly => Premium / 3m,
        PremiumInterval.HalfYearly => Premium / 6m,
        PremiumInterval.Yearly => Premium / 12m,
        _ => Premium,
    };

    /// <summary>Beitrag auf ein Jahr gerechnet — die Kopfzahl der Absicherung.</summary>
    public decimal AnnualPremium => PremiumInterval switch
    {
        PremiumInterval.Monthly => Premium * 12m,
        PremiumInterval.Quarterly => Premium * 4m,
        PremiumInterval.HalfYearly => Premium * 2m,
        PremiumInterval.Yearly => Premium,
        _ => Premium,
    };

    /// <summary>
    /// Was dieser Vertrag zum Vermögen beiträgt. Für Absicherung <b>immer null</b>, auch wenn
    /// versehentlich ein Wert eingetragen wäre — die Regel steht hier und nicht in jeder Auswertung.
    /// </summary>
    public decimal? AssetValue => IsCapitalForming ? CurrentValue : null;

    /// <summary>Letzter Tag, an dem noch gekündigt werden kann.</summary>
    public DateOnly? NoticeDeadline
        => EndsOn is { } ends ? ends.AddMonths(-NoticePeriodMonths) : null;
}

/// <summary>Eine Immobilie als Sammelpunkt für Darlehen, Verträge, Kosten und Dokumente.</summary>
public class Property : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Name { get; set; }
    public string? Address { get; set; }

    /// <summary>Haus, Wohnung, Grundstück — steht in der Zeile unter dem Namen.</summary>
    public PropertyKind Kind { get; set; } = PropertyKind.House;

    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal MarketValue { get; set; }

    /// <summary>Verweis auf das bestehende Darlehen. Es wird nicht kopiert.</summary>
    public int? LoanId { get; set; }
    public Loan? Loan { get; set; }

    public List<Contract> Contracts { get; set; } = [];
}

/// <summary>Ein laufender Vertrag — Strom, Internet, Wartung.</summary>
public class Contract : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int? PropertyId { get; set; }
    public Property? Property { get; set; }

    public required string Name { get; set; }
    public required string Provider { get; set; }
    public string? ContractNumber { get; set; }

    /// <summary>Monatlicher Abschlag.</summary>
    public decimal MonthlyAmount { get; set; }

    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }

    /// <summary>Kündigungsfrist in Wochen, wie sie in Versorgungsverträgen üblich ist.</summary>
    public int NoticePeriodWeeks { get; set; }

    /// <summary>Stichtag, zu dem gekündigt werden kann — etwa der 31.03. jedes Jahres.</summary>
    public DateOnly? NoticeToDate { get; set; }

    public DocumentArea Area { get; set; } = DocumentArea.Housing;

    public List<Invoice> Invoices { get; set; } = [];

    /// <summary>Letzter Tag für eine Kündigung zum Stichtag.</summary>
    public DateOnly? NoticeDeadline
        => NoticeToDate is { } to ? to.AddDays(-7 * NoticePeriodWeeks) : null;
}

/// <summary>Eine Rechnung zu einem Vertrag.</summary>
public class Invoice : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public required string Subject { get; set; }
    public string? Number { get; set; }

    public DateOnly IssuedOn { get; set; }
    public DateOnly DueOn { get; set; }
    public decimal Amount { get; set; }

    public InvoiceStatus Status { get; set; }

    /// <summary>Die Buchung, mit der bezahlt wurde. Keine eigene Geldbewegung.</summary>
    public int? TransactionId { get; set; }

    public bool IsOpen => Status == InvoiceStatus.Open;
}

/// <summary>
/// Eine Aufgabe oder Frist. Läuft im Vorgänge-Tab mit Erstattungen und Rechnungen zusammen.
/// </summary>
/// <remarks>
/// Die meisten Einträge entstehen von selbst — aus Vertragsende minus Frist, aus einer
/// Rechnungsfälligkeit, aus einer Erstattung ohne Zahlungseingang. Woher ein Eintrag stammt, steht
/// in <see cref="Source"/> und wird in der Zeile erklärt; ohne das wäre eine automatisch erzeugte
/// Aufgabe für den Nutzer nicht nachvollziehbar.
/// </remarks>
public class TaskItem : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Title { get; set; }
    public string? Detail { get; set; }

    public DateOnly? DueOn { get; set; }
    public TaskState State { get; set; }
    public TaskSource Source { get; set; }

    /// <summary>Objekt, aus dem die Aufgabe entstanden ist.</summary>
    public LinkTargetType? SourceType { get; set; }
    public int? SourceId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Ein aus einem Dokument gelesener Wert — mit seiner Herkunft und seinem Bestätigungsstand.
/// </summary>
/// <remarks>
/// <para>Der Handoff verlangt beides: „extrahierte Werte immer mit Herkunft (Seite, Konfidenz)
/// und Bestätigungsstatus speichern“ und „nichts Unbestätigtes verändert Vermögenszahlen“.
/// Ohne diese Tabelle wäre später nicht mehr feststellbar, ob eine Zahl gelesen oder getippt
/// wurde — und genau das ist die Frage, wenn eine Bilanz nicht stimmt.</para>
/// <para>Sie füllt sich erst, wenn eine Analyse angebunden ist. Bis dahin bleibt sie leer, und
/// das ist die ehrliche Aussage: nichts wurde gelesen.</para>
/// </remarks>
public class DocumentExtraction : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Schlüssel des Formularfelds, in das der Wert gehört.</summary>
    public required string FieldKey { get; set; }

    public required string Label { get; set; }

    /// <summary>Der gelesene Wert, im Eingabeformat des Felds.</summary>
    public required string Value { get; set; }

    /// <summary>Seite, auf der er stand.</summary>
    public int? SourcePage { get; set; }

    /// <summary>0 bis 1.</summary>
    public double Confidence { get; set; }

    /// <summary>Hat ein Mensch ihn übernommen? Vorher verändert er nichts.</summary>
    public bool Confirmed { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Ein Fahrzeug — Sammelpunkt für Versicherung, Steuer, Werkstatt und Finanzierung.
/// </summary>
/// <remarks>
/// Strukturgleich zur Immobilie, und das ist keine Bequemlichkeit: beides sind Objekte, an denen
/// Verträge, Rechnungen, Fristen und Dokumente hängen. Die Kfz-Versicherung wird deshalb
/// <em>verknüpft</em>, nicht kopiert — sie steht weiter unter Absicherung, genau wie der
/// Stromvertrag weiter unter Wohnen steht.
/// </remarks>
public class Vehicle : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Name { get; set; }

    /// <summary>Kennzeichen.</summary>
    public required string Plate { get; set; }

    /// <summary>Freitext: Erstwagen, Zweitwagen, Dienstwagen.</summary>
    public string? Usage { get; set; }

    public DateOnly? FirstRegistration { get; set; }

    public int? Mileage { get; set; }

    /// <summary>Verknüpfte Kfz-Versicherung. Sie bleibt eine <see cref="Policy"/>.</summary>
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
}

/// <summary>
/// Ein Beleg im Posteingang — eingescannt, aber noch nicht eingeordnet.
/// </summary>
/// <remarks>
/// <para>Der Handoff will einen <em>Posteingang</em> statt einzelner Belege: die Liste zeigt, was
/// wartet, mit Absender, Seitenzahl und dem Zustand „erkannt“ oder „prüfen“. Ein Beleg bleibt
/// darin, bis Typ <b>und</b> Objekt bestätigt sind — sonst verschwände er in der Ablage, ohne
/// dass jemand ihn zugeordnet hätte.</para>
/// <para>Er verweist auf ein bereits abgelegtes <see cref="Document"/>; die Datei liegt also
/// schon, nur ihre Bedeutung fehlt noch.</para>
/// </remarks>
public class ScanInboxItem : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    /// <summary>Absender, soweit erkannt.</summary>
    public string? Sender { get; set; }

    public int? PageCount { get; set; }

    /// <summary>Hat die Analyse etwas erkannt? Sonst muss ein Mensch hinsehen.</summary>
    public bool Recognised { get; set; }

    /// <summary>Erledigt, sobald Typ und Objekt bestätigt sind.</summary>
    public DateTime? FiledAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
