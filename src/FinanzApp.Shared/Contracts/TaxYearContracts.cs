namespace FinanzApp.Shared.Contracts;

/// <summary>Wie Buchungen einer Kategorie steuerlich zählen.</summary>
/// <remarks>
/// Nur die beiden Abschnitte, die aus Kontobuchungen entstehen. Vorsorgeaufwendungen kommen aus
/// Verträgen und Krankheitskosten aus den PKV-Vorgängen — beide haben ihre eigene Quelle und
/// brauchen keine Kennzeichnung an der Kategorie.
/// </remarks>
public enum TaxCategory
{
    None = 0,
    Handwerkerleistung = 1,
    Werbungskosten = 2,
}

/// <summary>Die vier Abschnitte des Steuerjahr-Berichts.</summary>
public enum TaxSectionKind
{
    Vorsorge = 0,
    Krankheit = 1,
    Handwerker = 2,
    Werbungskosten = 3,
}

/// <summary>
/// Eine Position des Steuerjahr-Berichts.
/// </summary>
/// <remarks>
/// <para><b>Zwei Kennzeichen, nie eines.</b> <see cref="DocumentMissing"/> und
/// <see cref="Estimated"/> sind unabhängig voneinander, und das ist der teuerste Fehler dieser
/// Runde gewesen: eine gerechnete Entfernungspauschale ist sehr wohl belegt, stand aber im Topf
/// „ohne Beleg“ und machte dort den größten Betrag aus. Für den, der das Blatt bekommt, ist der
/// Unterschied entscheidend — <em>einen fehlenden Beleg reicht man nach, eine Schätzung muss man
/// nachrechnen.</em></para>
/// <para><see cref="Evidence"/> benennt die <b>Sache</b>, nicht die Aussage: „Anbieter­bescheinigung“,
/// nicht „Bescheinigung fehlt“. Sonst setzt <see cref="Mark"/> daraus „⚠ fehlt: Bescheinigung
/// fehlt“ zusammen.</para>
/// </remarks>
public sealed record TaxPositionDto
{
    public required TaxSectionKind Section { get; init; }
    public required string Label { get; init; }

    /// <summary>Der Betrag. Null heißt: erwartet, aber noch ohne Zahl.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Die Sache, die als Beleg dient oder fehlt — ein Substantiv, keine Aussage.</summary>
    public required string Evidence { get; init; }

    /// <summary>Der Beleg fehlt und muss nachgereicht werden.</summary>
    public bool DocumentMissing { get; init; }

    /// <summary>Der Betrag ist gerechnet und muss nachgerechnet werden.</summary>
    public bool Estimated { get; init; }

    /// <summary>Wohin die Zeile führt.</summary>
    public string? Href { get; init; }

    /// <summary>
    /// Eine Position über 0 € ist keine Steuerposition.
    /// </summary>
    /// <remarks>
    /// Sie steht unter „Erwartet, noch ohne Betrag“ und zählt in keine Summe und keinen Zähler.
    /// </remarks>
    public bool Pending => Amount <= 0m;

    /// <summary>
    /// Marke und Ton dieser Position — die eine Formel, die Bericht und Druckblatt lesen.
    /// </summary>
    /// <remarks>
    /// Fehlender Beleg schlägt Schätzung: wer beides hat, muss zuerst den Beleg besorgen. Beide
    /// Marken nebeneinander zu setzen machte aus einer Zeile eine Fußnote.
    /// </remarks>
    public TaxMark Mark => DocumentMissing
        ? new(TaxMarkTone.Missing, "⚠ fehlt: " + Evidence)
        : Estimated
            ? new(TaxMarkTone.Estimated, "≈ geschätzt · " + Evidence)
            : new(TaxMarkTone.Plain, Evidence);
}

/// <summary>Die drei Töne einer Positionsmarke.</summary>
public enum TaxMarkTone
{
    /// <summary>Belegt und nicht geschätzt.</summary>
    Plain = 0,

    /// <summary>Beleg fehlt — nachreichen.</summary>
    Missing = 1,

    /// <summary>Betrag gerechnet — nachrechnen.</summary>
    Estimated = 2,
}

/// <summary>Marke und Ton einer Position.</summary>
public sealed record TaxMark(TaxMarkTone Tone, string Text);

