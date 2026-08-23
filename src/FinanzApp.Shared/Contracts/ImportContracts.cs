namespace FinanzApp.Shared.Contracts;

/// <summary>Ergebnis der Vorschau eines CAMT- oder CSV-Imports. Es wird nichts geschrieben,
/// bevor der Nutzer den Import bestätigt.</summary>
public sealed record ImportPreviewDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string BankName { get; init; }

    /// <summary>Erkanntes Format, z. B. „CAMT.053“.</summary>
    public required string Format { get; init; }

    /// <summary>Verwendetes Importprofil.</summary>
    public required string ProfileName { get; init; }

    public required int RecordCount { get; init; }

    /// <summary>Werden importiert.</summary>
    public required int NewCount { get; init; }

    /// <summary>Per Importreferenz als bereits vorhanden erkannt.</summary>
    public required int ExistingCount { get; init; }

    /// <summary>Gleicher Betrag und Tag, aber andere Referenz — Prüfung empfohlen.</summary>
    public required int DuplicateCount { get; init; }

    public required int ErrorCount { get; init; }
}

public sealed record ImportCommitResultDto
{
    public required int ImportedCount { get; init; }
}
