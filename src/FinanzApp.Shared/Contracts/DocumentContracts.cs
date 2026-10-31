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
