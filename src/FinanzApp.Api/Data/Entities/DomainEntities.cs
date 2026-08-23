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

/// <summary>Ein Versicherungsvertrag — Hausrat, Haftpflicht, Kfz, Risikoleben.</summary>
/// <remarks>
/// Nicht zu verwechseln mit <see cref="InsurancePolicy"/>: das ist die Kapitallebensversicherung
/// als <em>Vermögenswert</em> mit Rückkaufswert und speist die Dashboard-Kachel. Hier geht es um
/// den Vertrag mit Beitrag, Frist und Dokumenten.
/// </remarks>
public class Insurance : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Name { get; set; }
    public required string Insurer { get; set; }
    public string? PolicyNumber { get; set; }

    public decimal Premium { get; set; }
    public PremiumInterval PremiumInterval { get; set; }

    public DateOnly? StartsOn { get; set; }

    /// <summary>Vertragsende. Aus ihm und der Frist entsteht die Kündigungsfrist.</summary>
    public DateOnly? EndsOn { get; set; }

    /// <summary>Kündigungsfrist in Monaten vor Vertragsende.</summary>
    public int NoticePeriodMonths { get; set; }

    /// <summary>Konto, von dem der Beitrag abgeht.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

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
