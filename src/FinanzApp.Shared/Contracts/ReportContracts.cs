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
    /// Dieselben Posten nach Kostenart gebündelt, die schwerste Gruppe zuerst.
    /// </summary>
    /// <remarks>
    /// Zwanzig Einzelzeilen beantworten die Frage nicht, die man an eine Fixkostenliste stellt:
    /// <em>Wofür geht das Geld?</em> Vier Gruppen beantworten sie. Die Zeilen bleiben trotzdem
    /// da — die Gruppe sagt, wie viel, die Zeile, was gekündigt werden könnte.
    /// </remarks>
    public required IReadOnlyList<FixedCostGroupDto> Groups { get; init; }

    /// <summary>
    /// Dieselben Posten nach Konto gegliedert, das schwerste Konto zuerst.
    /// </summary>
    /// <remarks>
    /// <para>Beantwortet eine andere Frage als die Kostenart: nicht <em>wofür</em> das Geld
    /// geht, sondern <em>von wo</em>. Wer zwei Konten führt, braucht beides — die
    /// Kostenart, um zu entscheiden, was weg kann, und das Konto, um zu wissen, welches davon
    /// jeden Monat gedeckt sein muss.</para>
    /// <para>Posten ohne eingetragenes Konto stehen in einer eigenen Gruppe am Ende. Sie zu
    /// verteilen hieße raten; sie wegzulassen hieße, dass die Kontosummen nicht mehr die
    /// Gesamtsumme ergeben.</para>
    /// </remarks>
    public required IReadOnlyList<FixedCostAccountDto> Accounts { get; init; }

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

    /// <summary>
    /// Die Kostenart, unter der dieser Posten steht — „Vorsorge", „Wohnen", „Streaming".
    /// </summary>
    /// <remarks>
    /// Gepflegt am Vertrag oder an der Police; fehlt sie, leitet der Bericht sie aus Bereich
    /// und Art ab. Nie leer: Ein Posten ohne Gruppe fiele aus jeder Summe heraus, und ein
    /// Bericht, der nicht alles zeigt, was er zusammenzählt, ist keiner.
    /// </remarks>
    public required string Group { get; init; }

    /// <summary>Auf den Monat umgerechnet, egal in welchem Takt gezahlt wird.</summary>
    public required decimal MonthlyAmount { get; init; }

    /// <summary>Bereich und Kündigungsfrist im Klartext.</summary>
    public required string Note { get; init; }

    public required FixedCostBinding Binding { get; init; }

    /// <summary>Ob die Frist jetzt auf den Tisch gehört.</summary>
    public required bool NoticeDue { get; init; }

    /// <summary>
    /// Das Konto, von dem dieser Posten abgeht — <c>null</c>, solange keines eingetragen ist.
    /// </summary>
    /// <remarks>
    /// Gepflegt am Vertrag, an der Police oder am Darlehen. Nicht abgeleitet: Ein Konto aus dem
    /// Betrag zu erraten wäre in einer Auswertung, die gerade Konten gegenüberstellt, der
    /// eine Fehler, der alle Zahlen darin wertlos machte.
    /// </remarks>
    public int? AccountId { get; init; }

    /// <summary>Der Kurzname dazu — <c>null</c>, solange kein Konto eingetragen ist.</summary>
    public string? AccountName { get; init; }
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


// ── Datenqualität ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Was die Auswertungen darüber unvollständig macht.
/// </summary>
/// <remarks>
/// Kein Bericht über Geld, sondern über die Grundlage. Er steht im selben Bereich, weil eine
/// Lücke hier jede Summe eine Registerkarte weiter verzerrt — und weil man sie sonst nirgends
/// zählen würde.
/// </remarks>
public sealed record DataQualityDto
{
    /// <summary>Summe aller offenen Posten über alle Zeilen.</summary>
    public required int OpenCount { get; init; }

    /// <summary>„3 Lücken“ oder „vollständig“.</summary>
    public required string Headline { get; init; }

    /// <summary>Was daraus folgt.</summary>
    public required string Line { get; init; }

    public required IReadOnlyList<DataQualityRowDto> Rows { get; init; }
}

/// <summary>Eine Lücke: wie viele, was sie anrichtet, und wo man sie schließt.</summary>
/// <param name="Count">Wie viele Datensätze betroffen sind.</param>
/// <param name="Label">Woran es fehlt.</param>
/// <param name="Consequence">
/// Die Folge, nicht die Wiederholung des Labels. „Fehlt“ weiß der Leser schon; warum es
/// stört, steht nirgends sonst.
/// </param>
/// <param name="Action">Das Verb auf dem Sprungziel — „zuordnen“, „prüfen“, „ergänzen“.</param>
/// <param name="Route">Wohin der Sprung führt, samt Filter.</param>
public sealed record DataQualityRowDto(
    int Count, string Label, string Consequence, string Action, string Route);


// ── Gespeicherte Ansichten ──────────────────────────────────────────────────────────────────

