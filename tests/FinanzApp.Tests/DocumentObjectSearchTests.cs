using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Objektsuche beim Zuordnen eines Belegs.
/// </summary>
/// <remarks>
/// <para>Das Suchfeld im Prüfplatz beantwortet die Frage „wozu gehört dieser Zettel“. Was es
/// nicht findet, gibt es für den Benutzer nicht — ein Beleg ohne Treffer bleibt im Eingang
/// liegen, obwohl sein Objekt längst in der Anwendung steht.</para>
/// <para>Genau so war es bei Konten: Verknüpfen und Beschriften konnten sie längst, gefunden
/// wurden sie nicht. Ein Bankschreiben war damit nicht zuzuordnen.</para>
/// </remarks>
public sealed class DocumentObjectSearchTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private void Konto(string name, string bank, string? iban = null)
    {
        using var context = database.Context();

        context.Accounts.Add(new Account
        {
            Name = name,
            ShortName = name[..Math.Min(4, name.Length)],
            BankName = bank,
            Iban = iban,
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 1),
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task Ein_Konto_steht_als_Objekt_zur_Auswahl()
    {
        Konto("Gemeinschaftskonto", "Musterbank");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("Gemeinschaft");

        var konto = Assert.Single(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
        Assert.Equal("Gemeinschaftskonto", konto.Label);
        Assert.Equal("Konto", konto.TargetLabel);
    }

    /// <summary>
    /// Gefunden wird auch über die Bank und die IBAN.
    /// </summary>
    /// <remarks>
    /// Wer ein Bankschreiben in der Hand hält, liest darauf den Namen der Bank und eine
    /// Kontonummer — nicht den Namen, den das Konto in dieser Anwendung trägt.
    /// </remarks>
    [Theory]
    [InlineData("Musterbank")]
    [InlineData("DE02 1234")]
    public async Task Auch_Bank_und_IBAN_fuehren_zum_Konto(string suche)
    {
        Konto("Girokonto", "Musterbank", "DE02 1234 5678 9012 3456 78");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync(suche);

        Assert.Contains(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
    }

    /// <summary>Was nicht passt, steht auch nicht da.</summary>
    [Fact]
    public async Task Ohne_Treffer_bleibt_die_Liste_leer()
    {
        Konto("Girokonto", "Musterbank");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("Zahnarzt");

        Assert.DoesNotContain(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
    }

    private int Fahrzeug(string name, string kennzeichen, string? nutzung = null, int? police = null)
    {
        using var context = database.Context();

        var wagen = new Vehicle
        {
            Name = name,
            Plate = kennzeichen,
            Usage = nutzung,
            PolicyId = police,
            FirstRegistration = new DateOnly(2024, 3, 1),
        };

        context.Vehicles.Add(wagen);
        context.SaveChanges();

        return wagen.Id;
    }

    /// <summary>
    /// Ein Fahrzeug steht als Objekt zur Auswahl.
    /// </summary>
    /// <remarks>
    /// Dieselbe Lücke wie beim Konto, nur länger unbemerkt: Die Suchzeile nennt das Fahrzeug
    /// ausdrücklich, gesucht wurde es nicht.
    /// </remarks>
    [Fact]
    public async Task Ein_Fahrzeug_steht_als_Objekt_zur_Auswahl()
    {
        Fahrzeug("Opel Astra", "HD-OA 17", "Erstwagen");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("Astra");

        var wagen = Assert.Single(treffer.Objects, o => o.TargetType == LinkTargetType.Vehicle);
        Assert.Equal("Opel Astra · HD-OA 17", wagen.Label);
        Assert.Equal("Fahrzeug", wagen.TargetLabel);
        Assert.Contains("Erstwagen", wagen.Subtitle);
    }

    /// <summary>
    /// Auch das Kennzeichen führt zum Fahrzeug.
    /// </summary>
    /// <remarks>
    /// Der eigentliche Fall: Auf einer Werkstattrechnung steht das Kennzeichen, nicht der
    /// Name, den das Fahrzeug in dieser Anwendung trägt. Deshalb steht es in der Beschriftung
    /// und nicht nur im Untertitel — gesucht wird über beides.
    /// </remarks>
    [Theory]
    [InlineData("HD-OA")]
    [InlineData("hd-oa 17")]
    public async Task Auch_das_Kennzeichen_fuehrt_zum_Fahrzeug(string suche)
    {
        Fahrzeug("Opel Astra", "HD-OA 17");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync(suche);

        Assert.Contains(treffer.Objects, o => o.TargetType == LinkTargetType.Vehicle);
    }

    /// <summary>
    /// Die verknüpfte Versicherung steht im Untertitel.
    /// </summary>
    /// <remarks>
    /// Sie wird über die geladene Police gebildet. Ohne <c>Include</c> wäre sie an jedem
    /// Fahrzeug <c>null</c> und der Hinweis fehlte auch dort, wo ein Vertrag hängt — ein
    /// stiller Fehler, den keine Ausnahme meldet.
    /// </remarks>
    [Fact]
    public async Task Die_verknuepfte_Versicherung_steht_am_Treffer()
    {
        int policeId;
        using (var context = database.Context())
        {
            var police = new Policy
            {
                Name = "Kfz Musterschutz",
                Kind = PolicyKind.Vehicle,
                Provider = "Musterschutz",
                Premium = 42m,
                PremiumInterval = PremiumInterval.Monthly,
            };

            context.Policies.Add(police);
            context.SaveChanges();
            policeId = police.Id;
        }

        Fahrzeug("Fiat Panda", "HD-FP 8", police: policeId);

        using var check = database.Context();
        var treffer = await Documents(check).SearchAsync("Panda");

        var wagen = Assert.Single(treffer.Objects, o => o.TargetType == LinkTargetType.Vehicle);
        Assert.Contains("Versicherung verknüpft", wagen.Subtitle);
    }

    /// <summary>
    /// Depot, Darlehen und offener Posten stehen ebenso zur Auswahl.
    /// </summary>
    /// <remarks>
    /// Dieselbe Lücke, nur an drei weiteren Stellen: Ein Depotauszug, ein Darlehensschreiben
    /// und eine Rechnungsmahnung waren nicht zuzuordnen, obwohl ihr Objekt in der Anwendung
    /// stand. Geprüft wird je Art der Treffer <em>und</em> sein Untertitel — ohne den sagt die
    /// Zeile nur, dass es etwas gibt, aber nicht welches.
    /// </remarks>
    [Fact]
    public async Task Auch_Depot_Darlehen_und_offener_Posten_stehen_zur_Auswahl()
    {
        using (var context = database.Context())
        {
            var depot = new Depot { Name = "Musterbroker Depot", Broker = "Musterbroker", Number = "7781" };
            context.Depots.Add(depot);
            context.SaveChanges();

            context.PortfolioPositions.Add(new PortfolioPosition
            {
                DepotId = depot.Id, Name = "Sammelfonds Welt", Isin = "DE0000000001",
                Quantity = 10m, Price = 100m, PriceAsOf = new DateTime(2026, 11, 1),
            });

            context.Loans.Add(new Loan
            {
                Name = "Hausdarlehen", Lender = "Musterbank", RemainingDebt = 120_000m,
                InterestRatePercent = 3.4m, Installment = 820m,
                NextPaymentDate = new DateOnly(2026, 12, 1),
            });

            context.Invoices.Add(new Invoice
            {
                Subject = "Dachrinne erneuern", Payee = "Spenglerei Muster", Number = "R-4711",
                IssuedOn = new DateOnly(2026, 10, 20), DueOn = new DateOnly(2026, 11, 20),
                Amount = 940m, Status = InvoiceStatus.Open,
            });

            context.SaveChanges();
        }

        using var check = database.Context();
        var dienst = Documents(check);

        var depotTreffer = Assert.Single(
            (await dienst.SearchAsync("Musterbroker")).Objects,
            o => o.TargetType == LinkTargetType.Portfolio);
        Assert.Equal("Musterbroker Depot", depotTreffer.Label);
        Assert.Contains("1 Position", depotTreffer.Subtitle);

        var darlehen = Assert.Single(
            (await dienst.SearchAsync("Hausdarlehen")).Objects,
            o => o.TargetType == LinkTargetType.Loan);
        Assert.Contains("Musterbank", darlehen.Subtitle);

        // Über den Empfänger gefunden: Auf der Mahnung steht die Spenglerei, nicht der
        // Betreff, den der offene Posten in dieser Anwendung trägt.
        var posten = Assert.Single(
            (await dienst.SearchAsync("Spenglerei")).Objects,
            o => o.TargetType == LinkTargetType.Invoice);
        Assert.Equal("Dachrinne erneuern", posten.Label);
        Assert.Contains("Nr. R-4711", posten.Subtitle);
        Assert.Contains("fällig 20.11.2026", posten.Subtitle);
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
