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

    /// <summary>
    /// Ob am Konto ein Bankzugang steht.
    /// </summary>
    /// <remarks>
    /// Nur ja oder nein. Benutzerkennung und Zugangsadresse bleiben am Server — die Oberfläche
    /// muss wissen, ob sie den Abruf anbieten kann, und sonst nichts. Ob der Zugang auch
    /// <em>vollständig</em> ist, sagt dieses Feld nicht; danach fragt die Seite erst, wenn
    /// jemand hinsieht.
    /// </remarks>
    public bool HasBankAccess { get; init; }

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

// ── Bankabruf ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Was die Bank sagt, was die Anwendung rechnet — und der Unterschied.
/// </summary>
/// <remarks>
/// <para><b>Drei Zahlen, nicht eine.</b> Der Saldo dieser Anwendung ist nichts Gespeichertes,
/// sondern das Ergebnis aus Anfangsbestand und allen Buchungen. Ein Banksaldo ist eine zweite
/// Zahl aus einer anderen Quelle. Sie übereinanderzuschreiben hiesse, eine Rechnung durch eine
/// Behauptung zu ersetzen.</para>
/// <para><b>Eine Abweichung ist eine Auskunft.</b> Sie bedeutet: Es fehlen Buchungen, oder es
/// stehen welche zu viel da. Das ist der Befund, nach dem man sucht — und keine Zahl, die man
/// wegrechnet.</para>
/// </remarks>
public sealed record BankBalanceDto
{
    public required int AccountId { get; init; }
    public required string AccountName { get; init; }

    /// <summary>Was die Bank meldet.</summary>
    public required decimal BankBalance { get; init; }

    /// <summary>
    /// Wann abgerufen wurde.
    /// </summary>
    /// <remarks>
    /// Der Zeitpunkt der Frage, nicht ein von der Bank genannter Stichtag: Die Bank nennt im
    /// Saldo zwar einen Buchungstag, die verwendete Bibliothek reicht ihn aber nicht durch. Ein
    /// Feld „Stand vom“ behauptete deshalb mehr, als hier ankommt.
    /// </remarks>
    public required DateTime RetrievedAt { get; init; }

    /// <summary>Was die Anwendung aus Anfangsbestand und Buchungen rechnet.</summary>
    public required decimal LedgerBalance { get; init; }

    /// <summary>Bank minus Anwendung. Positiv heisst: die Bank hat mehr.</summary>
    public decimal Difference => BankBalance - LedgerBalance;

    /// <summary>
    /// Ob beide Zahlen dasselbe sagen.
    /// </summary>
    /// <remarks>
    /// Mit einem halben Cent Spielraum: Ein Vergleich auf Gleichheit zweier Dezimalzahlen aus
    /// verschiedenen Quellen findet sonst Unterschiede, die keine sind.
    /// </remarks>
    public bool Matches => Math.Abs(Difference) < 0.005m;
}

/// <summary>
/// Die PIN für einen einzelnen Abruf.
/// </summary>
/// <remarks>
/// Sie wird nicht gespeichert — weder in der Datenbank noch in der Konfiguration. Sie kommt mit
/// der Anfrage, lebt für den Dialog mit der Bank und ist danach weg. Der Preis ist bekannt: kein
/// Abruf im Hintergrund, nur auf Knopfdruck.
/// </remarks>
public sealed record BalanceCheckRequest
{
    public required string Pin { get; init; }
}

/// <summary>
/// Was der Umsatzabruf braucht: die PIN und wahlweise einen Zeitraum.
/// </summary>
/// <remarks>
/// <para>Der Zeitraum ist freiwillig. Ohne ihn fragt der Abruf die letzten neunzig Tage ab —
/// weiter geben die meisten Banken ohnehin nicht her, und was doppelt kommt, erkennt die
/// Duplikatprüfung.</para>
/// <para>Eine eigene Anfrage und nicht dieselbe wie beim Saldo: Der Saldo kennt keinen
/// Zeitraum, und ein gemeinsames Feld, das bei der einen Hälfte der Aufrufe leer bleibt, sagt
/// niemandem mehr, wozu es da ist.</para>
/// </remarks>
public sealed record StatementFetchRequest
{
    public required string Pin { get; init; }

    /// <summary>Erster Tag des Zeitraums. Leer heißt: neunzig Tage zurück.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Letzter Tag des Zeitraums. Leer heißt: heute.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>
/// Ein festgestellter Kontostand zu einem Stichtag.
/// </summary>
/// <remarks>
/// Beide Zahlen stehen nebeneinander, die der Bank und die der Anwendung. Die Differenz ist
/// nicht der Fehler, sondern der Befund: Sie beziffert, wie viel an Buchungen bis zu diesem Tag
/// fehlte oder zu viel dastand.
/// </remarks>
public sealed record BalanceCheckpointDto
{
    public required int Id { get; init; }
    public required int AccountId { get; init; }

    /// <summary>Der Tag, zu dem der Stand gilt.</summary>
    public required DateOnly AsOf { get; init; }

    /// <summary>Was tatsächlich auf dem Konto war.</summary>
    public required decimal Balance { get; init; }

    /// <summary>Was die Anwendung zu diesem Stichtag gerechnet hatte.</summary>
    public required decimal LedgerBalance { get; init; }

    public required BalanceCheckpointSource Source { get; init; }

    public string? Note { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>Festgestellt minus gerechnet. Positiv heißt: es fehlten Eingänge.</summary>
    public decimal Difference => Balance - LedgerBalance;

    /// <summary>Ob beide Zahlen schon dasselbe sagten — dann war nichts auszugleichen.</summary>
    public bool WasAligned => Difference == 0m;
}

/// <summary>
/// Was ein neuer Kontrollpunkt braucht.
/// </summary>
/// <remarks>
/// Der Stichtag ist Pflicht und nicht vorbelegt: Ein Kontostand ohne Datum ist keine halbe
/// Auskunft, sondern gar keine. Wer den Stand von Hand abliest, liest ihn zu einem Tag ab.
/// </remarks>
public sealed record BalanceCheckpointRequest
{
    public required DateOnly AsOf { get; init; }

    public required decimal Balance { get; init; }

    /// <summary>
    /// Woher die Zahl stammt.
    /// </summary>
    /// <remarks>
    /// Vorbelegt mit „von Hand“, weil das der Weg ist, den ein Mensch nimmt. Die Oberfläche
    /// setzt ihn auf <c>BankFetch</c>, wenn der Wert aus einer Saldenabfrage übernommen wird.
    /// </remarks>
    public BalanceCheckpointSource Source { get; init; } = BalanceCheckpointSource.Manual;

    public string? Note { get; init; }
}
