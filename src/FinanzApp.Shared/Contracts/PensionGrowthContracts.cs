namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Vorsorge: was ein Vertrag selbst erwirtschaftet hat.
/// </summary>
/// <remarks>
/// <para>Ein Vorsorgevertrag wächst aus zwei Quellen: aus dem, was eingezahlt wird, und aus dem,
/// was er daraus macht. Der Wert allein sagt darüber nichts — er steigt auch dann, wenn von jedem
/// eingezahlten Euro weniger als einer im Guthaben ankommt. Erst der Abstand zwischen
/// <b>Zuwachs</b> und <b>Beitrag</b> sagt, was der Vertrag leistet.</para>
/// <para><b>Zwei Beitragszahlen, nie eine.</b> Die gemessene kommt aus den Buchungen, die über
/// die Vertragsnummer an diesem Vertrag hängen; die hochgerechnete aus dem <em>heutigen</em>
/// Beitrag mal der Zahl der Monate. Beides zu einer Zahl zu verschmelzen wäre der bequeme Weg
/// und der falsche: Beiträge steigen über die Jahre, und die Anwendung kennt nur den von heute.
/// Wo die Buchungen den Zeitraum nicht abdecken, steht die gemessene Zahl deshalb nicht da —
/// und der Grund dafür steht in der Zeile.</para>
/// <para>Dasselbe Begriffspaar wie im Objektbericht: <em>gemessen</em> gegen
/// <em>fortgeschrieben</em>. Zwei Größen, die einander ergänzen und nie dieselbe Zahl tragen
/// müssen.</para>
/// </remarks>
public sealed record PensionGrowthDto
{
    public required IReadOnlyList<PensionGrowthRowDto> Rows { get; init; }

    /// <summary>Summe des Zuwachses über alle Verträge mit zwei Ständen.</summary>
    public required decimal TotalGrowth { get; init; }

    /// <summary>Summe der hochgerechneten Beiträge über dieselben Verträge.</summary>
    public required decimal TotalProjected { get; init; }

    /// <summary>Zuwachs minus hochgerechneter Beitrag.</summary>
    public decimal TotalDifference => TotalGrowth - TotalProjected;

    /// <summary>Wie viele Verträge in die Summe eingehen.</summary>
    public required int CountedPolicies { get; init; }

    /// <summary>Wie viele übergangen wurden, weil ihnen ein zweiter Stand fehlt.</summary>
    public required int SkippedPolicies { get; init; }

    /// <summary>
    /// Der Tag, ab dem überhaupt Buchungen vorliegen — die Grenze jeder gemessenen Summe.
    /// </summary>
    public DateOnly? BookingsFrom { get; init; }

    /// <summary>Was zu den Zahlen zu sagen ist. Ohne Beträge.</summary>
    public required string Note { get; init; }
}

/// <summary>Ein Vertrag mit seinem Zeitraum.</summary>
public sealed record PensionGrowthRowDto
{
    public required int PolicyId { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }

    /// <summary>Die beiden Stichtage, zwischen denen gerechnet wird.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Wie viele Monate dazwischen liegen — der Faktor der Hochrechnung.</summary>
    public int Months { get; init; }

    public decimal? ValueFrom { get; init; }
    public decimal? ValueTo { get; init; }

    /// <summary>Wert am Ende minus Wert am Anfang. Gemessen aus zwei Statusberichten.</summary>
    public decimal? Growth { get; init; }

    /// <summary>
    /// Heutiger Monatsbeitrag mal Monate. <c>null</c>, wenn kein Beitrag hinterlegt ist.
    /// </summary>
    public decimal? Projected { get; init; }

    /// <summary>
    /// Summe der Buchungen im Zeitraum, die an diesem Vertrag hängen.
    /// </summary>
    /// <remarks>
    /// <c>null</c>, wenn die Buchungen den Zeitraum nicht abdecken. Eine Teilsumme, die wie eine
    /// ganze aussieht, wäre schlimmer als keine — sie macht jeden Vertrag künstlich gut.
    /// </remarks>
    public decimal? Measured { get; init; }

    /// <summary>Wie viele Buchungen die gemessene Summe tragen.</summary>
    public int MeasuredCount { get; init; }

    /// <summary>Zuwachs minus hochgerechnetem Beitrag.</summary>
    public decimal? DifferenceProjected { get; init; }

    /// <summary>Zuwachs minus gemessenem Beitrag — nur wo beides den Zeitraum abdeckt.</summary>
    public decimal? DifferenceMeasured { get; init; }

    /// <summary>Was an dieser Zeile einzuschränken ist. Leer, wenn nichts.</summary>
    public required string Note { get; init; }
}
