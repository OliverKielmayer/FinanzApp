using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Takt eines Vertrags.
/// </summary>
/// <remarks>
/// <para>Ein Vertrag trug bisher nur einen <em>monatlichen</em> Abschlag. Eine Grundsteuer kommt
/// vierteljährlich und eine Gebäudeversicherung jährlich; wer sie eintrug, musste selbst durch
/// drei oder durch zwölf teilen, und im Feld stand danach ein Betrag, den kein Kontoauszug je
/// zeigte. Jetzt steht dort, was abgeht, und daneben der Takt.</para>
/// <para>Zwei Fallen stecken darin, und beide sind hier geprüft: Ein <b>fehlender</b> Takt darf
/// nicht als „monatlich“ gelten, und die <b>Jahreszahl</b> darf nicht aus der gerundeten
/// Monatszahl entstehen.</para>
/// </remarks>
public sealed class ContractIntervalTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 31);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private CreateFormService Service()
    {
        var context = database.Context();
        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new CreateFormService(context, clock, documents, new NoPolicyDocumentAnalyzer());
    }

    // ── Die Umrechnung ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PremiumInterval.Monthly, 150, 150)]
    [InlineData(PremiumInterval.Quarterly, 450, 150)]
    [InlineData(PremiumInterval.HalfYearly, 900, 150)]
    [InlineData(PremiumInterval.Yearly, 1800, 150)]
    public void Jeder_Takt_rechnet_auf_denselben_Monat(
        PremiumInterval takt, decimal betrag, decimal monatlich)
        => Assert.Equal(monatlich, PaymentIntervals.PerMonth(betrag, takt));

    /// <summary>
    /// Das Jahr kommt aus dem Betrag je Zahlung, nicht aus der gerundeten Monatszahl.
    /// </summary>
    /// <remarks>
    /// Eine Jahresprämie von 1.000 € ergibt monatlich 83,33 €, und zwölf davon sind 999,96 €.
    /// Vier Cent, die nirgends gezahlt wurden — und in einer Hochrechnung über ein Dutzend
    /// Verträge wird daraus ein Betrag, den niemand erklären kann.
    /// </remarks>
    [Fact]
    public void Die_Jahreszahl_entsteht_nicht_aus_der_gerundeten_Monatszahl()
    {
        Assert.Equal(83.33m, PaymentIntervals.PerMonth(1000m, PremiumInterval.Yearly));
        Assert.Equal(1000m, PaymentIntervals.PerYear(1000m, PremiumInterval.Yearly));

        // Der naive Weg, zum Vergleich.
        Assert.Equal(999.96m, PaymentIntervals.PerMonth(1000m, PremiumInterval.Yearly) * 12m);
    }

    // ── Am Vertrag ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_vierteljaehrlicher_Vertrag_wird_mit_einem_Drittel_gefuehrt()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Contract, Werte(
            amount: "450,00", rhythm: nameof(PremiumInterval.Quarterly)));

        Assert.True(angelegt.Ok, angelegt.Message);

        using var check = database.Context();
        var vertrag = Assert.Single(check.Contracts);

        Assert.Equal(450m, vertrag.Amount);
        Assert.Equal(PremiumInterval.Quarterly, vertrag.PaymentInterval);
        Assert.Equal(150m, vertrag.MonthlyAmount);
        Assert.Equal(1_800m, vertrag.YearAmount);
    }

    /// <summary>Ohne Angabe bleibt es beim monatlichen Takt — wie bisher.</summary>
    [Fact]
    public async Task Ohne_Angabe_bleibt_es_monatlich()
    {
        var werte = Werte(amount: "120,00", rhythm: null);
        werte.Remove("rhythm");

        var angelegt = await Service().CreateAsync(CreateObjectType.Contract, werte);

        Assert.True(angelegt.Ok, angelegt.Message);

        using var check = database.Context();
        var vertrag = Assert.Single(check.Contracts);
        Assert.Equal(PremiumInterval.Monthly, vertrag.PaymentInterval);
        Assert.Equal(120m, vertrag.MonthlyAmount);
    }

    /// <summary>
    /// Ein gepflegter Takt überlebt das bloße Öffnen und Speichern.
    /// </summary>
    /// <remarks>
    /// Derselbe Fehler, der schon einmal einen gepflegten Namen gekostet hat: Ein Formular, das
    /// das Feld nicht mitschickt, machte aus einer Jahresrechnung stillschweigend eine
    /// Monatsrechnung — und zwölffachte sie in jeder Hochrechnung.
    /// </remarks>
    [Fact]
    public async Task Ein_gepflegter_Takt_ueberlebt_Oeffnen_und_Speichern()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Contract, Werte(
            amount: "1.200,00", rhythm: nameof(PremiumInterval.Yearly)));

        var maske = await Service().GetFormAsync(CreateObjectType.Contract, angelegt.Id);
        Assert.Equal("1200,00", maske!.Values["amount"]);
        Assert.Equal(nameof(PremiumInterval.Yearly), maske.Values["rhythm"]);

        // Einmal unverändert speichern.
        await Service().UpdateAsync(
            CreateObjectType.Contract, angelegt.Id!.Value, new Dictionary<string, string?>(maske.Values));

        using var check = database.Context();
        var vertrag = Assert.Single(check.Contracts);
        Assert.Equal(PremiumInterval.Yearly, vertrag.PaymentInterval);
        Assert.Equal(1_200m, vertrag.Amount);

        // Und ohne das Feld bleibt er ebenfalls stehen.
        var ohne = new Dictionary<string, string?>(maske.Values);
        ohne.Remove("rhythm");
        await Service().UpdateAsync(CreateObjectType.Contract, angelegt.Id!.Value, ohne);

        using var zweiter = database.Context();
        Assert.Equal(PremiumInterval.Yearly, zweiter.Contracts.Single().PaymentInterval);
    }

    // ── Im Bericht ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Fixkostenbericht vergleicht in Monaten — auch einen Jahresvertrag.
    /// </summary>
    [Fact]
    public async Task Der_Fixkostenbericht_fuehrt_jeden_Vertrag_im_Monatsmass()
    {
        await Service().CreateAsync(CreateObjectType.Contract, Werte(
            name: "Wasser", amount: "600,00", rhythm: nameof(PremiumInterval.HalfYearly)));

        var bericht = await database.Reports(clock).GetFixedCostsAsync(new FixedCostsRequest());

        var zeile = Assert.Single(bericht.Rows, z => z.Name == "Wasser");
        Assert.Equal(100m, zeile.MonthlyAmount);
    }

    private static Dictionary<string, string?> Werte(
        string name = "Grundsteuer",
        string provider = "Gemeinde",
        string amount = "450,00",
        string? rhythm = null)
        => new()
        {
            ["name"] = name,
            ["provider"] = provider,
            ["amount"] = amount,
            ["rhythm"] = rhythm ?? nameof(PremiumInterval.Monthly),
        };

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
