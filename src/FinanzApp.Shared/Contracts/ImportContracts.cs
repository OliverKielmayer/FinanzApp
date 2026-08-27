namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Wie lange eine gelesene Vorschau auf dem Server liegen bleibt.
/// </summary>
/// <remarks>
/// Steht im gemeinsamen Vertrag, weil beide Seiten dieselbe Zahl brauchen: der Server räumt
/// danach auf, der Client darf einen begonnenen Import danach nicht mehr anbieten. Zwei getrennte
/// Zahlen liefen auseinander, und das Ergebnis wäre ein Entwurf, der beim Klick ins Leere greift.
/// </remarks>
public static class ImportPreviewCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
}

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
    /// Die Referenz, an der die Duplikatprüfung hängt — <c>AcctSvcrRef</c>.
    /// </summary>
    /// <remarks>
    /// Sie steht im Detailpanel als Tatsache aus der Datei. Ob sie mitgespeichert wird, sagt der
    /// Schalter darunter — beides in dasselbe Feld zu schreiben, ersätze die Angabe durch ihren
    /// eigenen Zustand.
    /// </remarks>
    public string? Reference { get; init; }

    /// <summary>Die Felder des Auszugs zu diesem Satz.</summary>
    public StatementDetailsDto? Details { get; init; }

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

/// <summary>
/// Die Felder eines Auszugssatzes, jedes mit seiner Herkunft aus ISO 20022.
/// </summary>
/// <remarks>
/// <para><c>null</c> heißt „steht nicht im Auszug“ — nie ein Leerstring. Die Anzeige verspricht,
/// genau diesen Unterschied zu zeigen, und kann ihn nur zeigen, wenn er im Vertrag steht.</para>
/// <para>Der Buchungstext ist bewusst <em>keine</em> Kategorie: an echten Daten geprüft trennt er
/// keine Gruppe, die Empfänger und Vorzeichen nicht schon trennen — von neun Empfängern mit mehr
/// als einem Text unterschieden acht nur Ein- von Ausgang.</para>
/// </remarks>
public sealed record StatementDetailsDto
{
    /// <summary>Wertstellung — <c>ValDt</c>.</summary>
    public DateOnly? ValueDate { get; init; }

    /// <summary>Währung — <c>Amt</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>IBAN der Gegenseite — <c>CdtrAcct</c> bzw. <c>DbtrAcct</c>.</summary>
    public string? CounterpartyIban { get; init; }

    /// <summary>BIC der Gegenseite — <c>Agt</c>.</summary>
    public string? CounterpartyBic { get; init; }

    /// <summary>Verwendungszweck — <c>RmtInf</c>.</summary>
    public string? Purpose { get; init; }

    /// <summary>Buchungsart — <c>AddtlNtryInf</c>.</summary>
    public string? BookingText { get; init; }

    /// <summary>Geschäftsvorfallcode — <c>Domn/Fmly</c>, etwa <c>PMNT-RDDT-ESDD</c>.</summary>
    public string? BankTransactionCode { get; init; }

    /// <summary>Der hauseigene Code der Bank — <c>Prtry</c>.</summary>
    public string? ProprietaryCode { get; init; }

    /// <summary>Der Auszug, aus dem der Satz stammt — <c>Stmt</c>.</summary>
    public string? StatementId { get; init; }
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

    /// <summary>Welche Auszugsfelder mitgespeichert werden. Vorgabe: alle.</summary>
    public ImportKeepFields Keep { get; init; } = new();

    /// <summary>Sätze, für die etwas anderes gilt als die Vorgabe.</summary>
    public IReadOnlyList<ImportKeepOverride> KeepOverrides { get; init; } = [];
}

/// <summary>
/// „Beim Import behalten“ — was von den Auszugsfeldern in der Buchung landet.
/// </summary>
/// <remarks>
/// Standard ist alles an. Die Referenz verdient dabei besondere Erwähnung: sie ist das
/// Duplikatkriterium. Tag, Betrag und Empfänger sind nur der Notnagel für Auszüge, die keine
/// Referenz liefern — wer sie abschaltet, verliert die verlässliche Wiedererkennung.
/// </remarks>
/// <param name="Purpose">Verwendungszweck speichern; danach in der Buchungsliste durchsuchbar.</param>
/// <param name="Counterparty">IBAN und BIC der Gegenseite speichern.</param>
/// <param name="Reference">Importreferenz speichern.</param>
public sealed record ImportKeepFields(
    bool Purpose = true, bool Counterparty = true, bool Reference = true);

/// <param name="Index">Der Satz, für den etwas anderes gilt.</param>
/// <param name="Keep">Was für ihn gespeichert wird.</param>
public sealed record ImportKeepOverride(int Index, ImportKeepFields Keep);

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
