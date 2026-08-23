namespace FinanzApp.Shared.Contracts;

public sealed record PortfolioDto
{
    public required string DepotName { get; init; }
    public required decimal TotalValue { get; init; }

    /// <summary>Gewinn/Verlust gegenüber dem Einstand, vorzeichenbehaftet.</summary>
    public required decimal Gain { get; init; }

    public required decimal GainPercent { get; init; }

    /// <summary>Zeitgewichtete Rendite p. a.</summary>
    public required decimal TwrorPercent { get; init; }

    /// <summary>Stand der Kursdaten. Muss sichtbar bleiben — die Kurse kommen aus einem
    /// austauschbaren Provider und können veralten.</summary>
    public required DateTime PricesAsOf { get; init; }

    /// <summary>Gesetzt, wenn der Kursprovider nicht erreichbar war und zuletzt bekannte
    /// Kurse ausgeliefert werden.</summary>
    public bool PricesStale { get; init; }

    public required IReadOnlyList<TimeSeriesPointDto> History { get; init; }
    public required IReadOnlyList<PositionDto> Positions { get; init; }
}

public sealed record PositionDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Isin { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required decimal Value { get; init; }
    public required decimal GainPercent { get; init; }
}
