namespace FinanzApp.Shared.Contracts;

public sealed record DocumentTypeDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required DocumentArea Area { get; init; }
}

/// <summary>Eine Zeile der Dokumentliste.</summary>
public sealed record DocumentListItemDto
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public string? TypeName { get; init; }
    public required DocumentArea Area { get; init; }
    public DateOnly? DocumentDate { get; init; }

    /// <summary>Woran das Dokument hängt, für die Untertitelzeile.</summary>
    public string? LinkedLabel { get; init; }

    /// <summary>Freie Schlagworte. Sie stehen in der Zeile und greifen in der Suche.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Wer es sehen darf. Die Zeile trägt ein Zeichen, wenn es privat ist.</summary>
    public DocumentSharing Sharing { get; init; } = DocumentSharing.Household;

    /// <summary>
    /// Ob die Datei am hinterlegten Pfad liegt. Fehlt sie, bleibt die Zeile stehen und wird
    /// markiert — ausblenden wäre der schlechteste aller Auswege.
    /// </summary>
    public required bool FileExists { get; init; }
}

public sealed record DocumentPageDto
{
    public required IReadOnlyList<DocumentListItemDto> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int MissingFileCount { get; init; }

    /// <summary>
    /// Wie viele Dokumente noch keinen gelesenen Text haben.
    /// </summary>
    /// <remarks>
    /// Alles, was vor der Suche nach Text abgelegt wurde. Die Zahl steht am Fuß der Liste und
    /// hängt an einem Knopf: Ein Nachlauf ohne Auslöser wäre eine Funktion, die niemand findet.
    /// </remarks>
    public int WithoutTextCount { get; init; }

    /// <summary>
    /// Wie viele Dokumente an keinem Objekt hängen.
    /// </summary>
    /// <remarks>
    /// Abgelegt, aber noch nicht eingeordnet. Die Zahl ist kein Mangel — manche Post hängt an
    /// nichts und wird es nie —, sondern die Liste der Belege, bei denen noch etwas offen sein
    /// <em>könnte</em>. Ohne sie verschwänden sie zwischen den anderen, und genau das war der
    /// Grund, warum früher niemand ohne Objekt ablegen durfte.
    /// </remarks>
    public int WithoutLinkCount { get; init; }
}

