namespace FinanzApp.Shared.Contracts;

/// <summary>Alles, was der Startscreen „Vermögen“ in einem Zug braucht.</summary>
public sealed record DashboardDto
{
    public required NetWorthDto NetWorth { get; init; }
    public required IReadOnlyList<AssetSliceDto> Assets { get; init; }
    public required IReadOnlyList<TimeSeriesPointDto> History { get; init; }
    public required MonthKpiDto Month { get; init; }
    public required LiabilityDto Liability { get; init; }
    public required IReadOnlyList<BudgetDto> TopBudgets { get; init; }
}

/// <summary>
/// Das Vermögen in drei Größen.
/// </summary>
/// <remarks>
/// <para>Früher stand hier ein einziges „Brutto“ — und das waren nur die Finanzwerte. Solange
/// Immobilien auf einem eigenen Bildschirm lagen, fiel das nicht auf. In <em>einer</em> Liste
/// unter <em>einer</em> Kennzahl wird daraus ein Widerspruch: der Kopf nannte 99.880 €, und in
/// den Zeilen darunter stand ein Haus mit 395.000 €. Wer nachrechnet, kommt auf 495 T€.</para>
/// <para>Darum drei Größen und keine zusammengeworfene. Dashboard, Navigationskennzahl und
/// Bestandskopf rechnen aus derselben Quelle — v5-Handoff, Abschnitt 3(b).</para>
/// </remarks>
public sealed record NetWorthDto
{
    /// <summary>
    /// Konten, Depot und kapitalbildende Verträge — was sich zu Geld machen lässt.
    /// </summary>
    public required decimal FinancialAssets { get; init; }

    /// <summary>
    /// Sachwerte: Immobilien.
    /// </summary>
    /// <remarks>
    /// Fahrzeuge stehen hier nicht. Sie tragen in diesem Bestand keinen Wert, sondern
    /// Jahreskosten — einen Vermögenswert für sie zu erfinden wäre schlimmer als keiner.
    /// </remarks>
    public required decimal TangibleAssets { get; init; }

    /// <summary>Wie viele Objekte die Sachwerte ausmachen — „3 Immobilien · Marktwert“.</summary>
    public required int TangibleCount { get; init; }

    /// <summary>Summe aller Verbindlichkeiten, positiv geführt.</summary>
    public required decimal Liabilities { get; init; }

    /// <summary>
    /// Gesamtvermögen netto — die eine Zahl.
    /// </summary>
    /// <remarks>
    /// Sie steht wortgleich im Dashboard-Hero, in der Navigationskennzahl „Heute“ und im Kopf
    /// des Bestands; die Dreiteilung erscheint überall nur als Unterzeile. Der Prototyp hatte
    /// das zweimal verletzt: erst nannte der Bestand-Kopf 99.880 €, während in derselben Liste
    /// eine Immobilie über 395.000 € stand — dann trug der Bestand die Dreiteilung und das
    /// Dashboard weiter die alte Zahl. Zwei Antworten auf dieselbe Frage, 395.000 € auseinander.
    /// </remarks>
    public decimal Net => FinancialAssets + TangibleAssets - Liabilities;

    /// <summary>
    /// Finanzvermögen abzüglich Verbindlichkeiten.
    /// </summary>
    /// <remarks>
    /// Die Größe, die die Verlaufskurve zeichnet: für Sachwerte gibt es keine Monatsreihe, und
    /// einen konstanten Immobilienwert in jeden Punkt zu addieren verschiebt die Kurve nur nach
    /// oben, ohne etwas zu zeigen. Die Kurve sagt darum ausdrücklich, was sie zeichnet.
    /// </remarks>
    public decimal FinancialNet => FinancialAssets - Liabilities;

    /// <summary>Veränderung des Finanzvermögens gegenüber dem Vormonat, vorzeichenbehaftet.</summary>
    public required decimal DeltaPreviousMonth { get; init; }

    /// <summary>Veränderung des Finanzvermögens im laufenden Jahr in Prozent.</summary>
    public required decimal DeltaYearPercent { get; init; }
}

/// <summary>Eine Kachel der Vermögensaufteilung.</summary>
public sealed record AssetSliceDto
{
    public required string Label { get; init; }
    public required string Subtitle { get; init; }
    public required decimal Value { get; init; }

    /// <summary>Anteil am Finanzvermögen, 0…1.</summary>
    public required decimal ShareOfFinancialAssets { get; init; }

    /// <summary>Zielroute für den Tap auf die Kachel.</summary>
    public required string Route { get; init; }
}

public sealed record TimeSeriesPointDto
{
    public required DateOnly Month { get; init; }
    public required decimal Value { get; init; }
}

/// <summary>Monatssummen. Umbuchungen sind hier nicht enthalten.</summary>
public sealed record MonthKpiDto
{
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required decimal Income { get; init; }
    public required decimal Expenses { get; init; }
    public required decimal SavingsRatePercent { get; init; }
}

public sealed record LiabilityDto
{
    public required int LoanId { get; init; }
    public required string Label { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Restschuld, positiv geführt.</summary>
    public required decimal Amount { get; init; }
}
