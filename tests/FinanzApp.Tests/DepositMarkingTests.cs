using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Eine eingelesene Zahlung nachträglich als Einlage einstufen.
/// </summary>
/// <remarks>
/// <para>Die Einlage gab es bisher nur beim Erfassen von Hand. Wer sein Gemeinschaftskonto
/// importiert — und das tut, wer es ernst meint —, konnte eine Einzahlung nur als Umbuchung
/// markieren: damit war sie wenigstens keine Einnahme, tauchte aber in der
/// Beteiligungsrechnung nicht auf. Die Gegenüberstellung von Soll und Eingang blieb leer,
/// obwohl beide Beträge auf dem Konto standen.</para>
/// <para>Geprüft ist vor allem, was <b>nicht</b> geht: Einlagen auf einem gewöhnlichen Konto,
/// Abflüsse als Einlage, und Personen ohne Anteil am Objekt.</para>
/// </remarks>
public sealed class DepositMarkingTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 15);
    private readonly int haushalt;
    private readonly int oliver;
    private readonly int sabine;
    private readonly int gemeinschaftskonto;
    private readonly int privatkonto;
    private readonly int objekt;

    public DepositMarkingTests()
    {
        haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var a = Benutzer("Oliver W.", "o@test.de", HouseholdRole.Owner);
        var b = Benutzer("Sabine K.", "s@test.de", HouseholdRole.Member);
        context.Users.AddRange(a, b);
        context.SaveChanges();

        oliver = a.Id;
        sabine = b.Id;

        var gemeinsam = new Account
        {
            Name = "Haushalt Giro", ShortName = "Haushalt", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 9, 15),
            OwnerUserId = oliver, Sharing = AccountSharing.Shared,
            Shares =
            {
                new AccountShare { UserId = oliver, MonthlyTarget = 1500m, DueDay = 1 },
                new AccountShare { UserId = sabine, MonthlyTarget = 1200m, DueDay = 1 },
            },
        };

        var privat = new Account
        {
            Name = "Oli Giro", ShortName = "Oli", BankName = "Raiffeisenbank",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 9, 15),
            OwnerUserId = oliver,
        };

        context.Accounts.AddRange(gemeinsam, privat);
        context.SaveChanges();
        gemeinschaftskonto = gemeinsam.Id;
        privatkonto = privat.Id;

        var haus = new Property
        {
            Name = "Haus zu zweit",
            MarketValue = 420_000m,
            Shares =
            {
                new PropertyShare { UserId = oliver, Percent = 50m, Equity = 90_000m },
                new PropertyShare { UserId = sabine, Percent = 50m, Equity = 50_000m },
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

    private TransactionService Dienst()
        => new(database.Context(haushalt, oliver), clock);

    /// <summary>Eine eingelesene Buchung, wie sie aus dem Auszug kommt — ohne Kategorie.</summary>
    private int Eingelesen(decimal betrag, int konto)
    {
        using var context = database.Context(haushalt, oliver);

        var buchung = new Transaction
        {
            BookingDate = new DateOnly(2026, 9, 1),
            Payee = "Sabine K.",
            Kind = betrag > 0 ? TransactionKind.Income : TransactionKind.Expense,
            Amount = betrag,
            AccountId = konto,
            CreatedAt = clock.Now,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();
        return buchung.Id;
    }

    private static AssignCategoryRequest AlsEinlage(int person, int? objekt = null)
        => new() { MarkAsDeposit = true, DepositUserId = person, PropertyId = objekt };

    // ── Was geht ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_eingelesene_Einzahlung_wird_zur_Einlage()
    {
        var id = Eingelesen(1_200m, gemeinschaftskonto);

        var danach = await Dienst().AssignCategoryAsync(id, AlsEinlage(sabine));

        Assert.NotNull(danach);
        Assert.Equal(TransactionKind.Deposit, danach.Kind);

        using var check = database.Context(haushalt, oliver);
        var buchung = check.Transactions.Single(t => t.Id == id);
        Assert.Equal(sabine, buchung.DepositUserId);
        Assert.Equal(objekt, buchung.PropertyId);
        Assert.Null(buchung.CategoryId);

        // Der Betrag bleibt, wie er im Auszug steht.
        Assert.Equal(1_200m, buchung.Amount);
    }

    /// <summary>
    /// Danach steht sie in der Gegenüberstellung von Soll und Eingang.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Zweck: Als Umbuchung markiert war sie zwar keine Einnahme mehr, aber
    /// die Beteiligungsrechnung sah sie nicht — und das Soll stand offen, obwohl gezahlt war.
    /// </remarks>
    [Fact]
    public async Task Danach_zaehlt_sie_in_der_Beteiligungsrechnung()
    {
        var id = Eingelesen(1_200m, gemeinschaftskonto);
        await Dienst().AssignCategoryAsync(id, AlsEinlage(sabine));

        var beteiligung = new ParticipationService(
            database.Context(haushalt, oliver), TestDatabase.SignedIn(oliver));

        var konto = Assert.Single(await beteiligung.JointAccountsAsync(clock.Today));
        var person = konto.Contributors.Single(c => c.UserId == sabine);

        Assert.Equal(1_200m, person.PaidThisMonth);
        Assert.True(person.Fulfilled);
    }

    // ── Was nicht geht ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Auf einem gewöhnlichen Konto gibt es keine Einlage.
    /// </summary>
    /// <remarks>
    /// Dort ist dieselbe Zahlung eine Überweisung. Sie als Einlage zu führen hieße, einen
    /// Ausgleichsstand auf ein Konto zu rechnen, auf dem niemand eine Erwartung hat.
    /// </remarks>
    [Fact]
    public async Task Auf_einem_Privatkonto_gibt_es_keine_Einlage()
    {
        var id = Eingelesen(1_200m, privatkonto);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AssignCategoryAsync(id, AlsEinlage(sabine)));

        Assert.Contains("Gemeinschaftskonto", fehler.Message);
        Assert.Equal(TransactionKind.Income, Gelesen(id).Kind);
    }

    /// <summary>
    /// Ein Abfluss ist keine Einlage.
    /// </summary>
    /// <remarks>
    /// Die Beteiligungsrechnung zählt nur Zuflüsse. Ein Abfluss als Einlage markiert verschwände
    /// dort stillschweigend: markiert, gezählt nirgends — die schlechteste von allen Lagen.
    /// </remarks>
    [Fact]
    public async Task Ein_Abfluss_ist_keine_Einlage()
    {
        var id = Eingelesen(-1_200m, gemeinschaftskonto);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AssignCategoryAsync(id, AlsEinlage(sabine)));

        Assert.Contains("Zufluss", fehler.Message);
    }

    [Fact]
    public async Task Ohne_Person_geht_es_nicht()
    {
        var id = Eingelesen(1_200m, gemeinschaftskonto);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AssignCategoryAsync(
                id, new AssignCategoryRequest { MarkAsDeposit = true }));

        Assert.Contains("Person", fehler.Message);
    }

    [Fact]
    public async Task Wer_am_Objekt_nicht_beteiligt_ist_zahlt_keine_Einlage()
    {
        int fremder;
        using (var context = database.Context(haushalt))
        {
            var dritter = Benutzer("Dritter", "d@test.de", HouseholdRole.Member);
            context.Users.Add(dritter);
            context.SaveChanges();
            fremder = dritter.Id;
        }

        var id = Eingelesen(500m, gemeinschaftskonto);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AssignCategoryAsync(id, AlsEinlage(fremder)));

        Assert.Contains("keinem Objekt beteiligt", fehler.Message);
    }

    /// <summary>
    /// Bei mehreren Objekten wird nicht geraten, sondern gefragt.
    /// </summary>
    [Fact]
    public async Task Bei_zwei_Objekten_fragt_die_Meldung_nach_dem_Objekt()
    {
        using (var context = database.Context(haushalt, oliver))
        {
            context.Properties.Add(new Property
            {
                Name = "Wohnung in der Stadt",
                MarketValue = 250_000m,
                Shares = { new PropertyShare { UserId = sabine, Percent = 100m } },
            });

            context.SaveChanges();
        }

        var id = Eingelesen(1_200m, gemeinschaftskonto);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AssignCategoryAsync(id, AlsEinlage(sabine)));

        Assert.Contains("2 Objekten", fehler.Message);

        // Mit Angabe geht es.
        var danach = await Dienst().AssignCategoryAsync(id, AlsEinlage(sabine, objekt));
        Assert.Equal(TransactionKind.Deposit, danach!.Kind);
        Assert.Equal(objekt, Gelesen(id).PropertyId);
    }

    private Transaction Gelesen(int id)
    {
        using var context = database.Context(haushalt, oliver);
        return context.Transactions.Single(t => t.Id == id);
    }

    public void Dispose() => database.Dispose();
}
