namespace FinanzApp.Shared.Contracts;

/// <summary>Eine Zeile im Vorgänge-Tab.</summary>
public sealed record TaskItemDto
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public DateOnly? DueOn { get; init; }
    public required TaskState State { get; init; }
    public required TaskSource Source { get; init; }
    public LinkTargetType? SourceType { get; init; }
    public int? SourceId { get; init; }

    /// <summary>Betrag, wenn der Vorgang einen trägt — Erstattung, Rechnung.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Tage seit der Fälligkeit. 0 oder kleiner heißt: noch nicht überfällig.</summary>
    public required int DaysOverdue { get; init; }

    public bool IsOverdue => DaysOverdue > 0 && State != TaskState.Done;

    /// <summary>
    /// Darf dieser Vorgang von Hand abgehakt werden?
    /// </summary>
    /// <remarks>
    /// <para><b>Abhaken darf, wer sonst niemanden hat.</b> Die meisten Vorgänge spiegeln den
    /// Stand eines Objekts — eine offene Rechnung, eine nicht eingereichte Arztrechnung, eine
    /// Kündigungsfrist. Sie schließen sich dort, wo dieser Stand sich ändert. Hier abgehakt
    /// hieße: Die Rechnung bleibt offen, und der einzige Hinweis darauf ist weg — und da ein
    /// erledigter Vorgang nicht wiederbelebt wird, käme er auch nicht zurück.</para>
    /// <para>Ein Schreiben hat kein solches Objekt. Was es verlangt, steht auf dem Papier und
    /// sonst nirgends; ob es jemand getan hat, weiß nur er selbst. Dasselbe gilt für eine
    /// Aufgabe, die jemand von Hand angelegt hat.</para>
    /// </remarks>
    public bool CanComplete => State != TaskState.Done
        && Source is TaskSource.Manual or TaskSource.DocumentAction;

    /// <summary>
    /// Buchungen, die diese Forderung bezahlt haben könnten.
    /// </summary>
    /// <remarks>
    /// Nur Vorschläge: Was eindeutig belegt war, ist längst zugeordnet und der Vorgang
    /// geschlossen — hier steht, was übrig blieb und wofür ein Mensch gebraucht wird. Leer ist
    /// der Normalfall.
    /// </remarks>
    public IReadOnlyList<TaskPaymentDto> PaymentSuggestions { get; init; } = [];
}

/// <summary>Eine Buchung, die zu einer Forderung aus einem Schreiben passt.</summary>
/// <remarks>
/// Verwendungszweck und Empfänger stehen dabei, weil nur sie die Entscheidung tragen: Betrag
/// und Tag passen bei jedem Vorschlag, sonst stünde er nicht da.
/// </remarks>
public sealed record TaskPaymentDto
{
    public required int TransactionId { get; init; }
    public required DateOnly BookedOn { get; init; }
    public required decimal Amount { get; init; }
    public string? Payee { get; init; }
    public string? Purpose { get; init; }

    /// <summary>
    /// Die Nummer, die in Verwendungszweck und Beleg steht — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Steht hier nichts, passt nur der Betrag im Zeitraum. Das kann jede andere Zahlung
    /// derselben Höhe auch, und deshalb ist dieser Vorschlag einer und keine Feststellung.
    /// </remarks>
    public string? Evidence { get; init; }
}

public sealed record TaskListDto
{
    public required IReadOnlyList<TaskItemDto> Items { get; init; }
    public required int OpenCount { get; init; }
    public required int WaitingCount { get; init; }
    public required int DoneCount { get; init; }
}

public sealed record CreateTaskRequest
{
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public DateOnly? DueOn { get; init; }
}

public sealed record UpdateTaskStateRequest
{
    public required TaskState State { get; init; }
}

/// <summary>Kurzfassung der offenen Vorgänge für das Banner auf dem Dashboard.</summary>
public sealed record OpenWorkSummaryDto
{
    public required int OpenCount { get; init; }

    /// <summary>Summe der erwarteten Erstattungen.</summary>
    public required decimal ExpectedReimbursement { get; init; }

    public required int DueInvoiceCount { get; init; }
    public required int OverdueCount { get; init; }
}
