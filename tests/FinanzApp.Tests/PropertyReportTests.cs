using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der achte Bericht: „Objekt &amp; Beteiligung“ — Handoff „Gemeinsame Immobilie“, 3.5.
/// </summary>
/// <remarks>
/// <para>Drei getrennte Aussagen, und zwischen ihnen die Fehler, die dieser Handoff über fünf
/// Runden gejagt hat: <b>angefallen gegen hochgerechnet</b> (gemessen gegen fortgeschrieben),
/// <b>Zins gegen Tilgung</b> (Aufwand gegen Vermögensaufbau) und <b>Objektkosten gegen
/// Kontoabfluss</b> (zwei Größen, die nie dieselbe Zahl tragen dürfen).</para>
/// <para>Die Beteiligung wird hier nicht nachgerechnet, sondern durchgereicht — geprüft ist
/// deshalb, dass die Zahlen mit denen des Beteiligungsdienstes übereinstimmen.</para>
/// </remarks>
public sealed class PropertyReportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 15);
    private readonly int haushalt;
    private readonly int oliver;
    private readonly int sabine;
    private readonly int konto;
    private int objekt;

    public PropertyReportTests()
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
            OwnerUserId = oliver,
        };

        context.Accounts.Add(haushaltskonto);
        context.SaveChanges();
        konto = haushaltskonto.Id;
    }

    private User Benutzer(string name, string email, HouseholdRole rolle) => new()
    {
        HouseholdId = haushalt, Name = name, Email = email, PasswordHash = "-",
        Role = rolle, CreatedAt = clock.Now,
    };

    private ReportService Berichte(int? alsBenutzer = null)
        => database.Reports(clock, userId: alsBenutzer);

    /// <summary>
    /// Ein Objekt mit dem, was der Bericht liest.
    /// </summary>
    /// <remarks>
    /// Das Darlehen trägt eine Rate, aus der sich Zins und Tilgung trennen lassen; die Fläche
    /// macht die €/m²-Zahl möglich, die Rücklage den Posten, der das Konto nicht verlässt.
    /// </remarks>
    private void Objekt(
        decimal? flaeche = 100m,
        decimal? ruecklage = 500m,
        bool mitDarlehen = true,
        bool geteilt = true)
    {
        using var context = database.Context(haushalt, oliver);

        int? darlehen = null;

        if (mitDarlehen)
        {
            var kredit = new Loan
            {
                Name = "Immobiliendarlehen",
                Lender = "Sparkasse",
                RemainingDebt = 120000m,
                InterestRatePercent = 2.40m,
                Installment = 1000m,
                NextPaymentDate = new DateOnly(2026, 10, 1),
            };

            context.Loans.Add(kredit);
            context.SaveChanges();
            darlehen = kredit.Id;
        }

        var haus = new Property
        {
            Name = "Haus zu zweit",
            Address = "Hauptstraße 1",
            MarketValue = 420000m,
            PurchaseDate = new DateOnly(2019, 4, 1),
            LoanId = darlehen,
            LivingArea = flaeche,
            MonthlyReserve = ruecklage,
        };

        if (geteilt)
        {
            haus.Shares.Add(new PropertyShare { UserId = oliver, Percent = 50m, Equity = 90000m });
            haus.Shares.Add(new PropertyShare { UserId = sabine, Percent = 50m, Equity = 50000m });
        }

        context.Properties.Add(haus);
        context.SaveChanges();
        objekt = haus.Id;
    }

    /// <summary>Ein Vertrag am Objekt, objektbezogen oder nicht.</summary>
    private void Vertrag(string name, decimal monatlich, bool objektbezogen)
    {
        using var context = database.Context(haushalt, oliver);

        context.Contracts.Add(new Contract
        {
            Name = name,
            Provider = "Stadtwerke",
            MonthlyAmount = monatlich,
            PropertyId = objekt,
            PropertyRelated = objektbezogen,
        });

        context.SaveChanges();
    }

    /// <summary>Eine gebuchte Ausgabe auf eine Kategorie mit oder ohne Kennzeichen.</summary>
    private int Buchung(string kategorie, decimal betrag, bool objektbezogen, DateOnly? am = null)
    {
        using var context = database.Context(haushalt, oliver);

        var vorhanden = context.Categories.FirstOrDefault(c => c.Name == kategorie);

        if (vorhanden is null)
        {
            vorhanden = new Category
            {
                Name = kategorie,
                Direction = CategoryDirection.Expense,
                PropertyRelated = objektbezogen,
            };

            context.Categories.Add(vorhanden);
            context.SaveChanges();
        }

        context.Transactions.Add(new Transaction
        {
            BookingDate = am ?? new DateOnly(2026, 3, 10),
            Payee = kategorie,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            AccountId = konto,
            CategoryId = vorhanden.Id,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
        return vorhanden.Id;
    }

    // ── Was kostet das Objekt ─────────────────────────────────────────────────────────────

    /// <summary>Ohne Objekt gibt es keinen Bericht.</summary>
    /// <remarks>
    /// Nicht eine Hülle aus Nullen: „0 € Objektkosten“ wäre eine Aussage über ein Haus, das es
    /// nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Ohne_Objekt_gibt_es_keinen_Bericht()
        => Assert.Null(await Berichte().GetPropertyReportAsync(null));

    /// <summary>
    /// Die Rate zerfällt in Zins und Tilgung, und nur der Zins ist Aufwand.
    /// </summary>
    /// <remarks>
    /// Von 12.000 € Jahresrate auf 120.000 € Restschuld zu 2,4 % sind 2.779 € Zins — nicht 2.880,
    /// weil der Zinsanteil mit der Restschuld fällt. Der Rest baut Vermögen auf; beides als
    /// Kosten zu zeigen machte das Haus teurer, als es ist.
    /// </remarks>
    [Fact]
    public async Task Zins_ist_Aufwand_und_Tilgung_Vermoegen()
    {
        Objekt();

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        var zins = bericht!.Items.Single(p => p.Label == "Darlehen — Zins");
        var tilgung = bericht.Items.Single(p => p.Label == "Darlehen — Tilgung");

        Assert.Equal(PropertyCostKind.Expense, zins.Kind);
        Assert.Equal(PropertyCostKind.Equity, tilgung.Kind);

        // Die Rate steht als Summe der beiden da und ist damit nachrechenbar.
        var darlehen = bericht.Loan!;
        Assert.Equal(12000m, darlehen.YearInstalment);
        Assert.Equal(darlehen.YearInstalment, zins.YearAmount + tilgung.YearAmount);
        Assert.Equal(2779m, darlehen.YearInterest);
        Assert.Equal(9221m, darlehen.YearPrincipal);
        Assert.Equal(23, darlehen.InterestPercent);
    }

    /// <summary>
    /// Nur objektbezogene Verträge zählen.
    /// </summary>
    /// <remarks>
    /// Der Internetanschluss hängt am Haus und zieht doch mit den Leuten um — Handoff 3.4. Zählte
    /// er mit, wäre die €/m²-Zahl zu hoch, und niemand sähe es ihr an.
    /// </remarks>
    [Fact]
    public async Task Nur_objektbezogene_Vertraege_zaehlen()
    {
        Objekt(mitDarlehen: false, ruecklage: null);
        Vertrag("Strom", 100m, objektbezogen: true);
        Vertrag("Internet", 40m, objektbezogen: false);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal("Strom", Assert.Single(bericht!.Items).Label);
        Assert.Equal(1200m, bericht.YearTotal);
        Assert.Equal(100m, bericht.MonthlyTotal);
    }

    /// <summary>
    /// Die Rücklage zählt zu den Objektkosten.
    /// </summary>
    /// <remarks>
    /// Sie verlässt das Konto nicht — genau der Fall, an dem Objektkosten und Kontoabfluss
    /// auseinanderfallen. Die Zeile im Bericht sagt es; hier steht, dass sie mitzählt.
    /// </remarks>
    [Fact]
    public async Task Die_Ruecklage_zaehlt_mit()
    {
        Objekt(mitDarlehen: false, ruecklage: 500m);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(6000m, bericht!.YearTotal);
        Assert.Equal(500m, bericht.MonthlyReserve);
    }

    /// <summary>
    /// Ohne Wohnfläche keine €/m²-Zahl.
    /// </summary>
    /// <remarks>
    /// Eine geschätzte Fläche wäre ein erfundener Nenner, und die Zahl sähe genauso aus wie eine
    /// richtige.
    /// </remarks>
    [Fact]
    public async Task Ohne_Flaeche_keine_Quadratmeterzahl()
    {
        Objekt(flaeche: null, mitDarlehen: false, ruecklage: 500m);

        var ohne = await Berichte(oliver).GetPropertyReportAsync(null);
        Assert.Null(ohne!.PerSquareMetre);

        using (var context = database.Context(haushalt, oliver))
        {
            context.Properties.Single(p => p.Id == objekt).LivingArea = 100m;
            context.SaveChanges();
        }

        var mit = await Berichte(oliver).GetPropertyReportAsync(null);

        // 500 €/Monat auf 100 m² sind 5 €/m² — nachrechenbar aus den Nachbarzahlen.
        Assert.Equal(5m, mit!.PerSquareMetre);
    }

    // ── Gemessen gegen fortgeschrieben ────────────────────────────────────────────────────

    /// <summary>
    /// „Angefallen“ kommt aus Buchungen, „aufs Jahr“ aus Verträgen.
    /// </summary>
    /// <remarks>
    /// Zwei Zahlen, zwei Quellen, zwei Namen. Sie müssen nicht übereinstimmen — die eine kommt
    /// aus dem Kontoauszug, die andere aus dem Bestand. Eine Zwölfmonatssumme als Jahresstand zu
    /// beschriften, wenn neun Monate erfasst sind, ist der Fehler, den Regel 2 nennt.
    /// </remarks>
    [Fact]
    public async Task Angefallen_ist_gemessen_und_nicht_hochgerechnet()
    {
        Objekt(mitDarlehen: false, ruecklage: 500m);

        Buchung("Wohnen", 300m, objektbezogen: true);
        Buchung("Wohnen", 200m, objektbezogen: true, am: new DateOnly(2026, 4, 10));
        Buchung("Lebensmittel", 400m, objektbezogen: false);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(500m, bericht!.Incurred);
        Assert.Equal(2, bericht.IncurredBookings);
        Assert.Equal(9, bericht.IncurredMonths);
        Assert.Equal(new DateOnly(2026, 1, 1), bericht.IncurredFrom);

        // Und die fortgeschriebene Zahl bleibt davon unberührt.
        Assert.Equal(6000m, bericht.YearTotal);
    }

    /// <summary>Eine Buchung aus dem Vorjahr zählt nicht ins laufende Jahr.</summary>
    [Fact]
    public async Task Das_Vorjahr_zaehlt_nicht_mit()
    {
        Objekt(mitDarlehen: false, ruecklage: null);

        Buchung("Wohnen", 300m, objektbezogen: true, am: new DateOnly(2025, 12, 30));

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(0m, bericht!.Incurred);
        Assert.Equal(0, bericht.IncurredBookings);
    }

    /// <summary>
    /// Der Ausschlussblock nennt, was bewusst fehlt — gezählt, nicht aufgezählt.
    /// </summary>
    /// <remarks>
    /// Eine feste Liste („Lebensmittel, Freizeit, Mobilität“) nennte womöglich Posten, die es in
    /// diesem Haushalt nicht gibt, und verschwiege die, die es gibt.
    /// </remarks>
    [Fact]
    public async Task Der_Ausschlussblock_nennt_die_uebrigen_Kategorien()
    {
        Objekt(mitDarlehen: false, ruecklage: null);

        Buchung("Wohnen", 300m, objektbezogen: true);
        Buchung("Lebensmittel", 400m, objektbezogen: false);
        Buchung("Freizeit", 150m, objektbezogen: false);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(2, bericht!.Excluded.Count);
        Assert.Equal(0, bericht.ExcludedMore);

        // Nach Betrag, damit der größte Posten oben steht.
        Assert.Equal("Lebensmittel", bericht.Excluded[0].Name);
        Assert.Equal(400m, bericht.Excluded[0].YearAmount);
        Assert.DoesNotContain(bericht.Excluded, a => a.Name == "Wohnen");
    }

    // ── Objektkosten gegen Kontoabfluss ───────────────────────────────────────────────────

    /// <summary>
    /// Ohne Gemeinschaftskonto fehlt die Bezugsgröße des Abflusses.
    /// </summary>
    /// <remarks>
    /// Eine Null stünde da wie ein Konto, von dem nichts abgeht. Die Zeile fällt dann weg,
    /// statt eine Rechnung ohne Gegenüber zu zeigen.
    /// </remarks>
    [Fact]
    public async Task Ohne_Gemeinschaftskonto_kein_Abfluss()
    {
        Objekt(mitDarlehen: false, ruecklage: null);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Null(bericht!.Outflow);
        Assert.Null(bericht.OutflowOther);
    }

    /// <summary>
    /// Der Abfluss des Gemeinschaftskontos zerfällt in objektbezogen und übrige.
    /// </summary>
    /// <remarks>
    /// Und er ist nicht die Objektkost: die Rücklage zählt dort mit und verlässt das Konto nicht.
    /// Beide Zahlen stehen im Bericht mit ihrem eigenen Namen.
    /// </remarks>
    [Fact]
    public async Task Der_Abfluss_zerfaellt_und_ist_nicht_die_Objektkost()
    {
        Objekt(mitDarlehen: false, ruecklage: 500m);

        var haus = Buchung("Wohnen", 240m, objektbezogen: true, am: new DateOnly(2026, 9, 3));
        var essen = Buchung("Lebensmittel", 160m, objektbezogen: false, am: new DateOnly(2026, 9, 4));

        Assert.NotEqual(haus, essen);

        using (var context = database.Context(haushalt, oliver))
        {
            var giro = context.Accounts.Single(a => a.Id == konto);
            giro.Sharing = AccountSharing.Shared;
            context.AccountShares.Add(new AccountShare { AccountId = konto, UserId = oliver });
            context.AccountShares.Add(new AccountShare { AccountId = konto, UserId = sabine });
            context.SaveChanges();
        }

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(400m, bericht!.Outflow);
        Assert.Equal(240m, bericht.OutflowPropertyRelated);
        Assert.Equal(160m, bericht.OutflowOther);

        // Die Objektkost ist eine andere Zahl — die Rücklage steckt in ihr und im Abfluss nicht.
        Assert.Equal(500m, bericht.MonthlyTotal);
    }

    // ── Wer hat wie viel getragen ─────────────────────────────────────────────────────────

    /// <summary>
    /// Die Beteiligungszahlen stimmen mit dem Beteiligungsdienst überein.
    /// </summary>
    /// <remarks>
    /// Der Bericht rechnet sie nicht nach, er reicht sie durch. Wären es zwei Rechnungen, liefen
    /// sie irgendwann auseinander — genau der Fehler, den der Handoff sieben Runden lang gejagt
    /// hat.
    /// </remarks>
    [Fact]
    public async Task Die_Beteiligung_kommt_aus_einer_Quelle()
    {
        Objekt();

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);
        var beteiligung = bericht!.Participation!;

        var dienst = new ParticipationService(
            database.Context(haushalt, oliver), TestDatabase.SignedIn(oliver));

        var direkt = await dienst.ForPropertyAsync(objekt);

        Assert.Equal(direkt!.Settlement, beteiligung.Settlement);

        // 90.000 gegen 50.000 bei halbe-halbe: der Ausgleich ist die halbe Differenz.
        Assert.Equal(20000m, beteiligung.Settlement);

        foreach (var person in beteiligung.Participants)
        {
            var gegenprobe = direkt.Participants.Single(p => p.UserId == person.UserId);
            Assert.Equal(gegenprobe.Contributed, person.Contributed);
            Assert.Equal(gegenprobe.Settlement, person.Settlement);
        }
    }

    /// <summary>Ohne Anteile gibt es niemanden, gegen den auszugleichen wäre.</summary>
    [Fact]
    public async Task Ohne_Anteile_kein_Ausgleich()
    {
        Objekt(geteilt: false, mitDarlehen: false, ruecklage: null);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.False(bericht!.Participation?.IsShared ?? false);
    }

    /// <summary>
    /// Die Anteilsspalte sagt, wenn sie nicht aufgeht.
    /// </summary>
    /// <remarks>
    /// Je Posten gerundet ergibt sie manchmal 99 oder 101 %. Eine Spalte, die sichtbar nicht
    /// aufgeht und dazu schweigt, lässt an allen Zahlen zweifeln.
    /// </remarks>
    [Fact]
    public async Task Die_Anteilsspalte_nennt_ihre_Summe()
    {
        Objekt(mitDarlehen: false, ruecklage: null);

        Vertrag("Strom", 100m, objektbezogen: true);
        Vertrag("Wasser", 100m, objektbezogen: true);
        Vertrag("Abfall", 100m, objektbezogen: true);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(null);

        Assert.Equal(3, bericht!.Items.Count);
        Assert.All(bericht.Items, p => Assert.Equal(33, p.SharePercent));
        Assert.Equal(99, bericht.SharePercentSum);
    }

    /// <summary>
    /// Ein unbekanntes Objekt fällt auf das erste zurück.
    /// </summary>
    /// <remarks>
    /// Der Bericht wird auch aus gespeicherten Adressen geöffnet. Ein Fehler statt eines
    /// Berichts wäre dort eine Sackgasse.
    /// </remarks>
    [Fact]
    public async Task Eine_unbekannte_Kennung_faellt_auf_das_erste_Objekt()
    {
        Objekt(mitDarlehen: false, ruecklage: null);

        var bericht = await Berichte(oliver).GetPropertyReportAsync(9999);

        Assert.Equal(objekt, bericht!.PropertyId);
        Assert.Equal("Haus zu zweit", bericht.Name);
    }

    public void Dispose() => database.Dispose();
}
