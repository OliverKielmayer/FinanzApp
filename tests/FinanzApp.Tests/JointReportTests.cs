using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Haushaltskonto-Bericht: wer bringt was ein, wofür geht es ab.
/// </summary>
/// <remarks>
/// <para><b>Drei Größen mit drei Namen.</b> Eingebracht und abgeflossen sind gemessen,
/// gebunden ist aus Verträgen fortgeschrieben. Sie dürfen auseinandergehen, und keine darf den
/// Namen einer anderen tragen.</para>
/// <para><b>Die Lücken gehören zum Ergebnis.</b> Ein Zufluss ohne zugeordnete Person und eine
/// Buchung ohne Kategorie stehen mit Anzahl und Betrag daneben — ohne sie läse sich eine halb
/// gepflegte Zuordnung wie ein Befund über zwei Menschen.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class JointReportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 5, 31);
    private readonly int haushalt;
    private readonly int anna;
    private readonly int bruno;
    private readonly int konto;

    public JointReportTests()
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
            BalanceAsOf = new DateOnly(2026, 5, 31),
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

    private Task<JointReportDto> Bericht(int? alsBenutzer = null)
        => database.Reports(clock, userId: alsBenutzer ?? anna)
            .GetJointReportAsync(new JointReportRequest(Period: PeriodScope.Month));

    private int Kategorie(string name, CategoryDirection richtung, int? beitragVon = null)
    {
        using var context = database.Context(haushalt);

        var kategorie = new Category
        {
            Name = name,
            Direction = richtung,
            ContributorUserId = beitragVon,
        };

        context.Categories.Add(kategorie);
        context.SaveChanges();

        return kategorie.Id;
    }

    private void Buchung(decimal betrag, DateOnly tag, int? kategorie = null)
    {
        using var context = database.Context(haushalt);

        context.Transactions.Add(new Transaction
        {
            BookingDate = tag,
            Payee = "Musterzahler",
            Kind = betrag >= 0m ? TransactionKind.Income : TransactionKind.Expense,
            Amount = betrag,
            AccountId = konto,
            CategoryId = kategorie,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    // ── Wer bringt was ein ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Anteile beziehen sich auf das Eingebrachte und ergeben zusammen hundert Prozent.
    /// </summary>
    /// <remarks>
    /// Gegen den ganzen Zufluss gerechnet täten sie es nicht — und ein Kreis, dessen Stücke
    /// nicht aufgehen, ist eine falsche Zeichnung.
    /// </remarks>
    [Fact]
    public async Task Die_Anteile_ergeben_hundert_Prozent()
    {
        var ihrer = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, anna);
        var seiner = Kategorie("Anteil Haushalt Bruno", CategoryDirection.Income, bruno);

        Buchung(1230m, new DateOnly(2026, 5, 3), ihrer);
        Buchung(410m, new DateOnly(2026, 5, 3), seiner);

        var bericht = await Bericht();

        Assert.Equal(1640m, bericht.ContributionTotal);
        Assert.Equal(100m, bericht.Contributions.Sum(c => c.SharePercent));

        var annaZeile = bericht.Contributions.Single(c => c.UserId == anna);
        Assert.Equal(75m, annaZeile.SharePercent);
        Assert.True(annaZeile.IsSelf);

        // Der Schwerste zuerst — die Zeile, auf die es ankommt, steht nicht unten.
        Assert.Equal(anna, bericht.Contributions[0].UserId);
    }

    /// <summary>
    /// <b>Die wichtigste Zahl, solange sie nicht null ist.</b> Was ankam, aber niemandem
    /// zugeordnet ist, steckt in keinem Anteil oben. Ohne diese Zeile läse sich eine halb
    /// gepflegte Zuordnung wie ein Befund über zwei Menschen.
    /// </summary>
    [Fact]
    public async Task Nicht_zugeordnete_Zufluesse_stehen_daneben()
    {
        var ihrer = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, anna);
        var erstattung = Kategorie("Erstattung", CategoryDirection.Income);

        Buchung(1340m, new DateOnly(2026, 5, 3), ihrer);
        Buchung(275.50m, new DateOnly(2026, 5, 9), erstattung);
        Buchung(12.85m, new DateOnly(2026, 5, 12));

        var bericht = await Bericht();

        Assert.Equal(1340m, bericht.ContributionTotal);
        Assert.Equal(288.35m, bericht.UnassignedInflow);
        Assert.Equal(2, bericht.UnassignedInflowCount);
    }

    /// <summary>
    /// Wer einzahlt, ohne am Konto als Beteiligter geführt zu sein, fällt nicht heraus.
    /// </summary>
    /// <remarks>
    /// Seine Summe wegzulassen hübe die Anteile der übrigen fälschlich an: Zwei Drittel von
    /// zwei Dritteln sind hundert Prozent, und niemand sähe, dass ein Drittel fehlt.
    /// </remarks>
    [Fact]
    public async Task Wer_einzahlt_ohne_beteiligt_zu_sein_faellt_nicht_heraus()
    {
        int clara;

        using (var context = database.Context(haushalt))
        {
            var dritte = Benutzer("Clara F.", "c@test.de", HouseholdRole.Member);
            context.Users.Add(dritte);
            context.SaveChanges();
            clara = dritte.Id;
        }

        var ihrer = Kategorie("Anteil Haushalt Anna", CategoryDirection.Income, anna);
        var claras = Kategorie("Anteil Haushalt Clara", CategoryDirection.Income, clara);

        Buchung(870m, new DateOnly(2026, 5, 3), ihrer);
        Buchung(580m, new DateOnly(2026, 5, 3), claras);

        var bericht = await Bericht();

        Assert.Equal(1450m, bericht.ContributionTotal);

        var fremde = bericht.Contributions.Single(c => c.UserId == clara);
        Assert.Equal("Clara F.", fremde.Name);
        Assert.Equal(580m, fremde.Amount);
        Assert.Equal(40m, fremde.SharePercent);

        // Sie hat kein Soll — vereinbart ist nur, was am Konto steht.
        Assert.Null(fremde.MonthlyTarget);
    }

    /// <summary>Wo nichts zugeordnet ist, steht keine Null als Anteil.</summary>
    [Fact]
    public async Task Ohne_Zuordnung_bleiben_die_Anteile_bei_null()
    {
        Buchung(1340m, new DateOnly(2026, 5, 3));

        var bericht = await Bericht();

        Assert.Equal(0m, bericht.ContributionTotal);
        Assert.All(bericht.Contributions, c => Assert.Equal(0m, c.SharePercent));
        Assert.Equal(1340m, bericht.UnassignedInflow);
    }

    // ── Wofür es abgeht ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Abflüsse gliedern sich nach Kategorie, die schwerste zuerst.
    /// </summary>
    [Fact]
    public async Task Die_Abfluesse_gliedern_sich_nach_Kategorie()
    {
        var essen = Kategorie("Lebensmittel", CategoryDirection.Expense);
        var moebel = Kategorie("Einrichtung", CategoryDirection.Expense);

        Buchung(-73.42m, new DateOnly(2026, 5, 6), essen);
        Buchung(-48.15m, new DateOnly(2026, 5, 13), essen);
        Buchung(-310m, new DateOnly(2026, 5, 18), moebel);

        var bericht = await Bericht();

        Assert.Equal(431.57m, bericht.SpendingTotal);

        Assert.Equal("Einrichtung", bericht.Spending[0].Name);
        Assert.Equal(310m, bericht.Spending[0].Amount);
        Assert.Equal(1, bericht.Spending[0].Count);

        Assert.Equal("Lebensmittel", bericht.Spending[1].Name);
        Assert.Equal(121.57m, bericht.Spending[1].Amount);
        Assert.Equal(2, bericht.Spending[1].Count);

        Assert.Equal(100m, bericht.Spending.Sum(z => z.SharePercent));
    }

    /// <summary>
    /// Zwei Kategorien dürfen gleich heißen — eine für Ausgaben, eine für Einnahmen.
    /// </summary>
    /// <remarks>
    /// Im echten Bestand ist das der Normalfall, und eine Ausgabe auf einer Einnahmekategorie
    /// kommt vor. Beide landen in derselben Liste; zwei gleich benannte Zeilen mit
    /// verschiedenen Beträgen sähen dort aus wie ein Fehler des Berichts. Die Richtung steht
    /// deshalb dabei — aber nur, wo sie etwas unterscheidet.
    /// </remarks>
    [Fact]
    public async Task Gleichnamige_Kategorien_werden_unterschieden()
    {
        var ausgabe = Kategorie("Freizeit", CategoryDirection.Expense);
        var einnahme = Kategorie("Freizeit", CategoryDirection.Income);
        var essen = Kategorie("Lebensmittel", CategoryDirection.Expense);

        Buchung(-310m, new DateOnly(2026, 5, 6), ausgabe);
        Buchung(-73.42m, new DateOnly(2026, 5, 7), einnahme);
        Buchung(-48.15m, new DateOnly(2026, 5, 8), essen);

        var namen = (await Bericht()).Spending.Select(z => z.Name).ToList();

        Assert.Contains("Freizeit (Ausgabenkategorie)", namen);
        Assert.Contains("Freizeit (Einnahmekategorie)", namen);

        // Der Zusatz steht nur, wo er etwas unterscheidet. Überall sonst wäre er Ballast.
        Assert.Contains("Lebensmittel", namen);
    }

    /// <summary>
    /// <b>„Ohne Kategorie“ steht zuletzt</b>, wie schwer sie auch wiegt: Sie ist keine Antwort
    /// auf die Frage, wofür das Geld geht, sondern die Stelle, an der die Antwort fehlt.
    /// </summary>
    [Fact]
    public async Task Ohne_Kategorie_steht_zuletzt_auch_wenn_es_schwerer_wiegt()
    {
        var essen = Kategorie("Lebensmittel", CategoryDirection.Expense);

        Buchung(-48.15m, new DateOnly(2026, 5, 13), essen);
        Buchung(-620m, new DateOnly(2026, 5, 19));

        var bericht = await Bericht();

        Assert.Equal(2, bericht.Spending.Count);
        Assert.Equal("Lebensmittel", bericht.Spending[0].Name);
        Assert.Equal("Ohne Kategorie", bericht.Spending[^1].Name);
        Assert.Null(bericht.Spending[^1].CategoryId);

        // Und dieselbe Lücke noch einmal als Zahl, weil die Zeile oben in der Liste untergeht.
        Assert.Equal(1, bericht.UncategorisedCount);
        Assert.Equal(620m, bericht.UncategorisedAmount);
    }

    /// <summary>
    /// Was gebunden ist, kommt aus demselben Fixkostenbericht — nicht aus einer zweiten
    /// Rechnung, und nur vom gezeigten Konto.
    /// </summary>
    /// <remarks>
    /// <b>Der Posten am fremden Konto wiegt hier absichtlich schwerer.</b> In der ersten
    /// Fassung war es umgekehrt, und damit prüfte der Test nichts: Die Kontoliste steht nach
    /// Betrag sortiert, also war das gesuchte Konto zufällig auch das erste — wer den Filter
    /// ganz wegließ, bekam dieselbe Zahl heraus. Erst mit dem schwereren fremden Posten
    /// scheitert, was ohne Filter rechnet.
    /// </remarks>
    [Fact]
    public async Task Gebunden_zaehlt_nur_was_an_diesem_Konto_haengt()
    {
        int anderes;

        using (var context = database.Context(haushalt))
        {
            var privat = new Account
            {
                Name = "Privat Giro", ShortName = "Privat", BankName = "Musterbank",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 5, 31),
                OwnerUserId = anna,
            };

            context.Accounts.Add(privat);
            context.SaveChanges();
            anderes = privat.Id;
        }

        using (var context = database.Context(haushalt))
        {
            context.Contracts.AddRange(
                new Contract
                {
                    Name = "Abfall", Provider = "Landkreis", Amount = 23.15m,
                    PaymentInterval = PremiumInterval.Monthly, AccountId = konto,
                },
                new Contract
                {
                    Name = "Mobilfunk", Provider = "Anbieter", Amount = 61.80m,
                    PaymentInterval = PremiumInterval.Monthly, AccountId = anderes,
                });

            context.SaveChanges();
        }

        var bericht = await Bericht();

        Assert.Equal(23.15m, bericht.MonthlyFixed);
        Assert.Equal(1, bericht.FixedCount);
    }

    // ── Was ohne Konto bleibt ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ohne Gemeinschaftskonto kommt ein leerer Bericht — kein Fehler und keine erfundene Null
    /// mit Namen daran.
    /// </summary>
    [Fact]
    public async Task Ohne_Gemeinschaftskonto_bleibt_der_Bericht_leer()
    {
        using (var context = database.Context(haushalt, anna))
        {
            context.Accounts.Single(a => a.Id == konto).Sharing = AccountSharing.Household;
            context.SaveChanges();
        }

        var bericht = await Bericht();

        Assert.Null(bericht.AccountId);
        Assert.Empty(bericht.Choices);
        Assert.Empty(bericht.Contributions);
        Assert.Empty(bericht.Spending);
        Assert.Equal(0m, bericht.ContributionTotal);
    }

    /// <summary>Das gezeigte Konto steht auch in der Auswahlliste.</summary>
    [Fact]
    public async Task Das_gezeigte_Konto_steht_in_der_Auswahl()
    {
        var bericht = await Bericht();

        Assert.Equal(konto, bericht.AccountId);
        Assert.Equal("Gemeinsam", bericht.AccountName);

        var wahl = Assert.Single(bericht.Choices);
        Assert.Equal(konto, wahl.Id);
        Assert.Equal("Gemeinsam", wahl.Name);
    }

    public void Dispose() => database.Dispose();
}
