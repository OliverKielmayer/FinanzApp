namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Der Kopf jedes Berichts — welcher Zeitraum, wogegen, über wie viele Konten.
/// </summary>
/// <remarks>
/// <para>Zeitraum und Vergleichszeitraum rechnen echt durch; sie sind keine Anzeigefilter. Die
/// Klartextzeile steht deshalb im Vertrag und nicht in der Anzeige: sie benennt genau die
/// Grenzen, gegen die gerechnet wurde, und darf nicht auseinanderlaufen mit dem, was der Dienst
/// tatsächlich getan hat.</para>
/// <para><see cref="MonthlyExpenseBase"/> ist die <b>eine</b> Monatsbasis für alle Berichte. Der
/// Handoff nennt es als erste seiner drei Regeln: Fixkosten und Kostentrend rechneten im
/// Prototyp gegen verschiedene Monatssummen und widersprachen sich direkt nebeneinander.
/// </para>
/// </remarks>
public sealed record ReportRangeDto
{
    public required PeriodScope Period { get; init; }
    public required ComparisonBasis Comparison { get; init; }

    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }

    /// <summary>
    /// Wie viele Monate der Zeitraum <b>bislang</b> umfasst.
    /// </summary>
    /// <remarks>
    /// Bei einem abgeschlossenen Zeitraum ist das seine Länge — 1, 3 oder 12. Bei einem
    /// laufenden sind es die angebrochenen Monate: ein Quartal, von dem sechs Wochen um sind,
    /// durch drei zu teilen ergäbe eine Monatsbasis, die es nie gab.
    /// </remarks>
    public required int Months { get; init; }

    /// <summary>„August 2026“.</summary>
    public required string PeriodLabel { get; init; }

    /// <summary>„August 2025“ beziehungsweise „Ø 12 Monate“.</summary>
    public required string ComparisonLabel { get; init; }

    /// <summary>„August 2026 gegen August 2025 · 3 sichtbare Konten“.</summary>
    public required string Line { get; init; }

    public required int VisibleAccountCount { get; init; }

    /// <summary>
    /// Ob im Vergleichsfenster überhaupt Buchungen liegen.
    /// </summary>
    /// <remarks>
    /// Ohne diese Angabe würde ein leerer Vorjahreszeitraum als „alles steigt um 100 %“
    /// erscheinen. Das wäre keine Auskunft, sondern eine erfundene.
    /// </remarks>
    public required bool HasComparison { get; init; }

    /// <summary>Die gemeinsame Monatsbasis: Ausgaben im Zeitraum, geteilt durch die Monate.</summary>
    public required decimal MonthlyExpenseBase { get; init; }
}

/// <summary>Was der Kostentrend abfragt.</summary>
/// <param name="Period">Monat, Quartal oder Jahr.</param>
/// <param name="Comparison">Wogegen gerechnet wird.</param>
/// <param name="Sort">Reihenfolge der Kategorien.</param>
/// <param name="ExcludedTransactionIds">
/// Buchungen, die aus der Auswertung fallen. Der Ausschluss ist eine Eigenschaft der Auswertung,
/// keine der Buchung — deshalb steht er hier und nicht als Merkmal am Datensatz.
/// </param>
/// <param name="OpenCategoryId">
/// Die aufgeklappte Kategorie. Nur sie liefert den Drilldown; die übrigen Zeilen brauchen ihn
/// nicht, und ihn für alle mitzuschicken wäre bei einem Jahreszeitraum das Vielfache an Daten.
/// </param>
public sealed record CostTrendRequest(
    PeriodScope Period = PeriodScope.Month,
    ComparisonBasis Comparison = ComparisonBasis.PreviousYear,
    CostTrendSort Sort = CostTrendSort.Increase,
    IReadOnlyList<int>? ExcludedTransactionIds = null,
    int? OpenCategoryId = null);

public sealed record CostTrendDto
{
    public required ReportRangeDto Range { get; init; }

    /// <summary>Summe der Zeilen — nicht mehr und nicht weniger.</summary>
    public required decimal Total { get; init; }

    public required decimal ComparisonTotal { get; init; }

    /// <summary><c>null</c>, wenn es nichts zu vergleichen gibt.</summary>
    public required decimal? ChangePercent { get; init; }

