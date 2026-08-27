namespace FinanzApp.Shared.Contracts;

/// <param name="UserId">Der Berechtigte.</param>
/// <param name="Name">Sein Name, für die Klartextfolge der Freigabe.</param>
public sealed record SharedWithDto(int UserId, string Name);

/// <summary>Eine geänderte Freigabe.</summary>
/// <param name="Sharing">Die Stufe.</param>
/// <param name="UserIds">Die namentlich Berechtigten — nur bei <c>Named</c> ausgewertet.</param>
public sealed record AccountSharingRequest(AccountSharing Sharing, IReadOnlyList<int> UserIds);

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

    /// <summary>Wem das Konto gehört. <c>null</c> bei Konten aus der Zeit vor den Freigaben.</summary>
    public int? OwnerUserId { get; init; }
    public string? OwnerName { get; init; }

    /// <summary>Ob der angemeldete Benutzer der Eigentümer ist — nur er darf die Freigabe ändern.</summary>
    public required bool IsMine { get; init; }

    public required AccountSharing Sharing { get; init; }

    /// <summary>
    /// Die namentlich Berechtigten, wenn <see cref="Sharing"/> auf Named steht.
    /// </summary>
    /// <remarks>
    /// Namen statt eines fertigen Satzes: der Tag an der Kontozeile ist <em>perspektivisch</em> —
    /// dasselbe Konto heißt für den Eigentümer „geteilt mit Sabine“ und für Sabine „geteilt von
    /// Oliver“. Ein serverseitig fertiger Text wäre für eine der beiden Seiten falsch.
    /// </remarks>
    public IReadOnlyList<SharedWithDto> SharedWith { get; init; } = [];

    /// <summary>Stand des Saldos — die Oberfläche macht daraus „heute“ oder ein Datum.</summary>
    public required DateOnly BalanceAsOf { get; init; }
}
