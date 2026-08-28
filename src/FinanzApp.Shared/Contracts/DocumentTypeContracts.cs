namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Ein Dokumenttyp mit seinem Verwendungsnachweis.
/// </summary>
/// <remarks>
/// Ein Typ ist keine Dekoration: er bestimmt den Vorschlag für den Ablagepfad und steuert, was
/// die Beleganalyse zu erkennen versucht. Wer ihn umbenennt oder löscht, greift darum in beides
/// ein — und muss sehen, wie viel daran hängt.
/// </remarks>
public sealed record DocumentTypeUsageDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required DocumentArea Area { get; init; }

    /// <summary>Wie viele abgelegte Dokumente diesen Typ tragen.</summary>
    public required int DocumentCount { get; init; }

    public bool IsUsed => DocumentCount > 0;
}

/// <summary>Ein Bereich mit der Zahl seiner Typen — die Chipreihe über der Liste.</summary>
public sealed record DocumentAreaCountDto(DocumentArea? Area, string Label, int Count);

public sealed record DocumentTypeOverviewDto
{
    public required int TotalCount { get; init; }
    public required IReadOnlyList<DocumentAreaCountDto> Areas { get; init; }
    public required IReadOnlyList<DocumentTypeUsageDto> Types { get; init; }
}

/// <param name="Name">Der Name des Typs.</param>
/// <param name="Area">
/// Der Bereich. Beim Anlegen setzt ihn der aktive Filter; steht der auf „Alle“, gibt es keinen
/// gewählten Bereich und der Typ landet unter „Sonstiges“.
/// </param>
public sealed record DocumentTypeNameRequest(string Name, DocumentArea Area = DocumentArea.Other);

/// <summary>Was eine Änderung angerichtet hat — die Meldung nennt die Zahl.</summary>
/// <param name="Name">Der betroffene Typ, nach der Änderung.</param>
/// <param name="DocumentCount">Wie viele Dokumente ihn tragen.</param>
public sealed record DocumentTypeChangeResultDto(string Name, int DocumentCount);
