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
    public required bool FileExists { get; init; }
    public required IReadOnlyList<DocumentLinkDto> Links { get; init; }
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
