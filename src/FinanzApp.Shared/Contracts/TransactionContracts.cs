namespace FinanzApp.Shared.Contracts;

public sealed record TransactionDto
{
    public required int Id { get; init; }
    public required DateOnly BookingDate { get; init; }
    public required string Payee { get; init; }
    public required TransactionKind Kind { get; init; }

    /// <summary>Vorzeichenbehaftet: Ausgaben und abgehende Umbuchungen negativ.</summary>
    public required decimal Amount { get; init; }

    public int? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public required int AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string AccountShortName { get; init; }
    public string? Note { get; init; }

    /// <summary>Die Importreferenz, sofern sie beim Import behalten wurde.</summary>
    public string? ImportReference { get; init; }

    /// <summary>
    /// Die Auszugsfelder, so wie sie an der Buchung stehen.
    /// </summary>
    /// <remarks>
    /// Gelesen wird ausschließlich, was gespeichert wurde — nie eine Nachschlagetabelle über den
    /// Empfängernamen. Sonst trüge auch eine von Hand erfasste Buchung plötzlich Auszugsdaten samt
    /// erfundener Referenz.
    /// </remarks>
    public StatementDetailsDto? Details { get; init; }

    /// <summary>Ob überhaupt etwas aus einem Auszug an der Buchung steht.</summary>
    public bool HasStatementData => ImportReference is not null || Details is not null;

    /// <summary>Umbuchungen brauchen keine Kategorie und gelten nie als „nicht zugeordnet“.</summary>
    public bool IsUncategorized => Kind != TransactionKind.Transfer && CategoryId is null;
}

/// <summary>Eine Seite der Buchungsliste inklusive der Zähler für Kopfzeile und Triage-Banner.</summary>
public sealed record TransactionPageDto
{
    public required IReadOnlyList<TransactionDto> Items { get; init; }

    /// <summary>Treffer der aktuellen Suche.</summary>
    public required int FilteredCount { get; init; }

    /// <summary>Buchungen insgesamt, ohne Suchfilter.</summary>
    public required int TotalCount { get; init; }

    /// <summary>Buchungen ohne Kategorie, ohne Suchfilter.</summary>
    public required int UncategorizedCount { get; init; }

    /// <summary>
    /// Buchungen ohne Kategorie <em>im gewählten Ausschnitt</em> — das speist das Triage-Banner.
    /// </summary>
    /// <remarks>
    /// Der Handoff verlangt den Bezug auf die sichtbare Menge: ein Banner über fünf
    /// unkategorisierte Buchungen, von denen der Filter keine einzige zeigt, wäre eine
    /// Aufforderung ins Leere.
    /// </remarks>
    public required int FilteredUncategorizedCount { get; init; }

    /// <summary>Summen über den sichtbaren Ausschnitt.</summary>
    public required TransactionTotalsDto Totals { get; init; }
}

/// <summary>
/// Einnahmen, Ausgaben und Saldo des sichtbaren Ausschnitts.
/// </summary>
/// <remarks>
/// Umbuchungen zählen in keiner der drei Zahlen mit: Geld, das von einem eigenen Konto auf ein
/// anderes wandert, ist weder Einnahme noch Ausgabe. Sie werden deshalb nur gezählt, nicht summiert.
/// </remarks>
public sealed record TransactionTotalsDto
{
    public required decimal Income { get; init; }

    /// <summary>Positiv geführt — das Vorzeichen steht in der Beschriftung.</summary>
    public required decimal Expense { get; init; }

    public required decimal Balance { get; init; }

    /// <summary>Wie viele Umbuchungen im Ausschnitt liegen, die hier bewusst nicht mitzählen.</summary>
    public required int TransferCount { get; init; }
}

public sealed record CreateTransactionRequest
{
    /// <summary>Vom Client vergebener Schlüssel. Ein erneuter Aufruf mit demselben Schlüssel
    /// liefert die bereits angelegte Buchung zurück, statt eine zweite anzulegen.</summary>
    public required Guid RequestKey { get; init; }

    public required TransactionKind Kind { get; init; }

    /// <summary>Betrag ohne Vorzeichen; das Vorzeichen ergibt sich aus <see cref="Kind"/>.</summary>
    public required decimal Amount { get; init; }

    public required int AccountId { get; init; }
    public int? CategoryId { get; init; }
    public string? Note { get; init; }
    public DateOnly? BookingDate { get; init; }
}

/// <summary>Stapelvergabe: eine Kategorie für mehrere Buchungen auf einmal.</summary>
public sealed record BatchAssignRequest
{
    public required IReadOnlyList<int> TransactionIds { get; init; }

    public int? CategoryId { get; init; }

    /// <summary>
    /// Stuft alle gewählten Buchungen als Umbuchung ein. Nur so werden bestehende Umbuchungen
    /// überhaupt angefasst.
    /// </summary>
    public bool MarkAsTransfer { get; init; }
}

/// <summary>
/// Was die Stapelvergabe getan hat — und was sie bewusst nicht angefasst hat.
/// </summary>
/// <remarks>
/// Die fachliche Regel des Handoffs: <b>Umbuchungen bleiben unverändert</b>, sofern nicht
/// ausdrücklich „Umbuchung“ gewählt wurde. Eine Umbuchung ist keine Ausgabe; sie nachträglich
/// in eine Kategorie zu zwingen, verfälschte jede Auswertung. Die Meldung nennt deshalb beides.
/// </remarks>
public sealed record BatchAssignResultDto
{
    /// <summary>Wie viele Buchungen die Kategorie bekommen haben.</summary>
    public required int Assigned { get; init; }

    /// <summary>Wie viele Umbuchungen unangetastet blieben.</summary>
    public required int ProtectedTransfers { get; init; }

    /// <summary>Der Satz für die Meldung, z. B. „6 × Wohnen · 1 Umbuchung geschützt“.</summary>
    public required string Message { get; init; }

    public required IReadOnlyList<TransactionDto> Items { get; init; }
}

public sealed record AssignCategoryRequest
{
    /// <summary><c>null</c> hebt die Zuordnung wieder auf.</summary>
    public int? CategoryId { get; init; }

    /// <summary>Legt zusätzlich eine Regel auf dem Empfänger-Präfix an.</summary>
    public bool CreateRule { get; init; }

    /// <summary>Stuft die Buchung als Umbuchung ein statt ihr eine Kategorie zu geben.
    /// Schließt <see cref="CategoryId"/> aus.</summary>
    public bool MarkAsTransfer { get; init; }
}
