using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Kostentrend aus Abschnitt 10b.
/// </summary>
/// <remarks>
/// Geprüft wird vor allem, was der Handoff als im Prototyp gebrochene Regeln mitgibt: dieselbe
/// Größe darf nicht zweimal verschieden herauskommen, ein Vergleich braucht denselben
/// Saisonpunkt, und zwei Zahlen über dieselbe Menge müssen dieselbe Menge zählen.
/// </remarks>
public sealed class ReportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly int konto;
    private readonly int freizeit;
    private readonly int wohnen;

    public ReportTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 20),
        };
        var a = new Category { Name = "Freizeit", Direction = CategoryDirection.Expense };
        var b = new Category { Name = "Wohnen", Direction = CategoryDirection.Expense };

        context.Accounts.Add(giro);
        context.Categories.AddRange(a, b);
        context.SaveChanges();

        konto = giro.Id;
        freizeit = a.Id;
        wohnen = b.Id;
    }

    private ReportService Service() => database.Reports(clock);

    /// <summary>Eine Ausgabe. Der Betrag wird negativ abgelegt, wie überall im Bestand.</summary>
    private int Ausgabe(DateOnly tag, int kategorie, decimal betrag, string empfaenger = "Laden")
    {
        using var context = database.Context();
        var buchung = new Transaction
        {
            BookingDate = tag,
            Payee = empfaenger,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            AccountId = konto,
            CategoryId = kategorie,
            CreatedAt = new DateTime(tag.Year, tag.Month, tag.Day, 6, 0, 0, DateTimeKind.Local),
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return buchung.Id;
    }

    private Task<CostTrendDto> TrendAsync(
        ComparisonBasis vergleich = ComparisonBasis.PreviousYear,
        PeriodScope zeitraum = PeriodScope.Month,
        IReadOnlyList<int>? ausgeschlossen = null,
        int? offen = null,
        CostTrendSort sortierung = CostTrendSort.Increase)
        => Service().GetCostTrendAsync(
            new CostTrendRequest(zeitraum, vergleich, sortierung, ausgeschlossen, offen));

    // ── Vergleich ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ohne_Vergleichszeitraum_wird_kein_Trend_behauptet()
    {
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);

        var trend = await TrendAsync();

        Assert.False(trend.Range.HasComparison);
        Assert.Equal(CostTrendStatus.Unknown, trend.Rows.Single().Status);
        Assert.Null(trend.Rows.Single().ChangePercent);
        Assert.Contains("ohne Vergleichszeitraum kein Trend", trend.RisingLine);

        // Kein erfundener Anstieg: die Riser-Zahl bleibt bei null.
        Assert.Equal(0, trend.RisingCount);
    }

    [Fact]
    public async Task Der_Vorjahresvergleich_trifft_denselben_Monat()
    {
        Ausgabe(new DateOnly(2025, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2025, 7, 5), freizeit, 999m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 120m);

        var trend = await TrendAsync();
        var zeile = trend.Rows.Single();

        Assert.True(trend.Range.HasComparison);
        Assert.Equal(120m, zeile.Amount);
        Assert.Equal(100m, zeile.ComparisonAmount);
        Assert.Equal(20m, zeile.ChangePercent);
        Assert.Equal(CostTrendStatus.Rising, zeile.Status);
    }

    /// <summary>
    /// Der laufende Monat wird gegen denselben Ausschnitt des Vorjahres gehalten.
    /// </summary>
    /// <remarks>
    /// Ohne die Kappung stünden zwanzig gelaufene Tage gegen einunddreißig, und jede Kategorie
    /// sänke — der Bericht mäße die Länge des Zeitraums statt der Kosten.
    /// </remarks>
    [Fact]
    public async Task Ein_laufender_Zeitraum_vergleicht_nur_bis_heute()
    {
        Ausgabe(new DateOnly(2025, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2025, 8, 28), freizeit, 400m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);

        var trend = await TrendAsync();

        Assert.Equal(new DateOnly(2026, 8, 20), trend.Range.To);
        Assert.Equal(100m, trend.Rows.Single().ComparisonAmount);
        Assert.Equal(CostTrendStatus.Stable, trend.Rows.Single().Status);
        Assert.Contains("bis 20.08.2026", trend.Range.Line);
    }

    [Fact]
    public async Task Die_Vorperiode_ist_der_gleich_lange_Zeitraum_davor()
    {
        Ausgabe(new DateOnly(2026, 7, 5), freizeit, 200m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 150m);

        var trend = await TrendAsync(ComparisonBasis.PreviousPeriod);
        var zeile = trend.Rows.Single();

        Assert.Equal(200m, zeile.ComparisonAmount);
        Assert.Equal(-25m, zeile.ChangePercent);
        Assert.Equal(CostTrendStatus.Falling, zeile.Status);
        Assert.Contains("Juli 2026", trend.Range.Line);
    }

    [Fact]
    public async Task Das_Zwoelfmonatsmittel_nimmt_die_Monate_vor_dem_Zeitraum()
    {
        // Zwölf Monate August 2025 bis Juli 2026, jeder 60 € — Mittel also 60 €.
        for (var i = 0; i < 12; i++)
        {
            Ausgabe(new DateOnly(2025, 8, 10).AddMonths(i), freizeit, 60m);
        }

        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 90m);

        var trend = await TrendAsync(ComparisonBasis.TwelveMonthAverage);
        var zeile = trend.Rows.Single();

        Assert.Equal(60m, zeile.TwelveMonthAverage);
        Assert.Equal(60m, zeile.ComparisonAmount);
        Assert.Equal(50m, zeile.ChangePercent);
        Assert.Equal("Ø 12 Monate", trend.Range.ComparisonLabel);
    }

    [Fact]
    public async Task Eine_Kategorie_ohne_Vorjahreswert_gilt_als_neu_und_nicht_als_Prozentzahl()
    {
        Ausgabe(new DateOnly(2025, 8, 5), wohnen, 500m);
        Ausgabe(new DateOnly(2026, 8, 5), wohnen, 500m);
        Ausgabe(new DateOnly(2026, 8, 6), freizeit, 80m);

        var neu = (await TrendAsync()).Rows.Single(r => r.Name == "Freizeit");

        Assert.Null(neu.ChangePercent);
        Assert.Equal(CostTrendStatus.Rising, neu.Status);
    }

    // ── Ausschluss ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_ausgeschlossene_Buchung_faellt_aus_jeder_Zahl()
    {
        Ausgabe(new DateOnly(2025, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);
        var weg = Ausgabe(new DateOnly(2026, 8, 6), freizeit, 50m, "Einmalig");

        var voll = (await TrendAsync()).Rows.Single();
        Assert.Equal(150m, voll.Amount);
        Assert.Equal(CostTrendStatus.Rising, voll.Status);

        var trend = await TrendAsync(ausgeschlossen: [weg]);
        var zeile = trend.Rows.Single();

        // Kategoriesumme, Gesamtsumme, Prozentwert und Status folgen gemeinsam.
        Assert.Equal(100m, zeile.Amount);
        Assert.Equal(100m, trend.Total);
        Assert.Equal(0m, zeile.ChangePercent);
        Assert.Equal(CostTrendStatus.Stable, zeile.Status);
        Assert.Equal(0, trend.RisingCount);
    }

    /// <summary>
    /// Zwei Zahlen über dieselbe Menge zählen dieselbe Menge — die dritte Regel des Handoffs.
    /// </summary>
    [Fact]
    public async Task Der_Drilldown_nennt_beide_Anteile()
    {
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m, "Kino");
        var weg = Ausgabe(new DateOnly(2026, 8, 6), freizeit, 50m, "Theater");

        var trend = await TrendAsync(ausgeschlossen: [weg], offen: freizeit);
        var zeile = trend.Rows.Single();

        Assert.Equal(2, zeile.TransactionCount);
        Assert.Equal(1, zeile.ExcludedCount);
        Assert.Equal(1, trend.ExcludedCount);

        // Die Zeilen stehen alle da, die ausgeschlossene erkennbar gemacht.
        Assert.Equal(2, zeile.Entries.Count);
        Assert.Single(zeile.Entries, e => e.Excluded);

        // Die Empfängergruppen zählen nur, was zählt.
        Assert.Equal(["Kino"], zeile.Payees.Select(p => p.Payee));
    }

    [Fact]
    public async Task Den_Drilldown_bekommt_nur_die_aufgeklappte_Kategorie()
    {
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2026, 8, 5), wohnen, 900m);

        var trend = await TrendAsync(offen: freizeit);

        Assert.NotEmpty(trend.Rows.Single(r => r.Name == "Freizeit").Entries);
        Assert.Empty(trend.Rows.Single(r => r.Name == "Wohnen").Entries);
    }

    // ── Was nicht mitzaehlt ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Umbuchungen_und_Einnahmen_zaehlen_in_keiner_Ausgabenauswertung()
    {
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);

        using (var context = database.Context())
        {
            context.Transactions.AddRange(
                new Transaction
                {
                    BookingDate = new DateOnly(2026, 8, 6), Payee = "Umbuchung → Tagesgeld",
                    Kind = TransactionKind.Transfer, Amount = -1500m,
                    AccountId = konto, CategoryId = freizeit,
                    CreatedAt = new DateTime(2026, 8, 6, 6, 0, 0, DateTimeKind.Local),
                },
                new Transaction
                {
                    BookingDate = new DateOnly(2026, 8, 7), Payee = "Erstattung",
                    Kind = TransactionKind.Income, Amount = 40m,
                    AccountId = konto, CategoryId = freizeit,
                    CreatedAt = new DateTime(2026, 8, 7, 6, 0, 0, DateTimeKind.Local),
                });
            context.SaveChanges();
        }

        var trend = await TrendAsync();

        Assert.Equal(100m, trend.Total);
        Assert.Equal(1, trend.Rows.Single().TransactionCount);
    }

    /// <summary>
    /// Was ohne Kategorie bleibt, steht in keiner Zeile — aber die Zahl wird genannt.
    /// </summary>
    /// <remarks>
    /// Verschwiegen wäre die Summe kleiner als die Ausgaben des Zeitraums, ohne dass jemand
    /// sagen könnte warum.
    /// </remarks>
    [Fact]
    public async Task Ausgaben_ohne_Kategorie_stehen_nicht_in_der_Summe_aber_in_der_Auskunft()
    {
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 100m);

        using (var context = database.Context())
        {
            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 8, 8), Payee = "Shell",
                Kind = TransactionKind.Expense, Amount = -84.10m, AccountId = konto,
                CreatedAt = new DateTime(2026, 8, 8, 6, 0, 0, DateTimeKind.Local),
            });
            context.SaveChanges();
        }

        var trend = await TrendAsync();

        Assert.Equal(100m, trend.Total);
        Assert.Equal(100m, trend.Rows.Sum(r => r.Amount));
        Assert.Equal(1, trend.UncategorisedCount);
        Assert.Equal(84.10m, trend.UncategorisedAmount);
    }

    // ── Rahmen ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Die eine Monatsbasis — die erste Regel des Handoffs.</summary>
    [Fact]
    public async Task Die_Monatsbasis_folgt_dem_Zeitraum()
    {
        Ausgabe(new DateOnly(2026, 6, 5), freizeit, 300m);
        Ausgabe(new DateOnly(2026, 7, 5), freizeit, 300m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 300m);

        var monat = await TrendAsync(zeitraum: PeriodScope.Month);
        Assert.Equal(300m, monat.Total);
        Assert.Equal(1, monat.Range.Months);
        Assert.Equal(300m, monat.Range.MonthlyExpenseBase);

        // Q3 fängt im Juli an — der Juni gehört nicht dazu — und ist am 20.08. erst zwei
        // Monate alt. Durch drei geteilt käme eine Monatsbasis heraus, die es nie gab.
        var quartal = await TrendAsync(zeitraum: PeriodScope.Quarter);
        Assert.Equal(600m, quartal.Total);
        Assert.Equal(2, quartal.Range.Months);
        Assert.Equal(300m, quartal.Range.MonthlyExpenseBase);
    }

    [Fact]
    public async Task Die_Sparkline_hat_vierundzwanzig_Monate_und_endet_im_Zeitraum()
    {
        Ausgabe(new DateOnly(2024, 9, 5), freizeit, 10m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 90m);

        var spark = (await TrendAsync()).Rows.Single().Spark;

        Assert.Equal(24, spark.Count);
        Assert.Equal(10m, spark[0]);
        Assert.Equal(90m, spark[^1]);
    }

    [Fact]
    public async Task Sortiert_wird_nach_Anstieg_Betrag_oder_Name()
    {
        Ausgabe(new DateOnly(2025, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2025, 8, 5), wohnen, 1000m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 200m);
        Ausgabe(new DateOnly(2026, 8, 5), wohnen, 1050m);

        var anstieg = await TrendAsync(sortierung: CostTrendSort.Increase);
        Assert.Equal(["Freizeit", "Wohnen"], anstieg.Rows.Select(r => r.Name));

        var betrag = await TrendAsync(sortierung: CostTrendSort.Amount);
        Assert.Equal(["Wohnen", "Freizeit"], betrag.Rows.Select(r => r.Name));

        var name = await TrendAsync(sortierung: CostTrendSort.Name);
        Assert.Equal(["Freizeit", "Wohnen"], name.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Die_Riser_Zeile_nennt_Anzahl_und_die_staerksten_Namen()
    {
        Ausgabe(new DateOnly(2025, 8, 5), freizeit, 100m);
        Ausgabe(new DateOnly(2025, 8, 5), wohnen, 1000m);
        Ausgabe(new DateOnly(2026, 8, 5), freizeit, 200m);
        Ausgabe(new DateOnly(2026, 8, 5), wohnen, 1500m);

        var trend = await TrendAsync();

        Assert.Equal(2, trend.RisingCount);
        Assert.Contains("2 Kategorien steigen um mehr als 5 %", trend.RisingLine);
        Assert.Contains("Freizeit, Wohnen", trend.RisingLine);
    }

    public void Dispose() => database.Dispose();
}
