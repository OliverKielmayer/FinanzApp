using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Data.Entities;

/// <summary>
/// Gehört einem Haushalt. Der <c>DbContext</c> hängt an diese Schnittstelle den globalen
/// Abfragefilter — dadurch kann kein Dienst die Mandantentrennung versehentlich umgehen.
/// </summary>
public interface IHouseholdOwned
{
    int HouseholdId { get; set; }
}

public enum AccountKind
{
    Checking = 0,
    Savings = 1,
}

public class Account : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }

    /// <summary>Kurzform für die Buchungsliste, z. B. „Sparkasse“.</summary>
    public required string ShortName { get; set; }

    /// <summary>Kreditinstitut. Speist die Untertitel der Vermögenskacheln.</summary>
    public required string BankName { get; set; }

    public AccountKind Kind { get; set; }
    public string? Iban { get; set; }
    public decimal? InterestRatePercent { get; set; }
    public decimal? InterestYearToDate { get; set; }

    /// <summary>Anfangsbestand. Der Saldo ergibt sich aus Anfangsbestand plus allen Buchungen —
    /// er wird nirgends redundant gespeichert.</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>Wann der Kontostand zuletzt mit der Bank abgeglichen wurde. Nicht identisch
    /// mit dem Datum der letzten Buchung — die Oberfläche zeigt daraus „heute“ oder ein Datum.</summary>
    public DateOnly BalanceAsOf { get; set; }

    public List<Transaction> Transactions { get; set; } = [];
}

public class Category : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public CategoryDirection Direction { get; set; }

    public List<Transaction> Transactions { get; set; } = [];
    public List<Budget> Budgets { get; set; } = [];
    public List<CategorizationRule> Rules { get; set; } = [];
}

public class Transaction : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public DateOnly BookingDate { get; set; }
    public required string Payee { get; set; }
    public TransactionKind Kind { get; set; }

    /// <summary>Vorzeichenbehaftet: Ausgaben und abgehende Umbuchungen negativ.</summary>
    public decimal Amount { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Gegenkonto einer Umbuchung. Die Gegenbuchung selbst legt der Prototyp noch
    /// nicht an — siehe „Offene Punkte“ in der README.</summary>
    public int? CounterAccountId { get; set; }

    public string? Note { get; set; }

    /// <summary>Referenz aus der Importdatei. Erkennt bereits importierte Sätze wieder.</summary>
    public string? ImportReference { get; set; }

    /// <summary>Idempotenzschlüssel des anlegenden Clients.</summary>
    public Guid? RequestKey { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class CategorizationRule : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    /// <summary>Präfix des Empfängers, case-insensitiv verglichen.</summary>
    public required string PayeePattern { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

public class Budget : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }

    /// <summary>Geplanter Betrag je Monat. Quartals- und Jahressicht rechnen hoch.</summary>
    public decimal PlannedPerMonth { get; set; }

    /// <summary>Reihenfolge in der Liste. Das Dashboard zeigt die ersten drei.</summary>
    public int SortOrder { get; set; }

    /// <summary>Bezugszeitraum. Der Plan wird intern immer je Monat geführt und hochgerechnet.</summary>
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Month;

    /// <summary>Ab wann das Budget gilt. Vorher zählt es nicht mit.</summary>
    public DateOnly? ValidFrom { get; set; }

    /// <summary>Ab welchem Anteil gewarnt wird — 80, 90 oder 100 Prozent.</summary>
    public int WarnThresholdPercent { get; set; } = 90;

    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}

public class Depot : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }

    /// <summary>Broker oder Bank, bei der das Depot liegt.</summary>
    public string? Broker { get; set; }

    public string? Number { get; set; }

    /// <summary>Depotart — Einzeldepot, Gemeinschaftsdepot, Kinderdepot.</summary>
    public string? DepotKind { get; set; }

    /// <summary>
    /// Angegebener Depotwert mit Stichtag. Greift nur, solange keine Positionen erfasst sind —
    /// sobald welche da sind, rechnet der Bestand und nicht mehr die Angabe.
    /// </summary>
    public decimal? StatedValue { get; set; }

    public DateOnly? ValuationDate { get; set; }

    /// <summary>Verrechnungskonto.</summary>
    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Woher die Kurse kommen sollen.</summary>
    public string? QuoteSource { get; set; }

    /// <summary>Zeitgewichtete Rendite p. a. Wird vom Depotanbieter geliefert.</summary>
    public decimal TwrorPercent { get; set; }

    public List<PortfolioPosition> Positions { get; set; } = [];

    /// <summary>
    /// Der Wert, der zählt: die Positionen, wenn es welche gibt, sonst die Angabe aus der Anlage.
    /// </summary>
    public decimal Value => Positions.Count > 0
        ? Positions.Sum(p => p.Quantity * p.Price)
        : StatedValue ?? 0m;
}

public class PortfolioPosition : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int DepotId { get; set; }
    public Depot? Depot { get; set; }
    public required string Name { get; set; }
    public required string Isin { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Letzter bekannter Kurs.</summary>
    public decimal Price { get; set; }

    /// <summary>Einstandswert. Gewinn und Rendite werden daraus gerechnet, nie gespeichert.</summary>
    public decimal CostBasis { get; set; }

    public DateTime PriceAsOf { get; set; }
}

public class Loan : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public required string Lender { get; set; }
    public decimal RemainingDebt { get; set; }
    public decimal InterestRatePercent { get; set; }
    public decimal Installment { get; set; }
    public DateOnly NextPaymentDate { get; set; }
}

public class ImportProfile : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public required string BankName { get; set; }

    /// <summary>Erkanntes Format, z. B. „CAMT.053“ oder „CSV“.</summary>
    public required string Format { get; set; }
}

/// <summary>Monatswert der Vermögensentwicklung. In einem Vollausbau würde diese Reihe
/// aus historischen Salden berechnet; hier ist sie eine eigene Tabelle.</summary>
public class NetWorthSnapshot : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Monatswert der Depotentwicklung.</summary>
public class PortfolioSnapshot : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Value { get; set; }
}

/// <summary>Sicherheitszustand für die Sammelseite „Mehr“.</summary>
public class SecurityState : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public DateTime LastBackup { get; set; }
}
