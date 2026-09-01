namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Der achte Bericht: „Objekt &amp; Beteiligung“ — Handoff „Gemeinsame Immobilie“, 3.5.
/// </summary>
/// <remarks>
/// <para>Drei getrennte Aussagen, jede mit ihrer eigenen Bezugsgröße:</para>
/// <list type="number">
///   <item><b>Was kostet das Objekt</b> — <see cref="Incurred"/> ist <em>gemessen</em> (aus
///     Buchungen), <see cref="YearTotal"/> ist <em>fortgeschrieben</em> (aus Darlehen, Verträgen,
///     Policen und Rücklage). Zwei Zahlen, zwei Namen; sie müssen nicht übereinstimmen.</item>
///   <item><b>Was ist objektbezogen</b> — der Kontoabfluss zerfällt in die objektbezogene
///     Teilmenge und den Rest, und was bewusst fehlt, wird benannt.</item>
///   <item><b>Wer hat wie viel getragen</b> — kommt vollständig aus
///     <see cref="Participation"/>; dieser Bericht rechnet daran nichts nach.</item>
/// </list>
/// <para><b>Objektkosten ≠ Kontoabfluss.</b> Die Rücklage zählt zu den Objektkosten und verlässt
/// das Konto nicht; ein Wocheneinkauf verlässt es und gehört nicht zum Objekt. Zwei Größen, die
/// nie dieselbe Zahl tragen dürfen.</para>
/// </remarks>
public sealed record PropertyReportDto
{
    public required int PropertyId { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }

    /// <summary>Die Objekte zur Auswahl — bei einem einzigen zeigt der Schirm keine.</summary>
    public required IReadOnlyList<PropertyChoiceDto> Properties { get; init; }

    // ── 1. Was kostet das Objekt ──────────────────────────────────────────────────────────

    /// <summary>
    /// Die Posten mit ihrer Art, auf das Jahr gerechnet.
    /// </summary>
    /// <remarks>
    /// Zins ist Aufwand, <b>Tilgung ist Vermögensaufbau</b> — beides steckt in derselben Rate.
    /// Ohne die Spalte „Art“ läse sich die ganze Rate als Kosten.
    /// </remarks>
    public required IReadOnlyList<PropertyCostRowDto> Items { get; init; }

    /// <summary>Summe der Posten aufs Jahr — fortgeschrieben, nicht gemessen.</summary>
    public required decimal YearTotal { get; init; }

    /// <summary>Dieselbe Summe je Monat.</summary>
    public required decimal MonthlyTotal { get; init; }

    /// <summary>
    /// Was im laufenden Jahr wirklich angefallen ist — gemessen aus Buchungen.
    /// </summary>
    /// <remarks>
    /// Gezählt werden Ausgaben, deren Kategorie das Kennzeichen <em>objektbezogen</em> trägt.
    /// Deshalb steht daneben, über wie viele Monate gemessen wurde: eine Jahreszahl aus acht
    /// Monaten wäre als Jahresstand beschriftet falsch.
    /// </remarks>
    public required decimal Incurred { get; init; }

    /// <summary>Wie viele Monate des Jahres die gemessene Zahl umfasst.</summary>
    public required int IncurredMonths { get; init; }

    /// <summary>Der erste und der letzte Tag des gemessenen Zeitraums.</summary>
    public required DateOnly IncurredFrom { get; init; }
    public required DateOnly IncurredTo { get; init; }

    /// <summary>Wie viele Buchungen die gemessene Zahl tragen.</summary>
    public required int IncurredBookings { get; init; }

    /// <summary>Wohnfläche in m² — <c>null</c>, wenn keine hinterlegt ist.</summary>
    public decimal? LivingArea { get; init; }

    /// <summary>
    /// Objektkosten je Quadratmeter und Monat.
    /// </summary>
    /// <remarks>
    /// <c>null</c> ohne Wohnfläche. Eine geschätzte Fläche wäre ein erfundener Nenner, und die
    /// Zahl sähe genauso aus wie eine richtige.
    /// </remarks>
    public decimal? PerSquareMetre { get; init; }

    /// <summary>Die monatliche Rücklage, falls hinterlegt.</summary>
    public decimal? MonthlyReserve { get; init; }

    /// <summary>
    /// Die Herleitung der Rate: was Kosten sind und was Vermögen aufbaut.
    /// </summary>
    /// <remarks>
    /// <c>null</c> ohne verknüpftes Darlehen. Abgeleitete Werte müssen herleitbar sein — ohne
    /// diese Zeile wäre nicht nachzurechnen, warum die Rate nicht ganz in den Kosten steht.
    /// </remarks>
    public PropertyLoanSplitDto? Loan { get; init; }

    /// <summary>
    /// Ob die Anteilsspalte der Posten auf 100 % summiert.
    /// </summary>
    /// <remarks>
    /// Je Posten gerundet ergibt sie manchmal 99 oder 101 %. Der Schirm sagt es dann — eine
    /// Spalte, die sichtbar nicht aufgeht und dazu schweigt, lässt an den Zahlen zweifeln.
    /// </remarks>
    public required int SharePercentSum { get; init; }