    /// <summary>Wie viele Kategorien um mehr als 5 % steigen.</summary>
    public required int RisingCount { get; init; }

    /// <summary>„3 Kategorien steigen um mehr als 5 % — Freizeit, Gesundheit, Wohnen“.</summary>
    public required string RisingLine { get; init; }

    /// <summary>Wie viele Buchungen ausgeschlossen sind, über alle Kategorien.</summary>
    public required int ExcludedCount { get; init; }

    /// <summary>
    /// Ausgaben ohne Kategorie im Zeitraum — Anzahl und Betrag.
    /// </summary>
    /// <remarks>
    /// Sie stehen in keiner Zeile, denn eine Kategorieauswertung ohne Kategorie hat keinen Ort.
    /// Verschwiegen wäre <see cref="Total"/> aber kleiner als die Ausgaben des Zeitraums, ohne
    /// dass jemand sagen könnte warum. Die Zahl gehört also dazu.
    /// </remarks>
    public required int UncategorisedCount { get; init; }

    public required decimal UncategorisedAmount { get; init; }

    public required IReadOnlyList<CostTrendRowDto> Rows { get; init; }
}

public sealed record CostTrendRowDto
{
    public required int CategoryId { get; init; }
    public required string Name { get; init; }

    /// <summary>Ausgaben der Kategorie im Zeitraum, ohne die ausgeschlossenen.</summary>
    public required decimal Amount { get; init; }

    public required decimal ComparisonAmount { get; init; }

    /// <summary>Monatsmittel der zwölf Monate <b>vor</b> dem Zeitraum.</summary>
    public required decimal TwelveMonthAverage { get; init; }

    /// <summary><c>null</c>, wenn kein Vergleichswert vorliegt — nie 0 und nie 100.</summary>
    public required decimal? ChangePercent { get; init; }

    public required CostTrendStatus Status { get; init; }

    /// <summary>24 Monatssummen, älteste zuerst.</summary>
    public required IReadOnlyList<decimal> Spark { get; init; }

    /// <summary>Ob auf dieser Kategorie ein Budget läuft — der Knopf heißt dann „prüfen“.</summary>
    public required bool HasBudget { get; init; }

    /// <summary>Buchungen im Zeitraum, die zählen.</summary>
    public required int TransactionCount { get; init; }

    /// <summary>Und wie viele davon ausgeschlossen sind.</summary>
    public required int ExcludedCount { get; init; }

    /// <summary>Nur für die aufgeklappte Kategorie gefüllt.</summary>
    public required IReadOnlyList<CostTrendPayeeDto> Payees { get; init; }

    /// <summary>Nur für die aufgeklappte Kategorie gefüllt.</summary>
    public required IReadOnlyList<CostTrendEntryDto> Entries { get; init; }
}

/// <summary>Eine Empfängergruppe im Drilldown — ohne die ausgeschlossenen Buchungen.</summary>
public sealed record CostTrendPayeeDto(string Payee, int Count, decimal Amount);

/// <summary>Eine Einzelbuchung im Drilldown.</summary>
public sealed record CostTrendEntryDto(
    int Id, DateOnly BookingDate, string Payee, decimal Amount, string AccountName, bool Excluded);


// ── Fixkosten & vertragliche Bindung ────────────────────────────────────────────────────────

/// <summary>Was der Fixkostenbericht abfragt.</summary>
/// <remarks>
/// Zeitraum, Vergleich und Ausschlüsse wie beim Kostentrend — die Monatsbasis unten ist
/// dieselbe Zahl, und sie muss auf dieselben Eingaben hören.
/// </remarks>
public sealed record FixedCostsRequest(
    PeriodScope Period = PeriodScope.Month,
    ComparisonBasis Comparison = ComparisonBasis.PreviousYear,
    IReadOnlyList<int>? ExcludedTransactionIds = null);

/// <summary>Wie fest ein Posten liegt.</summary>
public enum FixedCostBinding
{
    /// <summary>Kündbar — mit Frist.</summary>
    Cancellable = 0,

    /// <summary>Nicht kündbar, etwa ein laufendes Darlehen.</summary>
    Fixed = 1,

    /// <summary>Kapitalbildend: der Betrag fließt ab, bleibt aber Vermögen.</summary>
    Saving = 2,
}

