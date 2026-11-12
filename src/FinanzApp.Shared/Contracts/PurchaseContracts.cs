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

    /// <summary>
    /// Buchungen, die diese Anschaffung bezahlt haben — jede mit dem Grund, warum sie hier
    /// steht.
    /// </summary>
    /// <remarks>
    /// <para>Zugeordnet wird über <b>eine Nummer, die im Beleg und im Verwendungszweck
    /// steht</b> — dieselbe Regel, an der ein Vorgang seine Zahlung findet. Bei einer
    /// Amazon-Bestellung ist das die Bestellnummer: Sie steht auf der Rechnung und die Bank
    /// schreibt sie in den Verwendungszweck.</para>
    /// <para>Der Betrag allein genügt nicht. Zwei Bestellungen desselben Tages über dieselbe
    /// Summe sind nichts Ungewöhnliches, und eine Zuordnung über Kreuz füle nie auf.</para>
    /// <para>Ohne eingelesenen Beleg bleibt die Liste leer: Es gibt dann keinen Text, in dem
    /// eine Nummer stehen könnte. Das ist keine Fehlfunktion, sondern die Bedingung.</para>
    /// </remarks>
    public IReadOnlyList<LinkedPaymentDto> Payments { get; init; } = [];
}
