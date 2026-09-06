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

    /// <summary>
    /// Die Rechenproben, die vor der Übernahme sichtbar sind — Abschnitt 15.6.
    /// </summary>
    /// <remarks>
    /// Sie stehen im Prüfschritt mit ihrer Begründung. Wer eine Zahl in sein Vermögen übernimmt,
    /// soll sehen, woran sie geprüft wurde — eine stille Prüfung überzeugt niemanden.
    /// </remarks>
    public required IReadOnlyList<ScanProofDto> Proofs { get; init; }

    public required IReadOnlyList<ScanFieldDto> Fields { get; init; }

    /// <summary>
    /// Die Zeilen der Wiederholgruppe — Abschnitt 17.2. <c>null</c>, wenn der Typ keine hat.
    /// </summary>
    /// <remarks>
    /// Sie stehen im Prüfschritt <em>vor</em> den Einzelfeldern: eine Aufstellung ist zuerst
    /// eine Liste von Positionen und erst danach ein Kopf mit Nummern.
    /// </remarks>
    public ScanRepeatDto? Repeat { get; init; }

    /// <summary>Was der Übernahme im Weg steht — <c>null</c>, wenn nichts.</summary>
    public string? Blocker { get; init; }

    /// <summary>Wenn nichts erkannt wurde: warum.</summary>
    public string? Note { get; init; }
}

/// <summary>Eine wiederholte Zeile — eine Position einer Aufstellung.</summary>
public sealed record ScanRowDto
{
    public required string Name { get; init; }

    /// <summary>Die Zeile in Worten: „763 Stück × 125,24 € · IE00B4L5Y983“.</summary>
    public required string Meta { get; init; }

    /// <summary>Der Wert, den diese Zeile beiträgt.</summary>
    public required decimal? Value { get; init; }

    /// <summary>
    /// Der Wert steht nicht auf dem Papier, sondern hat ihn jemand eingetragen.
    /// </summary>
    /// <remarks>
    /// Der Schirm zeigt es an der Zeile, und die Rechenprobe sagt es im Satz. Eine Probe, die
    /// aufgeht, weil ein Mensch die fehlende Zahl beigesteuert hat, ist etwas anderes als eine,
    /// die das Papier trägt — und der Unterschied gehört dorthin, wo jemand entscheidet.
    /// </remarks>
    public bool ByHand { get; init; }
}

/// <summary>Die Wiederholgruppe eines Dokuments.</summary>
public sealed record ScanRepeatDto
{
    public required string Title { get; init; }
    public required IReadOnlyList<ScanRowDto> Rows { get; init; }

    /// <summary>Die Summe der Zeilen.</summary>
    public required decimal Total { get; init; }

    /// <summary>Ob sie zum ausgewiesenen Gesamtwert passt.</summary>
    public required bool Matches { get; init; }

    /// <summary>Der Satz unter der Liste.</summary>
    public required string Note { get; init; }
}

/// <summary>Eine Rechenprobe mit ihrer Begründung.</summary>
public sealed record ScanProofDto
{
    /// <summary>Die Rechnung im Klartext.</summary>
    public required string Line { get; init; }

    /// <summary>Warum sie gemacht wird.</summary>
    public required string Why { get; init; }

    public required bool Passed { get; init; }
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

    /// <summary>
    /// Der Wert steht nicht auf dem Papier, sondern hat ihn jemand eingetragen.
    /// </summary>
    /// <remarks>
    /// Er steht dann anstelle der Herkunftsseite in der Zeile darunter. „Seite 1“ über einer
    /// Zahl, die ein Mensch getippt hat, wäre eine Herkunftsangabe, die nicht stimmt.
    /// </remarks>
    public bool ByHand { get; init; }

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

            if (ByHand)
            {
                teile.Add("von Hand eingetragen");
            }
            else if (SourcePage is { } seite)
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

    /// <summary>
    /// Von Hand eingetragene Zeilenwerte einer Wiederholgruppe, nach Zeilennummer ab null.
    /// </summary>
    /// <remarks>
    /// Sie gehören mitgeschickt und nicht nur in den Schirm: bei einer Aufstellung wandern die
    /// Zeilen in Bestände, und eine Berichtigung, die nur die Anzeige beruhigt, verschwiegen
    /// beim Speichern.
    /// </remarks>
    public IReadOnlyDictionary<int, string>? RowValues { get; init; }
}

/// <summary>
/// Wohin der Prüfschritt eines bereits abgelegten Belegs führt.
/// </summary>
/// <remarks>
/// Nicht jeder Prüfschritt sieht gleich aus: ein Statusreport trägt einen Wert für einen Vertrag,
/// eine Vermögensübersicht acht Werte für acht Verträge. Die Weiche steht hier und nicht an jeder
/// Fläche, die sie braucht — zwei Fassungen davon liefen auseinander, sobald eine dritte Art
/// dazukommt.
/// </remarks>
public static class ScanReview
{
    public static string Href(string? scanKind, int documentId) => scanKind switch
    {
        AssetSheetKind.Key => AssetSheetKind.Href(documentId),

        // Auch ohne Leseabdruck: die Nachschau liest die Datei mit den Regeln von heute und
        // erkennt die Art dabei neu.
        _ => $"/scannen/{documentId}",
    };
}

/// <summary>Die Nachschau mit berichtigten Zeilen.</summary>
/// <remarks>
/// Gerechnet wird auf dem Server, auch wenn die Zahl aus dem Schirm kommt. Sonst gäbe es zwei
/// Rechenproben — eine im Browser und eine bei der Übernahme —, und die erste würde geglaubt.
/// </remarks>
public sealed record ScanReviewRequest
{
    public IReadOnlyDictionary<int, string>? RowValues { get; init; }

