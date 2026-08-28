namespace FinanzApp.Shared.Contracts;

/// <summary>Was angelegt werden kann. Ein Wert je Anlege-Flow.</summary>
public enum CreateObjectType
{
    Account = 0,
    Depot = 1,
    Pension = 2,
    Protection = 3,
    Property = 4,
    Contract = 5,
    Budget = 6,
    Vehicle = 7,
    Employment = 8,
}

/// <summary>Wie ein Feld einzugeben ist. Bestimmt allein die Darstellung, nicht die Bedeutung.</summary>
public enum CreateFieldKind
{
    /// <summary>Feste Auswahl, als Chips dargestellt.</summary>
    Choice = 0,

    Text = 1,
    Money = 2,
    Number = 3,
    Date = 4,

    /// <summary>Auswahl aus vorhandenen Objekten — Konto, Kategorie, Immobilie, Darlehen.</summary>
    Reference = 5,
}

/// <summary>
/// Ein Anlegeformular, vollständig vom Server beschrieben.
/// </summary>
/// <remarks>
/// Die Feldliste kommt bewusst nicht aus dem Client: sonst gäbe es sie zweimal — einmal zum
/// Anzeigen, einmal zum Prüfen — und die beiden liefen auseinander. So beschreibt dieselbe
/// Liste, was gezeigt und was verlangt wird, und eine Fehlermeldung kann das fehlende Feld
/// beim Namen nennen, den der Benutzer auch gesehen hat.
/// </remarks>
public sealed record CreateFormDto
{
    public required CreateObjectType Type { get; init; }
    public required string Kicker { get; init; }
    public required string Title { get; init; }

    /// <summary>Beschriftung der Primäraktion, z. B. „Konto anlegen“.</summary>
    public required string SubmitLabel { get; init; }

    /// <summary>Ein Satz über dem Formular, wenn es etwas zu erklären gibt.</summary>
    public string? Hint { get; init; }

    public required IReadOnlyList<CreateFieldDto> Fields { get; init; }

    /// <summary>
    /// Beim Bearbeiten die vorhandenen Werte, je Feldschlüssel. Beim Anlegen leer.
    /// </summary>
    /// <remarks>
    /// Sie kommen aus den <em>Rohfeldern</em> des Objekts, nicht aus seiner Anzeigezeile. Eine
    /// Anzeigezeile zurückzuparsen wäre der sichere Weg zu leeren Pflichtfeldern: „Risikoleben“
    /// trägt keinen Versicherer im Namen.
    /// </remarks>
    public IReadOnlyDictionary<string, string?> Values { get; init; } =
        new Dictionary<string, string?>();

    /// <summary>Gesetzt, wenn ein vorhandenes Objekt bearbeitet wird.</summary>
    public int? EditingId { get; init; }

    /// <summary>Was das Löschen nach sich zöge. Nur im Bearbeiten-Modus gesetzt.</summary>
    public DeleteImpactDto? DeleteImpact { get; init; }
}

/// <summary>
/// Die Folgen einer Löschung, typgenau und mit <b>echten</b> Zahlen.
/// </summary>
/// <remarks>
/// Der Handoff verlangt ausdrücklich, echte Bezüge zu zählen, statt Prüfungen zu behaupten, die
/// nicht stattfinden. Ein Satz wie „Sind noch Buchungen verknüpft?“ ohne nachzusehen wäre
/// schlimmer als gar keiner — er klingt nach Sorgfalt und ist keine.
/// </remarks>
public sealed record DeleteImpactDto
{
    /// <summary>Überschrift des Abschnitts, z. B. „Konto löschen“.</summary>
    public required string Title { get; init; }

    /// <summary>Ein Satz zu den Folgen, mit gezählten Bezügen und richtigem Singular.</summary>
    public required string Consequence { get; init; }

    /// <summary>Beschriftung der Aktion, z. B. „Konto löschen“.</summary>
    public required string ActionLabel { get; init; }
}

public sealed record CreateFieldDto
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required CreateFieldKind Kind { get; init; }

    /// <summary>Pflichtfeld. Fehlt es, nennt die Meldung genau dieses Label.</summary>
    public required bool Required { get; init; }

    public string? Placeholder { get; init; }

    /// <summary>Erklärung unter dem Feld.</summary>
    public string? Help { get; init; }

    /// <summary>Vorbelegung, im selben Format wie die Eingabe.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Auswahlwerte für <see cref="CreateFieldKind.Choice"/> und <c>Reference</c>.</summary>
    public IReadOnlyList<CreateOptionDto>? Options { get; init; }
}

