namespace FinanzApp.Shared.Contracts;

/// <summary>Art einer Buchung. Umbuchungen sind bewusst eine eigene Art und dürfen in
/// Auswertungen weder als Einnahme noch als Ausgabe gezählt werden.</summary>
public enum TransactionKind
{
    Expense = 0,
    Income = 1,
    Transfer = 2,

    /// <summary>
    /// Einlage auf ein Gemeinschaftskonto — Handoff „Gemeinsame Immobilie“, 3.4.
    /// </summary>
    /// <remarks>
    /// <para>Keine Einnahme, weil nichts von außen zufließt: das Geld gehörte schon einem der
    /// Beteiligten. Keine Umbuchung, weil der Eigentümer wechselt — bei einer Umbuchung bleibt
    /// er derselbe.</para>
    /// <para>Sie zählt in die Beteiligungsrechnung und <b>nicht</b> in Einnahmen, Sparquote oder
    /// Liquidität. Sonst stünde dasselbe Geld zweimal im Haushalt.</para>
    /// </remarks>
    Deposit = 3,
}

/// <summary>Für welche Buchungsrichtung eine Kategorie angeboten wird.</summary>
public enum CategoryDirection
{
    Expense = 0,
    Income = 1,
}

/// <summary>
/// Wer ein Konto sehen darf.
/// </summary>
/// <remarks>
/// „Nur ich“ ist eigentümerrelativ und darf nie global ausgewertet werden: die Sichtbarkeit
/// entscheidet sich immer am angemeldeten Benutzer.
/// </remarks>
public enum AccountSharing
{
    /// <summary>Alle Mitglieder des Haushalts.</summary>
    Household = 0,

    /// <summary>Privat — nur der Eigentümer.</summary>
    Private = 1,

    /// <summary>Namentlich für einzelne Mitglieder freigegeben.</summary>
    Named = 2,
}

/// <summary>Art einer Immobilie. Steht in der Zeile unter dem Namen.</summary>
public enum PropertyKind
{
    House = 0,
    Apartment = 1,
    Land = 2,
    Other = 9,
}

/// <summary>
/// Wogegen ein Bericht seinen Zeitraum hält.
/// </summary>
/// <remarks>
/// Der Vorjahresvergleich ist der Standard, weil er denselben Saisonpunkt trifft. Belastbar ist
/// er nur dann — einzelne Monate schwanken stark. Dafür gibt es das Zwölfmonatsmittel als
/// dritte Achse; es glättet, was ein einzelner Vormonat nur zufällig zeigt.
/// </remarks>
public enum ComparisonBasis
{
    /// <summary>Der gleich lange Zeitraum davor.</summary>
    PreviousPeriod = 0,

    /// <summary>Derselbe Zeitraum ein Jahr früher.</summary>
    PreviousYear = 1,

    /// <summary>Das Mittel der zwölf Monate vor dem Zeitraum, hochgerechnet.</summary>
    TwelveMonthAverage = 2,
}

/// <summary>Welcher Bericht im Auswertungsbereich gezeigt wird.</summary>
public enum ReportKind
{
    CostTrend = 0,
    FixedCosts = 1,
    PortfolioGainLoss = 2,
    DataQuality = 3,
    HealthBalance = 4,
    TaxYear = 5,
}

/// <summary>Wonach der Kostentrend seine Kategorien ordnet.</summary>
public enum CostTrendSort
{
    /// <summary>Stärkster Anstieg zuerst — die Frage, die den Bericht veranlasst.</summary>
    Increase = 0,

    Amount = 1,
    Name = 2,
}

/// <summary>Wie sich eine Kategorie gegen ihren Vergleichswert verhält.</summary>
/// <remarks>
/// Die Schwelle liegt bei ±5 %. <see cref="Unknown"/> ist kein Verlegenheitswert: ohne Daten im
/// Vergleichsfenster gibt es keinen Trend, und „stabil“ zu behaupten wäre eine Aussage über
/// nichts.
/// </remarks>
public enum CostTrendStatus
{
    Stable = 0,
    Rising = 1,
    Falling = 2,
    Unknown = 3,
}

/// <summary>
/// Der Zeitraum, über den gerechnet wird — für Budgets wie für Auswertungen derselbe.
/// </summary>
/// <remarks>
/// Hieß früher <c>BudgetPeriod</c>. Der Auswertungsbereich braucht dieselben drei Stufen und
/// dieselbe Auflösung in Grenzen; ein zweiter Aufzählungstyp daneben wäre dieselbe Größe
/// zweimal, und genau davon handelt die erste Regel aus Abschnitt 10b.
/// </remarks>
public enum PeriodScope
{
    Month = 0,
    Quarter = 1,
    Year = 2,
}
