namespace FinanzApp.Shared.Contracts;

/// <summary>Eine Anschaffung in der Liste.</summary>
public sealed record PurchaseListItemDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly PurchaseDate { get; init; }
    public string? Vendor { get; init; }
    public decimal? Price { get; init; }
    public DateOnly? WarrantyUntil { get; init; }

    /// <summary>Wie viele Belege daran hängen.</summary>
    public int DocumentCount { get; init; }

    /// <summary>
    /// Ob die Garantie am Stichtag der Abfrage noch läuft.
    /// </summary>
    /// <remarks>
    /// Vom Server entschieden und nicht in der Oberfläche gerechnet: „heute“ ist dort die Uhr
    /// des Geräts, und ein falsch gestellter Rechner zeigte eine abgelaufene Garantie als
    /// laufend. <c>null</c> heißt: Es ist keine Frist hinterlegt — nicht „abgelaufen“.
    /// </remarks>
    public bool? WarrantyRunning { get; init; }
}

/// <summary>Eine Anschaffung mit ihren Belegen.</summary>
public sealed record PurchaseDetailDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required DateOnly PurchaseDate { get; init; }
    public string? Vendor { get; init; }
    public decimal? Price { get; init; }
    public DateOnly? WarrantyUntil { get; init; }
    public bool? WarrantyRunning { get; init; }
    public string? Note { get; init; }

    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
}