/// <summary>
/// Eine gespeicherte Einstellung des Auswertungsbereichs.
/// </summary>
/// <remarks>
/// Gespeichert wird, <em>wie</em> gerechnet wird, nie ein Ergebnis. Ein festgehaltenes Ergebnis
/// wäre am nächsten Tag falsch, ohne dass jemand es merkt.
/// </remarks>
public sealed record ReportViewDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required ReportKind Report { get; init; }
    public required PeriodScope Period { get; init; }
    public required ComparisonBasis Comparison { get; init; }
    public required CostTrendSort Sort { get; init; }
    public required int? DepotId { get; init; }
    public required IReadOnlyList<int> ExcludedTransactionIds { get; init; }
}

/// <summary>Was eine Ansicht festhalten soll.</summary>
/// <param name="Name">
/// Frei wählbar. Leer gelassen, benennt der Server sie nach dem, was sie einstellt.
/// </param>
public sealed record SaveReportViewRequest(
    ReportKind Report = ReportKind.CostTrend,
    PeriodScope Period = PeriodScope.Month,
    ComparisonBasis Comparison = ComparisonBasis.PreviousYear,
    CostTrendSort Sort = CostTrendSort.Increase,
    int? DepotId = null,
    IReadOnlyList<int>? ExcludedTransactionIds = null,
    string? Name = null);

/// <summary>Eine Kostenart mit ihrer Summe.</summary>
public sealed record FixedCostGroupDto
{
    public required string Name { get; init; }

    /// <summary>Summe der Posten dieser Art je Monat.</summary>
    public required decimal MonthlyAmount { get; init; }

    /// <summary>Anteil an den gebundenen Kosten, 0…100.</summary>
    public required decimal SharePercent { get; init; }

    public required IReadOnlyList<FixedCostRowDto> Rows { get; init; }
}

/// <summary>Die gebundenen Posten eines Kontos.</summary>
/// <remarks>
/// Was hier steht, ist <b>fortgeschrieben</b> und nicht gemessen: Es kommt aus Verträgen,
/// Policen und Darlehen, nicht aus dem Kontoauszug. Was tatsächlich abging, steht in der
/// Buchungsliste des Kontos — und beide dürfen auseinandergehen, etwa wenn ein Jahresbeitrag
/// im Zeitraum gar nicht fällig war.
/// </remarks>
public sealed record FixedCostAccountDto
{
    /// <summary><c>null</c> bei der Gruppe der Posten ohne eingetragenes Konto.</summary>
    public int? AccountId { get; init; }

    /// <summary>Der Kurzname des Kontos, oder „Konto nicht eingetragen“.</summary>
    public required string Name { get; init; }

    /// <summary>Summe der gebundenen Posten dieses Kontos je Monat.</summary>
    public required decimal MonthlyAmount { get; init; }

    /// <summary>Anteil an den gebundenen Kosten insgesamt, 0…100.</summary>
    public required decimal SharePercent { get; init; }

    public required IReadOnlyList<FixedCostRowDto> Rows { get; init; }
}

/// <summary>Welches Gemeinschaftskonto der Bericht zeigt und über welchen Zeitraum.</summary>
/// <remarks>
/// Ohne <c>AccountId</c> nimmt der Bericht das erste Gemeinschaftskonto. Eine Vorauswahl zu
/// verlangen hieße, dass der Bericht beim ersten Öffnen leer bleibt, obwohl es nur eines gibt.
/// </remarks>
public sealed record JointReportRequest(
    int? AccountId = null,
    PeriodScope Period = PeriodScope.Month,
    ComparisonBasis Comparison = ComparisonBasis.PreviousYear);

// ── Haushaltskonto ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Die Kontosicht auf ein Gemeinschaftskonto: wer bringt was ein, wofür geht es ab.
/// </summary>
/// <remarks>
/// <para><b>Drei Größen, die nie dieselbe Zahl tragen dürfen.</b> Eingebracht ist, was einer
/// Person zugeordnet auf dem Konto ankam. Abgeflossen ist, was das Konto verlassen hat.
/// Gebunden ist, was Verträge, Policen und Darlehen dieses Kontos je Monat ausweisen — das
/// kommt aus dem Bestand und nicht aus dem Auszug, und es darf vom Abfluss abweichen, etwa
/// wenn ein Jahresbeitrag im Zeitraum gar nicht fällig war.</para>
/// <para><b>Die Lücken stehen daneben.</b> Zuflüsse ohne zugeordnete Person und Buchungen ohne
/// Kategorie sind keine Nebensache: Ohne sie sähe eine unvollständige Zuordnung aus wie ein
/// Ergebnis. Wer sie sieht, weiß, wie weit er den Zahlen trauen kann.</para>
/// </remarks>
public sealed record JointReportDto
{
    public required ReportRangeDto Range { get; init; }

