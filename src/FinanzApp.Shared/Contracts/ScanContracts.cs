namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Was die Analyse eines eingelesenen Dokuments ergeben hat — Abschnitt 14.5 des v5-Handoffs.
/// </summary>
/// <remarks>
/// Die Datei ist zu diesem Zeitpunkt bereits abgelegt; die <em>Werte</em> sind es nicht. Nichts
/// hier verändert eine Vermögenszahl, bevor ein Mensch bestätigt hat.
/// </remarks>
public sealed record ScanAnalysisDto
{
    public required int DocumentId { get; init; }
    public required string FileName { get; init; }

    /// <summary>Pfad unter dem Dokumentordner — der Vorschlag, der auch schon gilt.</summary>
    public required string RelativePath { get; init; }

    public required int PageCount { get; init; }

    /// <summary>Erkannter Typ. <c>null</c>, wenn keiner passt.</summary>
    public string? KindKey { get; init; }
    public string? KindLabel { get; init; }

    /// <summary>
    /// Beschaffenheit der Datei im Klartext: Textebene, Textebene hinter Bildern, oder nur Bild.
    /// </summary>
    /// <remarks>
    /// Steht im Vorschlag neben der Sicherheit, weil es dieselbe Frage beantwortet: wie sehr darf
    /// man den Werten trauen.
    /// </remarks>
    public required string TextNote { get; init; }
    public required bool HasTextLayer { get; init; }

    /// <summary>Das gefundene Zielobjekt.</summary>
    public string? TargetName { get; init; }
    public string? TargetSub { get; init; }
    public int? TargetId { get; init; }

    /// <summary>Wie das Zielobjekt heißt („Vertrag“, „Depot“) und wohin der Knopf führt.</summary>
    public string? TargetNoun { get; init; }
    public string? TargetLink { get; init; }
    public string? TargetHref { get; init; }

    /// <summary>Datum des Schreibens.</summary>
    public DateOnly? DocumentDate { get; init; }

    /// <summary>Fachlicher Stichtag — der maßgebliche der beiden.</summary>
    public DateOnly? AsOf { get; init; }

    /// <summary>Die Analyseschritte, wie sie gelaufen sind.</summary>
    public required IReadOnlyList<string> Steps { get; init; }

    public required IReadOnlyList<ScanFieldDto> Fields { get; init; }

    /// <summary>Was der Übernahme im Weg steht — <c>null</c>, wenn nichts.</summary>
    public string? Blocker { get; init; }

    /// <summary>Wenn nichts erkannt wurde: warum.</summary>
    public string? Note { get; init; }
}

/// <summary>Ein gelesener Wert mit seiner Herkunft.</summary>
public sealed record ScanFieldDto
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// Der Wert als Text — für alles, was kein Geld ist: ISIN, Wertpapiername, Datum, Stückzahl.
    /// </summary>
    /// <remarks>
    /// Beträge stehen <em>nicht</em> hier, sondern in <see cref="Number"/> mit
    /// <see cref="IsMoney"/>. Ein fertig formatierter Euro-Betrag aus dem Server ließe sich von
    /// „Beträge verbergen“ nicht mehr maskieren — die Maske greift nur, wo die Oberfläche selbst
    /// formatiert.
    /// </remarks>
    public required string Display { get; init; }

    /// <summary>Zahlwert, wo es einen gibt.</summary>
    public decimal? Number { get; init; }

    /// <summary>Der Zahlwert ist ein Geldbetrag und wird maskierbar dargestellt.</summary>
    public bool IsMoney { get; init; }

    /// <summary>Seite, auf der er stand.</summary>
    public int? SourcePage { get; init; }

    public double Confidence { get; init; }

    /// <summary>Wird ins Zielobjekt übernommen.</summary>
    public bool Lead { get; init; }

    /// <summary>Angabe ohne Garantie — nie Teil einer Vermögenssumme.</summary>
    public bool Soft { get; init; }

    /// <summary>Was an diesem Wert auffällt.</summary>
    public string? Warning { get; init; }

    /// <summary>Unsicher — die Zeile gehört angesehen.</summary>
    public bool IsUncertain => Confidence < 0.8;

    /// <summary>Die Herkunftszeile, wie sie unter der Beschriftung steht.</summary>
    public string Source
    {
        get
        {
            var teile = new List<string>();

            if (SourcePage is { } seite)
            {
                teile.Add($"Seite {seite}");
            }

            if (Warning is { Length: > 0 } hinweis)
            {
                teile.Add(hinweis);
            }
            else if (Soft)
            {
                teile.Add("nicht garantiert");
            }
            else if (Lead)
            {
                teile.Add("wird übernommen");
            }

            return string.Join(" · ", teile);
        }
    }
}

/// <summary>
/// Das Ergebnis einer Übernahme.
/// </summary>
/// <remarks>
/// <see cref="Effect"/> nennt die <b>Wirkung</b>, nicht den Vorgang — „20.481,52 € übernommen ·
/// +521,38 € gegenüber dem Stand vom 31.07.2024“ statt „Werte gespeichert“. Wer eine Zahl in
/// sein Vermögen schreibt, will wissen, was sich dadurch ändert.
/// </remarks>
public sealed record ScanResultDto
{
    public required bool Saved { get; init; }

    /// <summary>Überschrift der Bestätigungsseite.</summary>
    public required string Title { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Der übernommene Leitwert, beschriftet.</summary>
    public string? LeadLabel { get; init; }
    public decimal? LeadNumber { get; init; }
    public bool LeadIsMoney { get; init; }

    /// <summary>
    /// Die Wirkung, in Bausteinen statt als fertiger Satz.
    /// </summary>
    /// <remarks>
    /// Der Satz entsteht je Dokumenttyp im Dienst — welche Bausteine in welcher Reihenfolge
    /// kommen, ist Fachwissen und gehört nicht in die Oberfläche. Formatiert wird trotzdem dort,
    /// sonst entkäme jeder Betrag darin der Maske „Beträge verbergen“.
    /// </remarks>
    public required IReadOnlyList<ScanEffectPart> Effect { get; init; }

    /// <summary>Die gelernte Ablageregel.</summary>
    public string? Rule { get; init; }

    public string? TargetLink { get; init; }
    public string? TargetHref { get; init; }

    /// <summary>Wenn nichts gespeichert wurde: warum.</summary>
    public string? Problem { get; init; }
}

/// <summary>
/// Ein Stück des Wirkungssatzes. Genau eines der Felder ist gesetzt.
/// </summary>
public sealed record ScanEffectPart
{
    /// <summary>Unveränderlicher Text — Wörter, Trennzeichen, Daten.</summary>
    public string? Text { get; init; }

    /// <summary>Ein Geldbetrag. Wird von der Oberfläche formatiert und maskiert.</summary>
    public decimal? Money { get; init; }

    /// <summary>Eine Stückzahl.</summary>
    public decimal? Quantity { get; init; }

    /// <summary>Ein Wertpapierkurs.</summary>
    public decimal? Price { get; init; }

    /// <summary>Mit Vorzeichen zeigen — für Veränderungen gegenüber einem früheren Stand.</summary>
    public bool Signed { get; init; }
}

/// <summary>Welche Werte übernommen werden sollen.</summary>
/// <remarks>
/// Die Oberfläche schickt zurück, was der Mensch gesehen und gegebenenfalls berichtigt hat. Leer
/// heißt: die gelesenen Werte unverändert übernehmen.
/// </remarks>
public sealed record ConfirmScanRequest
{
    public required int DocumentId { get; init; }
    public IReadOnlyDictionary<string, string>? Values { get; init; }
}