    // ── 2. Was ist objektbezogen ──────────────────────────────────────────────────────────

    /// <summary>
    /// Der Kontoabfluss der Gemeinschaftskonten im gezeigten Monat.
    /// </summary>
    /// <remarks>
    /// <c>null</c>, wenn es kein Gemeinschaftskonto gibt — dann fehlt die Bezugsgröße, und die
    /// Trennung wäre eine Rechnung ohne Gegenüber.
    /// </remarks>
    public decimal? Outflow { get; init; }

    /// <summary>Der objektbezogene Teil des Abflusses.</summary>
    public decimal? OutflowPropertyRelated { get; init; }

    /// <summary>Der Rest: gemeinsame Ausgaben, die nicht zum Objekt gehören.</summary>
    public decimal? OutflowOther
        => Outflow is { } ab && OutflowPropertyRelated is { } davon ? ab - davon : null;

    /// <summary>Der Monat, für den der Abfluss gilt.</summary>
    public required DateOnly OutflowMonth { get; init; }

    /// <summary>
    /// Was bewusst nicht zu den Objektkosten zählt — mit Betrag im laufenden Jahr.
    /// </summary>
    /// <remarks>
    /// Nicht als feste Liste hinterlegt, sondern die Kategorien ohne Kennzeichen, die im Jahr
    /// wirklich Buchungen tragen. Eine erfundene Aufzählung nennte womöglich Posten, die es im
    /// Haushalt nicht gibt.
    /// </remarks>
    public required IReadOnlyList<PropertyExcludedDto> Excluded { get; init; }

    /// <summary>Wie viele Kategorien die Liste nicht nennt, weil sie gekürzt ist.</summary>
    public required int ExcludedMore { get; init; }

    // ── 3. Wer hat wie viel getragen ──────────────────────────────────────────────────────

    /// <summary>
    /// Anteile, Eingebrachtes und Ausgleich — aus einer Quelle.
    /// </summary>
    /// <remarks>
    /// <c>null</c>, wenn am Objekt keine Anteile gepflegt sind: dann gehört es dem Haushalt als
    /// Ganzem, und es gibt niemanden, gegen den sich etwas ausgleichen ließe.
    /// </remarks>
    public PropertyParticipationDto? Participation { get; init; }
}

/// <summary>Ein Objekt zur Auswahl im Bericht.</summary>
public sealed record PropertyChoiceDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>Ein Posten der Objektkosten, auf das Jahr gerechnet.</summary>
public sealed record PropertyCostRowDto
{
    public required string Label { get; init; }

    /// <summary>Woher der Posten kommt — steht als Zeile unter dem Namen.</summary>
    public required string Source { get; init; }

    public required PropertyCostKind Kind { get; init; }

    /// <summary>Betrag aufs Jahr.</summary>
    public required decimal YearAmount { get; init; }

    /// <summary>Anteil an den Objektkosten, ganzzahlig gerundet.</summary>
    public required int SharePercent { get; init; }
}

/// <summary>
/// Ob ein Posten Geld verbraucht oder Vermögen aufbaut.
/// </summary>
/// <remarks>
/// Die Unterscheidung, die den Bericht überhaupt lesbar macht: von einer Jahresrate sind nur die
/// Zinsen wirkliche Kosten, die Tilgung wird Eigentum. Beides als „Kosten“ zu zeigen macht ein
/// Haus teurer, als es ist.
/// </remarks>
public enum PropertyCostKind
{
    Expense = 0,
    Equity = 1,
}

/// <summary>Wie sich die Jahresrate des Darlehens teilt.</summary>
/// <remarks>
/// Aus dem Tilgungsplan der nächsten zwölf Monate, nicht aus einer Faustregel: der Zinsanteil
/// fällt mit der Restschuld, und im nächsten Jahr steht er anders.
/// </remarks>
public sealed record PropertyLoanSplitDto
{
    public required string Name { get; init; }
    public required decimal YearInstalment { get; init; }
    public required decimal YearInterest { get; init; }
    public required decimal YearPrincipal { get; init; }
    public required decimal MonthlyInstalment { get; init; }
    public required decimal MonthlyPrincipal { get; init; }

    /// <summary>Wie viel Prozent der Rate wirkliche Kosten sind.</summary>
    public int InterestPercent
        => YearInstalment <= 0m ? 0 : (int)decimal.Round(YearInterest / YearInstalment * 100m);
}

/// <summary>Eine Kategorie, die nicht zu den Objektkosten zählt.</summary>
public sealed record PropertyExcludedDto
{
    public required string Name { get; init; }

    /// <summary>Was im laufenden Jahr darauf gebucht wurde.</summary>
    public required decimal YearAmount { get; init; }
}
