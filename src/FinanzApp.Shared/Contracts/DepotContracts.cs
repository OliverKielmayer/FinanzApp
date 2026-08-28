namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Eine ausgeführte Order, wie die Anzeige sie braucht.
/// </summary>
/// <remarks>
/// Der Zuschlag steht getrennt vom Wert. Ihn einzurechnen und nur die Summe zu zeigen hieße,
/// eine Gebühr als schlechteren Kurs auszugeben.
/// </remarks>
public sealed record DepotTradeDto
{
    public required int Id { get; init; }
    public required string SecurityName { get; init; }
    public required string Isin { get; init; }
    public required string? Wkn { get; init; }

    public required DepotTradeKind Kind { get; init; }
    public required DepotOrderType OrderType { get; init; }
    public required decimal? LimitPrice { get; init; }

    public required DateTime ExecutedAt { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }

    /// <summary>Stück × Kurs.</summary>
    public required decimal Value { get; init; }

    /// <summary>Mindermengenzuschlag — eine Gebühr, kein Kursbestandteil.</summary>
    public required decimal Fee { get; init; }

    /// <summary>Was die Ausführung insgesamt gekostet hat.</summary>
    public decimal Total => Value + Fee;
}

/// <summary>Der Kopf des Transaktionsreiters. Zahlen, keine fertigen Sätze.</summary>
public sealed record DepotTradesHeadDto
{
    /// <summary>Anschaffungskosten aller Käufe abzüglich anteilig verkaufter Stücke.</summary>
    public required decimal CostBasis { get; init; }

    /// <summary>Enthaltene Gebühren — Teil der Anschaffungskosten, aber eigens ausgewiesen.</summary>
    public required decimal Fees { get; init; }

    public required decimal Quantity { get; init; }
    public required int ExecutionCount { get; init; }
    public required int BuyCount { get; init; }
    public required int SellCount { get; init; }

    /// <summary>Anschaffungskosten je Stück. <c>null</c>, solange nichts im Bestand ist.</summary>
    public required decimal? AverageCost { get; init; }

    /// <summary>Der letzte belegbare Kurs. <c>null</c>, wenn es keine Ausführung gibt.</summary>
    public required decimal? LastPrice { get; init; }

    /// <summary>Woher dieser Kurs stammt — ein Kurs ohne Herkunft darf nicht wie ein Live-Kurs aussehen.</summary>
    public required DateTime? LastPriceAt { get; init; }

    /// <summary>Aktueller Wert: Stück × letzter Kurs.</summary>
    public required decimal CurrentValue { get; init; }

    public decimal Gain => CurrentValue - CostBasis;

    /// <summary>Gewinn in Prozent. <c>null</c> ohne Einstand — dann gibt es nichts zu teilen.</summary>
    public required decimal? GainPercent { get; init; }

    /// <summary>Realisierter Gewinn aus Verkäufen.</summary>
    public required decimal RealisedGain { get; init; }
}

/// <summary>Ein Jahr im Filter mit seinen Zahlen.</summary>
public sealed record DepotYearDto(int? Year, int Count, decimal Quantity, decimal Value);

public sealed record DepotTradesDto
{
    public required DepotTradesHeadDto Head { get; init; }
    public required IReadOnlyList<DepotYearDto> Years { get; init; }
    public required IReadOnlyList<DepotTradeDto> Trades { get; init; }

    /// <summary>Anteil dieser Ausführung an den Anschaffungskosten — je Satz vorgerechnet.</summary>
    public required IReadOnlyDictionary<int, decimal> ShareOfCost { get; init; }
}

/// <summary>Eine Zeile, die beim Import nicht durchkam.</summary>
public sealed record DepotImportSkipDto(string Security, DateTime? ExecutedAt, string Reason);

/// <summary>
/// Was ein Orderimport ergeben hat.
/// </summary>
/// <remarks>
/// Die Zahlen sind drei, nicht eine: gelesen, übernommen, schon vorhanden. Nur „18 importiert“
/// ließe offen, ob acht fehlen oder acht schon da waren.
/// </remarks>
public sealed record DepotImportResultDto
{
    public required string FileName { get; init; }
    public required int ReadCount { get; init; }
    public required int ImportedCount { get; init; }

    /// <summary>Sätze, die schon im Bestand stehen — an Zeitpunkt, Stück und Kurs erkannt.</summary>
    public required int DuplicateCount { get; init; }

    public required IReadOnlyList<DepotImportSkipDto> Skipped { get; init; }
}
