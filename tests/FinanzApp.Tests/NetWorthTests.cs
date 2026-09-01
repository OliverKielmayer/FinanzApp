using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Das Vermögen in drei Größen — v5-Handoff, Abschnitt 3(b).
/// </summary>
/// <remarks>
/// Der Anlass ist ein Widerspruch, der erst sichtbar wurde, als Objekte in <em>eine</em> Liste
/// rückten: der Kopf nannte „Nettovermögen 99.880 €“, und darunter stand ein Haus mit
/// 395.000 €. Solange Immobilien einen eigenen Bildschirm hatten, fiel niemandem auf, dass sie
/// in keiner Summe vorkamen. Diese Tests halten die drei Größen und ihr Verhältnis fest.
/// </remarks>
public sealed class NetWorthTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private DashboardService Service()
    {
        var context = database.Context();

        return new DashboardService(
            context,
            new AccountService(context),
            TestDatabase.Portfolio(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock,
            TestDatabase.SignedIn(null));
    }

    private void Konto(decimal stand)
    {
        using var context = database.Context();
        context.Accounts.Add(new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
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
        });
        context.SaveChanges();
    }

    private void Darlehen(decimal restschuld)
    {
        using var context = database.Context();
        context.Loans.Add(new Loan
        {
            Name = "Baufinanzierung", Lender = "Sparkasse", RemainingDebt = restschuld,
            InterestRatePercent = 1.85m, Installment = 1180m,
            NextPaymentDate = new DateOnly(2026, 9, 1),
        });
        context.SaveChanges();
    }

    /// <summary>
    /// Die Immobilie steht in den Sachwerten, nicht im Finanzvermögen.
    /// </summary>
    /// <remarks>
    /// Beides wäre falsch: sie wegzulassen macht das Vermögen kleiner, als es ist; sie
    /// dazuzuzählen macht aus „was auf Konten liegt“ eine Zahl, die niemand abheben kann.
    /// </remarks>
    [Fact]
    public async Task Finanzvermoegen_und_Sachwerte_bleiben_getrennt()
    {
        Konto(248_179.95m);
        Immobilie(395_000m);
        Darlehen(148_300m);

        var wert = (await Service().GetAsync()).NetWorth;

        Assert.Equal(248_179.95m, wert.FinancialAssets);
        Assert.Equal(395_000m, wert.TangibleAssets);
        Assert.Equal(148_300m, wert.Liabilities);
    }

    /// <summary>
    /// Genau der Fall aus dem Handoff: die Zeilen summieren sich zur Kopfzahl.
    /// </summary>
    /// <remarks>
    /// Wer die Liste nachrechnet, muss auf dieselbe Zahl kommen wie der Kopf. Das war zuvor
    /// nicht so, und es fiel nur deshalb nicht auf, weil die Zeilen auf getrennten Screens
    /// lagen.
    /// </remarks>
    [Fact]
    public async Task Das_Nettovermoegen_ist_die_Summe_der_drei_Groessen()
    {
        Konto(248_179.95m);
        Immobilie(395_000m);
        Darlehen(148_300m);

        var wert = (await Service().GetAsync()).NetWorth;

        Assert.Equal(494_879.95m, wert.Net);
        Assert.Equal(wert.FinancialAssets + wert.TangibleAssets - wert.Liabilities, wert.Net);
    }

    /// <summary>
    /// Die Kurve zeichnet eine andere Größe als der Kopf — und die gibt es als eigene.
    /// </summary>
    /// <remarks>
    /// Für Sachwerte existiert keine Monatsreihe. Einen konstanten Immobilienwert in jeden
    /// Punkt zu addieren verschöbe die Kurve nur nach oben, ohne etwas zu zeigen. Also trägt
    /// das Modell beide Größen, statt eine davon stillschweigend für die andere auszugeben.
    /// </remarks>
    [Fact]
    public async Task Das_Finanznetto_steht_neben_dem_Gesamtnetto()
    {
        Konto(248_179.95m);
        Immobilie(395_000m);
        Darlehen(148_300m);

        var wert = (await Service().GetAsync()).NetWorth;

        Assert.Equal(99_879.95m, wert.FinancialNet);
        Assert.NotEqual(wert.Net, wert.FinancialNet);
    }

    [Fact]
    public async Task Ohne_Immobilie_sind_Sachwerte_null_und_beide_Netto_gleich()
    {
        Konto(10_000m);
        Darlehen(4_000m);

        var wert = (await Service().GetAsync()).NetWorth;

        Assert.Equal(0m, wert.TangibleAssets);
        Assert.Equal(6_000m, wert.Net);
        Assert.Equal(wert.Net, wert.FinancialNet);
    }

    /// <summary>
    /// Ein Fahrzeug trägt keinen Vermögenswert.
    /// </summary>
    /// <remarks>
    /// Es hat in diesem Bestand Jahreskosten und keinen Wert. Einen zu erfinden — Listenpreis,
    /// Schätzung, Restwert — wäre eine Zahl, die niemand geprüft hat, in einer Summe, der man
    /// glauben soll.
    /// </remarks>
    [Fact]
    public async Task Ein_Fahrzeug_zaehlt_in_keine_Vermoegenssumme()
    {
        Konto(10_000m);

        using (var context = database.Context())
        {
            context.Vehicles.Add(new Vehicle { Name = "VW Passat", Plate = "HD-AB 123" });
            context.SaveChanges();
        }

        var wert = (await Service().GetAsync()).NetWorth;

        Assert.Equal(0m, wert.TangibleAssets);
        Assert.Equal(10_000m, wert.Net);
    }

    /// <summary>Die Kachelanteile beziehen sich auf das Finanzvermögen, nicht auf alles.</summary>
    [Fact]
    public async Task Die_Kachelanteile_rechnen_gegen_das_Finanzvermoegen()
    {
        Konto(10_000m);
        Immobilie(90_000m);

        var dashboard = await Service().GetAsync();
        var giro = dashboard.Assets.Single(a => a.Label == "Girokonten");

        // Gegen das Gesamtvermögen waeren es 10 %.
        Assert.Equal(1m, giro.ShareOfFinancialAssets);
    }

    public void Dispose() => database.Dispose();
}
