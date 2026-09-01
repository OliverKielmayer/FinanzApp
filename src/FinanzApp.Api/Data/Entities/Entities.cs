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

    /// <summary>
    /// Was diese Person monatlich einzahlen soll — nur beim Gemeinschaftskonto.
    /// </summary>
    /// <remarks>
    /// Null heißt: kein Soll vereinbart. Dann steht am Schirm der Eingang ohne Vergleich; eine
    /// Null als Soll wäre die Aussage, es sei nichts vereinbart <em>und</em> nichts erwartet.
    /// </remarks>
    public decimal? MonthlyTarget { get; set; }

    /// <summary>Tag im Monat, zu dem es erwartet wird — 1 bis 31, oder null.</summary>
    public int? DueDay { get; set; }
}

public class Category : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public CategoryDirection Direction { get; set; }

    /// <summary>
    /// Wie Buchungen dieser Kategorie steuerlich einzuordnen sind.
    /// </summary>
    /// <remarks>
    /// Am Kategorienamen zu erkennen, was eine Handwerkerleistung ist, ginge nur, solange
    /// niemand seine Kategorien umbenennt. Die Einordnung gehört deshalb an die Kategorie und
    /// wird gepflegt, nicht geraten.
    /// </remarks>
    public TaxCategory TaxCategory { get; set; }

    /// <summary>
    /// Ob Ausgaben dieser Kategorie zum Objekt gehören — Handoff „Gemeinsame Immobilie“, 3.4.
    /// </summary>
    /// <remarks>
    /// Trennt Hauskosten von Lebenshaltung. Ohne die Trennung wäre jede €/m²-Zahl falsch, weil
    /// Lebensmittel vom selben Konto abgehen wie der Strom für das Haus. Wie die steuerliche
    /// Einordnung hängt sie an der Kategorie und nicht an ihrem Namen: sie wird gepflegt, nicht
    /// geraten.
    /// </remarks>
    public bool PropertyRelated { get; set; }

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

    /// <summary>
    /// Wer eingezahlt hat — nur bei einer Einlage.
    /// </summary>
    /// <remarks>
    /// Eine Einlage ohne Person ließe sich niemandem zurechnen, und der Ausgleichsstand lebt
    /// davon, wer wie viel eingebracht hat. Der Betrag allein sagt darüber nichts.
    /// </remarks>
    public int? DepositUserId { get; set; }

    public User? DepositUser { get; set; }

    /// <summary>
    /// Für welches Objekt eingezahlt wurde — nur bei einer Einlage.
    /// </summary>
    /// <remarks>
    /// Die Beteiligungsrechnung gehört zum Objekt: zwei Häuser mit verschiedenen Anteilen führen
    /// zwei Ausgleichsstände. Am Konto hängt sie deshalb nicht — ein Konto kann für mehrere
    /// Objekte laufen und ein Objekt über mehrere Konten.
    /// </remarks>
    public int? PropertyId { get; set; }

    public Property? Property { get; set; }

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

/// <summary>
/// Eine ausgeführte Order im Depot — v5-Handoff, Abschnitt 11.1.
/// </summary>
/// <remarks>
/// <para>Sie ist die Quelle des Depots: Positionen werden daraus <b>abgeleitet</b> und nicht
/// gepflegt. Der Prototyp führte beides nebeneinander und wies dieselbe ISIN mit drei
/// verschiedenen Stückzahlen aus — der falsche Depotwert lief über das Finanzvermögen bis ins
/// Gesamtvermögen netto.</para>
/// <para>Gespeichert wird, was tatsächlich ausgeführt wurde, nicht was bestellt war.</para>
/// </remarks>
/// <summary>
/// Ein Kurs eines Wertpapiers an einem Tag — v5-Handoff, Abschnitt 16.5.
/// </summary>
/// <remarks>
/// <para><b>Die Reihe gehört der Anwendung, nicht der Quelle.</b> Beide in Frage kommenden
/// Anbieter sind inoffiziell: keine dokumentierte Schnittstelle, keine Zusage über Bestand oder
/// Format. Wer seine Vermögenszahlen daran hängt, verliert sie beim ersten Umbau der Gegenseite.
/// Hier steht deshalb jeder je gesehene Kurs, mit seiner Herkunft, und er bleibt stehen, wenn
/// die Quelle ausfällt oder gewechselt wird.</para>
/// <para>Eindeutig über ISIN und Tag: ein zweiter Abruf desselben Tages aktualisiert, statt zu
/// verdoppeln. Bewertet wird immer mit dem jüngsten <em>gespeicherten</em> Kurs, nie mit einem
/// Live-Wert, der beim nächsten Aufruf fehlen kann.</para>
/// </remarks>
public class Quote : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Isin { get; set; }

    /// <summary>Der Handelstag, nicht der Abrufzeitpunkt.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Schlusskurs bzw. letzter festgestellter Kurs des Tages.</summary>
    public decimal Close { get; set; }

    public required string Currency { get; set; }

    /// <summary>Wer den Kurs geliefert hat. Steht sichtbar an der Position.</summary>
    public required string Source { get; set; }

    /// <summary>Wann er geholt wurde — getrennt von <see cref="Date"/> und nie mit ihm verwechselt.</summary>
    public DateTime FetchedAt { get; set; }
}