/// <summary>Ein Abschnitt des Berichts.</summary>
public sealed record TaxSectionDto
{
    public required TaxSectionKind Kind { get; init; }
    public required string Title { get; init; }

    /// <summary>
    /// Die Einschränkung im Klartext.
    /// </summary>
    /// <remarks>
    /// Teil der Aussage, nicht Kleingedrucktes. „Nur Arbeitslohn, nicht Material“ entscheidet
    /// darüber, ob die Summe darüber überhaupt etwas wert ist.
    /// </remarks>
    public required string Caveat { get; init; }

    public required IReadOnlyList<TaxPositionDto> Positions { get; init; }

    public decimal Total => Positions.Sum(p => p.Amount);
    public int MissingCount => Positions.Count(p => p.DocumentMissing);
    public int EstimatedCount => Positions.Count(p => p.Estimated);

    /// <summary>
    /// Die Zeile unter der Abschnittsüberschrift.
    /// </summary>
    /// <remarks>
    /// Der fehlerfreie Fall heißt „belegt, kein Schätzwert“ und nicht „belegt und gerechnet“ —
    /// „gerechnet“ heißt in der Marke <em>geschätzt</em>, und ein Wort darf nicht zweierlei
    /// bedeuten.
    /// </remarks>
    public string Meta
    {
        get
        {
            var teile = new List<string>
            {
                Positions.Count + (Positions.Count == 1 ? " Position" : " Positionen"),
            };

            if (MissingCount > 0)
            {
                teile.Add(MissingCount + " ohne Beleg");
            }

            if (EstimatedCount > 0)
            {
                teile.Add(EstimatedCount + " geschätzt");
            }

            if (MissingCount == 0 && EstimatedCount == 0)
            {
                teile.Add("belegt, kein Schätzwert");
            }

            return string.Join(" · ", teile);
        }
    }

    public bool NeedsAttention => MissingCount > 0 || EstimatedCount > 0;
}

/// <summary>Etwas, das bewusst fehlt — mit Grund.</summary>
/// <remarks>
/// Ohne diesen Block hält der Leser die Liste für vollständig und sucht später nach Posten, die
/// nie darin sein sollten.
/// </remarks>
public sealed record TaxExclusionDto(string Label, string Reason);

/// <summary>
/// Der Steuerjahr-Bericht — v5-Handoff, Abschnitt 15.1.
/// </summary>
/// <remarks>
/// Eine Sammlung von Kandidaten mit Belegbezug, ausdrücklich keine Steuerberechnung.
/// Höchstbeträge, zumutbare Belastung und die Trennung Arbeitslohn/Material bleiben außen vor —
/// sie hängen an Größen, die die Anwendung nicht kennt.
/// </remarks>
public sealed record TaxYearDto
{
    public required int Year { get; init; }
    public required IReadOnlyList<int> Years { get; init; }

    public required IReadOnlyList<TaxSectionDto> Sections { get; init; }

    /// <summary>Erwartet, aber noch ohne Betrag — zählt nirgends mit.</summary>
    public required IReadOnlyList<TaxPositionDto> Pending { get; init; }

    public required IReadOnlyList<TaxExclusionDto> Excluded { get; init; }

    public decimal Total => Sections.Sum(s => s.Total);
    public int PositionCount => Sections.Sum(s => s.Positions.Count);

    /// <summary>Betrag ohne Beleg.</summary>
    public decimal MissingAmount
        => Sections.SelectMany(s => s.Positions).Where(p => p.DocumentMissing).Sum(p => p.Amount);

    public int MissingCount => Sections.Sum(s => s.MissingCount);

    /// <summary>Geschätzter Betrag.</summary>
    public decimal EstimatedAmount
        => Sections.SelectMany(s => s.Positions).Where(p => p.Estimated).Sum(p => p.Amount);

    public int EstimatedCount => Sections.Sum(s => s.EstimatedCount);

    /// <summary>
    /// Belegquote in Prozent — <c>null</c>, solange es nichts zu belegen gibt.
    /// </summary>
    /// <remarks>
    /// Über den Beträgen und nicht über den Positionen: sonst stünde „belegt 100 %“ neben
    /// „1 Position ohne Beleg“, weil zwei Größen in derselben Kennzahl verrechnet würden.
    /// </remarks>
    public decimal? DocumentedPercent
        => Total == 0m ? null : decimal.Round((Total - MissingAmount) / Total * 100m, 0);
}