public sealed record DocumentDetailDto
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public string? Extension { get; init; }
    public int? DocumentTypeId { get; init; }
    public string? TypeName { get; init; }
    public required DocumentArea Area { get; init; }
    public string? Description { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public required DocumentStatus Status { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Wer es sehen darf — Haushalt oder nur der Eigentümer.</summary>
    public DocumentSharing Sharing { get; init; } = DocumentSharing.Household;

    public required bool FileExists { get; init; }
    public required IReadOnlyList<DocumentLinkDto> Links { get; init; }

    /// <summary>
    /// Gelesene Werte, die noch niemand übernommen hat.
    /// </summary>
    /// <remarks>
    /// <para>Ein Beleg kann eingelesen, abgelegt und verknüpft sein, ohne dass je ein Wert in
    /// seinem Objekt gelandet ist — wer ihn im Scaneingang <em>wegräumt</em>, tut genau das. Von
    /// außen sah so ein Beleg danach aus wie ein übernommener; dreizehn Statusreporte eines
    /// Vertrags standen so da, und am Vertrag stand eine Null.</para>
    /// <para>Null heißt: nichts offen. Das ist auch der Normalfall für alles, was gar keine
    /// gelesenen Werte trägt — ein Foto, ein Schreiben ohne Leseregel.</para>
    /// </remarks>
    public int UnconfirmedValues { get; init; }

    /// <summary>Wohin der Prüfschritt führt, an dem sie sich übernehmen lassen.</summary>
    public string? ValuesHref { get; init; }
}

public sealed record DocumentLinkDto
{
    public required int Id { get; init; }
    public required LinkTargetType TargetType { get; init; }
    public required int TargetId { get; init; }

    /// <summary>Name des verknüpften Objekts.</summary>
    public required string Label { get; init; }

    /// <summary>Art des Objekts im Klartext, etwa „Versicherung“.</summary>
    public required string TargetLabel { get; init; }
}

/// <summary>
/// Ergebnis der Dokumentsuche. Sie trifft ausdrücklich <em>auch Objekte</em> — wer „hausrat“ sucht,
/// meint meist den Vertrag und nicht nur den Dateinamen.
/// </summary>
public sealed record DocumentSearchResultDto
{
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
    public required IReadOnlyList<ObjectHitDto> Objects { get; init; }
}

public sealed record ObjectHitDto
{
    public required LinkTargetType TargetType { get; init; }
    public required int TargetId { get; init; }
    public required string Label { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Art des Objekts im Klartext, für die Kennzeichnung des Treffers.</summary>
    public required string TargetLabel { get; init; }
}

public sealed record UpdateDocumentRequest
{
    public required string Title { get; init; }
    public int? DocumentTypeId { get; init; }
    public required DocumentArea Area { get; init; }
    public string? Description { get; init; }
    public DateOnly? DocumentDate { get; init; }
    public DateOnly? ValidFrom { get; init; }
    public DateOnly? ValidUntil { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>Korrigiert den hinterlegten Pfad eines Dokuments, dessen Datei verschoben wurde.</summary>
public sealed record FixDocumentPathRequest
{
    public required string RelativePath { get; init; }
}

public sealed record CreateDocumentLinkRequest
{
    public required LinkTargetType TargetType { get; init; }
    public required int TargetId { get; init; }
}

/// <summary>Antwort nach dem Hochladen einer Datei.</summary>
public sealed record DocumentUploadResultDto
{
    public required int DocumentId { get; init; }
    public required string RelativePath { get; init; }
}

/// <summary>Die gewünschte Freigabe eines Dokuments.</summary>
public sealed record DocumentSharingRequest
{
    public required DocumentSharing Sharing { get; init; }
}

/// <summary>Wie viele Dokumente beim Nachlauf einen Text bekommen haben.</summary>
public sealed record BackfillResultDto
{
    public required int Gelesen { get; init; }
}

/// <summary>
/// Was das Durchsehen durch das Sprachmodell ergeben hat.
/// </summary>
/// <remarks>
/// <c>Offen</c> steht dabei, weil in Schüben gearbeitet wird: Jeder Beleg kostet das Modell
/// ein bis zwei Sekunden, und eine Anfrage, die Minuten braucht, bricht unterwegs ab. Die Zahl
/// sagt, ob sich ein zweiter Druck lohnt.
/// </remarks>
public sealed record ActionScanResultDto
{
    /// <summary>Ob überhaupt ein Modell eingerichtet ist.</summary>
    public required bool Verfuegbar { get; init; }

    /// <summary>Belege, die das Modell in diesem Schub angesehen hat.</summary>
    public required int Durchgesehen { get; init; }

    /// <summary>Davon solche, die etwas verlangen.</summary>
    public required int Gefunden { get; init; }

    /// <summary>Belege, die danach noch keiner angesehen hat.</summary>
    public required int Offen { get; init; }
}

/// <summary>
/// Was das Nachziehen am Bestand geändert hat.
/// </summary>
/// <remarks>
/// Drei Zahlen und nicht eine, weil drei verschiedene Dinge geschehen sind und der Nutzer
/// wissen soll, welches davon seinen Bestand angefasst hat. <c>Umbenannt</c> ist die einzige,
/// die Dateien auf der Platte bewegt.
/// </remarks>
public sealed record BackfillDatesAndNamesResultDto
{
    /// <summary>Belege, deren Name nachträglich als von der Anwendung vergeben gilt.</summary>
    public required int Gekennzeichnet { get; init; }

    /// <summary>Belege, die das Datum vom Blatt statt den Tag ihrer Ablage tragen.</summary>
    public required int Datiert { get; init; }

    /// <summary>Belege, deren Datei einen neuen Namen bekommen hat.</summary>
    public required int Umbenannt { get; init; }

    /// <summary>Belege, die etwas verlangen, ohne dass es bisher jemand wusste.</summary>
    public int Gefordert { get; init; }

    /// <summary>Ob überhaupt etwas geschehen ist.</summary>
    public bool Nichts => Gekennzeichnet == 0 && Datiert == 0 && Umbenannt == 0 && Gefordert == 0;
}
