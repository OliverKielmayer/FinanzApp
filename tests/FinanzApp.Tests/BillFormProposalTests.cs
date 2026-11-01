using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Was ein Beleg dem Anlegeformular vorschlägt.
/// </summary>
/// <remarks>
/// <para>Der Lieferschein liegt am Prüfplatz daneben, und das Objekt dazu gibt es noch nicht.
/// Was darauf steht — Händler, Kaufdatum, Preis — stand auch schon im gelesenen Text; nur
/// führte von dort kein Weg ins Formular, und es wurde abgetippt.</para>
/// <para>Die Belege hier sind erfunden, in der Gestalt echter.</para>
/// </remarks>
public sealed class BillFormProposalTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private const string Rechnung = """
        Musterelektro Handels GmbH Süd-West & Co.KG, Musterstr. 22, 11111
        Rechnung Nr.: VR5000012345 Unsere USt-IdNr. DE123456789
        Belegdatum 14. Juli 2026
        Lieferdatum 03. August 2026
        pos. Lagerort Beschreibung Artikelnr. VK-Preis Betrag MwSt.-Kz.
        04-01 1 Einbaukühlschrank 77700011122 1.007,00 1.007,00 19
        2 04-01 Lieferpaket 197,00 197,00 19
        Endsumme ohne MwSt 1.011,76 EUR
        MwSt.-Betrag 192,24 EUR
        Endsumme inkl. MwSt 1.204,00 EUR
        """;

    private CreateFormService Forms(FinanzAppDbContext context)
        => new(context, clock,
            new DocumentService(context, TestDatabase.PathService(root),
                new ObjectLabelService(context), clock, new PdfPigTextReader(),
                NullLogger<DocumentService>.Instance),
            new ObjectLabelService(context), new NoPolicyDocumentAnalyzer());

    /// <summary>Ein abgelegter Beleg mit gelesenem Text, wie ihn die Einlieferung hinterlässt.</summary>
    private int Beleg(FinanzAppDbContext context, string text)
    {
        var dokument = new Document
        {
            Title = "Scannen",
            RelativePath = "Sonstiges/Scannen.pdf",
            FileName = "Scannen.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Other,
            ContentText = text,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private static string? Vorgabe(CreateFormDto form, string key)
        => form.Fields.FirstOrDefault(f => f.Key == key)?.DefaultValue;

    // ── Die Zuordnung ──────────────────────────────────────────────────────────────────────

    /// <summary>Händler, Kaufdatum und Preis stehen vorbefüllt im Formular.</summary>
    [Fact]
    public async Task Eine_Rechnung_fuellt_das_Anschaffungsformular_vor()
    {
        using var context = database.Context();
        var beleg = Beleg(context, Rechnung);

        var form = await Forms(context).GetFormAsync(CreateObjectType.Purchase, null, beleg);

        Assert.NotNull(form);
        Assert.Equal("Musterelektro Handels GmbH Süd-West & Co.KG", Vorgabe(form, "vendor"));
        Assert.Equal("2026-07-14", Vorgabe(form, "purchase"));
        Assert.Equal("1204,00", Vorgabe(form, "price"));
        Assert.Equal("Beleg-Nr. VR5000012345", Vorgabe(form, "note"));
    }

    /// <summary>
    /// Die Bezeichnung bleibt leer — sie ist das eine Feld, das ein Mensch besser weiß.
    /// </summary>
    /// <remarks>
    /// Auf dem Blatt steht die Artikelzeile des Herstellers. Eine Anschaffung, die
    /// „77700011122“ heißt, findet niemand wieder, und ein falscher Name sieht aus wie eine
    /// Auskunft.
    /// </remarks>
    [Fact]
    public async Task Die_Bezeichnung_wird_nicht_geraten()
    {
        using var context = database.Context();
        var beleg = Beleg(context, Rechnung);

        var form = await Forms(context).GetFormAsync(CreateObjectType.Purchase, null, beleg);

        Assert.True(string.IsNullOrEmpty(Vorgabe(form!, "name")));
    }

    /// <summary>Der vorbefüllte Preis ist der Brutto- und nicht der Nettobetrag.</summary>
    /// <remarks>
    /// Beide heißen auf dem Blatt „Endsumme“, und der Nettobetrag steht zuerst. Ein Preis, der
    /// um die Umsatzsteuer zu niedrig ist, fällt niemandem auf.
    /// </remarks>
    [Fact]
    public async Task Der_vorbefuellte_Preis_ist_der_Bruttobetrag()
    {
        using var context = database.Context();
        var beleg = Beleg(context, Rechnung);

        var form = await Forms(context).GetFormAsync(CreateObjectType.Purchase, null, beleg);

        Assert.Equal("1204,00", Vorgabe(form!, "price"));
        Assert.NotEqual("1011,76", Vorgabe(form!, "price"));
    }

    // ── Und die Grenzen ────────────────────────────────────────────────────────────────────

    /// <summary>Ohne lesbaren Text bleibt das Formular, wie es war.</summary>
    [Fact]
    public async Task Ein_Beleg_ohne_Text_schlaegt_nichts_vor()
    {
        using var context = database.Context();
        var beleg = Beleg(context, string.Empty);

        var form = await Forms(context).GetFormAsync(CreateObjectType.Purchase, null, beleg);

        Assert.True(string.IsNullOrEmpty(Vorgabe(form!, "vendor")));
        Assert.True(string.IsNullOrEmpty(Vorgabe(form!, "price")));
    }

    /// <summary>
    /// Ein Beleg, den es nicht gibt, ändert nichts — und bricht nichts ab.
    /// </summary>
    /// <remarks>
    /// Das Formular ist die Hauptsache, der Vorschlag die Zugabe. Wäre es umgekehrt, bliebe der
    /// Prüfplatz stehen, weil ein Beleg nicht mehr auffindbar war.
    /// </remarks>
    [Fact]
    public async Task Ein_unbekannter_Beleg_laesst_das_Formular_stehen()
    {
        using var context = database.Context();

        var form = await Forms(context).GetFormAsync(CreateObjectType.Purchase, null, 987654);

        Assert.NotNull(form);
        Assert.NotEmpty(form.Fields);
        Assert.True(string.IsNullOrEmpty(Vorgabe(form, "vendor")));
    }

    /// <summary>
    /// Andere Objekte werden nicht vorbefüllt.
    /// </summary>
    /// <remarks>
    /// Eine Immobilie entsteht aus einem Kaufvertrag, nicht aus einer Rechnung. „Musterelektro“
    /// als Vorgabe im Immobilienformular wäre ein Vorschlag, der nur aussieht wie einer.
    /// </remarks>
    [Theory]
    [InlineData(CreateObjectType.Property)]
    [InlineData(CreateObjectType.Loan)]
    [InlineData(CreateObjectType.Protection)]
    public async Task Nur_die_Anschaffung_wird_vorbefuellt(CreateObjectType typ)
    {
        using var context = database.Context();
        var beleg = Beleg(context, Rechnung);

        var form = await Forms(context).GetFormAsync(typ, null, beleg);

        Assert.NotNull(form);
        Assert.DoesNotContain(
            form.Fields,
            f => f.DefaultValue is { Length: > 0 } wert
                 && wert.Contains("Musterelektro", StringComparison.Ordinal));
    }

    /// <summary>
    /// Beim Bearbeiten gilt das Objekt und nicht der Beleg.
    /// </summary>
    /// <remarks>
    /// Wer ein bestehendes Objekt öffnet, will seine Angaben sehen. Ein Beleg, der sie
    /// überschriebe, machte aus dem Bearbeiten ein unbemerktes Ersetzen.
    /// </remarks>
    [Fact]
    public async Task Beim_Bearbeiten_wird_nichts_vorbefuellt()
    {
        using var context = database.Context();
        var beleg = Beleg(context, Rechnung);

        var anschaffung = new Purchase
        {
            Name = "Waschmaschine",
            PurchaseDate = new DateOnly(2024, 3, 1),
            Vendor = "Musterbaumarkt",
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Purchases.Add(anschaffung);
        context.SaveChanges();

        var form = await Forms(context).GetFormAsync(
            CreateObjectType.Purchase, anschaffung.Id, beleg);

        Assert.NotNull(form);
        Assert.Equal("Musterbaumarkt", form.Values["vendor"]);
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
