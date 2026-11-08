using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Kontostand bei der Bank, neben den gerechneten gestellt.
/// </summary>
/// <remarks>
/// <para>Der Dialog mit der Bank ist hier durch eine Attrappe ersetzt. Das ist keine Lücke, die
/// man schliessen könnte: Ein echter Dialog braucht eine echte PIN und eine Gegenstelle, die
/// antwortet. Geprüft wird deshalb alles, was die Anwendung selbst entscheidet — wann sie
/// überhaupt fragt, was sie mit der Antwort macht, und was sie dabei <em>nicht</em> tut.</para>
/// <para><b>Was die Attrappe mitzählt, ist der halbe Test:</b> Bei fehlenden Angaben und
/// fehlender PIN darf sie gar nicht erst gerufen werden. Ein Abruf, der erst bei der Bank
/// scheitert, kostet einen Fehlversuch auf der PIN.</para>
/// <para>Die Zahlen sind erfunden.</para>
/// </remarks>
public sealed class BankBalanceTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 3);
    private readonly int haushalt;
    private readonly int konto;

    public BankBalanceTests()
    {
        haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var benutzer = new User
        {
            Name = "Oliver W.", Email = "o@test.de", Role = HouseholdRole.Owner,
            HouseholdId = haushalt, PasswordHash = "x",
        };

        context.Users.Add(benutzer);
        context.SaveChanges();

        var giro = new Account
        {
            Name = "Musterbank Giro", ShortName = "Giro", BankName = "Musterbank",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 11, 1),
            OpeningBalance = 1200m, OwnerUserId = benutzer.Id,
            Iban = "DE66999999991234567890",
            Blz = "99999999",
            Bic = "MUSTDEFFXXX",
            FinTsUrl = "https://banking.example.test/fints30",
            FinTsUserId = "mustermann1",
            FinTsTanMethod = 921,
        };

        context.Accounts.Add(giro);
        context.SaveChanges();
        konto = giro.Id;
    }

    public void Dispose() => database.Dispose();

    // ── Hilfsmittel ───────────────────────────────────────────────────────────────────────

    /// <summary>Eine Bank, die eine Zahl nennt — und mitzählt, wie oft sie gefragt wurde.</summary>
    private sealed class Attrappe(decimal saldo) : IBankBalanceSource
    {
        public int Fragen { get; private set; }

        public string? GesehenePin { get; private set; }

        public Task<BankBalanceReading> FetchAsync(
            Account account, string pin, CancellationToken ct = default)
        {
            Fragen++;
            GesehenePin = pin;
            return Task.FromResult(new BankBalanceReading(saldo, new DateTime(2026, 11, 3, 9, 15, 0)));
        }
    }

    /// <summary>Eine Bank, die ablehnt.</summary>
    private sealed class AbweisendeAttrappe(string grund) : IBankBalanceSource
    {
        public Task<BankBalanceReading> FetchAsync(
            Account account, string pin, CancellationToken ct = default)
            => throw new BankAccessException(grund);
    }

    private static BankAccessOptions Produkt() => new() { ProductId = "ABC123DEF456GHI789JKL0123" };

    private BankBalanceService Dienst(IBankBalanceSource quelle, BankAccessOptions? produkt = null)
    {
        var context = database.Context(haushalt);
        return new BankBalanceService(
            context, new AccountService(context), produkt ?? Produkt(), quelle);
    }

    // ── Die Differenz ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Weichen beide Zahlen ab, steht die Abweichung da.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Zweck des Abrufs. Eine Abweichung heisst: Es fehlen Buchungen, oder es
    /// stehen welche zu viel da. Sie zu verstecken — oder den gerechneten Stand still auf den
    /// der Bank zu setzen — nähme der Zahl genau die Aussage, für die sie geholt wurde.
    /// </remarks>
    [Fact]
    public async Task Eine_Abweichung_bleibt_stehen()
    {
        var befund = await Dienst(new Attrappe(1234.56m)).CheckAsync(konto, "1234");

        Assert.Equal(1234.56m, befund.BankBalance);
        Assert.Equal(1200m, befund.LedgerBalance);
        Assert.Equal(34.56m, befund.Difference);
        Assert.False(befund.Matches);
    }

    /// <summary>Stimmen beide überein, sagt der Befund das auch.</summary>
    [Fact]
    public async Task Gleiche_Zahlen_gelten_als_gleich()
    {
        var befund = await Dienst(new Attrappe(1200m)).CheckAsync(konto, "1234");

        Assert.Equal(0m, befund.Difference);
        Assert.True(befund.Matches);
    }

    /// <summary>
    /// Der Abruf ändert den gerechneten Stand nicht.
    /// </summary>
    /// <remarks>
    /// Die Versuchung ist gross: Die Bank weiss es doch besser. Nur rechnet die Anwendung den
    /// Stand aus Anfangsbestand und Buchungen — schriebe man die Zahl der Bank hinein, stünde
    /// dort eine Behauptung statt einer Rechnung, und beim nächsten Abruf verglichen sich zwei
    /// Zahlen aus derselben Quelle.
    /// </remarks>
    [Fact]
    public async Task Der_Abruf_schreibt_nichts()
    {
        await Dienst(new Attrappe(9999m)).CheckAsync(konto, "1234");

        using var context = database.Context(haushalt);
        var nachher = await new AccountService(context).GetBalancesAsync();

        Assert.Equal(1200m, nachher[konto]);
    }

    /// <summary>Der Zeitpunkt des Abrufs wird durchgereicht, nicht neu erfunden.</summary>
    [Fact]
    public async Task Der_Befund_sagt_wann_gefragt_wurde()
    {
        var befund = await Dienst(new Attrappe(1200m)).CheckAsync(konto, "1234");

        Assert.Equal(new DateTime(2026, 11, 3, 9, 15, 0), befund.RetrievedAt);
    }

    // ── Wann gar nicht erst gefragt wird ──────────────────────────────────────────────────

    /// <summary>
    /// Ohne PIN wird die Bank nicht gerufen.
    /// </summary>
    /// <remarks>
    /// Nicht nur wegen der Meldung: Ein Dialog mit leerer PIN ist bei der Bank ein
    /// Fehlversuch, und drei davon sperren den Zugang. Die leere Eingabe darf nicht auf das
    /// Konto des Benutzers gehen.
    /// </remarks>
    [Fact]
    public async Task Ohne_PIN_wird_die_Bank_nicht_gefragt()
    {
        var bank = new Attrappe(1200m);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(bank).CheckAsync(konto, "   "));

        Assert.Contains("Ohne PIN", fehler.Message, StringComparison.Ordinal);
        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>
    /// Fehlen Angaben am Konto, wird die Bank nicht gerufen — und auch nicht nach der PIN
    /// gefragt.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge ist Absicht: Wer die FinTS-Adresse noch gar nicht eingetragen hat, soll
    /// nicht zuerst seine PIN tippen, um dann zu erfahren, dass es daran nicht lag.
    /// </remarks>
    [Fact]
    public async Task Ohne_vollstaendigen_Zugang_wird_die_Bank_nicht_gefragt()
    {
        using (var context = database.Context(haushalt))
        {
            var giro = context.Accounts.Single(a => a.Id == konto);
            giro.FinTsUrl = null;
            context.SaveChanges();
        }

        var bank = new Attrappe(1200m);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(bank).CheckAsync(konto, "1234"));

        Assert.Contains("FinTS-Adresse", fehler.Message, StringComparison.Ordinal);
        Assert.Contains("Musterbank Giro", fehler.Message, StringComparison.Ordinal);
        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>Fehlt die Registrierungsnummer, wird ebenfalls nicht gefragt.</summary>
    [Fact]
    public async Task Ohne_Registrierungsnummer_wird_die_Bank_nicht_gefragt()
    {
        var bank = new Attrappe(1200m);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(bank, new BankAccessOptions()).CheckAsync(konto, "1234"));

        Assert.Contains("ProductId", fehler.Message, StringComparison.Ordinal);
        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>Ein Konto, das es nicht gibt, führt nicht zu einem Bankdialog.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Konto_fuehrt_zu_keinem_Dialog()
    {
        var bank = new Attrappe(1200m);

        await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(bank).CheckAsync(999_999, "1234"));

        Assert.Equal(0, bank.Fragen);
    }

    // ── Wenn die Bank ablehnt ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Meldung der Bank kommt unverändert durch.
    /// </summary>
    /// <remarks>
    /// „Der Abruf ist fehlgeschlagen“ hilft niemandem. Nur die Bank weiss, ob die PIN falsch
    /// war, der Zugang gesperrt ist oder eine Freigabe fehlt — und nur sie kann es sagen.
    /// </remarks>
    [Fact]
    public async Task Was_die_Bank_sagt_bleibt_stehen()
    {
        var fehler = await Assert.ThrowsAsync<BankAccessException>(
            () => Dienst(new AbweisendeAttrappe("9942 PIN ungültig")).CheckAsync(konto, "0000"));

        Assert.Equal("9942 PIN ungültig", fehler.Message);
    }

    // ── Was fehlt, ohne abzurufen ─────────────────────────────────────────────────────────

    /// <summary>
    /// Die Oberfläche kann fragen, was fehlt, ohne eine PIN zu verlangen.
    /// </summary>
    /// <remarks>
    /// Damit der Schalter dort gar nicht erst erscheint, wo er nur in eine Fehlermeldung
    /// führte. Geprüft wird trotzdem noch einmal beim Abruf — ein Schalter, der nicht erscheint,
    /// ist keine Zusicherung.
    /// </remarks>
    [Fact]
    public async Task Was_fehlt_laesst_sich_ohne_PIN_erfragen()
    {
        var bank = new Attrappe(1200m);

        Assert.Empty(await Dienst(bank).ProblemsAsync(konto));

        using (var context = database.Context(haushalt))
        {
            var giro = context.Accounts.Single(a => a.Id == konto);
            giro.FinTsTanMethod = null;
            context.SaveChanges();
        }

        var probleme = await Dienst(bank).ProblemsAsync(konto);

        Assert.Contains(probleme, p => p.Contains("TAN-Verfahren", StringComparison.Ordinal));
        Assert.Equal(0, bank.Fragen);
    }
}