public sealed record FixedCostsDto
{
    public required ReportRangeDto Range { get; init; }

    /// <summary>Summe der gebundenen Posten je Monat.</summary>
    public required decimal MonthlyFixed { get; init; }

    /// <summary>
    /// Was von der Monatsbasis übrig bleibt.
    /// </summary>
    /// <remarks>
    /// Ein Restwert, keine eigene Erhebung: Basis minus gebunden. Er kann negativ werden, wenn
    /// die Verträge mehr ausweisen, als im Zeitraum tatsächlich gebucht wurde — dann sagt
    /// <see cref="Note"/>, dass die beiden Seiten aus verschiedenen Quellen kommen.
    /// </remarks>
    public required decimal MonthlyFree { get; init; }

    /// <summary>Anteil der gebundenen Kosten an der Monatsbasis, 0…100.</summary>
    public required decimal FixedSharePercent { get; init; }

    /// <summary>
    /// Was zu den Zahlen zu sagen ist — auch, wenn etwas nicht aufgeht.
    /// </summary>
    /// <remarks>
    /// Ohne Beträge. Die Bezugsgröße gehört in den Balkentext, aber der entsteht in der
    /// Anzeige: ein hier fertig formatierter Euro-Betrag käme an „Beträge verbergen“ vorbei.
    /// </remarks>
    public required string Note { get; init; }

    public required IReadOnlyList<FixedCostRowDto> Rows { get; init; }

    /// <summary>
    /// Verträge, die keinen Beitrag ausweisen — sie stehen in keiner Zeile.
    /// </summary>
    /// <remarks>
    /// Ein Posten über 0,00 € ist in einer Kostenliste kein Eintrag, sondern eine Lücke im
    /// Bestand. Er gehört gezählt und benannt, nicht als Nullzeile zwischen die Beträge
    /// gesetzt — dort sagt er nichts und verdeckt, was etwas sagt.
    /// </remarks>
    public required int WithoutAmountCount { get; init; }
}

public sealed record FixedCostRowDto
{
    public required string Name { get; init; }

    /// <summary>Auf den Monat umgerechnet, egal in welchem Takt gezahlt wird.</summary>
    public required decimal MonthlyAmount { get; init; }

    /// <summary>Bereich und Kündigungsfrist im Klartext.</summary>
    public required string Note { get; init; }

    public required FixedCostBinding Binding { get; init; }

    /// <summary>Ob die Frist jetzt auf den Tisch gehört.</summary>
    public required bool NoticeDue { get; init; }
}

// ── Depot: Gewinn und Verlust ───────────────────────────────────────────────────────────────

public sealed record DepotChoiceDto(int Id, string Name);

/// <summary>
/// Gewinn und Verlust eines Depots — <b>unrealisiert</b>.
/// </summary>
/// <remarks>
/// Ohne Steuern und Gebühren. Realisierte Gewinne brauchen Wertpapiertransaktionen; die gibt
/// es noch nicht, und eine Zahl, die so tut, als gäbe es sie, wäre schlimmer als keine.
/// </remarks>
public sealed record PortfolioGainDto
{
    /// <summary>Alle Depots — die Auswahl steht als Chipreihe über dem Bericht.</summary>
    public required IReadOnlyList<DepotChoiceDto> Depots { get; init; }

    public required int DepotId { get; init; }
    public required string DepotName { get; init; }

    public required decimal CostBasis { get; init; }
    public required decimal CurrentValue { get; init; }
    public required decimal Gain { get; init; }

    /// <summary>Bezogen auf den Einstand. <c>null</c>, wenn kein Einstand hinterlegt ist.</summary>
    public required decimal? GainPercent { get; init; }

    /// <summary>Stand der Kurse — der älteste, nicht der jüngste.</summary>
    public required DateTime? PricesAsOf { get; init; }

    public required IReadOnlyList<PortfolioGainRowDto> Positions { get; init; }
}

public sealed record PortfolioGainRowDto
{
    public required string Name { get; init; }
    public required string Isin { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Einstand je Stück. <c>null</c> bei Stückzahl null.</summary>
    public required decimal? CostPerUnit { get; init; }

    public required decimal Price { get; init; }
    public required decimal Value { get; init; }
    public required decimal Gain { get; init; }
    public required decimal? GainPercent { get; init; }
}
