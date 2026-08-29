namespace FinanzApp.Shared.Contracts;

/// <summary>Wie es um die Kurse steht.</summary>
/// <remarks>
/// Kein Zustand blendet Zahlen aus — er sagt, wie alt sie sind. Dieselbe Regel wie beim
/// Ladefehler aus Abschnitt 7: vorhandene Daten werden nie durch eine leere Fläche ersetzt.
/// </remarks>
public enum QuoteState
{
    /// <summary>Der letzte Abruf ist aktuell.</summary>
    Fresh = 0,

    /// <summary>Der letzte Abruf liegt zurück; bewertet wird mit dem gespeicherten Kurs.</summary>
    Stale = 1,

    /// <summary>Der letzte Versuch ist gescheitert.</summary>
    Failed = 2,

    /// <summary>Es ist noch nie abgerufen worden.</summary>
    Never = 3,
}

/// <summary>Zeiträume des Kursverlaufs.</summary>
public enum QuoteRange
{
    Month = 0,
    HalfYear = 1,
    Year = 2,
    All = 3,
}

/// <summary>
/// Das Kursband über der Positionsliste — v5-Handoff, Abschnitt 16.3.
/// </summary>
/// <remarks>
/// <b>Ein Stand, überall derselbe.</b> Kopfzeile, Depotzeile, Band und Bestandsliste lesen
/// dieselben Angaben. Vorher stand „Stand 14.08.“ aus der Transaktionsableitung neben
/// „heute 17:35“ auf demselben Schirm — zwei Zahlen für dieselbe Größe.
/// </remarks>
public sealed record QuoteBandDto
{
    public required QuoteState State { get; init; }

    /// <summary>Wie die Quelle heißt.</summary>
    public required string Source { get; init; }

    /// <summary>Wann zuletzt abgerufen wurde. <c>null</c>, wenn noch nie.</summary>
    public DateTime? FetchedAt { get; init; }

    /// <summary>Der Handelstag des jüngsten gespeicherten Kurses.</summary>
    public DateOnly? LatestDate { get; init; }

    /// <summary>Der älteste gespeicherte Kurs — der Anfang der Reihe.</summary>
    public DateOnly? FirstDate { get; init; }

    /// <summary>Wie viele Kurse insgesamt gespeichert sind.</summary>
    public required int StoredCount { get; init; }

    /// <summary>Wie viele Papiere überhaupt einen Kurs bräuchten.</summary>
    public required int SecurityCount { get; init; }

    /// <summary>Beim Fehlschlag: warum.</summary>
    public string? Problem { get; init; }

    /// <summary>Ob ein Abruf überhaupt möglich ist.</summary>
    public required bool CanFetch { get; init; }
}

/// <summary>Ein Punkt der Kurszeitreihe.</summary>
public sealed record QuotePointDto
{
    public required DateOnly Date { get; init; }
    public required decimal Close { get; init; }

    /// <summary>Woher dieser eine Punkt stammt — Abruf, Ausführung oder Bestandsnachweis.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// Der Kursverlauf eines Papiers im gewählten Zeitraum — Abschnitt 16.2.
/// </summary>
public sealed record QuoteSeriesDto
{
    public required string Isin { get; init; }
    public required QuoteRange Range { get; init; }

    public required IReadOnlyList<QuotePointDto> Points { get; init; }

    /// <summary>Tief und Hoch im Zeitraum.</summary>
    public decimal Low => Points.Count == 0 ? 0m : Points.Min(p => p.Close);

    public decimal High => Points.Count == 0 ? 0m : Points.Max(p => p.Close);

    /// <summary>Der durchschnittliche Einstand je Stück.</summary>
    public required decimal? AverageCost { get; init; }

    /// <summary>
    /// Ob der Einstand im dargestellten Kursbereich liegt.
    /// </summary>
    /// <remarks>
    /// <para>Die Linie wird <b>nur dann</b> gezeichnet. Ein an den Rand geklemmter Wert behauptet
    /// eine Größenrelation, die es nicht gibt: in der Zwölfmonatsansicht sah die Kurve knapp
    /// über der Linie aus, tatsächlich lagen 33 % dazwischen.</para>
    /// <para>Und die Skala wird nicht aufgeweitet, um ihn hineinzuzwingen — dann würde die
    /// Kurve in kurzen Zeiträumen flach und damit nutzlos.</para>
    /// </remarks>
    public bool CostInRange
        => AverageCost is { } einstand && Points.Count > 0 && einstand >= Low && einstand <= High;

    /// <summary>Veränderung vom ersten zum letzten Punkt, in Prozent.</summary>
    public decimal? ChangePercent
        => Points.Count < 2 || Points[0].Close == 0m
            ? null
            : decimal.Round((Points[^1].Close / Points[0].Close - 1m) * 100m, 1);

    /// <summary>
    /// Seit wann der Kurs durchgehend über dem Einstand liegt.
    /// </summary>
    /// <remarks>
    /// Der erste Punkt der letzten ununterbrochenen Strecke darüber. Irgendein früherer Tag
    /// über dem Einstand sagt nichts, wenn die Kurve danach wieder darunter war.
    /// </remarks>
    public DateOnly? AboveCostSince
    {
        get
        {
            if (AverageCost is not { } einstand || Points.Count == 0 || Points[^1].Close < einstand)
            {
                return null;
            }

            var seit = Points[^1].Date;

            for (var i = Points.Count - 1; i >= 0 && Points[i].Close >= einstand; i--)
            {
                seit = Points[i].Date;
            }

            return seit;
        }
    }

    /// <summary>Wie viele Kurse insgesamt zu diesem Papier gespeichert sind.</summary>
    public required int StoredCount { get; init; }

    public required DateOnly? FirstStored { get; init; }

    /// <summary>Die Quellen, aus denen die gespeicherte Reihe stammt.</summary>
    public required IReadOnlyList<string> Sources { get; init; }
}

/// <summary>Was ein Abruf ergeben hat.</summary>
public sealed record QuoteRefreshDto
{
    public required int Requested { get; init; }
    public required int Stored { get; init; }
    public required int Failed { get; init; }

    public required QuoteBandDto Band { get; init; }

    /// <summary>Die Meldung, wie sie der Nutzer liest.</summary>
    public required string Message { get; init; }
}
