namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Die Beteiligung an einer Immobilie — Handoff „Gemeinsame Immobilie“, Abschnitt 3.1.
/// </summary>
/// <remarks>
/// <para><b>Ein Aggregat, aus dem alles kommt.</b> Anteile, Eigenkapital, Einlagen und die
/// daraus abgeleiteten Größen stehen hier zusammen — der Schirm rechnet nichts selbst. Im
/// Entwurf sind dieselben Zahlen siebenmal auseinandergelaufen, weil jede Stelle sie noch
/// einmal gerechnet hat.</para>
/// <para><b>Zwei Schuldgrößen, zwei Namen.</b> <see cref="DebtTotal"/> ist die Restschuld des
/// Darlehens und bleibt es — der Tilgungsplan liest sie. <see cref="DebtShare"/> ist der eigene
/// Haftungsanteil. Eine Größe umzudefinieren und ihren Namen zu lassen war der schwerste Fehler
/// des Entwurfs: der Widerspruch wandert dann nur.</para>
/// </remarks>
public sealed record PropertyParticipationDto
{
    /// <summary>Die Beteiligten mit ihren Anteilen, nach Anteil absteigend.</summary>
    public required IReadOnlyList<ParticipantDto> Participants { get; init; }

    /// <summary>Marktwert des Objekts — die ganze Immobilie.</summary>
    public required decimal MarketValue { get; init; }

    /// <summary>Restschuld des verknüpften Darlehens — die ganze Schuld.</summary>
    public required decimal DebtTotal { get; init; }

    /// <summary>
    /// Der eigene Anteil am Marktwert — <c>null</c>, wenn der Betrachter nicht beteiligt ist.
    /// </summary>
    public required decimal? ValueShare { get; init; }

    /// <summary>
    /// Der eigene Haftungsanteil an der Restschuld.
    /// </summary>
    /// <remarks>
    /// Bei gesamtschuldnerischer Haftung haftet jeder für alles; geteilt wird nach dem
    /// Eigentumsanteil, weil das die Quote ist, die dem Innenverhältnis entspricht. Die
    /// Darlehensquote zu verschieben wäre falsch: die Bank kennt sie nicht.
    /// </remarks>
    public required decimal? DebtShare { get; init; }

    /// <summary>Anteil am Wert minus Anteil an der Schuld — was ins eigene Vermögen zählt.</summary>
    public decimal? NetShare => ValueShare is { } wert && DebtShare is { } schuld ? wert - schuld : null;

    /// <summary>
    /// Der Ausgleichsstand des angemeldeten Benutzers. Positiv heißt Forderung.
    /// </summary>
    /// <remarks>
    /// Abgeleitet und nirgends erfasst: eingebrachtes Eigenkapital plus Einlagen, gemessen am
    /// Eigentumsanteil. Wer mehr eingebracht hat, als sein Anteil verlangt, hat eine Forderung
    /// gegen die anderen Beteiligten.
    /// </remarks>
    public required decimal? Settlement { get; init; }

    /// <summary>Ob überhaupt Anteile gepflegt sind.</summary>
    public bool IsShared => Participants.Count > 0;

    /// <summary>
    /// Ob die Anteile zusammen 100 % ergeben.
    /// </summary>
    /// <remarks>
    /// Sie müssen es, sonst speichert die Anwendung nicht. Das Kennzeichen deckt Bestände ab,
    /// die vor dieser Prüfung entstanden sind — der Schirm sagt es dann, statt mit einer
    /// Teilsumme weiterzurechnen.
    /// </remarks>
    public bool PercentComplete { get; init; }
}

/// <summary>Ein Beteiligter samt dem, was er eingebracht hat.</summary>
public sealed record ParticipantDto
{
    public required int UserId { get; init; }
    public required string Name { get; init; }

    /// <summary>Eigentumsanteil in Prozent — steht im Grundbuch.</summary>
    public required decimal Percent { get; init; }

    /// <summary>Eingebrachtes Eigenkapital beim Kauf.</summary>
    public required decimal Equity { get; init; }

    /// <summary>
    /// Laufende Einlagen auf das Gemeinschaftskonto.
    /// </summary>
    /// <remarks>
    /// Kommt aus den Buchungen der Art „Einlage“. Solange es die Buchungsart nicht gibt, steht
    /// hier null — und der Ausgleich rechnet mit dem Eigenkapital allein, statt eine Einlage zu
    /// erfinden.
    /// </remarks>
    public required decimal Deposits { get; init; }

    /// <summary>Eigenkapital plus Einlagen.</summary>
    public decimal Contributed => Equity + Deposits;

    /// <summary>Der eigene Ausgleichsstand dieser Person. Positiv heißt Forderung.</summary>
    public required decimal Settlement { get; init; }

    /// <summary>Ob das der angemeldete Benutzer ist.</summary>
    public required bool IsSelf { get; init; }
}
