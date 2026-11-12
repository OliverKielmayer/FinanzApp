using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Lücke, die ein Kontrollpunkt verschlucken kann.
/// </summary>
/// <remarks>
/// <para>Ein Kontrollpunkt sagt: An diesem Tag war es diese Zahl. Der Saldo zählt darum nur,
/// was <em>nach</em> seinem Stichtag gebucht wurde — alles davor steckt ja schon darin.</para>
/// <para><b>Es sei denn, es steckte nicht darin.</b> Wird eine Buchung mit älterem
/// Buchungstag erst eingelesen, nachdem der Punkt entstand, kann sie in seiner Zahl nicht
/// gewesen sein. Gezählt wird sie trotzdem nie. Genau so ist einmal ein Stand danebengelegen,
/// und gemerkt hat es der Inhaber, nicht die Anwendung.</para>
/// <para>Entscheidend ist deshalb der <b>Zeitpunkt</b> der Entstehung, nicht der Stichtag:
/// Der Stichtag ist ein Tag, die Entstehung ein Augenblick, und auf diesem Unterschied sitzt
/// der Fehler. Die Zahlen hier sind klein und ausgedacht.</para>
/// </remarks>
public sealed class AccountUncountedTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly int konto;

    public AccountUncountedTests()
    {
        using var context = database.Context();

        var account = new Account
        {
            Name = "Girokonto",
            ShortName = "Giro",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            OpeningBalance = 0m,
            BalanceAsOf = new DateOnly(2026, 1, 1),
        };

        context.Accounts.Add(account);
        context.SaveChanges();
        konto = account.Id;
    }

    private AccountService Service() => new(database.Context());

    /// <summary>Eine Buchung mit Buchungstag und dem Augenblick, in dem sie angelegt wurde.</summary>
    private void Buche(DateOnly tag, decimal betrag, DateTime angelegt)
    {
        using var context = database.Context();

        context.Transactions.Add(new Transaction
        {
            AccountId = konto,
            BookingDate = tag,
            Payee = "Musterwerke",
            Kind = betrag < 0 ? TransactionKind.Expense : TransactionKind.Income,
            Amount = betrag,
            CreatedAt = angelegt,
        });

        context.SaveChanges();
    }

    private void Kontrollpunkt(DateOnly stichtag, decimal stand, DateTime angelegt)
    {
        using var context = database.Context();

        context.BalanceCheckpoints.Add(new BalanceCheckpoint
        {
            AccountId = konto,
            AsOf = stichtag,
            Balance = stand,
            LedgerBalance = stand,
            Source = BalanceCheckpointSource.Manual,
            CreatedAt = angelegt,
        });

        context.SaveChanges();
    }

    private async Task<UncountedBookingsDto?> LueckeAsync()
        => (await Service().UncountedAsync()).GetValueOrDefault(konto);

    // ── Der Fall, um den es geht ───────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_spaeter_eingelesene_aeltere_Buchung_faellt_auf()
    {
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));
        Buche(new DateOnly(2026, 11, 10), -40m, new DateTime(2026, 11, 12, 17, 29, 0));

        var luecke = Assert.IsType<UncountedBookingsDto>(await LueckeAsync());

        Assert.Equal(1, luecke.Count);
        Assert.Equal(-40m, luecke.Amount);
        Assert.Equal(new DateOnly(2026, 11, 12), luecke.CheckpointAsOf);
    }

    /// <summary>
    /// Die Grenze: Eine Buchung <em>am</em> Stichtag fällt genauso heraus wie eine ältere —
    /// der Saldo zählt erst ab dem Tag danach.
    /// </summary>
    [Fact]
    public async Task Auch_eine_Buchung_vom_Stichtag_selbst_faellt_auf()
    {
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));
        Buche(new DateOnly(2026, 11, 12), -15m, new DateTime(2026, 11, 12, 17, 29, 0));

        Assert.Equal(1, Assert.IsType<UncountedBookingsDto>(await LueckeAsync()).Count);
    }

    // ── Was nicht gemeldet werden darf ─────────────────────────────────────────────────

    /// <summary>
    /// Der Normalfall: Die Buchung war schon da, als der Stand festgestellt wurde. Dann
    /// steckt sie in seiner Zahl, und sie herauszurechnen ist richtig.
    /// </summary>
    [Fact]
    public async Task Was_vor_dem_Punkt_da_war_steckt_in_seiner_Zahl()
    {
        Buche(new DateOnly(2026, 11, 10), -40m, new DateTime(2026, 11, 10, 9, 0, 0));
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));

        Assert.Null(await LueckeAsync());
    }

    /// <summary>
    /// Eine Buchung nach dem Stichtag zählt der Saldo ohnehin mit — da fehlt nichts.
    /// </summary>
    [Fact]
    public async Task Was_nach_dem_Stichtag_gebucht_wurde_zaehlt_ohnehin()
    {
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));
        Buche(new DateOnly(2026, 11, 13), -40m, new DateTime(2026, 11, 13, 8, 0, 0));

        Assert.Null(await LueckeAsync());
    }

    [Fact]
    public async Task Ohne_Kontrollpunkt_gibt_es_keine_Luecke()
    {
        Buche(new DateOnly(2026, 11, 10), -40m, new DateTime(2026, 11, 12, 17, 29, 0));

        Assert.Null(await LueckeAsync());
    }

    /// <summary>
    /// Es gilt nur der jüngste Punkt — derselbe, auf dem der Saldo aufsetzt. Was ein älterer
    /// verschluckt hätte, hat der jüngere längst überschrieben.
    /// </summary>
    [Fact]
    public async Task Nur_der_juengste_Punkt_zaehlt()
    {
        Kontrollpunkt(new DateOnly(2026, 10, 31), 300m, new DateTime(2026, 10, 31, 9, 0, 0));
        Buche(new DateOnly(2026, 10, 30), -40m, new DateTime(2026, 11, 1, 9, 0, 0));

        // Der jüngere entstand nach der Buchung: Sie steckt in seiner Zahl.
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));

        Assert.Null(await LueckeAsync());
    }

    /// <summary>
    /// Mehrere auf einmal: Gezählt und summiert wird, was zusammen fehlt — nicht die größte
    /// Einzelbuchung und nicht der Betrag ohne die Anzahl.
    /// </summary>
    [Fact]
    public async Task Mehrere_werden_gezaehlt_und_summiert()
    {
        Kontrollpunkt(new DateOnly(2026, 11, 12), 500m, new DateTime(2026, 11, 12, 17, 28, 0));
        Buche(new DateOnly(2026, 11, 10), -40m, new DateTime(2026, 11, 12, 17, 29, 0));
        Buche(new DateOnly(2026, 11, 11), 65m, new DateTime(2026, 11, 12, 17, 29, 0));

        var luecke = Assert.IsType<UncountedBookingsDto>(await LueckeAsync());

        Assert.Equal(2, luecke.Count);
        Assert.Equal(25m, luecke.Amount);
    }

    public void Dispose() => database.Dispose();
}
