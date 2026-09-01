using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using User = FinanzApp.Api.Data.Entities.User;

namespace FinanzApp.Tests;

/// <summary>
/// Die Demo-Daten von Arbeit &amp; Beruf halten der eigenen Logik stand.
/// </summary>
/// <remarks>
/// <para>Das ist die erste Regel, die der v5-Handoff aus dem Prototypenbau mitgibt: dort war die
/// Abrechnung 08/2026 an eine Gehaltsbuchung über 5.240 € geknüpft, führte selbst aber 3.812 €
/// Auszahlung — 37 % daneben, eine Paarung, die der eigene Matcher (±15 %) nie vorgeschlagen
/// hätte, und ein Widerspruch zur Einnahmenzahl des Dashboards.</para>
/// <para>Deshalb wird hier nicht geprüft, ob die Zahlen hübsch sind, sondern ob der Matcher
/// jede vorverknüpfte Zahlung selbst als besten Treffer vorschlagen würde. Beispieldaten, die
/// der eigene Dienst ablehnen würde, sind keine Beispiele, sondern ein Gegenbeweis.</para>
/// </remarks>
public sealed class EmploymentSeedTests : IDisposable
{
    private readonly TestDatabase database = new();

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private EmploymentService Service()
        => new(database.Context(), TestDatabase.ClockAt(2026, 8, 28));

    [Fact]
    public async Task Jede_vorverknuepfte_Zahlung_waere_auch_der_beste_Vorschlag()
    {
        await SeedAsync();

        var abrechnungen = (await Service().GetAsync()).Payslips
            .Where(p => p.TransactionId is not null)
            .ToList();

        Assert.NotEmpty(abrechnungen);

        foreach (var zeile in abrechnungen)
        {
            var treffer = await Service().GetPaymentCandidatesAsync(zeile.Id);

            Assert.True(
                treffer.Count > 0,
                $"Für {zeile.Month:MM/yyyy} schlägt der Matcher gar nichts vor, "
                + "obwohl der Seed eine Zahlung verknüpft hat.");

            Assert.True(
                treffer[0].TransactionId == zeile.TransactionId,
                $"Für {zeile.Month:MM/yyyy} ist die verknüpfte Buchung nicht der beste Treffer.");
        }
    }

    /// <summary>
    /// Auszahlungsbetrag und gebuchter Betrag sind dieselbe Zahl.
    /// </summary>
    /// <remarks>
    /// „Der Gehaltseingang ist app-weit eine Größe“ — Lohnabrechnung, Bankbuchung und
    /// Dashboard-Einnahmen müssen dieselbe nennen. Der Seed liest den Auszahlungsbetrag darum
    /// aus der Buchung, statt ihn danebenzusetzen.
    /// </remarks>
    [Fact]
    public async Task Auszahlungsbetrag_und_Buchung_nennen_dieselbe_Zahl()
    {
        await SeedAsync();

        foreach (var zeile in (await Service().GetAsync()).Payslips.Where(p => p.PaidAmount is not null))
        {
            Assert.Equal(zeile.Payout, zeile.PaidAmount);
        }
    }

    /// <summary>Beide Zustände des Bereichs sollen sich ohne Vorarbeit vorführen lassen.</summary>
    [Fact]
    public async Task Der_Seed_zeigt_beide_Luecken_je_einmal()
    {
        await SeedAsync();

        var arbeit = await Service().GetAsync();

        Assert.Equal(4, arbeit.Payslips.Count);
        Assert.Equal(1, arbeit.WithoutDocumentCount);
        Assert.Equal(1, arbeit.WithoutPaymentCount);
    }

    /// <summary>Ein laufendes und ein beendetes Verhältnis — sonst bliebe Regel (b) ungeprüft.</summary>
    [Fact]
    public async Task Der_Seed_enthaelt_ein_laufendes_und_ein_beendetes_Verhaeltnis()
    {
        await SeedAsync();

        var kopf = (await Service().GetAsync()).Head;

        Assert.Equal(1, kopf.ActiveCount);
        Assert.Equal(2, kopf.TotalCount);
        Assert.Equal("EWV Kontrollsysteme", kopf.Employer);
    }

    /// <summary>
    /// Die Jahreszahl des Bestands ist dieselbe wie die des Bereichs.
    /// </summary>
    /// <remarks>
    /// Genau hier gingen im Prototyp 49.440 € verloren: die Bestandsklasse summierte beide
    /// Verhältnisse, der Bereich nur das laufende.
    /// </remarks>
    [Fact]
    public async Task Bestand_und_Bereich_nennen_dieselbe_Jahreszahl()
    {
        await SeedAsync();

        var bereich = (await Service().GetAsync()).Head.YearlyGross;

        var uhr = TestDatabase.ClockAt(2026, 8, 28);

        using var context = database.Context();

        var dokumente = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), uhr,
            NullLogger<DocumentService>.Instance);

        var bestand = new HoldingsService(
            context,
            new DashboardService(
                context,
                new AccountService(context),
                TestDatabase.Portfolio(context),
                new LoanService(context),
                new BudgetService(context, uhr),
                uhr,
                new ParticipationService(context, TestDatabase.SignedIn(null))),
            new VehicleService(context, dokumente, uhr),
            TestDatabase.Portfolio(context),
            uhr);

        Assert.Equal(bereich, (await bestand.GetAsync(HoldingClass.Work)).Head.Value);
    }

    private async Task SeedAsync()
    {
        using var context = database.Context();
        await SeedData.EnsureSeededAsync(
            context, new PasswordHasher<User>(), TestDatabase.PathService(root));
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
