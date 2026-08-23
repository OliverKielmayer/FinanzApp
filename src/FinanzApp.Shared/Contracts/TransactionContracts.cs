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

    /// <summary>Buchungen ohne Kategorie, ohne Suchfilter — speist das Triage-Banner.</summary>
    public required int UncategorizedCount { get; init; }
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
