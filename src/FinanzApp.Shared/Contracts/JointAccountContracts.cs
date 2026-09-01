namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Ein Gemeinschaftskonto und sein Einzahlungssoll — Handoff „Gemeinsame Immobilie“, 3.3.
/// </summary>
/// <remarks>
/// <para>Der Schirm stellt Soll und Eingang gegenüber und nennt den Jahresstand. Er
/// <b>mahnt nicht</b> — er sagt, was steht. Ein Rückstand ist eine Feststellung, keine
/// Aufforderung: die Anwendung weiß nicht, was zwischen zwei Menschen vereinbart wurde,
/// nachdem sie das Soll eingetragen haben.</para>
/// <para>Gerechnet wird im Dienst, nicht im Schirm. Dieselbe Zahl an zwei Stellen zweimal zu
/// rechnen ist der Fehler, den dieser Handoff sieben Runden lang gejagt hat.</para>
/// </remarks>
public sealed record JointAccountDto
{
    public required int AccountId { get; init; }
    public required string Name { get; init; }

    /// <summary>Der Monat, den die Gegenüberstellung zeigt.</summary>
    public required DateOnly Month { get; init; }

    /// <summary>Die Beteiligten mit Soll und Eingang, nach Namen.</summary>
    public required IReadOnlyList<JointContributorDto> Contributors { get; init; }

    /// <summary>Summe der Sollbeträge im Monat.</summary>
    public decimal TargetTotal => Contributors.Sum(c => c.MonthlyTarget ?? 0m);

    /// <summary>Summe der Einlagen im Monat.</summary>
    public decimal PaidTotal => Contributors.Sum(c => c.PaidThisMonth);

    /// <summary>
    /// Was in diesem Jahr insgesamt eingezahlt wurde.
    /// </summary>
    /// <remarks>
    /// Der Jahresstand steht daneben, weil ein einzelner Monat wenig sagt: wer im Mai zweimal
    /// gezahlt hat, steht im Juni scheinbar im Rückstand.
    /// </remarks>
    public required decimal PaidThisYear { get; init; }

    /// <summary>Ob überhaupt ein Soll vereinbart ist.</summary>
    public bool HasTargets => Contributors.Any(c => c.MonthlyTarget is not null);

    /// <summary>
    /// Was im gezeigten Monat vom Konto abgegangen ist.
    /// </summary>
    /// <remarks>
    /// <b>Kontoabfluss, nicht Objektkosten.</b> Zwei verschiedene Größen, die nie dieselbe Zahl
    /// tragen dürfen: eine Rücklage zählt zu den Objektkosten und verlässt das Konto nicht, ein
    /// Wocheneinkauf verlässt es und gehört nicht zum Objekt.
    /// </remarks>
    public required decimal Outflow { get; init; }

    /// <summary>
    /// Der objektbezogene Teil des Abflusses.
    /// </summary>
    /// <remarks>
    /// Aus dem Kennzeichen an der Kategorie — Handoff 3.4. Verträge zählen hier nicht mit: ihr
    /// Abschlag wird von den Buchungen bezahlt, die ihn zahlen, und stünde sonst zweimal da.
    /// </remarks>
    public required decimal OutflowPropertyRelated { get; init; }

    /// <summary>Der Rest: gemeinsame Ausgaben, die nicht zum Objekt gehören.</summary>
    public decimal OutflowOther => Outflow - OutflowPropertyRelated;

    /// <summary>
    /// Ob überhaupt eine Kategorie als objektbezogen gekennzeichnet ist.
    /// </summary>
    /// <remarks>
    /// Ohne diese Unterscheidung stünde „davon objektbezogen 0 €“ da, wo niemand das Kennzeichen
    /// gesetzt hat — eine Aussage über das Haus, wo es eine über die Pflege der Kategorien ist.
    /// </remarks>
    public required bool HasPropertyRelatedCategories { get; init; }
}

/// <summary>Ein Beteiligter am Gemeinschaftskonto.</summary>
public sealed record JointContributorDto
{
    public required int UserId { get; init; }
    public required string Name { get; init; }

    /// <summary>Vereinbartes Soll je Monat — <c>null</c>, wenn keines vereinbart ist.</summary>
    public required decimal? MonthlyTarget { get; init; }

    /// <summary>Tag im Monat, zu dem es erwartet wird.</summary>
    public int? DueDay { get; init; }

    /// <summary>Was diese Person im gezeigten Monat eingezahlt hat.</summary>
    public required decimal PaidThisMonth { get; init; }

    /// <summary>Was diese Person im laufenden Jahr eingezahlt hat.</summary>
    public required decimal PaidThisYear { get; init; }

    /// <summary>
    /// Wann im gezeigten Monat zuletzt — <c>null</c>, wenn nichts kam.
    /// </summary>
    /// <remarks>
    /// Der Termin steht neben dem Stand: „erfüllt“ ohne Datum sagt nicht, ob es rechtzeitig war,
    /// und der vereinbarte Tag steht daneben.
    /// </remarks>
    public DateOnly? LastPaidOn { get; init; }

    /// <summary>
    /// Die Abweichung zum Soll — positiv heißt darüber, negativ darunter.
    /// </summary>
    /// <remarks>
    /// <c>null</c>, wenn kein Soll vereinbart ist: ohne Vereinbarung gibt es keine Abweichung,
    /// nur einen Eingang.
    /// </remarks>
    public decimal? Difference
        => MonthlyTarget is { } soll ? PaidThisMonth - soll : null;

    /// <summary>Ob das Soll erfüllt ist.</summary>
    public bool Fulfilled => Difference is { } abweichung && abweichung >= 0m;
}
