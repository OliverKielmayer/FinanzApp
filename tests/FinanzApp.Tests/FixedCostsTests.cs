using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Fixkosten, vertragliche Bindung und Depot-G/V aus Abschnitt 10b.
/// </summary>
/// <remarks>
/// Der wichtigste Test hier ist der auf die gemeinsame Monatsbasis. Im Prototyp rechneten
/// Fixkosten und Kostentrend gegen verschiedene Monatssummen und widersprachen sich direkt
/// nebeneinander; der Handoff nennt das als erste seiner drei Regeln.
/// </remarks>
public sealed class FixedCostsTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly int konto;
    private readonly int wohnen;

    public FixedCostsTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 20),
        };
        var kategorie = new Category { Name = "Wohnen", Direction = CategoryDirection.Expense };

        context.Accounts.Add(giro);
        context.Categories.Add(kategorie);
        context.SaveChanges();

        konto = giro.Id;
        wohnen = kategorie.Id;
    }

    private ReportService Service() => database.Reports(clock);

    private void Ausgabe(DateOnly tag, decimal betrag)
    {
        using var context = database.Context();
        context.Transactions.Add(new Transaction
        {
            BookingDate = tag, Payee = "Laden", Kind = TransactionKind.Expense,
            Amount = -betrag, AccountId = konto, CategoryId = wohnen,
            CreatedAt = new DateTime(tag.Year, tag.Month, tag.Day, 6, 0, 0, DateTimeKind.Local),
        });
        context.SaveChanges();
    }

    private void Police(
        string name, decimal beitrag, PremiumInterval takt,
        bool kapitalbildend = false, int fristMonate = 0, DateOnly? erinnerung = null,
        DateOnly? ende = null)
    {
        using var context = database.Context();
        context.Policies.Add(new Policy
        {
            Name = name, Provider = "Allianz", Premium = beitrag, PremiumInterval = takt,
            IsCapitalForming = kapitalbildend, NoticePeriodMonths = fristMonate,
            NoticeReminderOn = erinnerung, EndsOn = ende, Kind = PolicyKind.Liability,
        });
        context.SaveChanges();
    }

    private Task<FixedCostsDto> FixAsync()
        => Service().GetFixedCostsAsync(new FixedCostsRequest());

    // ── Die gemeinsame Monatsbasis ─────────────────────────────────────────────────────────

    /// <summary>
    /// Beide Berichte nennen dieselbe Monatsbasis — auch nach einem Ausschluss.
    /// </summary>
    /// <remarks>
    /// Das ist die erste Regel des Handoffs, und sie ist genau hier zu brechen: zwei Berichte,
    /// zwei Aufrufe, dieselbe Zahl. Der Ausschluss gehört mitgeprüft, weil er die Basis
    /// verändert und der zweite Bericht ihn sonst schlicht nicht kennt.
    /// </remarks>
    [Fact]
    public async Task Kostentrend_und_Fixkosten_rechnen_gegen_dieselbe_Basis()
    {
        Ausgabe(new DateOnly(2026, 8, 5), 400m);
        Ausgabe(new DateOnly(2026, 8, 6), 200m);

        var trend = await Service().GetCostTrendAsync(new CostTrendRequest());
        var fix = await FixAsync();

        Assert.Equal(600m, trend.Range.MonthlyExpenseBase);
        Assert.Equal(trend.Range.MonthlyExpenseBase, fix.Range.MonthlyExpenseBase);

        // Und die Zeilensumme des Trends ist dieselbe Menge, nicht nur zufällig gleich groß.
        Assert.Equal(trend.Total, trend.Range.MonthlyExpenseBase * trend.Range.Months);

        var id = database.Context().Transactions.OrderBy(t => t.Id).Last().Id;

        var trendOhne = await Service().GetCostTrendAsync(new CostTrendRequest(
            ExcludedTransactionIds: [id]));
        var fixOhne = await Service().GetFixedCostsAsync(new FixedCostsRequest(
            ExcludedTransactionIds: [id]));

        Assert.Equal(400m, trendOhne.Range.MonthlyExpenseBase);
        Assert.Equal(trendOhne.Range.MonthlyExpenseBase, fixOhne.Range.MonthlyExpenseBase);
    }

    [Fact]
    public async Task Der_Anteil_rechnet_gegen_die_Monatsbasis()
    {
        Ausgabe(new DateOnly(2026, 8, 5), 1000m);
        Police("Haftpflicht", 250m, PremiumInterval.Monthly);

        var fix = await FixAsync();

        Assert.Equal(250m, fix.MonthlyFixed);
        Assert.Equal(750m, fix.MonthlyFree);
        Assert.Equal(25m, fix.FixedSharePercent);
    }

    /// <summary>
    /// Vertraege und Buchungen sind zwei Quellen und muessen nicht aufgehen.
    /// </summary>
    /// <remarks>
    /// Eine negative Zahl unter „frei disponibel“ waere eine Behauptung ueber verfuegbares
    /// Geld. Sie steht da, aber die Anmerkung sagt, woher der Widerspruch kommt.
    /// </remarks>
    [Fact]
    public async Task Mehr_gebunden_als_gebucht_wird_gesagt_und_nicht_verrechnet()
    {
        Ausgabe(new DateOnly(2026, 8, 5), 100m);
        Police("Haftpflicht", 250m, PremiumInterval.Monthly);

        var fix = await FixAsync();

        Assert.Equal(250m, fix.MonthlyFixed);
        Assert.Equal(-150m, fix.MonthlyFree);
        Assert.Contains("stammen aus den Verträgen, nicht aus dem Kontoauszug", fix.Note);
    }

    [Fact]
    public async Task Ohne_gebuchte_Ausgaben_gibt_es_keinen_Anteil()
    {
        Police("Haftpflicht", 250m, PremiumInterval.Monthly);

        var fix = await FixAsync();

        Assert.Equal(0m, fix.FixedSharePercent);
        Assert.Contains("keine Ausgaben gebucht", fix.Note);
    }

    // ── Takt und Frist kommen aus den Rohfeldern ───────────────────────────────────────────

    [Theory]
    [InlineData(PremiumInterval.Monthly, 60)]
    [InlineData(PremiumInterval.Quarterly, 20)]
    [InlineData(PremiumInterval.HalfYearly, 10)]
    [InlineData(PremiumInterval.Yearly, 5)]
    public async Task Jeder_Takt_wird_auf_den_Monat_gerechnet(PremiumInterval takt, decimal proMonat)
    {
        Police("Haftpflicht", 60m, takt);

        Assert.Equal(proMonat, (await FixAsync()).Rows.Single().MonthlyAmount);
    }

    [Fact]
    public async Task Eine_fehlende_Frist_heisst_unbekannt_und_nicht_null()
    {
        Police("Haftpflicht", 20m, PremiumInterval.Monthly);

        var zeile = (await FixAsync()).Rows.Single();

        Assert.Contains("Kündigungsfrist unbekannt", zeile.Note);
        Assert.DoesNotContain("0 Monate", zeile.Note);
    }

    [Fact]
    public async Task Eine_hinterlegte_Frist_steht_im_Klartext()
    {
        Police("Haftpflicht", 20m, PremiumInterval.Monthly, fristMonate: 3);

        Assert.Contains("Kündigungsfrist 3 Monate", (await FixAsync()).Rows.Single().Note);
    }

    [Fact]
    public async Task Eine_Frist_von_eins_steht_in_der_Einzahl()
    {
        Police("Haftpflicht", 20m, PremiumInterval.Monthly, fristMonate: 1);

        var text = (await FixAsync()).Rows.Single().Note;

        Assert.Contains("Kündigungsfrist 1 Monat", text);
        Assert.DoesNotContain("1 Monate", text);
    }

    /// <summary>
    /// Ohne Frist ist ein hinterlegtes Datum das Vertragsende, keine Frist.
    /// </summary>
    /// <remarks>
    /// „Kündigungsfrist unbekannt zum 31.12.2027“ ist ein Satz, der sich selbst widerspricht:
    /// er nennt einen Stichtag für eine Frist, die er im selben Atemzug nicht kennt.
    /// </remarks>
    [Fact]
    public async Task Ohne_Frist_nennt_das_Datum_das_Vertragsende()
    {
        Police("Haftpflicht", 20m, PremiumInterval.Monthly, ende: new DateOnly(2027, 12, 31));

        var text = (await FixAsync()).Rows.Single().Note;

        Assert.Equal("Absicherung · Kündigungsfrist unbekannt · Ende 31.12.2027", text);
    }

    /// <summary>
    /// Ein Vertrag ohne Beitrag steht in keiner Zeile, wird aber gezählt.
    /// </summary>
    /// <remarks>
    /// Eine Null in einer Kostenliste ist kein Eintrag, sondern eine Lücke im Bestand — sie
    /// sagt an dieser Stelle nichts und verdeckt, was etwas sagt. Verschwiegen wäre dafür
    /// „Fix pro Monat“ zu klein, ohne dass jemand sagen könnte warum.
    /// </remarks>
    [Fact]
    public async Task Vertraege_ohne_Beitrag_stehen_nicht_in_der_Liste_aber_in_der_Auskunft()
    {
        Ausgabe(new DateOnly(2026, 8, 5), 1000m);
        Police("Haftpflicht", 20m, PremiumInterval.Monthly);
        Police("Kapital-LV", 0m, PremiumInterval.Monthly, kapitalbildend: true);
        Police("Bausparen", 0m, PremiumInterval.Monthly, kapitalbildend: true);

        var fix = await FixAsync();

        Assert.Equal(["Haftpflicht"], fix.Rows.Select(z => z.Name));
        Assert.Equal(2, fix.WithoutAmountCount);
        Assert.Equal(20m, fix.MonthlyFixed);
    }

    /// <summary>
    /// Ein kapitalbildender Beitrag ist kein Kostenposten.
    /// </summary>
    /// <remarks>
    /// Er fließt ab wie jeder andere und bindet genauso — aber er wird Vermögen. Ihn unter
    /// „Kündigungsfrist“ zu führen, hieße ihn als Ausgabe auszugeben.
    /// </remarks>
    [Fact]
    public async Task Kapitalbildende_Beitraege_zaehlen_als_Sparen()
    {
        Police("Kapital-LV", 212m, PremiumInterval.Monthly, kapitalbildend: true, fristMonate: 3);

        var zeile = (await FixAsync()).Rows.Single();

        Assert.Equal(FixedCostBinding.Saving, zeile.Binding);
        Assert.Equal("kapitalbildend · zählt als Sparen", zeile.Note);
    }

    [Fact]
    public async Task Eine_faellige_Frist_ist_als_faellig_gekennzeichnet()
    {
        Police("Hausrat", 15m, PremiumInterval.Monthly, fristMonate: 3,
            erinnerung: new DateOnly(2026, 8, 1));
        Police("Haftpflicht", 12m, PremiumInterval.Monthly, fristMonate: 3,
            erinnerung: new DateOnly(2026, 12, 1));

        var zeilen = (await FixAsync()).Rows;

        Assert.True(zeilen.Single(z => z.Name == "Hausrat").NoticeDue);
        Assert.False(zeilen.Single(z => z.Name == "Haftpflicht").NoticeDue);
    }

    [Fact]
    public async Task Ein_Darlehen_ist_nicht_kuendbar()
    {
        using (var context = database.Context())
        {
            context.Loans.Add(new Loan
            {
                Name = "Baufinanzierung", Lender = "Sparkasse", RemainingDebt = 148300m,
                InterestRatePercent = 1.85m, Installment = 1180m,
                NextPaymentDate = new DateOnly(2026, 9, 1),
            });
            context.SaveChanges();
        }

        var zeile = (await FixAsync()).Rows.Single();

        Assert.Equal(1180m, zeile.MonthlyAmount);
        Assert.Equal(FixedCostBinding.Fixed, zeile.Binding);
        Assert.Contains("nicht kündbar", zeile.Note);
    }

    // ── Depot ──────────────────────────────────────────────────────────────────────────────

    private int Depot(string name)
    {
        using var context = database.Context();
        var depot = new Depot { Name = name };
        context.Depots.Add(depot);
        context.SaveChanges();
        return depot.Id;
    }

    private void Position(int depot, string name, decimal stueck, decimal kurs, decimal einstand,
        DateTime? stand = null)
    {
        using var context = database.Context();
        context.PortfolioPositions.Add(new PortfolioPosition
        {
            DepotId = depot, Name = name, Isin = "IE00" + name.GetHashCode().ToString("X8"),
            Quantity = stueck, Price = kurs, CostBasis = einstand,
            PriceAsOf = stand ?? new DateTime(2026, 8, 22, 17, 35, 0, DateTimeKind.Local),
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task Ohne_Depot_gibt_es_keinen_Bericht()
        => Assert.Null(await Service().GetPortfolioGainAsync());

    [Fact]
    public async Task Gewinn_wird_auf_den_Einstand_bezogen()
    {
        var depot = Depot("finanzen.net ZERO");
        Position(depot, "Vanguard FTSE All-World", 100m, 120m, 10000m);
        Position(depot, "Allianz SE", 10m, 300m, 4000m);

        var gv = await Service().GetPortfolioGainAsync();

        Assert.NotNull(gv);
        Assert.Equal(14000m, gv.CostBasis);
        Assert.Equal(15000m, gv.CurrentValue);
        Assert.Equal(1000m, gv.Gain);
        Assert.Equal(7.1m, gv.GainPercent);

        var vanguard = gv.Positions.Single(p => p.Name.StartsWith("Vanguard"));
        Assert.Equal(100m, vanguard.CostPerUnit);
        Assert.Equal(2000m, vanguard.Gain);
        Assert.Equal(20m, vanguard.GainPercent);
    }

    /// <summary>Der älteste Kursstichtag zählt — eine Summe ist so frisch wie ihr ältester Teil.</summary>
    [Fact]
    public async Task Der_Kursstand_ist_der_aelteste_nicht_der_juengste()
    {
        var depot = Depot("finanzen.net ZERO");
        Position(depot, "Frisch", 1m, 10m, 10m, new DateTime(2026, 8, 22, 17, 35, 0, DateTimeKind.Local));
        Position(depot, "Alt", 1m, 10m, 10m, new DateTime(2026, 6, 1, 17, 35, 0, DateTimeKind.Local));

        var gv = await Service().GetPortfolioGainAsync();

        Assert.Equal(new DateTime(2026, 6, 1, 17, 35, 0, DateTimeKind.Local), gv!.PricesAsOf);
    }

    [Fact]
    public async Task Ohne_Einstand_gibt_es_keinen_Prozentsatz()
    {
        var depot = Depot("Geschenkdepot");
        Position(depot, "Erbstück", 10m, 50m, 0m);

        var gv = await Service().GetPortfolioGainAsync();

        Assert.Equal(500m, gv!.Gain);
        Assert.Null(gv.GainPercent);
        Assert.Null(gv.Positions.Single().GainPercent);
    }

    [Fact]
    public async Task Die_Depotauswahl_steht_zur_Verfuegung_und_wird_befolgt()
    {
        var erstes = Depot("finanzen.net ZERO");
        var zweites = Depot("Comdirect");
        Position(erstes, "A", 1m, 10m, 5m);
        Position(zweites, "B", 1m, 20m, 5m);

        var vorgabe = await Service().GetPortfolioGainAsync();
        Assert.Equal(erstes, vorgabe!.DepotId);
        Assert.Equal(2, vorgabe.Depots.Count);
        Assert.Equal("A", vorgabe.Positions.Single().Name);

        var gewaehlt = await Service().GetPortfolioGainAsync(zweites);
        Assert.Equal("Comdirect", gewaehlt!.DepotName);
        Assert.Equal("B", gewaehlt.Positions.Single().Name);
    }

    /// <summary>Eine unbekannte Depot-Id fällt auf das erste zurück, statt leer zu antworten.</summary>
    [Fact]
    public async Task Eine_unbekannte_Depotwahl_faellt_auf_das_erste_zurueck()
    {
        var depot = Depot("finanzen.net ZERO");
        Position(depot, "A", 1m, 10m, 5m);

        Assert.Equal(depot, (await Service().GetPortfolioGainAsync(9999))!.DepotId);
    }

    public void Dispose() => database.Dispose();
}