public sealed record CreateOptionDto
{
    public required string Value { get; init; }
    public required string Label { get; init; }

    /// <summary>Zweite Zeile am Chip, etwa der Kontostand.</summary>
    public string? Hint { get; init; }

    /// <summary>
    /// Führt diese Wahl in einen anderen Flow statt in dieses Formular? Dann steht hier sein
    /// Pfad. Der Handoff verlangt das für „Depot“ im Konto-Formular: ein Depot ist kein Konto.
    /// </summary>
    public string? RedirectTo { get; init; }
}

/// <summary>Ergebnis einer Löschung.</summary>
public sealed record DeleteResultDto
{
    public required bool Ok { get; init; }
    public string? Message { get; init; }

    /// <summary>Wohin danach gesprungen wird.</summary>
    public string? Route { get; init; }
}

/// <summary>Die ausgefüllten Werte, als Zeichenketten wie eingegeben.</summary>
public sealed record CreateRequest
{
    public required Dictionary<string, string?> Values { get; init; }
}

/// <summary>
/// Ergebnis eines Anlegeversuchs. Schlägt er fehl, sagt <see cref="FieldKey"/>, welches Feld
/// gemeint ist — die Oberfläche kann es dann hervorheben, statt nur einen Satz zu zeigen.
/// </summary>
public sealed record CreateResultDto
{
    public required bool Ok { get; init; }
    public string? FieldKey { get; init; }
    public string? Message { get; init; }

    public int? Id { get; init; }

    /// <summary>Wohin nach dem Anlegen gesprungen wird.</summary>
    public string? Route { get; init; }
}

// ── Police / Beleg einlesen ─────────────────────────────────────────────────

/// <summary>
/// Was aus einem hochgeladenen Dokument gelesen wurde.
/// </summary>
/// <remarks>
/// <para>Die Datei ist zu diesem Zeitpunkt bereits abgelegt — gespeichert wird nur ihr relativer
/// Pfad, wie überall. Die Werte sind ein <em>Vorschlag</em>: sie stehen hier, sie stehen noch
/// nicht im Formular. Erst „Übernehmen“ bringt sie hinein, und erst das Anlegen schreibt sie
/// fort. Nichts Unbestätigtes verändert eine Vermögenszahl.</para>
/// <para>Metadaten kommen aus dem <b>Inhalt</b>, nie aus dem Dateinamen — der wird trotzdem
/// mitgespeichert, weil er zur Wiedererkennung taugt.</para>
/// </remarks>
public sealed record DocumentAnalysisDto
{
    /// <summary>Ob überhaupt etwas erkannt wurde.</summary>
    public required bool HasContent { get; init; }

    /// <summary>Originalname der Datei.</summary>
    public required string FileName { get; init; }

    /// <summary>Wo sie liegt — relativ zum Dokumentordner.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Seiten des Dokuments, soweit bekannt.</summary>
    public int? PageCount { get; init; }

    /// <summary>Wenn nichts erkannt wurde: warum.</summary>
    public string? Note { get; init; }

    public required IReadOnlyList<ExtractedFieldDto> Fields { get; init; }
}

/// <summary>Ein erkannter Wert, immer mit seiner Herkunft.</summary>
public sealed record ExtractedFieldDto
{
    /// <summary>Schlüssel des Formularfelds, in das der Wert gehört.</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }

    /// <summary>Der Wert im Eingabeformat des Felds.</summary>
    public required string Value { get; init; }

    /// <summary>Der Wert, wie er dem Menschen gezeigt wird.</summary>
    public required string Display { get; init; }

    /// <summary>Auf welcher Seite er stand.</summary>
    public int? SourcePage { get; init; }

    /// <summary>0 bis 1. Niedrig heißt: bitte hinsehen.</summary>
    public double Confidence { get; init; }

    /// <summary>Unsicher — die Zeile steht im Akzent und nennt die Seite.</summary>
    public bool IsUncertain => Confidence < 0.8;
}
