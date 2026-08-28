namespace FinanzApp.Shared.Contracts;

/// <summary>Eine Zeile des Bestandsnachweises.</summary>
public sealed record DepotStatementPositionDto
{
    public required int Id { get; init; }
    public required string SecurityName { get; init; }
    public required string Isin { get; init; }
    public required string? Wkn { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required decimal Value { get; init; }
    public required string? SafeCustody { get; init; }
    public required string? Country { get; init; }
    public required string? Depository { get; init; }
}

/// <summary>
/// Eine Quartalsaufstellung.
/// </summary>
/// <remarks>
/// <see cref="AsOf"/> und <see cref="IssuedOn"/> sind zwei verschiedene Daten und werden nie
/// vermischt: ein Schreiben vom Mai über den Bestand vom März sagt etwas über den März.
/// </remarks>
public sealed record DepotStatementDto
{
    public required int Id { get; init; }
    public required DateOnly AsOf { get; init; }
    public required DateOnly? IssuedOn { get; init; }
    public required string? DepotNumber { get; init; }
    public required string? Reference { get; init; }
    public required string? Custodian { get; init; }

    public required int? DocumentId { get; init; }
    public required string? DocumentTitle { get; init; }

    public required IReadOnlyList<DepotStatementPositionDto> Positions { get; init; }

    /// <summary>Der ausgewiesene Depotwert zum Stichtag.</summary>
    public required decimal Value { get; init; }
}

/// <summary>Ein Wertpapier im Abgleich: was die Bank ausweist gegen das, was die Orders ergeben.</summary>
public sealed record ReconciliationRowDto
{
    public required string SecurityName { get; init; }
    public required string Isin { get; init; }

    /// <summary>Nominale laut Aufstellung.</summary>
    public required decimal StatementQuantity { get; init; }

    /// <summary>Stück aus den Ausführungen bis zum Stichtag.</summary>
    public required decimal TradeQuantity { get; init; }

    public decimal Difference => TradeQuantity - StatementQuantity;

    /// <summary>Anschaffungskosten der Stücke bis zum Stichtag.</summary>
    public required decimal TradeCost { get; init; }

    public required decimal StatementValue { get; init; }
}

/// <summary>
/// Der Bestandsabgleich — v5-Handoff, Abschnitt 11.3.
/// </summary>
/// <remarks>
/// <para>Stimmen die Stückzahlen, ist der Depotwert <b>belegt</b>. Weichen sie ab, fehlen meist
/// Käufe aus einer nicht importierten Datei — und dann ist jede Zahl darüber unsicher.</para>
/// <para>Verglichen wird je Wertpapier. Stückzahlen verschiedener Papiere zu addieren ergäbe
/// eine Zahl, die nichts bedeutet.</para>
/// </remarks>
public sealed record DepotReconciliationDto
{
    public required DateOnly AsOf { get; init; }

    /// <summary>Ausgewiesener Wert laut Aufstellung.</summary>
    public required decimal StatementValue { get; init; }

    /// <summary>Anschaffungskosten aller Ausführungen bis zum Stichtag.</summary>
    public required decimal TradeCost { get; init; }

    /// <summary>Wert minus Einstand — der Buchgewinn zum Stichtag.</summary>
    public decimal BookGain => StatementValue - TradeCost;

    /// <summary>Ob jede Stückzahl stimmt und kein Papier nur auf einer Seite steht.</summary>
    public required bool Matches { get; init; }

    public required IReadOnlyList<ReconciliationRowDto> Rows { get; init; }

    /// <summary>Wie viele Ausführungen bis zum Stichtag eingerechnet wurden.</summary>
    public required int TradeCount { get; init; }
}

public sealed record DepotStatementsDto
{
    public required IReadOnlyList<DepotStatementDto> Statements { get; init; }

    /// <summary>Der Abgleich der jüngsten Aufstellung. <c>null</c>, solange keine erfasst ist.</summary>
    public required DepotReconciliationDto? Reconciliation { get; init; }
}

public sealed record CreateDepotStatementRequest
{
    public required DateOnly AsOf { get; init; }
    public DateOnly? IssuedOn { get; init; }
    public string? DepotNumber { get; init; }
    public string? Reference { get; init; }
    public string? Custodian { get; init; }
    public int? DocumentId { get; init; }

    public required IReadOnlyList<CreateDepotStatementPosition> Positions { get; init; }
}

public sealed record CreateDepotStatementPosition
{
    public required string SecurityName { get; init; }
    public required string Isin { get; init; }
    public string? Wkn { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }

    /// <summary>Kurswert laut Schreiben. Ohne Angabe Nominale × Kurs.</summary>
    public decimal? Value { get; init; }

    public string? SafeCustody { get; init; }
    public string? Country { get; init; }
    public string? Depository { get; init; }
}
