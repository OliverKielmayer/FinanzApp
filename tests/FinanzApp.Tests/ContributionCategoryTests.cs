using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Wessen Beitrag ein Zufluss auf dem Gemeinschaftskonto ist.
/// </summary>
/// <remarks>
/// <para><b>Zwei gepflegte Wege, und beide gelten.</b> Am Beleg als Einlage einer Person
/// markiert ist die genauere Angabe und behält den Vorrang; eine Einnahmekategorie mit
/// eingetragener Person trägt, wo an der Buchung nichts steht.</para>
/// <para><b>Warum die Kategorie überhaupt dazukam.</b> Die Markierung am Beleg ist Handarbeit
/// an jeder einzelnen Buchung, also Monat für Monat neu — bis dahin steht die Rechnung auf
/// null, während das Geld jeden Monat ankommt. An der Kategorie steht sie einmal, und die
/// Kategorieregeln tragen sie weiter.</para>
/// <para><b>Geraten wird nichts.</b> Der Zahlername bleibt außen vor: Derselbe Mensch heißt im
/// Kontoauszug in mehreren Schreibweisen, und wer eine Erstattung empfängt, hat nichts
/// eingebracht.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class ContributionCategoryTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 5, 20);
    private readonly int haushalt;
    private readonly int anna;
    private readonly int bruno;
    private readonly int konto;

    public ContributionCategoryTests()
    {
        haushalt = database.AddHousehold("Probehaushalt");

        using var context = database.Context(haushalt);

        var a = Benutzer("Anna P.", "a@test.de", HouseholdRole.Owner);
        var b = Benutzer("Bruno T.", "b@test.de", HouseholdRole.Member);
        context.Users.AddRange(a, b);
        context.SaveChanges();

        anna = a.Id;
        bruno = b.Id;

        var gemeinsam = new Account
        {
            Name = "Gemeinsam Giro",
            ShortName = "Gemeinsam",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 5, 20),
            OwnerUserId = anna,
            Sharing = AccountSharing.Shared,
            Shares =
            {
                new AccountShare { UserId = anna, MonthlyTarget = 1340m, DueDay = 3 },
                new AccountShare { UserId = bruno, MonthlyTarget = 860m, DueDay = 3 },
            },
        };

        context.Accounts.Add(gemeinsam);
        context.SaveChanges();
        konto = gemeinsam.Id;
    }

    private User Benutzer(string name, string email, HouseholdRole rolle) => new()
    {
        HouseholdId = haushalt, Name = name, Email = email, PasswordHash = "-",
        Role = rolle, CreatedAt = clock.Now,
    };

    private CatalogService Katalog() => new(database.Context(haushalt));

    private ParticipationService Beteiligung(int? alsBenutzer)
        => new(database.Context(haushalt, alsBenutzer), TestDatabase.SignedIn(alsBenutzer));

    /// <summary>Eine Kategorie, wahlweise mit eingetragener Person.</summary>
    private int Kategorie(string name, CategoryDirection richtung, int? beitragVon = null)
    {
        using var context = database.Context(haushalt);

        var kategorie = new Category { Name = name, Direction = richtung };
        context.Categories.Add(kategorie);
        context.SaveChanges();

        if (beitragVon is { } person)
        {
            kategorie.ContributorUserId = person;
            context.SaveChanges();
        }

        return kategorie.Id;
    }

    /// <summary>Eine Buchung auf dem Gemeinschaftskonto.</summary>
    private void Buchung(
        decimal betrag, DateOnly tag, int? kategorie = null, int? einlageVon = null)
    {
        using var context = database.Context(haushalt);

        context.Transactions.Add(new Transaction
        {
            BookingDate = tag,
            Payee = "Musterzahler",
            Kind = einlageVon is null
                ? betrag >= 0m ? TransactionKind.Income : TransactionKind.Expense
                : TransactionKind.Deposit,
            Amount = betrag,
            AccountId = konto,
            CategoryId = kategorie,
            DepositUserId = einlageVon,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    private async Task<decimal> EingebrachtAsync(int person)
    {
        var zeilen = await Beteiligung(anna).ContributionsAsync(
            [konto], new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));

        return zeilen.Where(z => z.UserId == person).Sum(z => z.Amount);
    }

    // ── Was sich eintragen lässt ───────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_Einnahmekategorie_nimmt_die_Person_an()
    {
        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income);

        Assert.True(await Katalog().SetContributorAsync(id, anna));

        var gepflegt = await Katalog().GetUsageAsync(CategoryDirection.Income);
        var zeile = gepflegt.Single(k => k.Id == id);

        Assert.Equal(anna, zeile.ContributorUserId);
        Assert.Equal("Anna P.", zeile.ContributorName);
    }

    /// <summary>
    /// <b>Eine Ausgabe ist kein Beitrag.</b> Wer vom gemeinsamen Konto einkauft, bringt nichts
    /// ein — er gibt aus. Die Richtung dort zuzulassen hieße, denselben Abfluss als Zufluss
    /// zählbar zu machen.
    /// </summary>
    [Fact]
    public async Task Eine_Ausgabenkategorie_weist_die_Person_ab()
    {
        var id = Kategorie("Lebensmittel", CategoryDirection.Expense);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Katalog().SetContributorAsync(id, anna));

        Assert.Contains("Einnahmekategorien", fehler.Message);
    }

    /// <summary>
    /// Eine Person aus einem anderen Haushalt wird abgewiesen — sonst stünde ein fremder Name
    /// in einer Rechnung über zwei Menschen.
    /// </summary>
    [Fact]
    public async Task Eine_fremde_Person_wird_abgewiesen()
    {
        var fremder = database.AddHousehold("Nachbarn");
        int wer;

        using (var context = database.Context(fremder))
        {
            var gast = new User
            {
                HouseholdId = fremder, Name = "Clara F.", Email = "c@test.de",
                PasswordHash = "-", Role = HouseholdRole.Owner, CreatedAt = clock.Now,
            };

            context.Users.Add(gast);
            context.SaveChanges();
            wer = gast.Id;
        }

        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Katalog().SetContributorAsync(id, wer));

        Assert.Contains("Haushalt", fehler.Message);
    }

    /// <summary>
    /// Der Weg zurück muss immer offen sein — auch dort, wo sich die Person nicht mehr setzen
    /// ließe. Sonst bliebe eine falsch gesetzte Zuordnung für immer stehen.
    /// </summary>
    [Fact]
    public async Task Die_Person_laesst_sich_wieder_herausnehmen()
    {
        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, beitragVon: anna);

        Assert.True(await Katalog().SetContributorAsync(id, null));

        var zeile = (await Katalog().GetUsageAsync(CategoryDirection.Income)).Single(k => k.Id == id);
        Assert.Null(zeile.ContributorUserId);
    }

    // ── Was die Rechnung daraus macht ──────────────────────────────────────────────────

    [Fact]
    public async Task Ein_Zufluss_mit_Beitragskategorie_zaehlt_der_Person()
    {
        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, beitragVon: anna);

        Buchung(1340m, new DateOnly(2026, 5, 3), kategorie: id);

        Assert.Equal(1340m, await EingebrachtAsync(anna));
        Assert.Equal(0m, await EingebrachtAsync(bruno));
    }

    /// <summary>
    /// <b>Das Vorzeichen trägt die Richtung.</b> Geht Geld derselben Kategorie vom Konto ab —
    /// eine Rückzahlung, eine Korrektur —, ist das kein Eingang auf ihm. Es als einen zu
    /// zählen machte aus einem Abfluss eine Einbringung.
    /// </summary>
    [Fact]
    public async Task Ein_Abfluss_derselben_Kategorie_zaehlt_nicht()
    {
        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, beitragVon: anna);

        Buchung(1340m, new DateOnly(2026, 5, 3), kategorie: id);
        Buchung(-1340m, new DateOnly(2026, 5, 4), kategorie: id);

        Assert.Equal(1340m, await EingebrachtAsync(anna));
    }

    /// <summary>
    /// <b>Gepflegt am Beleg schlägt gepflegt an der Kategorie.</b> Beides ist eine Angabe des
    /// Inhabers, aber die an der Buchung meint genau diese Buchung — die an der Kategorie meint
    /// alle. Die genauere gewinnt, sonst wäre sie nicht zu gebrauchen.
    /// </summary>
    [Fact]
    public async Task Die_Markierung_an_der_Buchung_schlaegt_die_Kategorie()
    {
        var id = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, beitragVon: anna);

        // Dieselbe Kategorie, aber am Beleg steht Bruno. Die Einlage trägt beides, wie es nach
        // einer nachträglichen Markierung im Bestand stehen kann.
        using (var context = database.Context(haushalt))
        {
            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 5, 3),
                Payee = "Musterzahler",
                Kind = TransactionKind.Deposit,
                Amount = 860m,
                AccountId = konto,
                CategoryId = id,
                DepositUserId = bruno,
                CreatedAt = clock.Now,
            });

            context.SaveChanges();
        }

        Assert.Equal(860m, await EingebrachtAsync(bruno));
        Assert.Equal(0m, await EingebrachtAsync(anna));
    }

    /// <summary>
    /// <b>Die Grenze, auf die es ankommt.</b> Ein Zufluss ohne beides zählt niemandem — auch
    /// dann nicht, wenn der Zahlername wie eine Person des Haushalts aussieht. Genau hier
    /// stünde sonst eine Erstattung als Einbringung.
    /// </summary>
    [Fact]
    public async Task Ein_Zufluss_ohne_beides_zaehlt_niemandem()
    {
        var neutral = Kategorie("Erstattung", CategoryDirection.Income);

        Buchung(275.50m, new DateOnly(2026, 5, 6), kategorie: neutral);
        Buchung(92.30m, new DateOnly(2026, 5, 7));

        Assert.Equal(0m, await EingebrachtAsync(anna));
        Assert.Equal(0m, await EingebrachtAsync(bruno));

        var zeilen = await Beteiligung(anna).ContributionsAsync(
            [konto], new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));

        Assert.Empty(zeilen);
    }

    /// <summary>
    /// Der Monatsvergleich am Konto liest dieselbe Regel — er rechnet sie nicht noch einmal.
    /// </summary>
    [Fact]
    public async Task Der_Monatsvergleich_sieht_denselben_Beitrag()
    {
        var id = Kategorie("Anteil Haushalt Bruno", CategoryDirection.Income, beitragVon: bruno);

        Buchung(860m, new DateOnly(2026, 5, 3), kategorie: id);

        var gemeinschaft = Assert.Single(
            await Beteiligung(anna).JointAccountsAsync(clock.Today));

        var seiner = gemeinschaft.Contributors.Single(p => p.UserId == bruno);

        Assert.Equal(860m, seiner.PaidThisMonth);
        Assert.True(seiner.Fulfilled);
        Assert.Equal(new DateOnly(2026, 5, 3), seiner.LastPaidOn);
    }

    public void Dispose() => database.Dispose();
}
