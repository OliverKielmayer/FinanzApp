using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Bestand — v5-Handoff, Abschnitt 3.
/// </summary>
/// <remarks>
/// Geprüft wird vor allem, woran der erste Bauversuch des Prototyps gescheitert ist: Wertarten
/// in eine Summe zwingen, eine Kopfzahl, die den Zeilen widerspricht, und Metazeilen aus einem
/// Anzeigefeld, das die Objekte gar nicht haben.
/// </remarks>
public sealed class HoldingsTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DashboardService Dashboard()
    {
        var context = database.Context();

        return new DashboardService(
            context,
            new AccountService(context),
            new PortfolioService(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock);
    }

    private HoldingsService Service()
    {
        var context = database.Context();
        var dashboard = Dashboard();

        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new HoldingsService(
            context, dashboard, new VehicleService(context, documents, clock), clock);
    }

    private void Arbeitsverhaeltnis(string arbeitgeber, decimal brutto, DateOnly? ende = null)
    {
        using var context = database.Context();

        context.Employments.Add(new Employment
        {
            Employer = arbeitgeber,
            Position = "Softwareentwicklung",
            GrossMonthly = brutto,
            StartsOn = new DateOnly(2019, 3, 1),
            EndsOn = ende,
            IsActive = ende is null,
        });

        context.SaveChanges();
    }

    private void Konto(string name, decimal stand)
    {
        using var context = database.Context();
        context.Accounts.Add(new Account
        {
            Name = name, ShortName = name, BankName = "Sparkasse",
            Kind = AccountKind.Checking, OpeningBalance = stand,
            BalanceAsOf = new DateOnly(2026, 8, 20),
        });
        context.SaveChanges();
    }

    private void Immobilie(decimal wert)
    {
        using var context = database.Context();
        context.Properties.Add(new Property
        {
            Name = "Haus Hauptstraße", Kind = PropertyKind.House, MarketValue = wert,
            Address = "Hauptstraße 5",
        });
        context.SaveChanges();
    }

    private void Police(string name, decimal beitrag, bool kapitalbildend, DateOnly? erinnerung = null)
    {
        using var context = database.Context();
        context.Policies.Add(new Policy
        {
            Name = name, Provider = "Allianz", Premium = beitrag,
            PremiumInterval = PremiumInterval.Monthly, IsCapitalForming = kapitalbildend,
            CurrentValue = kapitalbildend ? 20_000m : null,
            ValuationDate = kapitalbildend ? new DateOnly(2025, 12, 31) : null,
            NoticeReminderOn = erinnerung, Kind = PolicyKind.Liability,
        });
        context.SaveChanges();
    }

    // ── Wertarten ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Vertrag trägt Jahreskosten und keinen Vermögenswert.
    /// </summary>
    /// <remarks>
    /// Zwei Spaltenbedeutungen in einer Liste sind zulässig, solange die Einheit an der Zahl
    /// steht. Einen Vermögenswert zu erfinden, damit die Spalte einheitlich aussieht, wäre es
    /// nicht — er landete in einer Summe, der man glauben soll.
    /// </remarks>
    [Fact]
    public async Task Ein_Vertrag_traegt_Kosten_und_keinen_Wert()
    {
        Police("Haftpflicht", 20m, kapitalbildend: false);

        var zeile = (await Service().GetAsync(HoldingClass.Protection)).Rows.Single();

        Assert.Null(zeile.Value);
        Assert.Equal(240m, zeile.YearlyCost);
    }

    [Fact]
    public async Task Ein_kapitalbildender_Vertrag_traegt_einen_Wert()
    {
        Police("Kapital-LV", 212m, kapitalbildend: true);

        var zeile = (await Service().GetAsync(HoldingClass.Pension)).Rows.Single();

        Assert.Equal(20_000m, zeile.Value);
        Assert.Null(zeile.YearlyCost);
        Assert.Equal("Stand 31.12.2025", zeile.Note);
    }

    /// <summary>Ein Sachwert ist als solcher gekennzeichnet — er zählt in eine andere Summe.</summary>
    [Fact]
    public async Task Eine_Immobilie_ist_ein_Sachwert()
    {
        Immobilie(395_000m);

        var zeile = (await Service().GetAsync(HoldingClass.Housing)).Rows.Single();

        Assert.True(zeile.IsTangible);
        Assert.Equal(395_000m, zeile.Value);
    }

    // ── Die Kopfkennzahl ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bei „Alle“ steht das Gesamtvermögen netto, und die Dreiteilung liegt darunter bereit.
    /// </summary>
    /// <remarks>
    /// Zwei Fassungen dieser Regel sind schon gescheitert. Erst nannte der Kopf 99.880 €,
    /// während in derselben Liste eine Immobilie über 395.000 € stand. Dann trug der Kopf
    /// Finanzvermögen und Sachwerte nebeneinander, das Dashboard aber weiter eine einzelne
    /// Zahl — zwei Antworten auf dieselbe Frage. Jetzt gilt: eine Zahl, drei Flächen.
    /// </remarks>
    [Fact]
    public async Task Der_Kopf_nennt_das_Gesamtvermoegen_und_haelt_die_Dreiteilung_bereit()
    {
        Konto("Sparkasse Giro", 248_179.95m);
        Immobilie(395_000m);

        var kopf = (await Service().GetAsync()).Head;

        Assert.Null(kopf.Class);
        Assert.Equal(643_179.95m, kopf.Value);
        Assert.Equal(kopf.Net, kopf.Value);

        Assert.Equal(248_179.95m, kopf.FinancialAssets);
        Assert.Equal(395_000m, kopf.TangibleAssets);
        Assert.Equal(0m, kopf.Liabilities);
    }

    /// <summary>
    /// Bestand und Dashboard nennen dieselbe Zahl.
    /// </summary>
    /// <remarks>
    /// Nicht „dieselbe Rechnung“, sondern dieselbe Zahl aus derselben Quelle: der Bestand fragt
    /// den Vermögensdienst und rechnet nichts nach. Genau dort ist es zuletzt auseinander-
    /// gelaufen — die Zahlen stimmten je für sich und widersprachen einander trotzdem.
    /// </remarks>
    [Fact]
    public async Task Bestand_und_Dashboard_nennen_dieselbe_Zahl()
    {
        Konto("Sparkasse Giro", 248_179.95m);
        Immobilie(395_000m);

        var kopf = (await Service().GetAsync()).Head;
        var dashboard = await Dashboard().GetAsync();

        Assert.Equal(dashboard.NetWorth.Net, kopf.Value);
        Assert.Equal(dashboard.NetWorth.FinancialAssets, kopf.FinancialAssets);
        Assert.Equal(dashboard.NetWorth.TangibleAssets, kopf.TangibleAssets);
        Assert.Equal(1, dashboard.NetWorth.TangibleCount);
    }

    [Fact]
    public async Task Bei_Absicherung_zaehlt_der_Kopf_Jahresbeitraege_und_Fristen()
    {
        Police("Haftpflicht", 20m, kapitalbildend: false);
        Police("Hausrat", 15m, kapitalbildend: false, erinnerung: new DateOnly(2026, 9, 1));

        var kopf = (await Service().GetAsync(HoldingClass.Protection)).Head;

        Assert.Equal(420m, kopf.Value);
        Assert.Equal(2, kopf.Count);
        Assert.Equal(1, kopf.UrgentCount);
    }

    /// <summary>Wohnen führt Objekte und Verträge — und zählt beide getrennt.</summary>
    [Fact]
    public async Task Wohnen_zaehlt_Objekte_und_Vertraege_getrennt()
    {
        Immobilie(395_000m);

        using (var context = database.Context())
        {
            context.Contracts.Add(new Contract
            {
                Name = "Strom", Provider = "Stadtwerke", MonthlyAmount = 142.50m,
            });
            context.SaveChanges();
        }

        var bestand = await Service().GetAsync(HoldingClass.Housing);

        Assert.Equal(1, bestand.Head.Count);
        Assert.Equal(1, bestand.Head.SecondaryCount);
        Assert.Equal(395_000m, bestand.Head.Value);
    }

    /// <summary>
    /// Arbeit trennt laufend von beendet — und die Summe kennt nur die laufenden.
    /// </summary>
    /// <remarks>
    /// Der Chip daneben heißt „Arbeit 2“ und zählt die Zeilen, die die Liste zeigt. Zwei Zähler
    /// unter demselben Wort müssten dieselbe Menge zählen; weil sie es nicht tun, benennt sich
    /// jeder — „2“ am Chip, „1 laufend“ in der Unterzeile.
    /// </remarks>
    [Fact]
    public async Task Arbeit_summiert_nur_laufende_Verhaeltnisse()
    {
        Arbeitsverhaeltnis("EWV Kontrollsysteme", 6480m);
        Arbeitsverhaeltnis("Rheinpark Klinikum", 4120m, ende: new DateOnly(2024, 8, 31));

        var bestand = await Service().GetAsync(HoldingClass.Work);

        Assert.Equal(2, bestand.Rows.Count);
        Assert.Equal(2, bestand.Classes.Single(c => c.Class == HoldingClass.Work).Count);

        Assert.Equal(77_760m, bestand.Head.Value);
        Assert.Equal(1, bestand.Head.Count);
        Assert.Equal(1, bestand.Head.SecondaryCount);
    }

    /// <summary>
    /// Ein Gehalt ist weder Vermögenswert noch Jahreskosten.
    /// </summary>
    /// <remarks>
    /// Stünde es unter <c>YearlyCost</c>, liefe eine Einnahme in jede Kostensumme — und die
    /// Zeile läse sich als Ausgabe. Die beendete Zeile trägt gar keine Zahl: sie zeigt „—“.
    /// </remarks>
    [Fact]
    public async Task Ein_Arbeitsverhaeltnis_traegt_Einkommen_statt_Wert_oder_Kosten()
    {
        Arbeitsverhaeltnis("EWV Kontrollsysteme", 6480m);
        Arbeitsverhaeltnis("Rheinpark Klinikum", 4120m, ende: new DateOnly(2024, 8, 31));

        var zeilen = (await Service().GetAsync(HoldingClass.Work)).Rows;

        var laufend = zeilen.Single(z => z.Name == "EWV Kontrollsysteme");
        Assert.Equal(77_760m, laufend.YearlyIncome);
        Assert.Null(laufend.Value);
        Assert.Null(laufend.YearlyCost);

        var beendet = zeilen.Single(z => z.Name == "Rheinpark Klinikum");
        Assert.Null(beendet.YearlyIncome);
        Assert.Null(beendet.Value);
        Assert.Null(beendet.YearlyCost);
    }

    /// <summary>Ein Gehalt darf in keiner Vermögenssumme auftauchen.</summary>
    [Fact]
    public async Task Ein_Gehalt_zaehlt_nicht_ins_Finanzvermoegen()
    {
        Konto("Sparkasse Giro", 1000m);
        Arbeitsverhaeltnis("EWV Kontrollsysteme", 6480m);

        Assert.Equal(1000m, (await Service().GetAsync()).Head.Value);
    }

    // ── Metazeilen ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Jede Zeile bekommt einen Untertitel aus ihren Rohfeldern.
    /// </summary>
    /// <remarks>
    /// Der erste Bauversuch las ein Anzeigefeld, das die Objekte nicht haben: 22 von 25 Zeilen
    /// blieben leer, und die Liste war ärmer als jeder Einzelbereich vorher.
    /// </remarks>
    [Fact]
    public async Task Jede_Zeile_hat_eine_Metazeile()
    {
        Konto("Sparkasse Giro", 1000m);
        Immobilie(395_000m);
        Police("Haftpflicht", 20m, kapitalbildend: false);
        Police("Kapital-LV", 212m, kapitalbildend: true);

        var zeilen = (await Service().GetAsync()).Rows;

        Assert.NotEmpty(zeilen);
        Assert.All(zeilen, z => Assert.False(string.IsNullOrWhiteSpace(z.Meta)));
    }

    /// <summary>
    /// Was ein Objekt nicht hat, steht nicht da.
    /// </summary>
    /// <remarks>
    /// Eine Zeile „Vertrag · ohne Konto“ behauptet etwas über ein Feld, das schlicht leer ist.
    /// Richtig ist, die Angabe wegzulassen, nicht ihre Abwesenheit zu formulieren.
    /// </remarks>
    [Fact]
    public async Task Leere_Felder_werden_weggelassen_und_nicht_ausgeschrieben()
    {
        Police("Haftpflicht", 20m, kapitalbildend: false);

        var meta = (await Service().GetAsync(HoldingClass.Protection)).Rows.Single().Meta;

        // Art und Anbieter stehen da; Nummer, Ende, Frist und Notiz sind leer und fehlen
        // darum — ohne Trennzeichen, das ins Leere zeigt.
        Assert.Equal("Haftpflicht · Allianz", meta);
        Assert.DoesNotContain("ohne", meta);
        Assert.DoesNotContain("· ·", meta);
        Assert.False(meta.EndsWith('·'));
    }

    // ── Filter ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Der_Klassenfilter_zaehlt_immer_alle_Klassen()
    {
        Konto("Sparkasse Giro", 1000m);
        Immobilie(395_000m);

        var bestand = await Service().GetAsync(HoldingClass.Accounts);

        Assert.Single(bestand.Rows);
        Assert.Equal(2, bestand.Classes.Single(c => c.Class is null).Count);
        Assert.Equal(1, bestand.Classes.Single(c => c.Class == HoldingClass.Housing).Count);
        Assert.Equal(HoldingClass.Accounts, bestand.AddIn);
    }

    /// <summary>Ohne Filter gibt es keine Klasse, in der die „+“-Zeile anlegen könnte.</summary>
    [Fact]
    public async Task Ohne_Filter_legt_die_Plus_Zeile_in_keiner_Klasse_an()
        => Assert.Null((await Service().GetAsync()).AddIn);

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
