using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Das Datum vom Blatt und der Name, den ein Scanner nicht vergibt.
/// </summary>
/// <remarks>
/// <para>Zwei Mängel mit einer Ursache. Ein eingelieferter Beleg bekam den <em>Tag der
/// Einlieferung</em> als Dokumentdatum, obwohl das Datum auf dem Papier steht und der Leser es
/// findet. Die Dokumentliste sortiert nach Dokumentdatum — zehn Schreiben aus zehn Monaten
/// standen damit alle am selben Tag, abgelegt, aber zeitlich nicht mehr zu lesen.</para>
/// <para>Und er hieß weiter „Scannen0007“. Umbenannt wurde erst beim Zuordnen; alles, was im
/// Prüfplatz lag, trug den Gerätenamen. Dabei ist die vermutete Art schon beim Einliefern
/// bekannt, und mit dem Datum zusammen ergibt sie einen brauchbaren Namen.</para>
/// </remarks>
public sealed class DocumentDateAndNameTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 6);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private ScanIntakeService Service(PdfContent inhalt, FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);
        var documents = Documents(context);

        var scans = new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new StubReader(inhalt), clock);

        return new ScanIntakeService(
            context, scans, documents, new ScanInboxService(context, documents, clock),
            new AssetSheetParser(), new CamtStatementParser(),
            TestDatabase.NoActions(context, clock), clock);
    }

    private int eingeliefert;

    private async Task<ScanIntakeResultDto> Einliefern(PdfContent inhalt, string dateiname = "Scannen0007.pdf")
    {
        using var context = database.Context();
        var datei = $"%PDF-1.4 Beleg {++eingeliefert}";

        return await Service(inhalt, context).TakeInAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(datei)), dateiname, source: null);
    }

    private static PdfContent Inhalt(params string[] zeilen) => new()
    {
        PageCount = 1,
        Lines = [.. zeilen.Select(z => new PdfLine(1, [z]))],
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>Ein Gebührenbescheid, wie er auf dem Postweg ankommt — erfundene Angaben.</summary>
    private static PdfContent Bescheid() => Inhalt(
        "Landkreis Musterkreis Abfallwirtschaftsamt",
        "Musterstadt, den 12.03.2026",
        "Abfallgebührenbescheid 2026",
        "Festsetzung der Gebühr nach Veranlagung",
        "Gesamtbetrag 2026 63,40",
        "Gegen diesen Bescheid kann Widerspruch erhoben werden");

    private Document Geladen(int id)
    {
        using var context = database.Context();
        return context.Documents.Single(d => d.Id == id);
    }

    // ── Das Datum vom Blatt ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Der_Beleg_traegt_das_Datum_vom_Blatt_und_nicht_den_Tag_der_Einlieferung()
    {
        var ergebnis = await Einliefern(Bescheid());

        var dokument = Geladen(ergebnis.DocumentId);
        Assert.Equal(new DateOnly(2026, 3, 12), dokument.DocumentDate);
        Assert.NotEqual(clock.Today, dokument.DocumentDate);
    }

    /// <summary>
    /// Ein Datum aus der Zukunft wird nicht übernommen.
    /// </summary>
    /// <remarks>
    /// Ein Papier entsteht, bevor es ankommt. Hielte der Leser eine Zahlungsfrist für das
    /// Briefdatum, sortierte sich der Beleg nach vorn und stünde zwischen Schreiben, die es
    /// noch nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Ein_Datum_aus_der_Zukunft_wird_nicht_uebernommen()
    {
        var ergebnis = await Einliefern(Inhalt(
            "Musterwerke GmbH",
            "Musterstadt, den 31.12.2030",
            "Rechnungsbetrag: 90,00"));

        Assert.Equal(clock.Today, Geladen(ergebnis.DocumentId).DocumentDate);
    }

    /// <summary>Gibt der Text kein Datum her, bleibt der Tag der Einlieferung stehen.</summary>
    [Fact]
    public async Task Ohne_lesbares_Datum_bleibt_der_Tag_der_Einlieferung()
    {
        var ergebnis = await Einliefern(Inhalt("Ein Zettel ohne jede Angabe"));

        Assert.Equal(clock.Today, Geladen(ergebnis.DocumentId).DocumentDate);
    }

    // ── Der Name ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aus dem Gerätenamen wird Art und Datum.
    /// </summary>
    /// <remarks>
    /// Die Art ist hier eine Vermutung aus Schlagwörtern, kein gelesener Wert. „Gebührenbescheid“
    /// ist auch als Vermutung mehr wert als „Scannen0007“ — und falsch liegt sie höchstens in
    /// der Schublade, nicht in einer Zahl.
    /// </remarks>
    [Fact]
    public async Task Aus_dem_Geraetenamen_wird_Art_und_Datum()
    {
        var ergebnis = await Einliefern(Bescheid());

        var dokument = Geladen(ergebnis.DocumentId);
        Assert.DoesNotContain("Scannen", dokument.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-03-12", dokument.FileName);
        Assert.True(dokument.HasGeneratedName);
    }

    /// <summary>
    /// Was ein Mensch benannt hat, bleibt.
    /// </summary>
    /// <remarks>
    /// Ein überschriebener Name ist nicht zurückzuholen, und die Anwendung weiß nicht besser,
    /// wie sein Beleg heißen soll.
    /// </remarks>
    [Fact]
    public async Task Ein_gepflegter_Name_bleibt_unangetastet()
    {
        var ergebnis = await Einliefern(Bescheid(), "Muellgebuehren Haus Musterweg.pdf");

        // Leerzeichen werden beim Ablegen zu Unterstrichen — das ist die Dateisystemform
        // desselben Namens und keine Umbenennung. Entscheidend ist, dass weder Art noch Datum
        // darin auftauchen und das Kennzeichen falsch bleibt.
        var dokument = Geladen(ergebnis.DocumentId);
        Assert.Equal("Muellgebuehren_Haus_Musterweg.pdf", dokument.FileName);
        Assert.DoesNotContain("2026-03-12", dokument.FileName);
        Assert.False(dokument.HasGeneratedName);
    }

    /// <summary>
    /// Der erzeugte Name darf später geschärft werden, der gepflegte nicht.
    /// </summary>
    /// <remarks>
    /// Der Kern des Kennzeichens: Nach dem Einliefern heißt der Beleg „Gebührenbescheid_…“ und
    /// sieht nicht mehr nach Gerät aus. Ohne das Kennzeichen bliebe er auf halber Strecke
    /// stehen, wenn beim Zuordnen das Objekt dazukommt.
    /// </remarks>
    [Fact]
    public async Task Ein_erzeugter_Name_nimmt_das_Objekt_spaeter_auf()
    {
        var ergebnis = await Einliefern(Bescheid());

        using var context = database.Context();
        var neu = await Documents(context).RenameIfScannedAsync(
            ergebnis.DocumentId, "Gebührenbescheid", "Haus Musterweg 3", new DateOnly(2026, 3, 12));

        Assert.NotNull(neu);
        Assert.Contains("Haus", neu);
    }

    [Fact]
    public async Task Ein_gepflegter_Name_nimmt_das_Objekt_nicht_auf()
    {
        var ergebnis = await Einliefern(Bescheid(), "Muellgebuehren Haus Musterweg.pdf");

        using var context = database.Context();
        var neu = await Documents(context).RenameIfScannedAsync(
            ergebnis.DocumentId, "Gebührenbescheid", "Haus Musterweg 3", new DateOnly(2026, 3, 12));

        Assert.Null(neu);
    }

    // ── Suchen nach Monat und Jahr ─────────────────────────────────────────────────────────

    /// <summary>
    /// Nach dem Dokumentdatum lässt sich in jeder gebräuchlichen Schreibweise suchen.
    /// </summary>
    /// <remarks>
    /// Es gibt keinen Grund anzunehmen, dass jemand die Form trifft, in der die Datenbank das
    /// Datum führt. Eine Suche, die nur eine davon kennt, ist für die anderen kaputt, ohne es
    /// zu sagen.
    /// </remarks>
    [Theory]
    [InlineData("März 2026")]
    [InlineData("03/2026")]
    [InlineData("2026-03")]
    [InlineData("12.03.2026")]
    [InlineData("2026")]
    public async Task Das_Dokumentdatum_ist_in_jeder_Schreibweise_zu_finden(string suche)
    {
        var ergebnis = await Einliefern(Bescheid());

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync(suche);

        Assert.Contains(treffer.Documents, d => d.Id == ergebnis.DocumentId);
    }

    /// <summary>Ein anderer Monat findet ihn nicht.</summary>
    [Fact]
    public async Task Ein_anderer_Monat_findet_den_Beleg_nicht()
    {
        var ergebnis = await Einliefern(Bescheid());

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("04/2026");

        Assert.DoesNotContain(treffer.Documents, d => d.Id == ergebnis.DocumentId);
    }

    // ── Das Nachziehen am Bestand ──────────────────────────────────────────────────────────

    /// <summary>
    /// Versetzt einen Beleg in den Zustand vor dieser Fassung.
    /// </summary>
    /// <remarks>
    /// So lag der Bestand bis heute da: das Datum auf dem Tag der Ablage, der Name ohne
    /// Kennzeichen. Nachgebaut statt angenommen — die Datei wandert dabei wirklich, sonst
    /// prüfte der Test eine Umbenennung, die auf der Platte gar nicht stattfindet.
    /// </remarks>
    private void AlterBestand(int id, string dateiname)
    {
        var paths = TestDatabase.PathService(root);
        using var context = database.Context();
        var dokument = context.Documents.Single(d => d.Id == id);

        // Nur bewegen, wenn der Name sich unterscheidet: Ein Zug auf den eigenen Pfad sieht
        // für die Ablage wie ein besetzter Name aus, und sie wiche mit _1 aus.
        if (!string.Equals(dokument.FileName, dateiname, StringComparison.Ordinal))
        {
            var ordner = dokument.RelativePath[..(dokument.RelativePath.LastIndexOf('/') + 1)];
            var ziel = paths.Move(dokument.RelativePath, ordner + dateiname);
            Assert.NotNull(ziel);

            dokument.RelativePath = ziel;
            dokument.FileName = Path.GetFileName(ziel);
        }

        dokument.HasGeneratedName = false;
        dokument.DocumentDate = DateOnly.FromDateTime(dokument.CreatedAt);
        context.SaveChanges();
    }

    private async Task<BackfillDatesAndNamesResultDto> Nachziehen()
    {
        using var context = database.Context();
        return await Documents(context).BackfillDatesAndNamesAsync();
    }

    [Fact]
    public async Task Der_Bestand_bekommt_das_Datum_vom_Blatt_nachgetragen()
    {
        var ergebnis = await Einliefern(Bescheid(), "Rechnung Mustermann.pdf");
        AlterBestand(ergebnis.DocumentId, "Rechnung_Mustermann.pdf");

        var lauf = await Nachziehen();

        Assert.Equal(1, lauf.Datiert);
        Assert.Equal(0, lauf.Umbenannt);
        Assert.Equal(0, lauf.Gekennzeichnet);
        Assert.Equal(new DateOnly(2026, 3, 12), Geladen(ergebnis.DocumentId).DocumentDate);
    }

    /// <summary>
    /// Ein von Hand benannter Beleg behält seinen Namen, auch wenn sein Datum wandert.
    /// </summary>
    /// <remarks>
    /// Der Kern der Entscheidung: Das Datum nachzutragen ist eine Berichtigung, den Namen zu
    /// überschreiben ein Verlust. Nur das erste geschieht ungefragt.
    /// </remarks>
    [Fact]
    public async Task Ein_Name_von_Hand_bleibt_auch_beim_Nachziehen()
    {
        var ergebnis = await Einliefern(Bescheid(), "Rechnung Mustermann.pdf");
        AlterBestand(ergebnis.DocumentId, "Rechnung_Mustermann.pdf");

        await Nachziehen();

        var dokument = Geladen(ergebnis.DocumentId);
        Assert.Equal("Rechnung_Mustermann.pdf", dokument.FileName);
        Assert.False(dokument.HasGeneratedName);
    }

    /// <summary>
    /// Trägt ein Beleg genau den Namen, den die Anwendung vergeben würde, zieht der Name mit.
    /// </summary>
    /// <remarks>
    /// Sonst stünde in der Liste das Datum vom Blatt und im Dateinamen der Tag der Ablage —
    /// schlechter als beides, weil die Ablage sich dann selbst widerspricht.
    /// </remarks>
    [Fact]
    public async Task Ein_Name_in_der_Form_der_Anwendung_zieht_mit_dem_Datum_nach()
    {
        var ergebnis = await Einliefern(Bescheid(), "Platzhalter.pdf");
        AlterBestand(ergebnis.DocumentId, "Gebuehrenbescheid_2026-11-06.pdf");

        var lauf = await Nachziehen();

        Assert.Equal(1, lauf.Gekennzeichnet);
        Assert.Equal(1, lauf.Datiert);
        Assert.Equal(1, lauf.Umbenannt);

        var dokument = Geladen(ergebnis.DocumentId);
        Assert.Equal("Gebuehrenbescheid_2026-03-12.pdf", dokument.FileName);
        Assert.True(dokument.HasGeneratedName);
    }

    /// <summary>Ein Name, der einem besetzten ausgewichen ist, gilt genauso.</summary>
    [Fact]
    public async Task Ein_Ausweichname_gilt_auch_als_von_der_Anwendung_vergeben()
    {
        var ergebnis = await Einliefern(Bescheid(), "Platzhalter.pdf");
        AlterBestand(ergebnis.DocumentId, "Gebuehrenbescheid_2026-11-06_1.pdf");

        var lauf = await Nachziehen();

        Assert.Equal(1, lauf.Gekennzeichnet);
        Assert.True(Geladen(ergebnis.DocumentId).HasGeneratedName);
    }

    /// <summary>Ein gepflegtes Datum wird nicht überschrieben.</summary>
    /// <remarks>
    /// Weicht das Datum vom Tag der Ablage ab, hat es jemand gesetzt — oder ein genauerer Weg
    /// als ein Briefdatum, etwa der Stichtag eines Statusreports.
    /// </remarks>
    [Fact]
    public async Task Ein_gepflegtes_Datum_bleibt_beim_Nachziehen_stehen()
    {
        var ergebnis = await Einliefern(Bescheid(), "Rechnung Mustermann.pdf");
        AlterBestand(ergebnis.DocumentId, "Rechnung_Mustermann.pdf");

        using (var context = database.Context())
        {
            var dokument = context.Documents.Single(d => d.Id == ergebnis.DocumentId);
            dokument.DocumentDate = new DateOnly(2026, 1, 9);
            context.SaveChanges();
        }

        var lauf = await Nachziehen();

        Assert.Equal(0, lauf.Datiert);
        Assert.Equal(new DateOnly(2026, 1, 9), Geladen(ergebnis.DocumentId).DocumentDate);
    }

    /// <summary>
    /// Der zweite Lauf findet nichts mehr.
    /// </summary>
    /// <remarks>
    /// Ein Nachlauf, der bei jedem Druck etwas tut, ist keiner: Er verschöbe Dateien im Kreis
    /// und die Meldung sagte jedes Mal dasselbe, ohne dass es stimmt.
    /// </remarks>
    [Fact]
    public async Task Ein_zweiter_Lauf_aendert_nichts_mehr()
    {
        var ergebnis = await Einliefern(Bescheid(), "Platzhalter.pdf");
        AlterBestand(ergebnis.DocumentId, "Gebuehrenbescheid_2026-11-06.pdf");

        var erster = await Nachziehen();
        Assert.False(erster.Nichts);

        var zweiter = await Nachziehen();
        Assert.True(zweiter.Nichts);
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