    /// <summary>Die Gemeinschaftskonten zur Auswahl.</summary>
    public required IReadOnlyList<JointChoiceDto> Choices { get; init; }

    /// <summary><c>null</c>, wenn es kein Gemeinschaftskonto gibt.</summary>
    public int? AccountId { get; init; }

    public string? AccountName { get; init; }

    // ── Wer bringt was ein ──────────────────────────────────────────────────────────────

    /// <summary>Die Beteiligten mit dem, was sie im Zeitraum eingebracht haben.</summary>
    public required IReadOnlyList<JointContributionDto> Contributions { get; init; }

    /// <summary>Summe des Eingebrachten im Zeitraum.</summary>
    public required decimal ContributionTotal { get; init; }

    /// <summary>
    /// Zuflüsse im Zeitraum, die niemandem zugeordnet sind.
    /// </summary>
    /// <remarks>
    /// Die wichtigste Zahl des ganzen Berichts, solange sie nicht null ist: Sie sagt, wie viel
    /// von dem, was ankam, in den Anteilen oben <em>nicht</em> steckt. Ohne sie läse sich eine
    /// halb gepflegte Zuordnung wie ein Befund über zwei Menschen.
    /// </remarks>
    public required decimal UnassignedInflow { get; init; }

    public required int UnassignedInflowCount { get; init; }

    // ── Wofür geht es ab ────────────────────────────────────────────────────────────────

    /// <summary>Die Abflüsse nach Kategorie, die schwerste zuerst.</summary>
    public required IReadOnlyList<JointSpendDto> Spending { get; init; }

    /// <summary>Summe der Abflüsse im Zeitraum, positiv angegeben.</summary>
    public required decimal SpendingTotal { get; init; }

    /// <summary>Was gebundene Posten dieses Kontos je Monat ausweisen.</summary>
    public required decimal MonthlyFixed { get; init; }

    /// <summary>Wie viele Posten das sind.</summary>
    public required int FixedCount { get; init; }

    // ── Was noch nicht zugeordnet ist ───────────────────────────────────────────────────

    /// <summary>Buchungen dieses Kontos im Zeitraum ohne Kategorie.</summary>
    public required int UncategorisedCount { get; init; }

    /// <summary>Was an ihnen hängt, positiv angegeben.</summary>
    public required decimal UncategorisedAmount { get; init; }
}

/// <summary>Ein Gemeinschaftskonto zur Auswahl.</summary>
public sealed record JointChoiceDto(int Id, string Name);

/// <summary>Was eine Person im Zeitraum eingebracht hat.</summary>
public sealed record JointContributionDto
{
    public required int UserId { get; init; }
    public required string Name { get; init; }

    /// <summary>Eingebracht im Zeitraum.</summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// Anteil am Eingebrachten insgesamt, 0…100.
    /// </summary>
    /// <remarks>
    /// Bezogen auf das <em>zugeordnete</em> Eingebrachte, nicht auf alles, was ankam. Die
    /// Anteile der Beteiligten ergeben so hundert Prozent; gegen den ganzen Zufluss gerechnet
    /// täten sie es nicht, und ein Kreis, dessen Stücke nicht aufgehen, ist eine falsche
    /// Zeichnung.
    /// </remarks>
    public required decimal SharePercent { get; init; }

    /// <summary>Das vereinbarte Soll je Monat — <c>null</c>, wenn keines vereinbart ist.</summary>
    public decimal? MonthlyTarget { get; init; }

    /// <summary>Wie oft im Zeitraum etwas kam.</summary>
    public required int Count { get; init; }

    /// <summary>Ob das der angemeldete Benutzer selbst ist.</summary>
    public required bool IsSelf { get; init; }
}

/// <summary>Was im Zeitraum auf eine Kategorie abgeflossen ist.</summary>
public sealed record JointSpendDto
{
    /// <summary><c>null</c> bei der Zeile der Buchungen ohne Kategorie.</summary>
    public int? CategoryId { get; init; }

    public required string Name { get; init; }

    /// <summary>Positiv angegeben: In einer Ausgabenliste ist das Vorzeichen redundant.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Anteil an den Abflüssen, 0…100.</summary>
    public required decimal SharePercent { get; init; }

    /// <summary>
    /// Auf den Monat heruntergerechnet.
    /// </summary>
    /// <remarks>
    /// <para>„Was brauchen wir für Lebensmittel“ ist eine Monatsfrage, und ein Jahreswert
    /// beantwortet sie nicht — wer ihn im Kopf durch zwölf teilt, teilt beim laufenden Jahr
    /// durch die falsche Zahl.</para>
    /// <para>Geteilt wird durch die <em>gelaufenen</em> Monate, nicht durch die nominellen:
    /// Ein Quartal, von dem sechs Wochen um sind, durch drei zu teilen ergäbe einen
    /// Monatswert, den es nie gab.</para>
    /// </remarks>
    public required decimal PerMonth { get; init; }

    public required int Count { get; init; }
}
