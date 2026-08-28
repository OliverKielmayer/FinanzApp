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
