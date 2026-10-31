namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Fachbereich, unter dem ein Dokument oder Objekt einsortiert ist. Speist die Chips der
/// Dokumentliste und die Bereichsliste auf „Mehr“.
/// </summary>
public enum DocumentArea
{
    Other = 0,
    Insurance = 1,
    Health = 2,
    Housing = 3,
    Work = 4,
    Finance = 5,

    /// <summary>Fahrzeuge — Versicherung, Werkstatt, Steuer, Finanzierung.</summary>
    /// <remarks>
    /// Die Anwendung kennt Fahrzeuge als Objekt, die Ablage kannte sie nicht: Jede Werkstattrechnung
    /// fiel nach „Sonstiges“, obwohl daneben ein Fahrzeug mit Kennzeichen steht.
    /// </remarks>
    Vehicle = 6,

    /// <summary>Persönliche Papiere — Ausweis, Zeugnis, Urkunde.</summary>
    Personal = 7,
}

/// <summary>
/// Woher der gelesene Text stammt.
/// </summary>
/// <remarks>
/// Es ist ein Unterschied, ob eine Zahl im Papier steht oder ob eine Erkennung sie aus einem
/// Bild gelesen hat — und der Unterschied gehört an den Beleg. Was erkannt wurde, prüfen die
/// Rechenproben wie jeden anderen Wert nach; verschwiegen wäre es eine Zahl wie jede andere.
/// </remarks>
public enum TextSource
{
    /// <summary>Aus der Textebene eines PDF.</summary>
    PdfTextLayer = 0,

    /// <summary>Ein Bild, das niemand gelesen hat — es ist keine Erkennung eingerichtet.</summary>
    Image = 1,

    /// <summary>Aus einer Texterkennung über ein Bild.</summary>
    Recognition = 2,
}

/// <summary>
/// Wer ein Dokument sehen darf.
/// </summary>
/// <remarks>
/// <para>Zwei Stufen, nicht vier. Konten tragen zusätzlich „namentlich“ und „Gemeinschaft“;
/// beides beantwortet Fragen, die ein Dokument nicht stellt — das eine den Zugriff einzelner,
/// das andere ein Einzahlungssoll. Für den Fall, der sie verlangt hat — eine Arztrechnung, die
/// nur den angeht, der sie bekommen hat — genügt die Unterscheidung „alle“ oder „ich“.</para>
/// <para><see cref="Household"/> ist die Vorgabe und bleibt es. Ein Bestand, der nach einer
/// Erweiterung plötzlich halb unsichtbar wäre, nähme dem Haushalt Dokumente weg, ohne dass
/// jemand etwas getan hat.</para>
/// </remarks>
public enum DocumentSharing
{
    /// <summary>Alle Mitglieder des Haushalts.</summary>
    Household = 0,

    /// <summary>Nur der Eigentümer — eigentümerrelativ, nie global auswertbar.</summary>
    Private = 1,
}

public enum DocumentStatus
{
    Active = 0,

    /// <summary>Gültigkeit abgelaufen, Eintrag bleibt auffindbar.</summary>
    Expired = 1,

    Archived = 2,
}

/// <summary>
/// Woran ein Dokument hängt. Bewusst als Aufzählung im Vertrag statt als Fremdschlüssel je
/// Zieltyp — ein neuer Zieltyp soll das Dokumentmodell nicht anfassen müssen.
/// </summary>
public enum LinkTargetType
{
    Account = 0,
    Transaction = 1,
    Portfolio = 2,
    /// <summary>Vorsorge- oder Absicherungsvertrag — beide liegen im selben Modell.</summary>
    Policy = 3,

    /// <summary>Frei. Trug früher die Kapitallebensversicherung, die jetzt eine
    /// <see cref="Policy"/> mit gesetztem Flag ist.</summary>
    [Obsolete("Ging in Policy auf.")]
    LifeInsurance = 4,
    Loan = 5,
    Property = 6,
    Contract = 7,
    Invoice = 8,
    Employer = 9,
    EmploymentContract = 10,
    Payslip = 11,
    MedicalBill = 12,
    Vehicle = 13,

    /// <summary>Ein abgelegter Beleg.</summary>
    Document = 14,
}

/// <summary>
/// Stationen eines PKV-Vorgangs. Die Reihenfolge ist die Kette der Oberfläche; die beiden
/// Ausgänge <see cref="PartiallyReimbursed"/> und <see cref="Rejected"/> stehen daneben.
/// </summary>
public enum MedicalBillStatus
{
    Recorded = 0,
    Submitted = 1,
    SettlementReceived = 2,
    PaymentReceived = 3,
    Completed = 4,
    PartiallyReimbursed = 5,
    Rejected = 6,
}

public enum TaskState
{
    Open = 0,

    /// <summary>Angestoßen, aber es fehlt eine Antwort von außen.</summary>
    Waiting = 1,

    Done = 2,
}

/// <summary>Warum eine Aufgabe entstanden ist. Wird in der Zeile erklärt.</summary>
public enum TaskSource
{
    Manual = 0,
    ContractNotice = 1,
    InvoiceDue = 2,
    ReimbursementOverdue = 3,
    DocumentExpiry = 4,
    MedicalBillOpen = 5,

    /// <summary>
    /// Ein Beleg, dessen gelesene Werte noch niemand übernommen hat.
    /// </summary>
    /// <remarks>
    /// Die einzige Quelle ohne Termin: hier läuft nichts ab, es bleibt nur etwas liegen. Sie
    /// gibt es, weil ein weggeräumter Beleg aus dem Scaneingang verschwindet und danach nichts
    /// mehr auf seine offenen Werte zeigte.
    /// </remarks>
    DocumentValuesOpen = 6,
}

public enum InvoiceStatus
{
    Open = 0,
    Paid = 1,
    Cancelled = 2,
}

/// <summary>
/// Vertragsart einer <c>Policy</c>. Die ersten fünf sind kapitalbildend, der Rest nicht —
/// maßgeblich für die Vermögensrechnung bleibt aber das Flag am Vertrag, nicht diese Liste.
/// </summary>
public enum PolicyKind
{
    // Vorsorge & Kapital
    CapitalLife = 0,
    Pension = 1,
    Riester = 2,
    BuildingSociety = 3,
    OccupationalPension = 4,

    // Absicherung
    TermLife = 20,
    DisabilityInsurance = 21,
    Liability = 22,
    HouseholdContents = 23,
    Building = 24,
    Vehicle = 25,
    Accident = 26,
    LegalExpenses = 27,
    Health = 28,
    Other = 99,
}

public enum PremiumInterval
{
    Monthly = 0,
    Quarterly = 1,
    HalfYearly = 2,
    Yearly = 3,
}


/// <summary>Art eines Arbeitsverhältnisses.</summary>
public enum EmploymentKind
{
    Permanent = 0,
    FixedTerm = 1,
    PartTime = 2,
    Freelance = 3,
}

/// <summary>Art einer Vereinbarung zum Arbeitsverhältnis.</summary>
public enum WorkAgreementKind
{
    SalaryChange = 0,
    Bonus = 1,
    OccupationalPension = 2,
    Other = 9,
}

/// <summary>Richtung einer Depotausführung.</summary>
public enum DepotTradeKind
{
    Buy = 0,
    Sell = 1,
}

/// <summary>Wie die Order aufgegeben war.</summary>
public enum DepotOrderType
{
    Market = 0,
    Limit = 1,
}
