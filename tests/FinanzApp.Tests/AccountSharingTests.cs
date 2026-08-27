using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Kontofreigaben — die zweite Stufe der Mandantentrennung.
/// </summary>
/// <remarks>
/// <para>Geprüft wird über die <b>Dienste</b>, nicht über die Entitäten. Ein Test, der direkt in
/// die Tabelle sieht, würde den Abfragefilter umgehen und damit genau das nicht prüfen, worum es
/// geht: dass ein Mitglied über keinen Weg an ein nicht freigegebenes Konto kommt.</para>
/// <para>Die Sichtbarkeit ist eigentümerrelativ. Derselbe Datenbestand muss für zwei Benutzer
/// zwei verschiedene Antworten liefern — sonst ist die Regel nur eine Anzeige-Konvention.</para>
/// </remarks>
public sealed class AccountSharingTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 26);

    private readonly int oliver;
    private readonly int sabine;
    private readonly int kanzlei;

    private readonly int gemeinsam;
    private readonly int privatOliver;
    private readonly int fuerSabine;

    public AccountSharingTests()
    {
        // Benutzer sind nicht IHouseholdOwned — Anmeldedaten bleiben ungefiltert, also stempelt
        // der Kontext den Haushalt dort nicht. Er muss von Hand dran, sonst greift der
        // Fremdschlüssel ins Leere.
        var haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var a = Benutzer(haushalt, "Oliver", "o@example.de", HouseholdRole.Owner);
        var b = Benutzer(haushalt, "Sabine", "s@example.de", HouseholdRole.Member);
        var c = Benutzer(haushalt, "Kanzlei", "k@example.de", HouseholdRole.ReadOnly);

        context.Users.AddRange(a, b, c);
        context.SaveChanges();

        oliver = a.Id;
        sabine = b.Id;
        kanzlei = c.Id;

        var haushaltskonto = Konto("Haushalt Giro", oliver, AccountSharing.Household);
        var privat = Konto("Olivers Depot", oliver, AccountSharing.Private);
        var geteilt = Konto("Nebenkonto", oliver, AccountSharing.Named);

        context.Accounts.AddRange(haushaltskonto, privat, geteilt);
        context.SaveChanges();

        gemeinsam = haushaltskonto.Id;
        privatOliver = privat.Id;
        fuerSabine = geteilt.Id;

        context.AccountShares.Add(new AccountShare { AccountId = fuerSabine, UserId = sabine });

        // Je eine Buchung, damit sich Summen und Zähler prüfen lassen.
        context.Transactions.AddRange(
            Buchung(gemeinsam, -10m, "Bäcker"),
            Buchung(privatOliver, -500m, "Broker"),
            Buchung(fuerSabine, -20m, "Kiosk"));

        context.SaveChanges();
    }

    private static User Benutzer(int haushalt, string name, string email, HouseholdRole rolle) => new()
    {
        HouseholdId = haushalt, Name = name, Email = email, PasswordHash = "-",
        Role = rolle, CreatedAt = new DateTime(2026, 1, 1),
    };

    private static Account Konto(string name, int owner, AccountSharing sharing) => new()
    {
        Name = name, ShortName = name, BankName = "Bank", Kind = AccountKind.Checking,
        BalanceAsOf = new DateOnly(2026, 8, 26), OwnerUserId = owner, Sharing = sharing,
    };

    private static Transaction Buchung(int account, decimal betrag, string payee) => new()
    {
        BookingDate = new DateOnly(2026, 8, 10), Payee = payee,
        Kind = betrag >= 0 ? TransactionKind.Income : TransactionKind.Expense,
        Amount = betrag, AccountId = account, CreatedAt = new DateTime(2026, 8, 10),
    };

    /// <summary>Ein Kontext, der auf einen bestimmten Benutzer sieht.</summary>
    private FinanzAppDbContext Als(int userId)
    {
        var context = database.Context();
        context.CurrentUserId = userId;

        return context;
    }

    private AccountService Konten(int userId) => new(Als(userId));

    private TransactionService Buchungen(int userId) => new(Als(userId), clock);

    [Fact]
    public async Task Der_Eigentuemer_sieht_alle_seine_Konten()
    {
        var konten = await Konten(oliver).GetAccountsAsync();

        Assert.Equal(3, konten.Count);
    }

    [Fact]
    public async Task Ein_privates_Konto_sieht_sonst_niemand()
    {
        var konten = await Konten(sabine).GetAccountsAsync();

        Assert.DoesNotContain(konten, k => k.Id == privatOliver);
        Assert.Contains(konten, k => k.Id == gemeinsam);
        Assert.Contains(konten, k => k.Id == fuerSabine);
    }

    [Fact]
    public async Task Wer_nicht_benannt_ist_sieht_das_benannte_Konto_nicht()
    {
        var konten = await Konten(kanzlei).GetAccountsAsync();

        Assert.DoesNotContain(konten, k => k.Id == privatOliver);
        Assert.DoesNotContain(konten, k => k.Id == fuerSabine);
        Assert.Single(konten);
    }

    [Fact]
    public async Task Die_Buchungen_folgen_ihrem_Konto()
    {
        var seite = await Buchungen(sabine).GetPageAsync(search: null);

        Assert.DoesNotContain(seite.Items, t => t.Payee == "Broker");
        Assert.Contains(seite.Items, t => t.Payee == "Bäcker");
        Assert.Contains(seite.Items, t => t.Payee == "Kiosk");
    }

    /// <summary>
    /// Auch die Zähler: ein fremdes privates Konto zählt in keiner Summe.
    /// </summary>
    /// <remarks>
    /// Der Handoff nennt das ausdrücklich — „erscheint nirgends und zählt in keiner Summe, auch
    /// nicht in der Nav-Kennzahl“. Eine gefilterte Liste über einer ungefilterten Summe wäre die
    /// halbe Arbeit und das ganze Leck.
    /// </remarks>
    [Fact]
    public async Task Ein_fremdes_privates_Konto_zaehlt_in_keiner_Summe()
    {
        var meins = await Buchungen(oliver).GetPageAsync(search: null);
        var ihres = await Buchungen(sabine).GetPageAsync(search: null);

        Assert.Equal(3, meins.TotalCount);
        Assert.Equal(2, ihres.TotalCount);

        Assert.Equal(-530m, meins.Totals.Balance);
        Assert.Equal(-30m, ihres.Totals.Balance);
    }

    [Fact]
    public async Task Auch_die_Suche_findet_es_nicht()
    {
        // Der Weg über die Suche ist derselbe Zugriffspfad — er darf keine Hintertür sein.
        var treffer = await Buchungen(sabine).GetPageAsync("Broker");

        Assert.Empty(treffer.Items);
    }

    [Fact]
    public async Task Ein_gezielter_Zugriff_auf_die_Id_hilft_auch_nicht()
    {
        // Über direkte API-Aufrufe darf ein Mitglied ein nicht freigegebenes Konto nicht lesen.
        var seite = await Buchungen(sabine).GetPageAsync(search: null, accountId: privatOliver);

        Assert.Empty(seite.Items);
    }

    [Fact]
    public async Task Eine_Freigabe_wirkt_sofort()
    {
        using (var context = Als(oliver))
        {
            context.AccountShares.Add(new AccountShare { AccountId = privatOliver, UserId = sabine });
            context.Accounts.Single(a => a.Id == privatOliver).Sharing = AccountSharing.Named;
            context.SaveChanges();
        }

        var konten = await Konten(sabine).GetAccountsAsync();
        Assert.Contains(konten, k => k.Id == privatOliver);
    }

    [Fact]
    public async Task Ein_Konto_ohne_Eigentuemer_bleibt_fuer_alle_sichtbar()
    {
        // Bestandskonten aus der Zeit vor den Freigaben stehen auf „Haushalt“ — die Umstellung
        // darf niemandem ein Konto wegnehmen.
        using (var context = database.Context())
        {
            context.Accounts.Add(new Account
            {
                Name = "Altbestand", ShortName = "Alt", BankName = "Bank",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 26),
            });

            context.SaveChanges();
        }

        Assert.Contains(await Konten(sabine).GetAccountsAsync(), k => k.Name == "Altbestand");
        Assert.Contains(await Konten(kanzlei).GetAccountsAsync(), k => k.Name == "Altbestand");
    }

    public void Dispose() => database.Dispose();
}
