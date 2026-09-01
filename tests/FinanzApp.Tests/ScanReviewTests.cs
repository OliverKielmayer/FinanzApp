using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Prüfschritt für einen bereits abgelegten Beleg — der vierte Schritt, nachgeholt.
/// </summary>
/// <remarks>
/// <para>Der Ordnerdienst legt ab und ordnet zu; die Werte übernimmt erst ein Mensch. Diese Regel
/// ist richtig und hatte eine Lücke: es führte <b>kein Weg</b> zur Bestätigung. Wer einen Beleg
/// über den Ordner einlieferte, hatte danach keine Möglichkeit mehr, seine Werte in den Vertrag zu
/// bringen — außer dieselbe Datei ein zweites Mal einzulesen, was sie ein zweites Mal ablegt.</para>
/// <para>Der wichtigste Test ist der ganze Bogen: einliefern verändert nichts, nachschauen und
/// bestätigen verändert genau das, was auf dem Papier steht.</para>
/// </remarks>
public sealed class ScanReviewTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int vertragId;

    public ScanReviewTests()
    {
        using var context = database.Context();

        var vertrag = new Policy
        {
            Name = "Muster bestpartner classic",
            Provider = "Nordstern Leben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            PolicyNumber = "77001122-01",
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();
        vertragId = vertrag.Id;
    }

    /// <summary>Ein Leser, der eine vorgegebene Textebene liefert.</summary>
    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    private DocumentScanService Scans(PdfContent inhalt, FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);

        var documents = new DocumentService(
            context, paths, new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new StubReader(inhalt), clock);
    }

    private ScanIntakeService Intake(PdfContent inhalt, FinanzAppDbContext context)
    {
        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new ScanIntakeService(
            context, Scans(inhalt, context), documents,
            new ScanInboxService(context, documents, clock));
    }

    /// <summary>Liefert einen Beleg ein, so wie der Ordnerdienst es täte.</summary>
    private async Task<ScanIntakeResultDto> EinliefernAsync(PdfContent inhalt)
    {
        using var context = database.Context();

        return await Intake(inhalt, context).TakeInAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4")), "scan_0001.pdf", "C:\\Scans\\ein.pdf");
    }

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(params PdfLine[] zeilen) => new()
    {
        PageCount = zeilen.Length == 0 ? 0 : zeilen.Max(z => z.Page),
        Lines = zeilen,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>Ein Statusreport im Aufbau des Originals, mit erfundenen Zahlen.</summary>
    private static PdfContent Statusreport() => Content(
        Line(1, "Nordstern Lebensversicherung AG"),
        Line(1, "Hamburg, 12.08.2025"),
        Line(1, "Versicherungsnummer:", "77001122-01"),
        Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport"),
        Line(1, "Ihr Vertragsstand zum 31.07.2025"),
        Line(2, "Wert der Versicherung"),
        Line(2, "Rückkaufswert", "12.345,67 EUR"),
        Line(2, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
        Line(2, "Gesamtleistung*", "13.580,23 EUR"));

    private Policy Vertrag()
    {
        using var context = database.Context();
        return context.Policies.Single(p => p.Id == vertragId);
    }

    // ── Der ganze Bogen ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Einliefern verändert nichts, nachschauen und bestätigen verändert den Vertrag.
    /// </summary>
    /// <remarks>
    /// Der Befund, der diesen Weg nötig machte: über den Ordner eingelieferte Belege kamen nie im
    /// Vertrag an, weil keine Fläche zur Bestätigung führte.
    /// </remarks>
    [Fact]
    public async Task Aus_der_Nachschau_wird_der_Wert_uebernommen()
    {
        var eingeliefert = await EinliefernAsync(Statusreport());

        // Erst einmal steht nichts im Vertrag — das ist die Regel des Dienstes.
        Assert.Null(Vertrag().CurrentValue);

        using (var context = database.Context())
        {
            var prüfschritt = await Scans(Statusreport(), context).ReviewAsync(eingeliefert.DocumentId);

            Assert.Equal(eingeliefert.DocumentId, prüfschritt.DocumentId);
            Assert.Equal("Nordstern Leben", prüfschritt.TargetName);
            Assert.Null(prüfschritt.Blocker);
            Assert.NotEmpty(prüfschritt.Fields);
            Assert.All(prüfschritt.Proofs, p => Assert.True(p.Passed));
        }

        using (var context = database.Context())
        {
            await Scans(Statusreport(), context).ConfirmAsync(
                new ConfirmScanRequest { DocumentId = eingeliefert.DocumentId });
        }

        var vertrag = Vertrag();

        Assert.Equal(13580.23m, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2025, 7, 31), vertrag.ValuationDate);
        Assert.Equal(12345.67m, vertrag.BaseValue);

        // Und der Stand steht in der Berichtsreihe, wie bei jeder anderen Übernahme auch.
        using var prüfung = database.Context();
        var bericht = Assert.Single(prüfung.PolicyReports);
        Assert.Equal(new DateOnly(2025, 7, 31), bericht.AsOf);
        Assert.Equal(13580.23m, bericht.Value);
    }

    /// <summary>
    /// Nach der Übernahme ist der Beleg aus dem Eingang heraus.
    /// </summary>
    /// <remarks>
    /// Sonst stünde dort weiter „wartet auf Zuordnung“ über einen Beleg, dessen Werte schon im
    /// Vertrag stehen. Übernommen ist mehr als weggeräumt.
    /// </remarks>
    [Fact]
    public async Task Nach_der_Uebernahme_ist_der_Beleg_aus_dem_Eingang_heraus()
    {
        var eingeliefert = await EinliefernAsync(Statusreport());

        using (var vorher = database.Context())
        {
            Assert.Equal(1, (await new ScanInboxService(
                vorher,
                new DocumentService(
                    vorher, TestDatabase.PathService(root), new ObjectLabelService(vorher), clock,
                    NullLogger<DocumentService>.Instance),
                clock).GetAsync()).WaitingCount);
        }

        using (var context = database.Context())
        {
            await Scans(Statusreport(), context).ConfirmAsync(
                new ConfirmScanRequest { DocumentId = eingeliefert.DocumentId });
        }

        using var nachher = database.Context();
        Assert.NotNull(nachher.ScanInbox.Single().FiledAt);
    }

    // ── Die Nachschau selbst ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Nachschau legt kein zweites Dokument an.
    /// </summary>
    /// <remarks>
    /// Der Umweg, den es vorher gab: dieselbe Datei ein zweites Mal einlesen. Das legt sie ein
    /// zweites Mal ab, und im Bestand stehen danach zwei Belege für einen Vorgang.
    /// </remarks>
    [Fact]
    public async Task Die_Nachschau_legt_kein_zweites_Dokument_an()
    {
        var eingeliefert = await EinliefernAsync(Statusreport());

        using (var context = database.Context())
        {
            await Scans(Statusreport(), context).ReviewAsync(eingeliefert.DocumentId);
        }

        using var prüfung = database.Context();
        Assert.Single(prüfung.Documents);
    }

    /// <summary>
    /// Der gespeicherte Leseabdruck wird ersetzt, nicht verdoppelt.
    /// </summary>
    /// <remarks>
    /// Er ist die Grundlage der Übernahme. Stünde dort zweimal dasselbe Feld, entschiede die
    /// Reihenfolge über den Wert im Vertrag.
    /// </remarks>
    [Fact]
    public async Task Der_Leseabdruck_wird_ersetzt_und_nicht_verdoppelt()
    {
        var eingeliefert = await EinliefernAsync(Statusreport());

        int vorher;

        using (var context = database.Context())
        {
            vorher = context.DocumentExtractions.Count(x => x.DocumentId == eingeliefert.DocumentId);
        }

        using (var context = database.Context())
        {
            await Scans(Statusreport(), context).ReviewAsync(eingeliefert.DocumentId);
        }

        using (var context = database.Context())
        {
            await Scans(Statusreport(), context).ReviewAsync(eingeliefert.DocumentId);
        }

        using var prüfung = database.Context();

        Assert.Equal(
            vorher, prüfung.DocumentExtractions.Count(x => x.DocumentId == eingeliefert.DocumentId));
    }

    /// <summary>Ohne erkannte Art gibt es nichts zu übernehmen — und die Meldung sagt das.</summary>
    [Fact]
    public async Task Ohne_erkannte_Art_gibt_es_keine_Nachschau()
    {
        int id;

        using (var context = database.Context())
        {
            var dokument = new Document
            {
                Title = "Irgendwas",
                RelativePath = "Sonstiges/beleg.pdf",
                FileName = "beleg.pdf",
                Area = DocumentArea.Other,
                CreatedAt = clock.Now,
                UpdatedAt = clock.Now,
            };

            context.Documents.Add(dokument);
            context.SaveChanges();
            id = dokument.Id;
        }

        using var prüfung = database.Context();

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Scans(Statusreport(), prüfung).ReviewAsync(id));

        Assert.Contains("kein Typ erkannt", fehler.Message);
    }

    /// <summary>
    /// Fehlt die abgelegte Datei, sagt die Meldung, welche.
    /// </summary>
    /// <remarks>
    /// Gelesen wird die Datei und nicht der gespeicherte Abdruck — ohne sie lässt sich der
    /// Prüfschritt nicht aufbauen. Mit dem Pfad in der Meldung weiß der Nutzer, wo er suchen muss.
    /// </remarks>
    [Fact]
    public async Task Ohne_Datei_gibt_es_keine_Nachschau()
    {
        var eingeliefert = await EinliefernAsync(Statusreport());

        string pfad;

        using (var context = database.Context())
        {
            pfad = context.Documents.Single(d => d.Id == eingeliefert.DocumentId).RelativePath;
        }

        File.Delete(Path.Combine(root, pfad.Replace('/', Path.DirectorySeparatorChar)));

        using var prüfung = database.Context();

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Scans(Statusreport(), prüfung).ReviewAsync(eingeliefert.DocumentId));

        Assert.Contains(pfad, fehler.Message);
    }

    // ── Der Eingang zeigt den Weg nur, wo er hinführt ──────────────────────────────────────

    /// <summary>
    /// Der Eingang kennzeichnet, aus welchem Beleg sich Werte übernehmen lassen.
    /// </summary>
    /// <remarks>
    /// Ohne erkannte Art gibt es keine gelesenen Werte — ein Schalter dorthin führte in eine
    /// Meldung statt in einen Prüfschritt.
    /// </remarks>
    [Fact]
    public async Task Der_Eingang_kennzeichnet_uebernehmbare_Belege()
    {
        await EinliefernAsync(Statusreport());
        await EinliefernAsync(Content(Line(1, "Stromrechnung"), Line(1, "Verbrauch", "2.400 kWh")));

        using var context = database.Context();

        var eingang = await new ScanInboxService(
            context,
            new DocumentService(
                context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
                NullLogger<DocumentService>.Instance),
            clock).GetAsync();

        Assert.Equal(2, eingang.Items.Count);
        Assert.Single(eingang.Items, i => i.CanTakeValues);
        Assert.Single(eingang.Items, i => !i.CanTakeValues);
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
