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

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
