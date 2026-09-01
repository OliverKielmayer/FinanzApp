using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Das Gemeinschaftskonto und sein Einzahlungssoll — Handoff „Gemeinsame Immobilie“, 3.3.
/// </summary>
/// <remarks>
/// <para>Die vierte Freigabestufe unterscheidet sich von „namentlich freigegeben“ nicht im
/// Zugriff, sondern in der Erwartung: hier zahlt jeder monatlich etwas ein, und der Schirm stellt
/// Soll und Eingang gegenüber.</para>
/// <para><b>Er mahnt nicht — er sagt, was steht.</b> Deshalb keine Fälligkeitslogik, keine
/// Erinnerung: eine Feststellung, und daneben der Jahresstand, weil ein einzelner Monat wenig
/// sagt.</para>
/// </remarks>
public sealed class JointAccountTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 15);
    private readonly int haushalt;
    private readonly int oliver;
    private readonly int sabine;
    private readonly int konto;
    private readonly int objekt;

    public JointAccountTests()
    {
        haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var a = Benutzer("Oliver W.", "o@test.de", HouseholdRole.Owner);
        var b = Benutzer("Sabine K.", "s@test.de", HouseholdRole.Member);
        context.Users.AddRange(a, b);
        context.SaveChanges();

        oliver = a.Id;
        sabine = b.Id;

        var haushaltskonto = new Account
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

        context.Accounts.Add(haushaltskonto);
        context.SaveChanges();
        konto = haushaltskonto.Id;

        var haus = new Property
        {
            Name = "Haus zu zweit",
            MarketValue = 420000m,
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

    /// <remarks>
    /// Der Benutzer muss auch in den Kontext: ein Gemeinschaftskonto sieht nur, wer in seiner
    /// Liste steht, und das prüft der Abfragefilter — nicht der Dienst.
    /// </remarks>
    private ParticipationService Dienst(int? alsBenutzer)
        => new(database.Context(haushalt, alsBenutzer), TestDatabase.SignedIn(alsBenutzer));

    /// <remarks>
    /// Positives Vorzeichen: auf dem Gemeinschaftskonto kommt die Einlage an. Vom eigenen Konto
    /// aus wäre dieselbe Buchung ein Abfluss — siehe <c>TransactionService.Vorzeichen</c>.
    /// </remarks>
    private void Einlage(decimal betrag, int person, DateOnly tag)
    {
        using var context = database.Context(haushalt);

        context.Transactions.Add(new Transaction
        {
            BookingDate = tag,
            Payee = "Einlage",
            Kind = TransactionKind.Deposit,
            Amount = betrag,
            AccountId = konto,
            DepositUserId = person,
            PropertyId = objekt,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    /// <summary>Ohne Einlagen steht das Soll da und daneben eine Null.</summary>
    [Fact]
    public async Task Ohne_Einlagen_steht_das_Soll_offen()
    {
        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        Assert.Equal(2700m, gemeinschaft.TargetTotal);
        Assert.Equal(0m, gemeinschaft.PaidTotal);
        Assert.All(gemeinschaft.Contributors, p => Assert.False(p.Fulfilled));
    }

    /// <summary>
    /// Soll und Eingang stehen je Person gegenüber.
    /// </summary>
    /// <remarks>
    /// Der Fall aus dem Handoff: „Oliver 1.500 € ✓ · Sabine 1.200 €, 300 € unter Soll“. Verglichen
    /// wird je Person — über die Summe gerechnet fiele der Rückstand nicht auf.
    /// </remarks>
    [Fact]
    public async Task Soll_und_Eingang_stehen_je_Person_gegenueber()
    {
        Einlage(1500m, oliver, new DateOnly(2026, 9, 1));
        Einlage(900m, sabine, new DateOnly(2026, 9, 1));

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        var meiner = gemeinschaft.Contributors.Single(p => p.UserId == oliver);
        Assert.True(meiner.Fulfilled);
        Assert.Equal(0m, meiner.Difference);

        var ihrer = gemeinschaft.Contributors.Single(p => p.UserId == sabine);
        Assert.False(ihrer.Fulfilled);
        Assert.Equal(-300m, ihrer.Difference);

        // Der Termin steht neben dem Stand: „erfüllt“ ohne Datum sagt nicht, ob es rechtzeitig war.
        Assert.Equal(new DateOnly(2026, 9, 1), meiner.LastPaidOn);
    }

    /// <summary>
    /// Ohne Eingang gibt es keinen Termin.
    /// </summary>
    /// <remarks>
    /// Ein Datum, wo nichts gebucht ist, wäre eine Erfindung. Stattdessen steht der vereinbarte
    /// Tag daneben — der ist bekannt.
    /// </remarks>
    [Fact]
    public async Task Ohne_Eingang_gibt_es_keinen_Termin()
    {
        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        Assert.All(gemeinschaft.Contributors, p => Assert.Null(p.LastPaidOn));
        Assert.All(gemeinschaft.Contributors, p => Assert.Equal(1, p.DueDay));
    }

    /// <summary>
    /// Was vom Konto abgeht, ist kein Eingang auf ihm.
    /// </summary>
    /// <remarks>
    /// Eine Einlage kann auch vom Gemeinschaftskonto <em>weg</em> gebucht sein — dann hat jemand
    /// von hier aus für das Objekt eingelegt. Sie als Eingang zu zählen machte aus einem Abfluss
    /// eine erfüllte Vereinbarung.
    /// </remarks>
    [Fact]
    public async Task Ein_Abfluss_ist_kein_Eingang()
    {
        using (var context = database.Context(haushalt, oliver))
        {
            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 9, 4),
                Payee = "Einlage vom Gemeinschaftskonto",
                Kind = TransactionKind.Deposit,
                Amount = -1500m,
                AccountId = konto,
                DepositUserId = oliver,
                PropertyId = objekt,
                CreatedAt = clock.Now,
            });

            context.SaveChanges();
        }

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        Assert.Equal(0m, gemeinschaft.PaidTotal);
        Assert.Equal(0m, gemeinschaft.PaidThisYear);
    }

    /// <summary>
    /// Der Monat zählt nur seine eigenen Einlagen, der Jahresstand alle.
    /// </summary>
    /// <remarks>
    /// Wer im Mai zweimal gezahlt hat, steht im September nicht besser da — deshalb steht der
    /// Jahresstand daneben und nicht anstelle des Monats.
    /// </remarks>
    [Fact]
    public async Task Der_Monat_und_das_Jahr_werden_getrennt_gezaehlt()
    {
        Einlage(1500m, oliver, new DateOnly(2026, 5, 1));
        Einlage(1500m, oliver, new DateOnly(2026, 9, 1));

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));
        var meiner = gemeinschaft.Contributors.Single(p => p.UserId == oliver);

        Assert.Equal(1500m, meiner.PaidThisMonth);
        Assert.Equal(3000m, meiner.PaidThisYear);
        Assert.Equal(3000m, gemeinschaft.PaidThisYear);
    }

    /// <summary>
    /// Was später gebucht ist, zählt nicht in diesen Monat.
    /// </summary>
    /// <remarks>
    /// Eine vordatierte Einlage wäre sonst in jedem Monat vor ihr schon erfüllt. Im Jahresstand
    /// steht sie dagegen: gebucht ist gebucht, und das Jahr ist das Kalenderjahr.
    /// </remarks>
    [Fact]
    public async Task Ein_spaeterer_Monat_zaehlt_nicht_mit()
    {
        Einlage(1500m, oliver, new DateOnly(2026, 10, 1));

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));
        var meiner = gemeinschaft.Contributors.Single(p => p.UserId == oliver);

        Assert.Equal(0m, meiner.PaidThisMonth);
        Assert.Null(meiner.LastPaidOn);
        Assert.Equal(1500m, meiner.PaidThisYear);
    }

    /// <summary>
    /// Ohne vereinbartes Soll gibt es keine Abweichung.
    /// </summary>
    /// <remarks>
    /// „300 € unter Soll“ ohne Soll wäre ein Vorwurf ohne Grundlage. Dann steht der Eingang da,
    /// und mehr sagt der Schirm nicht.
    /// </remarks>
    [Fact]
    public async Task Ohne_Soll_gibt_es_keine_Abweichung()
    {
        using (var context = database.Context(haushalt, oliver))
        {
            foreach (var anteil in context.AccountShares.Where(a => a.AccountId == konto))
            {
                anteil.MonthlyTarget = null;
                anteil.DueDay = null;
            }

            context.SaveChanges();
        }

        Einlage(800m, sabine, new DateOnly(2026, 9, 3));

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        Assert.False(gemeinschaft.HasTargets);
        Assert.All(gemeinschaft.Contributors, p => Assert.Null(p.Difference));
        Assert.Equal(800m, gemeinschaft.PaidTotal);
    }

    /// <summary>
    /// Nur Einlagen zählen — nicht jeder Eingang.
    /// </summary>
    /// <remarks>
    /// Eine Lohnzahlung auf das Haushaltskonto ist keine Einlage. Sie mitzuzählen machte aus
    /// einem Rückstand einen Überschuss.
    /// </remarks>
    [Fact]
    public async Task Eine_Einnahme_ist_keine_Einlage()
    {
        using (var context = database.Context(haushalt))
        {
            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 9, 2),
                Payee = "Arbeitgeber",
                Kind = TransactionKind.Income,
                Amount = 4000m,
                AccountId = konto,
                CreatedAt = clock.Now,
            });

            context.SaveChanges();
        }

        var gemeinschaft = Assert.Single(await Dienst(oliver).JointAccountsAsync(clock.Today));

        Assert.Equal(0m, gemeinschaft.PaidTotal);
    }

    /// <summary>Ein Konto ohne die vierte Stufe erscheint nicht.</summary>
    [Fact]
    public async Task Nur_Gemeinschaftskonten_erscheinen()
    {
        using (var context = database.Context(haushalt, oliver))
        {
            context.Accounts.Single(a => a.Id == konto).Sharing = AccountSharing.Household;
            context.SaveChanges();
        }

        Assert.Empty(await Dienst(oliver).JointAccountsAsync(clock.Today));
    }

    public void Dispose() => database.Dispose();
}