    /// <summary>
    /// Von Hand eingetragene Einzelwerte, nach Feldschlüssel.
    /// </summary>
    /// <remarks>
    /// Sie gehen denselben Weg wie die Zeilen: der Server liest sie mit derselben Regel und
    /// rechnet die Proben neu. Vorher berichtigte der Schirm nur seinen eigenen Merkzettel — die
    /// Anzeige blieb auf dem gelesenen Wert stehen, und über der Zeile stand trotzdem „Was hier
    /// steht, wird übernommen“. Übernommen wurde der berichtigte; zu sehen war der alte.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Values { get; init; }
}

// ── Einlieferung aus einem überwachten Ordner ───────────────────────────────────────────────

/// <summary>Wie weit eine eingelieferte Datei gekommen ist.</summary>
public enum ScanIntakeOutcome
{
    /// <summary>
    /// Bereich, Typ und Objekt stehen. Der Beleg wartet im Scaneingang nur noch darauf,
    /// dass ein Mensch ihn bestätigt.
    /// </summary>
    Assigned = 0,

    /// <summary>
    /// Etwas fehlt. Die Datei ist abgelegt und liegt im Scaneingang, bis jemand nachträgt,
    /// wozu sie gehört.
    /// </summary>
    Waiting = 1,

    /// <summary>
    /// Byte für Byte dieselbe Datei liegt schon in der Ablage. Nichts ist abgelegt worden,
    /// nichts wartet im Eingang; <see cref="ScanIntakeResultDto.DocumentId"/> nennt das
    /// vorhandene Dokument.
    /// </summary>
    /// <remarks>
    /// Kein Fehler: der überwachte Ordner bekommt dieselbe Datei mitunter zweimal, und wer im
    /// Zweifel noch einmal einliest, tut das Richtige. Der Fehler wäre, sie ein zweites Mal
    /// abzulegen — dann stünde derselbe Beleg zweimal in der Ablage, und beim nächsten Suchen
    /// wüsste niemand, welcher der gültige ist.
    /// </remarks>
    Duplicate = 2,
}

/// <summary>
/// Was aus einer Datei geworden ist, die ein überwachter Ordner hereingereicht hat.
/// </summary>
/// <remarks>
/// <para>Die Antwort auf <c>POST /api/scan/intake</c>. Sie geht an einen Dienst und nicht an
/// einen Bildschirm — deshalb steht in <see cref="Summary"/> ein fertiger Satz fürs Protokoll.
/// Beträge kommen darin nicht vor: die Einlieferung ordnet ein, sie übernimmt keine Werte.</para>
/// <para><b>Nichts hier verändert eine Vermögenszahl.</b> Ein unbeaufsichtigter Dienst kann
/// keinen Menschen ersetzen, der die gelesenen Werte neben ihrer Herkunftsseite gesehen hat —
/// die Übernahme bleibt <c>POST /api/scan/confirm</c> und damit dem Nutzer vorbehalten.</para>
/// </remarks>
public sealed record ScanIntakeResultDto
{
    public required int DocumentId { get; init; }

    /// <summary>
    /// Der Eintrag im Scaneingang, unter dem der Beleg wartet — <c>null</c> bei einer Dublette.
    /// </summary>
    /// <remarks>
    /// Eine abgewiesene Datei wartet nirgends: sie ist nicht abgelegt worden, also gibt es
    /// keinen Eintrag. Eine Null stünde hier für einen Eintrag, den es nicht gibt.
    /// </remarks>
    public int? InboxId { get; init; }

    public required string FileName { get; init; }

    /// <summary>Pfad unter dem Dokumentordner — wo die Datei nun wirklich liegt.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Der Bereich, in den sie eingeordnet wurde.</summary>
    public required DocumentArea Area { get; init; }

    public required ScanIntakeOutcome Outcome { get; init; }

    public required int PageCount { get; init; }

    /// <summary>Erkannte Dokumentart, <c>null</c>, wenn keine passt.</summary>
    public string? KindKey { get; init; }
    public string? KindLabel { get; init; }

    /// <summary>Gesetzter Dokumenttyp aus der Liste des Haushalts.</summary>
    public string? TypeName { get; init; }

    /// <summary>Das verknüpfte Objekt und wie es heißt („Vertrag“, „Depot“).</summary>
    public string? TargetName { get; init; }
    public string? TargetNoun { get; init; }

    /// <summary>Was zur vollständigen Zuordnung noch fehlt — <c>null</c>, wenn nichts.</summary>
    public string? Missing { get; init; }

    /// <summary>
    /// Das Dokument, das denselben Beleg schon trägt.
    /// </summary>
    /// <remarks>
    /// Bei <see cref="ScanIntakeOutcome.Duplicate"/> ist es dieselbe Datei, und dann steht hier
    /// dasselbe wie in <see cref="DocumentId"/>. Sonst ist es ein <em>Verdacht</em>: ein Beleg,
    /// den die Ablage unter denselben Namen gelegt hätte — gleiche Art, gleicher Stichtag,
    /// gleiche Zuordnung. Der ist abgelegt worden und wartet im Eingang; es kann eine berichtigte
    /// Fassung sein, und das entscheidet kein Dienst.
    /// </remarks>
    public int? DuplicateOfDocumentId { get; init; }

    /// <summary>Eine Zeile fürs Protokoll des einliefernden Dienstes.</summary>
    public required string Summary { get; init; }
}
