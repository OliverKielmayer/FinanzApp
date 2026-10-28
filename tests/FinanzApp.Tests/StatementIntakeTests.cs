using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Kontoauszug aus dem überwachten Ordner — abgelegt ja, gebucht nein.
/// </summary>
/// <remarks>
/// <para>Vorher lief eine camt-Datei in den PDF-Leser, fand keine Textebene und landete als
/// unbekannter Zettel in „Sonstiges“, wo einer zwei Monate lag. Der Import konnte sie lesen —
/// er stand nur woanders und kannte keine abgelegte Datei.</para>
/// <para>Die Schwelle bleibt dieselbe wie beim Beleg: aus den Sätzen werden Buchungen, und daran
/// hängen Kategorie, Zielkonto und die Frage, ob ein Satz schon im Bestand steht. Das entscheidet
/// ein Mensch an der Vorschau und kein Dienst.</para>
/// </remarks>
public sealed class StatementIntakeTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 27);
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Ein_Kontoauszug_wird_erkannt_und_abgelegt()
    {
        var ergebnis = await TakeInAsync();

        Assert.Equal(StatementKind.Key, ergebnis.KindKey);
        Assert.Equal(StatementKind.Label, ergebnis.KindLabel);
        Assert.Equal(DocumentArea.Finance, ergebnis.Area);
        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.NotNull(ergebnis.InboxId);
    }

    /// <summary>
    /// Der Ablagename nennt Konto und Zeitraum, nicht den Tag der Einlieferung.
    /// </summary>
    /// <remarks>
    /// Ein nachgereichter Auszug vom Juli käme sonst unter dem Namen des Augusts an. Vom Konto
    /// stehen nur die letzten vier Stellen darin — sie unterscheiden die Konten eines Haushalts,
    /// und mehr braucht ein Dateiname nicht; die ganze IBAN stünde sonst in jedem Ordnerlisting.
    /// </remarks>
    [Fact]
    public async Task Der_Ablagename_nennt_Konto_und_Zeitraum()
    {
        var ergebnis = await TakeInAsync();

        Assert.EndsWith("Kontoauszug_9102_2026-08-24_2026-08-26.xml", ergebnis.RelativePath);
        Assert.Contains("/2026/", ergebnis.RelativePath);
        Assert.DoesNotContain("DE44672500200034889102", ergebnis.RelativePath);
    }

    /// <summary>
    /// Die Einlieferung bucht nichts.
    /// </summary>
    /// <remarks>
    /// Die Regel des ganzen Hauses, hier für den Auszug: ein Dienst, dem niemand zusieht,
    /// verändert keine Zahl. Ließe er die Sätze durch, stünden Buchungen im Konto, die nie
    /// jemand gesehen hat — mit geratenen Kategorien und ohne Dublettenprüfung.
    /// </remarks>
    [Fact]
    public async Task Die_Einlieferung_bucht_nichts()
    {
        await TakeInAsync();

        using var context = database.Context();
        Assert.Empty(context.Transactions.ToList());
    }

    /// <summary>
    /// Der Eingang führt in den Import, und der Schalter heißt danach.
    /// </summary>
    /// <remarks>
    /// „Werte übernehmen“ über einem Kontoauszug wäre falsch — aus ihm werden Buchungen, keine
    /// Werte. Welcher Schirm folgt und wie der Schalter heißt, weiß der Server aus der erkannten
    /// Art; die Oberfläche soll es nicht aus einem Kennzeichen zusammenreimen.
    /// </remarks>
    [Fact]
    public async Task Der_Eingang_fuehrt_in_den_Import()
    {
        var ergebnis = await TakeInAsync();

        using var context = database.Context();
        var eingang = await Inbox(context).GetAsync();
        var zeile = Assert.Single(eingang.Items);

        Assert.Equal($"/import/{ergebnis.DocumentId}", zeile.ValuesHref);
        Assert.Equal("Buchungen einlesen", zeile.ValuesLabel);
        Assert.False(zeile.Recognised);
    }

    /// <summary>
    /// Die abgelegte Datei lässt sich wieder lesen — darauf hängt der ganze Weg.
    /// </summary>
    /// <remarks>
    /// Die Vorschau der Einlieferung aufzuheben ginge nicht: sie liegt im Arbeitsspeicher und
    /// läuft ab, und ein Entwurf, der auf den Nutzer wartet, überlebte die Nacht nicht. Gelesen
    /// wird darum die Datei, die seit der Einlieferung unverändert daliegt.
    /// </remarks>
    [Fact]
    public async Task Der_abgelegte_Auszug_laesst_sich_wieder_lesen()
    {
        var ergebnis = await TakeInAsync();

        using var context = database.Context();
        var vorschau = await Imports(context).ReadFiledAsync(ergebnis.DocumentId);

        // Sieben Sätze aus sechs Einträgen: einer der Einträge ist eine Sammelbuchung und
        // trägt zwei Einzelumsätze. Der Leser zerlegt sie, und das soll der Weg über die
        // abgelegte Datei genauso tun wie der über die hochgeladene.
        Assert.Equal(7, vorschau.RecordCount);
        Assert.EndsWith("Kontoauszug_9102_2026-08-24_2026-08-26.xml", vorschau.FileName);
    }

    [Fact]
    public async Task Ein_Auszug_ohne_Beleg_laesst_sich_nicht_lesen()
    {
        using var context = database.Context();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => Imports(context).ReadFiledAsync(4711));
    }

    /// <summary>
    /// Eine XML-Datei, die kein Auszug ist, wird nicht dafür gehalten.
    /// </summary>
    /// <remarks>
    /// Die Endung entscheidet nicht. „.xml“ tragen auch ein Export aus dem Steuerprogramm und
    /// eine Rechnung im ZUGFeRD-Format; als Kontoauszug abgelegt stünden sie unter Finanzen und
    /// böten einen Import an, der nichts findet. Sie fällt an die gewöhnliche Analyse zurück und
    /// wird abgelegt — verloren gehen soll nichts.
    /// </remarks>
    [Fact]
    public async Task Ein_fremdes_XML_wird_nicht_fuer_einen_Auszug_gehalten()
    {
        var ergebnis = await TakeInAsync(
            "<?xml version=\"1.0\"?><rechnung><betrag>12,00</betrag></rechnung>",
            "rechnung.xml");

        Assert.Null(ergebnis.KindKey);
        Assert.Equal(DocumentArea.Other, ergebnis.Area);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Leser ohne Textebene — so sieht eine XML-Datei für den PDF-Leser aus.</summary>
    private sealed class BlindReader : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => new()
        {
            PageCount = 0,
            Lines = [],
            TextIsInvisible = false,
            ImageCount = 0,
        };
    }

    private static string Sample()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Beispiele", "camt052-sparkasse.xml"));

    private async Task<ScanIntakeResultDto> TakeInAsync(
        string? inhalt = null, string dateiname = "C52_DE44672500200034889102.xml")
    {
        using var context = database.Context();
        using var datei = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(inhalt ?? Sample()));

        return await Intake(context).TakeInAsync(datei, dateiname, source: "Scanordner");
    }

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

    private ScanInboxService Inbox(FinanzAppDbContext context)
        => new(context, Documents(context), clock);

    private ImportService Imports(FinanzAppDbContext context)
        => new(context, clock, new CamtStatementParser(), cache,
            TestDatabase.PathService(root), TestDatabase.SignedIn(null));

    private ScanIntakeService Intake(FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);
        var documents = Documents(context);

        var scans = new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new BlindReader(), clock);

        return new ScanIntakeService(
            context, scans, documents, new ScanInboxService(context, documents, clock),
            new AssetSheetParser(), new CamtStatementParser(), clock);
    }

    public void Dispose()
    {
        cache.Dispose();
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
