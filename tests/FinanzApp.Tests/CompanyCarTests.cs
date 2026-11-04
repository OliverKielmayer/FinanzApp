using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Dienstwagen und die 1-%-Regelung.
/// </summary>
/// <remarks>
/// <para>Geprüft wird vor allem das, was an der Regelung leicht falsch gerechnet wird: die
/// Abrundung auf volle hundert Euro, der Zuschlag für den Arbeitsweg, und die Minderung bei
/// Hybrid und Elektro, die an der <em>Bemessungsgrundlage</em> ansetzt und nicht am
/// Monatssatz — sonst bliebe der Wegzuschlag beim Elektroauto auf voller Höhe stehen.</para>
/// <para>Und das, was keine Rechnung ergibt: ohne Listenpreis und nach Ende des
/// Arbeitsverhältnisses entsteht gar kein Vorteil.</para>
/// </remarks>
public sealed class CompanyCarTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private VehicleService Service()
    {
        var context = database.Context();

        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        return new VehicleService(context, documents, clock);
    }

    private int Arbeitsverhaeltnis(decimal? entfernung, bool laeuft = true)
    {
        using var context = database.Context();

        var stelle = new Employment
        {
            Employer = "Nordwind Messtechnik",
            Position = "Softwareentwicklung",
            GrossMonthly = 6700m,
            StartsOn = new DateOnly(2019, 3, 1),
            EndsOn = laeuft ? null : new DateOnly(2026, 6, 30),
            IsActive = laeuft,
            CommuteKilometres = entfernung,
            WorkDaysPerYear = entfernung is null ? null : 220,
        };

        context.Employments.Add(stelle);
        context.SaveChanges();

        return stelle.Id;
    }

    private int Dienstwagen(
        decimal? listenpreis = 48_900m, decimal? anteil = 100m, int? stelle = null)
    {
        using var context = database.Context();

        var wagen = new Vehicle
        {
            Name = "Firmenwagen Nordwind",
            Plate = "HD-NW 41",
            Usage = "Dienstwagen",
            GrossListPrice = listenpreis,
            TaxableSharePercent = anteil,
            EmploymentId = stelle,
        };

        context.Vehicles.Add(wagen);
        context.SaveChanges();

        return wagen.Id;
    }

    /// <summary>Ohne Listenpreis gibt es nichts zu rechnen — und es wird nichts geraten.</summary>
    [Fact]
    public async Task Ohne_Listenpreis_entsteht_kein_geldwerter_Vorteil()
    {
        var id = Dienstwagen(listenpreis: null, anteil: null);

        var wagen = await Service().GetAsync(id);

        Assert.NotNull(wagen);
        Assert.Null(wagen.Benefit);
        Assert.Equal(0m, wagen.CostsLastTwelveMonths);
        Assert.Empty(wagen.CostParts);
    }

    /// <summary>
    /// Ein Prozent vom abgerundeten Listenpreis.
    /// </summary>
    /// <remarks>
    /// Die Abrundung auf volle hundert Euro ist keine Schönheit, sondern Gesetz
    /// (§ 6 Abs. 1 Nr. 4 EStG). Aus 48.949 € werden 48.900 € — und nicht 489,49 €.
    /// </remarks>
    [Fact]
    public async Task Ein_Prozent_rechnet_auf_den_abgerundeten_Listenpreis()
    {
        var id = Dienstwagen(listenpreis: 48_949m);

        var vorteil = (await Service().GetAsync(id))!.Benefit;

        Assert.NotNull(vorteil);
        Assert.Equal(48_900m, vorteil.Basis);
        Assert.Equal(489m, vorteil.UsePerMonth);
        Assert.Equal(48_949m, vorteil.GrossListPrice);
    }

    /// <summary>Der Zuschlag rechnet mit der Entfernung, die am Arbeitsverhältnis steht.</summary>
    [Fact]
    public async Task Der_Wegzuschlag_rechnet_mit_der_Entfernung_des_Arbeitsverhaeltnisses()
    {
        var stelle = Arbeitsverhaeltnis(entfernung: 24m);
        var id = Dienstwagen(stelle: stelle);

        var vorteil = (await Service().GetAsync(id))!.Benefit;

        Assert.NotNull(vorteil);
        Assert.Equal(24m, vorteil.CommuteKilometres);
        Assert.Equal(352.08m, vorteil.CommutePerMonth);
        Assert.Equal(841.08m, vorteil.PerMonth);
        Assert.Equal(10_092.96m, vorteil.PerYear);
        Assert.Equal("Nordwind Messtechnik", vorteil.EmployerName);
    }

    /// <summary>
    /// Ohne erfasste Entfernung entsteht der Zuschlag nicht.
    /// </summary>
    /// <remarks>
    /// Dieselbe Regel wie bei der Entfernungspauschale: eine geratene Strecke wäre keine
    /// Schätzung mehr, sondern eine Erfindung — und sie stünde in einer Jahreslast.
    /// </remarks>
    [Fact]
    public async Task Ohne_Entfernung_entsteht_kein_Wegzuschlag()
    {
        var stelle = Arbeitsverhaeltnis(entfernung: null);
        var id = Dienstwagen(stelle: stelle);

        var vorteil = (await Service().GetAsync(id))!.Benefit;

        Assert.NotNull(vorteil);
        Assert.Null(vorteil.CommuteKilometres);
        Assert.Equal(0m, vorteil.CommutePerMonth);
        Assert.Equal(489m, vorteil.PerMonth);
    }

    /// <summary>
    /// Beim Elektroauto sinkt mit der Bemessungsgrundlage auch der Wegzuschlag.
    /// </summary>
    /// <remarks>
    /// Der Unterschied, an dem sich die Modellierung entscheidet: Wäre statt der Bemessung der
    /// Monatssatz gemindert, bliebe der Zuschlag bei 352,08 € stehen, und das Elektroauto käme
    /// auf 474,33 € statt 210,27 € im Monat.
    /// </remarks>
    [Fact]
    public async Task Elektro_mindert_die_Bemessung_und_damit_auch_den_Wegzuschlag()
    {
        var stelle = Arbeitsverhaeltnis(entfernung: 24m);
        var id = Dienstwagen(anteil: 25m, stelle: stelle);

        var vorteil = (await Service().GetAsync(id))!.Benefit;

        Assert.NotNull(vorteil);
        Assert.Equal(12_225m, vorteil.Basis);
        Assert.Equal(122.25m, vorteil.UsePerMonth);
        Assert.Equal(88.02m, vorteil.CommutePerMonth);
        Assert.Equal(210.27m, vorteil.PerMonth);
    }

    /// <summary>Ein beendetes Arbeitsverhältnis trägt keinen Dienstwagen mehr.</summary>
    [Fact]
    public async Task Nach_dem_Ende_des_Arbeitsverhaeltnisses_entfaellt_der_Vorteil()
    {
        var stelle = Arbeitsverhaeltnis(entfernung: 24m, laeuft: false);
        var id = Dienstwagen(stelle: stelle);

        var wagen = await Service().GetAsync(id);

        Assert.NotNull(wagen);
        Assert.Null(wagen.Benefit);
        Assert.Equal(0m, wagen.CostsLastTwelveMonths);
    }

    /// <summary>
    /// Der Vorteil steht in den Jahreskosten — und wird dort benannt.
    /// </summary>
    /// <remarks>
    /// Er ist der eine Posten, der nicht vom Konto geht. Ihn wegzulassen hieße, für einen
    /// Dienstwagen 0 € auszuweisen; ihn ungenannt hineinzurechnen hieße, zwei Arten Geld in
    /// einer Zahl zu vermischen, ohne es zu sagen.
    /// </remarks>
    [Fact]
    public async Task Der_Vorteil_steht_benannt_in_den_Jahreskosten()
    {
        var stelle = Arbeitsverhaeltnis(entfernung: 24m);
        Dienstwagen(stelle: stelle);

        var zeile = Assert.Single(await Service().GetListAsync());

        Assert.Equal(10_092.96m, zeile.CostsLastTwelveMonths);

        var wagen = await Service().GetAsync(zeile.Id);
        Assert.Equal("Geldwerter Vorteil", Assert.Single(wagen!.CostParts));
    }

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
