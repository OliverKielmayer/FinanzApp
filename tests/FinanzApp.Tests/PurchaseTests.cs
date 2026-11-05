using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Anschaffungen — der Platz, der zwischen den Objekten fehlte.
/// </summary>
/// <remarks>
/// <para>Ein Lieferschein für eine Einbauküche oder ein medizinisches Gerät gehört an keinen
/// Vertrag, an kein Konto und an keine Immobilie. Ohne diesen Platz landete er im Nirgendwo
/// oder, schlimmer, an irgendetwas Naheliegendem — und eine falsche Verknüpfung sieht aus wie
/// eine Auskunft.</para>
/// <para>Der wichtigste Fall hier ist die Garantie: Was nicht hinterlegt ist, gilt nicht als
/// abgelaufen. Nach dieser Unterscheidung richtet sich jemand, der überlegt, ob eine
/// Reklamation noch geht.</para>
/// </remarks>
public sealed class PurchaseTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private PurchaseService Purchases(FinanzAppDbContext context)
        => new(context, Documents(context), clock);

    private CreateFormService Forms(FinanzAppDbContext context)
        => new(context, clock, Documents(context), new ObjectLabelService(context),
            new NoPolicyDocumentAnalyzer());

    private static int Anlegen(
        FinanzAppDbContext context, string name,
        DateOnly gekauft, DateOnly? garantieBis = null, decimal? preis = null)
    {
        var purchase = new Purchase
        {
            Name = name,
            PurchaseDate = gekauft,
            WarrantyUntil = garantieBis,
            Price = preis,
            CreatedAt = new DateTime(2026, 11, 1),
            UpdatedAt = new DateTime(2026, 11, 1),
        };

        context.Purchases.Add(purchase);
        context.SaveChanges();
        return purchase.Id;
    }

    // ── Anlegen ────────────────────────────────────────────────────────────────────────────

    /// <summary>Über denselben Anlegeweg wie jedes andere Objekt — und mit Zielart.</summary>
    [Fact]
    public async Task Eine_Anschaffung_entsteht_als_Verknuepfungsziel()
    {
        using var context = database.Context();

        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Waschmaschine",
                ["purchase"] = "2026-10-20",
                ["vendor"] = "Musterelektro",
                ["price"] = "749,00",
                ["warranty"] = "2028-10-20",
            });

        Assert.True(ergebnis.Ok);
        Assert.Equal(LinkTargetType.Purchase, ergebnis.LinkTarget);
        Assert.Equal("Waschmaschine", ergebnis.Label);

        var gespeichert = context.Purchases.Single();
        Assert.Equal(749.00m, gespeichert.Price);
        Assert.Equal(new DateOnly(2028, 10, 20), gespeichert.WarrantyUntil);
    }

    /// <summary>Ohne Kaufdatum wird nichts angelegt — das Feld nennt sich beim Namen.</summary>
    [Fact]
    public async Task Ohne_Kaufdatum_wird_nichts_angelegt()
    {
        using var context = database.Context();

        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Purchase,
            new Dictionary<string, string?> { ["name"] = "Waschmaschine" });

        Assert.False(ergebnis.Ok);
        Assert.Equal("purchase", ergebnis.FieldKey);
        Assert.Empty(context.Purchases);
    }

    // ── Die Garantie ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine fehlende Frist ist keine abgelaufene.
    /// </summary>
    /// <remarks>
    /// <c>null</c> statt <c>false</c>: Wer eine Reklamation erwägt, liest „abgelaufen“ als
    /// Auskunft und lässt es sein. Dass niemand die Frist eingetragen hat, ist etwas anderes.
    /// </remarks>
    [Fact]
    public async Task Ohne_Frist_ist_die_Garantie_weder_laufend_noch_abgelaufen()
    {
        using var context = database.Context();
        Anlegen(context, "Sofa", new DateOnly(2020, 1, 1));

        var liste = await Purchases(context).GetListAsync();

        Assert.Null(Assert.Single(liste).WarrantyRunning);
    }

    /// <summary>Am letzten Tag läuft sie noch — der Stichtag zählt mit.</summary>
    [Theory]
    [InlineData("2026-11-01", true)]
    [InlineData("2026-11-02", true)]
    [InlineData("2026-10-31", false)]
    public async Task Der_Stichtag_zaehlt_zur_Garantie(string bis, bool laeuft)
    {
        using var context = database.Context();
        Anlegen(context, "Bohrmaschine", new DateOnly(2024, 11, 1), DateOnly.Parse(bis));

        var liste = await Purchases(context).GetListAsync();

        Assert.Equal(laeuft, Assert.Single(liste).WarrantyRunning);
    }

    // ── Die Belege ─────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Lieferschein findet endlich ein Objekt — und steht an ihm.</summary>
    [Fact]
    public async Task Ein_Beleg_haengt_an_der_Anschaffung()
    {
        using var context = database.Context();
        var id = Anlegen(context, "Schlafapnoe-Gerät", new DateOnly(2026, 9, 3));

        var dokument = new Document
        {
            Title = "Lieferschein",
            RelativePath = "Gesundheit/Unbekannt/lieferschein.pdf",
            FileName = "lieferschein.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Health,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        await Documents(context).LinkAsync(dokument.Id, LinkTargetType.Purchase, id);

        var detail = await Purchases(context).GetAsync(id);

        Assert.NotNull(detail);
        Assert.Equal("lieferschein.pdf", Assert.Single(detail.Documents).FileName);
        Assert.Equal(1, Assert.Single(await Purchases(context).GetListAsync()).DocumentCount);
    }

    /// <summary>
    /// Die Objektsuche kennt sie — sonst wäre sie am Prüfplatz nicht zu finden.
    /// </summary>
    /// <remarks>
    /// Wer einen Lieferschein in der Hand hält, tippt den Namen des Geräts. Ein Objekt, das nur
    /// über seine eigene Liste erreichbar ist, hilft beim Zuordnen nicht.
    /// </remarks>
    [Fact]
    public async Task Die_Objektsuche_findet_die_Anschaffung()
    {
        using var context = database.Context();
        Anlegen(context, "Einbauküche", new DateOnly(2026, 3, 14));

        var treffer = await Documents(context).SearchAsync("küche");

        Assert.Contains(treffer.Objects, o =>
            o.TargetType == LinkTargetType.Purchase && o.Label == "Einbauküche");
    }

    // ── Investitionen am Objekt ────────────────────────────────────────────────────────────
    //
    // Ein Bodenbelag gehört an das Haus, ein Schlafapnoe-Gerät zieht mit um. Ohne diese
    // Unterscheidung standen beide in einer flachen Liste ohne Summe, und die Frage „was hat
    // der Einzug gekostet“ war nicht zu beantworten.

    private PropertyService Properties(FinanzAppDbContext context)
        => new(context, Documents(context), clock,
            new ParticipationService(context, TestDatabase.SignedIn(null)));

    private int Immobilie(string name = "Haus Musterweg 3")
    {
        using var context = database.Context();
        var objekt = new Property { Name = name, MarketValue = 400_000m };
        context.Properties.Add(objekt);
        context.SaveChanges();
        return objekt.Id;
    }

    private void AnHaus(int anschaffung, int haus)
    {
        using var context = database.Context();
        var p = context.Purchases.Single(x => x.Id == anschaffung);
        p.PropertyId = haus;
        context.SaveChanges();
    }

    [Fact]
    public async Task Die_zugeordneten_Anschaffungen_stehen_am_Haus()
    {
        var haus = Immobilie();

        using (var context = database.Context())
        {
            AnHaus(Anlegen(context, "Bodenbelag", new DateOnly(2026, 5, 14), preis: 392.40m), haus);
            AnHaus(Anlegen(context, "Einbaukühlschrank", new DateOnly(2026, 5, 5), preis: 820m), haus);

            // Zieht mit um, gehört an kein Objekt.
            Anlegen(context, "Fernseher", new DateOnly(2024, 1, 1), preis: 540m);
        }

        using var check = database.Context();
        var objekt = await Properties(check).GetAsync(haus);

        Assert.NotNull(objekt);
        Assert.Equal(1_212.40m, objekt.Investments);
        Assert.Equal(2, objekt.InvestmentCount);
    }

    /// <summary>
    /// Die Investitionen stehen neben den Kosten, nicht darin.
    /// </summary>
    /// <remarks>
    /// Eine Einbauküche in der Jahreslast stünde als Dauerkosten da und fiele im Jahr darauf
    /// ohne erkennbaren Grund wieder weg. Der Test hält beide Zahlen auseinander.
    /// </remarks>
    [Fact]
    public async Task Eine_Anschaffung_erhoeht_die_laufenden_Kosten_nicht()
    {
        var haus = Immobilie();

        using (var context = database.Context())
        {
            AnHaus(Anlegen(context, "Bodenbelag", new DateOnly(2026, 5, 14), preis: 392.40m), haus);
        }

        using var check = database.Context();
        var objekt = await Properties(check).GetAsync(haus);

        Assert.Equal(392.40m, objekt!.Investments);
        Assert.Equal(0m, objekt.CostsLastTwelveMonths);
        Assert.Empty(objekt.CostParts);
    }

    /// <summary>
    /// Ein Kauf <em>vor</em> dem Kaufdatum der Immobilie zählt mit.
    /// </summary>
    /// <remarks>
    /// Der Fall, an dem ein Stichtag gescheitert wäre: Der Einbaukühlschrank wurde neun Tage
    /// vor dem Kaufdatum des Hauses gekauft — und zwar für dieses Haus. „Seit Kauf“ hätte ihn
    /// stillschweigend herausgenommen. Die Zuordnung entscheidet, nicht das Datum.
    /// </remarks>
    [Fact]
    public async Task Ein_Kauf_vor_dem_Kaufdatum_der_Immobilie_zaehlt_mit()
    {
        int haus;
        using (var context = database.Context())
        {
            var objekt = new Property
            {
                Name = "Haus Musterweg 3",
                MarketValue = 400_000m,
                PurchaseDate = new DateOnly(2026, 5, 14),
            };
            context.Properties.Add(objekt);
            context.SaveChanges();
            haus = objekt.Id;

            AnHaus(Anlegen(context, "Einbaukühlschrank", new DateOnly(2026, 5, 5), preis: 820m), haus);
        }

        using var check = database.Context();
        Assert.Equal(820m, (await Properties(check).GetAsync(haus))!.Investments);
    }

    /// <summary>
    /// Ohne Preis zählt die Anschaffung in der Anzahl, nicht in der Summe.
    /// </summary>
    /// <remarks>
    /// Ein fehlender Preis ist keine Null. Ihn als null zu summieren wäre unauffällig falsch —
    /// die Summe sähe vollständig aus und wäre es nicht. Die Anzahl verrät den Unterschied.
    /// </remarks>
    [Fact]
    public async Task Ohne_Preis_zaehlt_die_Anschaffung_nur_in_der_Anzahl()
    {
        var haus = Immobilie();

        using (var context = database.Context())
        {
            AnHaus(Anlegen(context, "Bodenbelag", new DateOnly(2026, 5, 14), preis: 392.40m), haus);
            AnHaus(Anlegen(context, "Wandverkleidung", new DateOnly(2026, 6, 2)), haus);
        }

        using var check = database.Context();
        var objekt = await Properties(check).GetAsync(haus);

        Assert.Equal(392.40m, objekt!.Investments);
        Assert.Equal(2, objekt.InvestmentCount);
    }

    /// <summary>Das Formular legt die Zuordnung an und gibt sie beim Bearbeiten zurück.</summary>
    [Fact]
    public async Task Das_Formular_traegt_die_Immobilie()
    {
        var haus = Immobilie();

        using var context = database.Context();
        var angelegt = await Forms(context).CreateAsync(CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Bodenbelag",
                ["purchase"] = "2026-05-14",
                ["price"] = "392,40",
                ["property"] = haus.ToString(),
            });

        Assert.True(angelegt.Ok, angelegt.Message);

        var maske = await Forms(context).GetFormAsync(CreateObjectType.Purchase, angelegt.Id);
        Assert.Equal(haus.ToString(), maske!.Values!["property"]);
        Assert.Contains(maske.Fields, f => f.Key == "property");

        using var check = database.Context();
        Assert.Equal(haus, check.Purchases.Single().PropertyId);
    }

    /// <summary>
    /// Die gelöschte Immobilie nimmt die Anschaffung nicht mit.
    /// </summary>
    /// <remarks>
    /// Ein gekaufter Gegenstand ist eine Tatsache, das Objekt war nur seine Schublade —
    /// dasselbe Verhältnis wie zwischen Buchung und Konto. Und die Löschfolge zählt ihn
    /// vorher, statt ihn zu behaupten.
    /// </remarks>
    [Fact]
    public async Task Die_geloeschte_Immobilie_laesst_die_Anschaffung_stehen()
    {
        var haus = Immobilie();

        using (var context = database.Context())
        {
            AnHaus(Anlegen(context, "Bodenbelag", new DateOnly(2026, 5, 14), preis: 392.40m), haus);
        }

        using var context2 = database.Context();
        var folge = (await Forms(context2).GetFormAsync(CreateObjectType.Property, haus))!
            .DeleteImpact!.Consequence;

        Assert.Contains("1 Anschaffung bleibt erhalten", folge);

        Assert.True((await Forms(context2).DeleteAsync(CreateObjectType.Property, haus)).Ok);

        using var check = database.Context();
        var uebrig = Assert.Single(check.Purchases);
        Assert.Equal("Bodenbelag", uebrig.Name);
        Assert.Null(uebrig.PropertyId);
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
