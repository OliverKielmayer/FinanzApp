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

    /// <summary>
    /// Wem das Konto gehört. <c>null</c> bei Konten aus der Zeit vor den Freigaben.
    /// </summary>
    /// <remarks>
    /// Neu angelegte Konten gehören dem Anmeldenden. Ein Konto ohne Eigentümer steht auf
    /// „Haushalt“ und bleibt damit für alle sichtbar — ein Bestandskonto soll durch die Umstellung
    /// niemandem verschwinden.
    /// </remarks>
    public int? OwnerUserId { get; set; }
    public User? Owner { get; set; }

    public AccountSharing Sharing { get; set; } = AccountSharing.Household;

    /// <summary>Die namentlich Berechtigten, wenn <see cref="Sharing"/> auf Named steht.</summary>
    public List<AccountShare> Shares { get; set; } = [];
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

/// <summary>Ein namentlich Berechtigter an einem Konto.</summary>
/// <remarks>
/// Eigene Tabelle statt einer Liste am Konto: die Freigabe ist eine Beziehung zwischen zwei
/// Datensätzen, und nur so lässt sie sich im Abfragefilter auswerten, ohne Text zu zerlegen.
/// </remarks>
public class AccountShare : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }
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

    // ── Auszugsfelder ──────────────────────────────────────────────────────────────────────
    // Gespeichert wird nur, was der Nutzer beim Import behalten wollte; alles übrige bleibt
    // null. Die Anzeige unterscheidet daran „nicht im Auszug“ von „nicht gespeichert“ — ein
    // Leerstring würde beides ununterscheidbar machen.

    /// <summary>Wertstellung aus dem Auszug — <c>ValDt</c>.</summary>
    public DateOnly? ValueDate { get; set; }

    /// <summary>Währung aus dem Auszug — <c>Amt</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>IBAN der Gegenseite — Grundlage künftiger Zuordnung nach Gegenkonto.</summary>
    public string? CounterpartyIban { get; set; }

    /// <summary>BIC der Gegenseite — <c>Agt</c>.</summary>
    public string? CounterpartyBic { get; set; }

    /// <summary>Verwendungszweck — <c>RmtInf</c>. Wird in der Buchungsliste mitdurchsucht.</summary>
    public string? Purpose { get; set; }

    /// <summary>Buchungsart der Bank — <c>AddtlNtryInf</c>.</summary>
    public string? BookingText { get; set; }

    /// <summary>Geschäftsvorfallcode — <c>Domn/Fmly</c>.</summary>
    public string? BankTransactionCode { get; set; }

    /// <summary>Hauseigener Code der Bank — <c>Prtry</c>.</summary>
    public string? ProprietaryCode { get; set; }

    /// <summary>Der Auszug, aus dem die Buchung stammt — <c>Stmt</c>.</summary>
    public string? StatementId { get; set; }

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

    /// <summary>Präfix des Empfängers, normalisiert verglichen.</summary>
    public required string PayeePattern { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// Wann die Regel gelernt wurde. <c>null</c> für die, die von Anfang an dabei waren.
    /// </summary>
    /// <remarks>
    /// Der Regelscreen unterscheidet daran „beim Import gelernt“ von „seit dem ersten Import“.
    /// Ohne den Zeitpunkt sähen beide gleich aus, und niemand wüsste, was die App sich selbst
    /// beigebracht hat.
    /// </remarks>
    public DateTime? LearnedAt { get; set; }
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
    public PeriodScope Period { get; set; } = PeriodScope.Month;

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


/// <summary>
/// Eine gespeicherte Ansicht des Auswertungsbereichs.
/// </summary>
/// <remarks>
/// <para>Sie hält fest, <em>wie</em> gerechnet wird, nie ein Ergebnis: Bericht, Zeitraum,
/// Vergleich, Sortierung, Depotwahl und die ausgeschlossenen Buchungen. Ein gespeichertes
/// Ergebnis wäre am nächsten Tag falsch, ohne dass jemand es merkt.</para>
/// <para>Sie gehört einem <b>Benutzer</b>, nicht dem Haushalt. Ein Ausschluss ist eine
/// persönliche Entscheidung über eine Auswertung — und die ausgeschlossenen Buchungen können
/// auf Konten liegen, die ein anderes Mitglied gar nicht sieht.</para>
/// </remarks>
public class ReportView : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    /// <summary>Wem sie gehört.</summary>
    public int OwnerUserId { get; set; }

    public required string Name { get; set; }

    public ReportKind Report { get; set; }
    public PeriodScope Period { get; set; }
    public ComparisonBasis Comparison { get; set; }
    public CostTrendSort Sort { get; set; }

    /// <summary>Das gewählte Depot — nur für den Depotbericht.</summary>
    public int? DepotId { get; set; }

    /// <summary>
    /// Die ausgeschlossenen Buchungen.
    /// </summary>
    /// <remarks>
    /// Liegt als kommagetrennte Liste in einer Spalte. Eine eigene Tabelle für eine Handvoll
    /// Zahlen, die nur zusammen mit ihrer Ansicht Sinn ergeben, wäre mehr Verwaltung als Nutzen
    /// — gefragt wird nie nach einer einzelnen, immer nach allen einer Ansicht.
    /// </remarks>
    public List<int> ExcludedTransactionIds { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}
