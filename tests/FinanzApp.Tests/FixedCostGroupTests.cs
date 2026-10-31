using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Fixkosten nach Kostenart.
/// </summary>
/// <remarks>
/// <para>Zwanzig Einzelzeilen beantworten die Frage nicht, die man an eine Fixkostenliste
/// stellt: <em>Wofür geht das Geld?</em> Vier Gruppen beantworten sie.</para>
/// <para>Die Kostenart wird gepflegt oder abgeleitet, nie geraten — und sie ist nie leer. Ein
/// Posten ohne Gruppe fiele aus jeder Summe heraus, und ein Bericht, der nicht alles zeigt, was
/// er zusammenzählt, ist keiner.</para>
/// </remarks>
public sealed class FixedCostGroupTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 31);

    private Task<FixedCostsDto> Bericht()
        => database.Reports(clock).GetFixedCostsAsync(new FixedCostsRequest());

    private void Vertrag(string name, decimal betrag, DocumentArea bereich, string? kostenart = null)
    {
        using var context = database.Context();
        context.Contracts.Add(new Contract
        {
            Name = name,
            Provider = "Anbieter",
            Amount = betrag,
            PaymentInterval = PremiumInterval.Monthly,
            Area = bereich,
            CostGroup = kostenart,
        });

        context.SaveChanges();
    }

    private void Police(string name, decimal beitrag, PolicyKind art, bool kapitalbildend,
        string? kostenart = null)
    {
        using var context = database.Context();
        context.Policies.Add(new Policy
        {
            Name = name,
            Provider = "Versicherer",
            Kind = art,
            IsCapitalForming = kapitalbildend,
            Premium = beitrag,
            PremiumInterval = PremiumInterval.Monthly,
            CostGroup = kostenart,
        });

        context.SaveChanges();
    }

    // ── Die Ableitung ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ohne gepflegte Kostenart leitet der Bericht sie ab — der Bestand ist sofort gruppiert.
    /// </summary>
    /// <remarks>
    /// Eine Erweiterung, nach der erst einmal lauter Einzelzeilen dastünden, wäre schlechter
    /// als der Zustand davor.
    /// </remarks>
    [Fact]
    public async Task Ohne_Pflege_wird_die_Kostenart_abgeleitet()
    {
        Vertrag("Strom", 102m, DocumentArea.Housing);
        Police("Kapitallebensversicherung", 100m, PolicyKind.CapitalLife, kapitalbildend: true);
        Police("Gebäudeversicherung", 50m, PolicyKind.Building, kapitalbildend: false);
        Police("Krankenversicherung", 400m, PolicyKind.Health, kapitalbildend: false);
        Police("Haftpflicht", 8m, PolicyKind.Liability, kapitalbildend: false);

        var gruppen = (await Bericht()).Groups.ToDictionary(g => g.Name, g => g.MonthlyAmount);

        Assert.Equal(400m, gruppen["Gesundheit"]);
        Assert.Equal(152m, gruppen["Wohnen"]);      // Strom und Gebäudeversicherung
        Assert.Equal(100m, gruppen["Vorsorge"]);
        Assert.Equal(8m, gruppen["Absicherung"]);
    }

    /// <summary>
    /// Ein kapitalbildender Beitrag ist Vorsorge, auch wenn er von der Absicherungsseite kommt.
    /// </summary>
    [Fact]
    public async Task Kapitalbildend_schlaegt_die_Policenart()
    {
        Police("Fondspolice", 120m, PolicyKind.TermLife, kapitalbildend: true);

        var gruppe = Assert.Single((await Bericht()).Groups);
        Assert.Equal("Vorsorge", gruppe.Name);
    }

    // ── Die Pflege ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine gepflegte Kostenart schlägt die Ableitung — darum gibt es sie.
    /// </summary>
    /// <remarks>
    /// „Streaming“ steht in keinem Bereich und in keiner Policenart. Genau solche Gruppen will
    /// man bilden, und keine Ableitung kann sie kennen.
    /// </remarks>
    [Fact]
    public async Task Eine_gepflegte_Kostenart_schlaegt_die_Ableitung()
    {
        Vertrag("Netflix", 15.99m, DocumentArea.Housing, kostenart: "Streaming");
        Vertrag("Spotify", 11.99m, DocumentArea.Housing, kostenart: "Streaming");
        Vertrag("Strom", 102m, DocumentArea.Housing);

        var bericht = await Bericht();
        var streaming = Assert.Single(bericht.Groups, g => g.Name == "Streaming");

        Assert.Equal(27.98m, streaming.MonthlyAmount);
        Assert.Equal(2, streaming.Rows.Count);

        // Der Strom bleibt bei Wohnen — dieselbe Ablage, andere Kostenart.
        Assert.Equal(102m, Assert.Single(bericht.Groups, g => g.Name == "Wohnen").MonthlyAmount);
    }

    /// <summary>Groß- und Kleinschreibung trennt keine Gruppe.</summary>
    [Fact]
    public async Task Schreibweise_trennt_keine_Gruppe()
    {
        Vertrag("Netflix", 15m, DocumentArea.Other, kostenart: "Streaming");
        Vertrag("Disney", 10m, DocumentArea.Other, kostenart: "streaming");

        var gruppe = Assert.Single((await Bericht()).Groups);
        Assert.Equal(25m, gruppe.MonthlyAmount);
        Assert.Equal(2, gruppe.Rows.Count);
    }

    // ── Die Summen ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Anteile der Gruppen ergeben zusammen hundert Prozent.
    /// </summary>
    /// <remarks>
    /// Sie beziehen sich auf die <em>gebundenen</em> Kosten und nicht auf die Monatsbasis. Gegen
    /// die gerechnet gingen sie nicht auf, und ein Kreis, dessen Stücke nicht zusammenpassen,
    /// ist eine falsche Zeichnung.
    /// </remarks>
    [Fact]
    public async Task Die_Anteile_ergeben_hundert_Prozent()
    {
        Vertrag("Strom", 150m, DocumentArea.Housing);
        Police("Vorsorge", 100m, PolicyKind.CapitalLife, kapitalbildend: true);
        Police("Haftpflicht", 50m, PolicyKind.Liability, kapitalbildend: false);

        var bericht = await Bericht();

        Assert.Equal(300m, bericht.MonthlyFixed);
        Assert.Equal(100m, bericht.Groups.Sum(g => g.SharePercent));
        Assert.Equal(300m, bericht.Groups.Sum(g => g.MonthlyAmount));
    }

    /// <summary>Die schwerste Gruppe steht oben — danach fragt, wer eine Liste öffnet.</summary>
    [Fact]
    public async Task Die_schwerste_Gruppe_steht_oben()
    {
        Vertrag("Kleinkram", 5m, DocumentArea.Other, kostenart: "Sonstiges");
        Police("Krankenversicherung", 900m, PolicyKind.Health, kapitalbildend: false);
        Vertrag("Strom", 100m, DocumentArea.Housing);

        var gruppen = (await Bericht()).Groups;

        Assert.Equal("Gesundheit", gruppen[0].Name);
        Assert.Equal("Wohnen", gruppen[1].Name);
        Assert.Equal("Sonstiges", gruppen[2].Name);
    }

    /// <summary>
    /// Jede Zeile des Berichts steht in genau einer Gruppe.
    /// </summary>
    /// <remarks>
    /// Die Probe auf das Ganze: Was oben als Summe steht, muss unten auch zu sehen sein. Ein
    /// Posten, der aus der Gruppierung fällt, zählte in die Summe und stünde nirgends.
    /// </remarks>
    [Fact]
    public async Task Keine_Zeile_faellt_aus_der_Gruppierung()
    {
        Vertrag("Strom", 102m, DocumentArea.Housing);
        Vertrag("Netflix", 15m, DocumentArea.Other, kostenart: "Streaming");
        Police("Vorsorge", 100m, PolicyKind.CapitalLife, kapitalbildend: true);

        using (var context = database.Context())
        {
            context.Loans.Add(new Loan
            {
                Name = "Immobiliendarlehen",
                Lender = "Sparkasse",
                RemainingDebt = 100_000m,
                InterestRatePercent = 3m,
                Installment = 500m,
                NextPaymentDate = new DateOnly(2026, 11, 30),
            });

            context.SaveChanges();
        }

        var bericht = await Bericht();

        Assert.Equal(bericht.Rows.Count, bericht.Groups.Sum(g => g.Rows.Count));
        Assert.Equal(bericht.MonthlyFixed, bericht.Groups.Sum(g => g.MonthlyAmount));
        Assert.All(bericht.Rows, z => Assert.False(string.IsNullOrWhiteSpace(z.Group)));

        // Ein Darlehen ist nicht kündbar und steht für sich.
        Assert.Equal(500m, Assert.Single(bericht.Groups, g => g.Name == "Finanzierung").MonthlyAmount);
    }

    public void Dispose() => database.Dispose();
}
