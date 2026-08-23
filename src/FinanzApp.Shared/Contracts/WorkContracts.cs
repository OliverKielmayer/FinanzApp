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
