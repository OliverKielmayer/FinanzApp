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

    /// <summary>
    /// Wie die Bank die Buchung nennt — „Dauerauftrag“, „SB-Auszahlung“, „Lohn/Gehalt/Rente“.
    /// </summary>
    /// <remarks>
    /// Bewusst <em>keine</em> Kategorie: an echten Daten geprüft trennt der Buchungstext keine
    /// Gruppe, die Empfänger und Vorzeichen nicht schon trennen — von neun Empfängern mit mehr
    /// als einem Text unterschieden acht nur Ein- von Ausgang. Eine Zuordnung daraus abzuleiten
    /// wäre geraten. Als Angabe an der Gruppe sagt er dagegen, um welche Art Umsatz es geht.
    /// </remarks>
    public string? BookingText { get; init; }

    /// <summary>Vorgeschlagene Kategorie, falls eine Regel greift.</summary>
    public int? SuggestedCategoryId { get; init; }

    /// <summary>Name dazu — dieselbe Kategorie, für die Anzeige.</summary>
    public string? CategoryName { get; init; }

    /// <summary>
    /// Woher der Vorschlag kommt: die Regel, die gegriffen hat.
    /// </summary>
    /// <remarks>
    /// Die Herkunft gehört in die Vorschau, nicht in den Client. Nur so lässt sich „automatisch
    /// zugeordnet“ von „im Import von Hand gewählt“ unterscheiden — und der Client zeigt und
    /// bestätigt bloß, statt die Zuordnung selbst zu erfinden.
    /// </remarks>
    public int? RuleId { get; init; }

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

/// <summary>Was tatsächlich übernommen werden soll — Auswahl, Zielkonto, Zuordnungen.</summary>
public sealed record ImportCommitRequest
{
    public required Guid PreviewId { get; init; }
    public required int AccountId { get; init; }
    public required IReadOnlyList<int> Indexes { get; init; }

    /// <summary>
    /// Im Import getroffene Zuordnungen, je Empfänger — nicht je Buchung.
    /// </summary>
    /// <remarks>
    /// Niemand kategorisiert dreihundert Buchungen einzeln. Gefragt wird nach dem Empfänger, und
    /// die Antwort gilt für alle seine Sätze. Was hier steht, hat Vorrang vor jeder Regel.
    /// </remarks>
    public IReadOnlyList<ImportCategoryChoice> Choices { get; init; } = [];
}

/// <summary>Eine Zuordnung aus dem Import.</summary>
/// <param name="Payee">Der Empfänger, für den sie gilt.</param>
/// <param name="CategoryId">Die gewählte Kategorie.</param>
/// <param name="RememberRule">
/// Ob daraus eine Regel wird. Gelernt wird erst bei der Übernahme — wer den Import verwirft,
/// soll keine Regel hinterlassen haben.
/// </param>
public sealed record ImportCategoryChoice(string Payee, int CategoryId, bool RememberRule);

public sealed record ImportCommitResultDto
{
    public required int ImportedCount { get; init; }

    /// <summary>Wie viele davon vorher als Duplikat galten und ausdrücklich zugeschaltet wurden.</summary>
    public required int ForcedDuplicates { get; init; }

    /// <summary>
    /// Wie viele Buchungen ohne Kategorie geblieben sind — die Brücke zum Triage-Banner der
    /// Buchungsliste. Wer das verschweigt, lässt sie dort unerklärt auftauchen.
    /// </summary>
    public required int WithoutCategory { get; init; }

    /// <summary>Die in diesem Lauf gelernten Regeln.</summary>
    public required IReadOnlyList<int> LearnedRuleIds { get; init; }
}
