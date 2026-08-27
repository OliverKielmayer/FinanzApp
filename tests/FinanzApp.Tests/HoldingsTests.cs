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

    private HoldingsService Service()
    {
        var context = database.Context();

        var dashboard = new DashboardService(
            context,
            new AccountService(context),
            new PortfolioService(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock);

        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new HoldingsService(
            context, dashboard, new VehicleService(context, documents, clock));
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
    /// Der Fall aus dem Handoff: Kopf und Zeilen dürfen sich nicht widersprechen.
    /// </summary>
    /// <remarks>
    /// Eine einzige Zahl über einer Liste, in der Konten und ein Haus stehen, ist ein
    /// Widerspruch — wer die Zeilen summiert, kommt woanders heraus. Also stehen Finanzvermögen
    /// und Sachwerte getrennt, und das Gesamt-netto darunter.
    /// </remarks>
    [Fact]
    public async Task Der_Kopf_trennt_Finanzvermoegen_und_Sachwerte()
    {
        Konto("Sparkasse Giro", 248_179.95m);
        Immobilie(395_000m);

        var kopf = (await Service().GetAsync()).Head;

        Assert.Null(kopf.Class);
        Assert.Equal(248_179.95m, kopf.Value);
        Assert.Equal(395_000m, kopf.TangibleAssets);
        Assert.Equal(643_179.95m, kopf.Net);
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

        Assert.Equal("Allianz", meta);
        Assert.DoesNotContain("ohne", meta);
        Assert.DoesNotContain("··", meta);
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
