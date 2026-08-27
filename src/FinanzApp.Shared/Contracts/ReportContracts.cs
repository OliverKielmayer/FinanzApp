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
