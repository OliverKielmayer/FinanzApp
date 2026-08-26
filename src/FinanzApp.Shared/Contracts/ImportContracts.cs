namespace FinanzApp.Shared.Contracts;

/// <summary>Zustand eines Satzes aus dem Auszug.</summary>
public enum ImportRowState
{
    /// <summary>Neu — vorgeschlagen zur Übernahme.</summary>
    New = 0,

    /// <summary>Per Importreferenz als bereits gebucht erkannt.</summary>
    Existing = 1,

    /// <summary>Gleicher Tag, Empfänger und Betrag, aber andere Referenz.</summary>
    Duplicate = 2,

    /// <summary>Unlesbar — gezählt und benannt, nie stillschweigend übersprungen.</summary>
    Error = 3,
}

/// <summary>
/// Ergebnis der Vorschau eines CAMT- oder CSV-Imports. Es wird nichts geschrieben, bevor der
/// Nutzer den Import bestätigt.
/// </summary>
public sealed record ImportPreviewDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string BankName { get; init; }

    /// <summary>Erkanntes Format, z. B. „CAMT.053“.</summary>
    public required string Format { get; init; }

    /// <summary>Verwendetes Importprofil.</summary>
    public required string ProfileName { get; init; }

    /// <summary>Zeitraum des Auszugs.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Auszugssaldo, sofern die Datei einen nennt.</summary>
    public decimal? StatementBalance { get; init; }

    /// <summary>Trennzeichen bei CSV; bei CAMT leer.</summary>
    public string? Separator { get; init; }

    /// <summary>Konten zur Auswahl — das erkannte steht vorne.</summary>
    public required IReadOnlyList<ImportAccountDto> Accounts { get; init; }

    /// <summary>Aus IBAN bzw. CSV-Kopfzeile vorgeschlagen. Änderbar.</summary>
    public int? SuggestedAccountId { get; init; }

    public required int RecordCount { get; init; }
    public required int NewCount { get; init; }
    public required int ExistingCount { get; init; }
    public required int DuplicateCount { get; init; }
    public required int ErrorCount { get; init; }

    /// <summary>Woran die Duplikatprüfung hängt — der Hinweistext nennt das Kriterium.</summary>
    public required string DuplicateCriterion { get; init; }

    public required IReadOnlyList<ImportRowDto> Rows { get; init; }

    /// <summary>Der letzte Import, für den Leerzustand.</summary>
    public ImportHistoryDto? LastImport { get; init; }
}

public sealed record ImportAccountDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Iban { get; init; }
}

public sealed record ImportRowDto
{
    /// <summary>Stelle im Auszug — zugleich der Schlüssel der Auswahl.</summary>
    public required int Index { get; init; }

    public DateOnly? BookingDate { get; init; }
    public required string Payee { get; init; }
    public decimal? Amount { get; init; }
    public required ImportRowState State { get; init; }

    /// <summary>Bei fehlerhaften Sätzen: was nicht lesbar war.</summary>
    public string? Problem { get; init; }

    /// <summary>Kategorie aus den gelernten Regeln, falls eine greift.</summary>
    public string? CategoryName { get; init; }

    /// <summary>Vorschlag der App: neue Sätze angehakt, Treffer abgewählt.</summary>
    public required bool PreSelected { get; init; }
}

public sealed record ImportHistoryDto
{
    public required string FileName { get; init; }
    public required DateOnly ImportedOn { get; init; }
    public required string AccountName { get; init; }
    public required int RecordCount { get; init; }
}

/// <summary>Was tatsächlich übernommen werden soll — Auswahl und Zielkonto.</summary>
public sealed record ImportCommitRequest
{
    public required Guid PreviewId { get; init; }
    public required int AccountId { get; init; }
    public required IReadOnlyList<int> Indexes { get; init; }
}

public sealed record ImportCommitResultDto
{
    public required int ImportedCount { get; init; }

    /// <summary>Wie viele davon vorher als Duplikat galten und ausdrücklich zugeschaltet wurden.</summary>
    public required int ForcedDuplicates { get; init; }
}
