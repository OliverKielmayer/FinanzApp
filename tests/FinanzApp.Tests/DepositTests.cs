using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Buchungsart „Einlage“ — Handoff „Gemeinsame Immobilie“, 3.4.
/// </summary>
/// <remarks>
/// <para>Sie ist <b>keine Einnahme</b>, weil nichts von außen zufließt: das Geld gehörte schon
/// einem der Beteiligten. Und <b>keine Umbuchung</b>, weil der Eigentümer wechselt. Sie zählt in
/// die Beteiligungsrechnung und nicht in Einnahmen, Sparquote oder Liquidität — sonst stünde
/// dasselbe Geld zweimal im Haushalt.</para>
/// <para>Sie trägt Person und Objekt. Ohne Person ließe sie sich niemandem zurechnen, ohne
/// Objekt nirgends verrechnen; der Ausgleichsstand lebt von genau diesen zwei Angaben.</para>
/// </remarks>
public sealed class DepositTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);
    private readonly int haushalt;
    private readonly int oliver;
    private readonly int sabine;
    private readonly int fremder;
    private readonly int konto;
    private readonly int objekt;

    public DepositTests()
    {
        haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var a = Benutzer("Oliver W.", "o@test.de", HouseholdRole.Owner);
        var b = Benutzer("Sabine K.", "s@test.de", HouseholdRole.Member);
        var c = Benutzer("Steuerbüro", "k@test.de", HouseholdRole.ReadOnly);

        context.Users.AddRange(a, b, c);
        context.SaveChanges();

        oliver = a.Id;
        sabine = b.Id;
        fremder = c.Id;

        var haushaltskonto = new Account
        {
            Name = "Haushalt Giro", ShortName = "Haushalt", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 9, 1),
            OpeningBalance = 5000m, OwnerUserId = oliver,
        };

        context.Accounts.Add(haushaltskonto);
        context.SaveChanges();
        konto = haushaltskonto.Id;

        var haus = new Property
        {
            Name = "Haus zu zweit",
            MarketValue = 420000m,
            PurchaseDate = new DateOnly(2026, 4, 1),
            Shares =
            {
                new PropertyShare { UserId = oliver, Percent = 50m, Equity = 90000m },
                new PropertyShare { UserId = sabine, Percent = 50m, Equity = 50000m },
            },
        };

        context.Properties.Add(haus);
        context.SaveChanges();
        objekt = haus.Id;
    }

    private User Benutzer(string name, string email, HouseholdRole rolle) => new()
    {
        HouseholdId = haushalt, Name = name, Email = email, PasswordHash = "-",
        Role = rolle, CreatedAt = clock.Now,
    };

    private TransactionService Buchungen(int? alsBenutzer = null)
        => new(database.Context(haushalt, alsBenutzer), clock);

    private PropertyService Objekte(int? alsBenutzer)
    {
        // Der Benutzer muss in den Kontext: Buchungen auf einem Gemeinschaftskonto sieht nur, wer
        // in seiner Liste steht, und das prueft der Abfragefilter.
        var context = database.Context(haushalt, alsBenutzer);

        return new PropertyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(Path.Combine(Path.GetTempPath(), "finanzapp-tests", "einlage")),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(alsBenutzer)));
    }

    private CreateTransactionRequest Einlage(decimal betrag, int? person, int? fuerObjekt)
        => new()
        {
            RequestKey = Guid.NewGuid(),
            Kind = TransactionKind.Deposit,
            Amount = betrag,
            AccountId = konto,
            DepositUserId = person,
            PropertyId = fuerObjekt,
            Note = "Einlage",
        };

    // ── Was eine Einlage braucht ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_Einlage_ohne_Person_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Buchungen().CreateAsync(Einlage(1500m, person: null, fuerObjekt: objekt)));

        Assert.Contains("Person", fehler.Message);
    }

    [Fact]
    public async Task Eine_Einlage_ohne_Objekt_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Buchungen().CreateAsync(Einlage(1500m, person: oliver, fuerObjekt: null)));

        Assert.Contains("Objekt", fehler.Message);
    }

    /// <summary>
    /// Wer am Objekt nicht beteiligt ist, legt nichts ein.
    /// </summary>
    /// <remarks>
    /// Er schenkt oder leiht — beides wäre eine andere Buchung. Eine Einlage von jemandem ohne
    /// Anteil verschöbe den Ausgleich zwischen Leuten, die davon nichts wissen.
    /// </remarks>
    [Fact]
    public async Task Nur_Beteiligte_koennen_einlegen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Buchungen().CreateAsync(Einlage(1500m, person: fremder, fuerObjekt: objekt)));

        Assert.Contains("nicht beteiligt", fehler.Message);
    }

    /// <summary>Die Einlage steht als Abfluss und trägt keine Kategorie.</summary>
    [Fact]
    public async Task Die_Einlage_wird_ohne_Kategorie_gebucht()
    {
        var gebucht = await Buchungen().CreateAsync(Einlage(1500m, oliver, objekt));

        Assert.Equal(TransactionKind.Deposit, gebucht.Kind);
        Assert.Equal(-1500m, gebucht.Amount);
        Assert.Null(gebucht.CategoryId);

        // Ohne Kategorie und trotzdem nicht in der Triage: sie trägt keine.
        Assert.False(gebucht.IsUncategorized);
    }

    /// <summary>
    /// Auf einem Gemeinschaftskonto steht die Einlage als Zufluss.
    /// </summary>
    /// <remarks>
    /// Dort kommt das Geld an. Stünde sie auch hier als Abfluss, fiele der Saldo, während der
    /// Kontoblock „Eingang“ meldet — zwei Zahlen desselben Vorgangs, die einander widersprechen.
    /// </remarks>
    [Fact]
    public async Task Auf_dem_Gemeinschaftskonto_ist_die_Einlage_ein_Zufluss()
    {
        Gemeinschaftskonto();

        var gebucht = await Buchungen(oliver).CreateAsync(Einlage(1500m, oliver, objekt));

        Assert.Equal(1500m, gebucht.Amount);
    }

    /// <summary>
    /// Die Richtung ändert nichts daran, was eingebracht wurde.
    /// </summary>
    /// <remarks>
    /// Eingebracht ist der Betrag, nicht seine Richtung. Würde über die Summe abgewertet statt je
    /// Zeile, hoben sich Zufluss und Abfluss auf und der Ausgleichsstand wäre falsch.
    /// </remarks>
    [Fact]
    public async Task Beide_Richtungen_zaehlen_als_eingebracht()
    {
        await Buchungen().CreateAsync(Einlage(2000m, oliver, objekt));

        Gemeinschaftskonto();

        await Buchungen(oliver).CreateAsync(Einlage(500m, oliver, objekt));

        var beteiligung = (await Objekte(oliver).GetAsync(objekt))!.Participation!;

        Assert.Equal(2500m, beteiligung.Participants.Single(p => p.IsSelf).Deposits);
    }

    /// <summary>Macht aus dem Haushaltskonto ein Gemeinschaftskonto der zwei Beteiligten.</summary>
    private void Gemeinschaftskonto()
    {
        using var context = database.Context(haushalt, oliver);

        context.Accounts.Single(a => a.Id == konto).Sharing = AccountSharing.Shared;
        context.AccountShares.Add(new AccountShare { AccountId = konto, UserId = oliver });
        context.AccountShares.Add(new AccountShare { AccountId = konto, UserId = sabine });
        context.SaveChanges();
    }

    // ── Was sie nicht ist ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Einlage zählt nicht in Einnahmen, Ausgaben oder Sparquote.
    /// </summary>
    /// <remarks>
    /// Der Kern der Buchungsart. Zählte sie mit, sänke die Sparquote durch eine Bewegung, bei der
    /// der Haushalt nichts verbraucht hat.
    /// </remarks>
    [Fact]
    public async Task Die_Einlage_zaehlt_nicht_in_die_Monatszahlen()
    {
        await Buchungen().CreateAsync(Einlage(1500m, oliver, objekt));

        using var context = database.Context(haushalt);

        var dashboard = new DashboardService(
            context,
            new AccountService(context),
            TestDatabase.Portfolio(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(oliver)));

        var monat = (await dashboard.GetAsync()).Month;

        Assert.Equal(0m, monat.Income);
        Assert.Equal(0m, monat.Expenses);
    }

    // ── Was sie bewirkt ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Einlagen verschieben den Ausgleichsstand.
    /// </summary>
    /// <remarks>
    /// Eigenkapital 90.000 zu 50.000, dazu Einlagen von 7.500 zu 7.200: zusammen 97.500 zu
    /// 57.200. Die Hälfte von 154.700 sind 77.350 — Olivers Ausgleich ist +20.150. Genau die
    /// Zahl aus dem Handoff.
    /// </remarks>
    [Fact]
    public async Task Einlagen_verschieben_den_Ausgleich()
    {
        await Buchungen().CreateAsync(Einlage(7500m, oliver, objekt));
        await Buchungen().CreateAsync(Einlage(7200m, sabine, objekt));

        var meiner = (await Objekte(oliver).GetAsync(objekt))!.Participation!;

        Assert.Equal(7500m, meiner.Participants.Single(p => p.IsSelf).Deposits);
        Assert.Equal(97500m, meiner.Participants.Single(p => p.IsSelf).Contributed);
        Assert.Equal(20150m, meiner.Settlement);

        var ihrer = (await Objekte(sabine).GetAsync(objekt))!.Participation!;
        Assert.Equal(-20150m, ihrer.Settlement);
    }

    /// <summary>Auch mit Einlagen heben sich die Ausgleiche auf.</summary>
    [Fact]
    public async Task Auch_mit_Einlagen_heben_sich_die_Ausgleiche_auf()
    {
        await Buchungen().CreateAsync(Einlage(3000m, oliver, objekt));
        await Buchungen().CreateAsync(Einlage(1200m, sabine, objekt));

        var beteiligung = (await Objekte(oliver).GetAsync(objekt))!.Participation!;

        Assert.Equal(0m, beteiligung.Participants.Sum(p => p.Settlement));
    }

    /// <summary>
    /// Eine Einlage für ein anderes Objekt verschiebt diesen Ausgleich nicht.
    /// </summary>
    /// <remarks>
    /// Die Beteiligungsrechnung gehört zum Objekt. Zwei Häuser führen zwei Ausgleichsstände, und
    /// eine Einlage weiß, für welches sie war.
    /// </remarks>
    [Fact]
    public async Task Eine_Einlage_zaehlt_nur_fuer_ihr_Objekt()
    {
        int zweites;

        using (var context = database.Context(haushalt))
        {
            var haus = new Property
            {
                Name = "Zweites Haus",
                MarketValue = 200000m,
                Shares =
                {
                    new PropertyShare { UserId = oliver, Percent = 50m, Equity = 10000m },
                    new PropertyShare { UserId = sabine, Percent = 50m, Equity = 10000m },
                },
            };

            context.Properties.Add(haus);
            context.SaveChanges();
            zweites = haus.Id;
        }

        await Buchungen().CreateAsync(Einlage(5000m, oliver, zweites));

        var erstes = (await Objekte(oliver).GetAsync(objekt))!.Participation!;
        Assert.Equal(0m, erstes.Participants.Single(p => p.IsSelf).Deposits);
        Assert.Equal(20000m, erstes.Settlement);

        var andere = (await Objekte(oliver).GetAsync(zweites))!.Participation!;
        Assert.Equal(5000m, andere.Participants.Single(p => p.IsSelf).Deposits);
        Assert.Equal(2500m, andere.Settlement);
    }

    /// <summary>
    /// Objektschirm und Bilanz nennen denselben Ausgleich.
    /// </summary>
    /// <remarks>
    /// Beim Bauen liefen sie auseinander: der Objektschirm rechnete mit Eigenkapital und
    /// Einlagen, die Bilanz nur mit dem Eigenkapital — 20.750 € gegen 20.000 € für dieselbe
    /// Forderung. Seitdem rechnet der Beteiligungsdienst, und beide fragen ihn. Dieser Test
    /// hält das fest, damit es nicht wieder auseinanderläuft.
    /// </remarks>
    [Fact]
    public async Task Objektschirm_und_Bilanz_nennen_dieselbe_Forderung()
    {
        await Buchungen().CreateAsync(Einlage(1500m, oliver, objekt));

        var amObjekt = (await Objekte(oliver).GetAsync(objekt))!.Participation!.Settlement;

        using var context = database.Context(haushalt);

        var dashboard = new DashboardService(
            context,
            new AccountService(context),
            TestDatabase.Portfolio(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(oliver)));

        var inDerBilanz = (await dashboard.GetAsync()).NetWorth.Receivables;

        Assert.Equal(20750m, amObjekt);
        Assert.Equal(amObjekt, inDerBilanz);
    }

    public void Dispose() => database.Dispose();
}