/// <summary>
/// Ein Abrufdurchgang und was er ergab.
/// </summary>
/// <remarks>
/// Der Zustand des Kursbands liest hier und wird nicht aus den Kursen erraten. „Zuletzt
/// versucht und gescheitert“ ist ein anderer Zustand als „zuletzt erfolgreich, aber schon zwei
/// Tage her“, und aus einer Kurstabelle allein ließen sich beide nicht unterscheiden.
/// </remarks>
public class QuoteRun : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }

    public required string Source { get; set; }

    /// <summary>Wie viele Papiere gefragt wurden.</summary>
    public int Requested { get; set; }

    /// <summary>Wie viele Kurse danach in der Reihe standen.</summary>
    public int Stored { get; set; }

    /// <summary>Bei wie vielen es nicht klappte.</summary>
    public int Failed { get; set; }

    /// <summary>Der erste aufgetretene Grund. <c>null</c>, wenn alles ging.</summary>
    public string? Problem { get; set; }

    /// <summary>Ob der Durchgang von Hand angestoßen wurde.</summary>
    public bool Manual { get; set; }
}

public class DepotTrade : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int DepotId { get; set; }
    public Depot? Depot { get; set; }

    public required string SecurityName { get; set; }
    public required string Isin { get; set; }
    public string? Wkn { get; set; }

    public DepotTradeKind Kind { get; set; }
    public DepotOrderType OrderType { get; set; }

    /// <summary>Das gesetzte Limit, wenn es eine Limit-Order war.</summary>
    public decimal? LimitPrice { get; set; }

    /// <summary>Ausführungszeitpunkt — Datum <em>und</em> Uhrzeit, beides trägt die Wiedererkennung.</summary>
    public DateTime ExecutedAt { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Ausführungskurs.
    /// </summary>
    /// <remarks>
    /// Kein Cent-Konverter: die Broker rechnen mit mehr als zwei Nachkommastellen (89,238), und
    /// auf Cent gerundet ergäbe Stück × Kurs nicht mehr den Wert, der wirklich belastet wurde.
    /// </remarks>
    public decimal Price { get; set; }

    /// <summary>Stück × Kurs, wie die Datei ihn ausweist. Positiv geführt.</summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Mindermengenzuschlag — eine Gebühr, keine Kursdifferenz.
    /// </summary>
    /// <remarks>
    /// Sie liegt <em>auf</em> dem Wert, nicht darin: geprüft an der echten Datei, dort ist
    /// Wert exakt Stück × Kurs. Sie gehört in die Anschaffungskosten, bleibt aber ein eigener
    /// Bestandteil, damit niemand sie für einen schlechteren Kurs hält.
    /// </remarks>
    public decimal Fee { get; set; }

    /// <summary>
    /// Woran ein schon eingelesener Satz wiedererkannt wird.
    /// </summary>
    /// <remarks>
    /// Die Datei führt keine Ordernummer. Der Handoff nennt Ausführungsdatum, Uhrzeit, Stück
    /// und Kurs; die ISIN steht zusätzlich darin — sie kann keinen echten Wiedergänger
    /// verstecken, wohl aber zwei zufällig gleiche Ausführungen verschiedener Papiere trennen.
    /// </remarks>
    public required string ImportReference { get; set; }
}

/// <summary>
/// Eine Quartalsaufstellung der Bank — Bestandsnachweis nach MiFID II, v5-Handoff 11.2.
/// </summary>
/// <remarks>
/// Sie ist die zweite Quelle neben den Ausführungen und dient dem Abgleich: stimmen die
/// Stückzahlen, ist der Depotwert belegt. Das ist der einzige Weg, eine Depotbewertung zu
/// prüfen, ohne dem Broker blind zu glauben.
/// </remarks>
public class DepotStatement : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int DepotId { get; set; }
    public Depot? Depot { get; set; }

    /// <summary>
    /// Der Stichtag, zu dem der Bestand ausgewiesen ist — fachlich maßgeblich.
    /// </summary>
    /// <remarks>
    /// Nicht zu verwechseln mit <see cref="IssuedOn"/>. Dieselbe Unterscheidung wie beim
    /// Statusreport der Lebensversicherung: ein Schreiben vom Mai über den Bestand vom März
    /// sagt etwas über den März.
    /// </remarks>
    public DateOnly AsOf { get; set; }

    /// <summary>Wann das Schreiben erstellt wurde. Sagt nichts über den Bestand.</summary>
    public DateOnly? IssuedOn { get; set; }

    public string? DepotNumber { get; set; }
    public string? Reference { get; set; }

    /// <summary>Verwahrstelle mit Lagerstelle, wie das Schreiben sie nennt.</summary>
    public string? Custodian { get; set; }

    /// <summary>Das abgelegte Schreiben. <c>null</c>, solange keines hinterlegt ist.</summary>
    public int? DocumentId { get; set; }
    public Document? Document { get; set; }

    public List<DepotStatementPosition> Positions { get; set; } = [];
}

/// <summary>Eine Zeile des Bestandsnachweises.</summary>
public class DepotStatementPosition : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int StatementId { get; set; }
    public DepotStatement? Statement { get; set; }

    public required string SecurityName { get; set; }
    public required string Isin { get; set; }
    public string? Wkn { get; set; }

    /// <summary>Nominale — die Stückzahl, die die Bank ausweist.</summary>
    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    /// <summary>
    /// Kurswert, wie das Schreiben ihn nennt.
    /// </summary>
    /// <remarks>
    /// Gespeichert statt gerechnet: die Bank rundet auf ihre Weise, und der Abgleich soll gegen
    /// das prüfen, was wirklich dasteht — nicht gegen unsere Nachrechnung davon.
    /// </remarks>
    public decimal Value { get; set; }

    /// <summary>Verwahrart, Lagerland, Lagerstelle — wie das Schreiben sie ausweist.</summary>
    public string? SafeCustody { get; set; }
    public string? Country { get; set; }
    public string? Depository { get; set; }
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
