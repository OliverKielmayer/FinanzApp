namespace FinanzApp.Shared.Contracts;

public sealed record AccountDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Kurzform für die Buchungsliste, z. B. „Sparkasse“.</summary>
    public required string ShortName { get; init; }

    /// <summary>IBAN bei Girokonten, sonst <c>null</c>.</summary>
    public string? Iban { get; init; }

    /// <summary>Nominalzins bei Tagesgeldkonten, sonst <c>null</c>.</summary>
    public decimal? InterestRatePercent { get; init; }

    /// <summary>Bisher gutgeschriebene Zinsen im laufenden Jahr.</summary>
    public decimal? InterestYearToDate { get; init; }

    public required decimal Balance { get; init; }

    /// <summary>Stand des Saldos — die Oberfläche macht daraus „heute“ oder ein Datum.</summary>
    public required DateOnly BalanceAsOf { get; init; }
}
