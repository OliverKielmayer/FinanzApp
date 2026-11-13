using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Was eine bestätigte Gehaltsabrechnung am Arbeitsverhältnis ändert.
/// </summary>
/// <remarks>
/// <para><b>Diese Tests stehen für zwei Fehler, die erst im laufenden Programm auffielen.</b>
/// Die Leseregel war geprüft, die Werte standen richtig im Prüfschritt — und „Werte
/// übernehmen“ scheiterte trotzdem: <c>ObjectLabelService</c> konnte den Arbeitgeber nicht
/// auflösen, und eine Verknüpfung auf ein nicht auflösbares Ziel weist
/// <c>DocumentService.LinkAsync</c> ab. Der Typ stand im Enum, trug ein Etikett — und fehlte
/// in der einen Abfrage, auf die es ankam.</para>
/// <para><b>Der zweite Fehler war der schwerere.</b> Das Speichern stand vor dem Verknüpfen.
/// Als die Verknüpfung scheiterte, war das Gehalt an der Stelle trotzdem schon geschrieben:
/// Der Anwender sah eine Fehlermeldung und hatte einen veränderten Bestand. Vier Fehlversuche
/// hintereinander, und jeder hat geschrieben.</para>
/// <para><b>Was diese Tests nicht decken:</b> die Reihenfolge selbst. Geeicht wurde es — wer
/// das Speichern wieder vor das Verknüpfen zieht, bringt keine dieser Prüfungen zum Fallen,
/// weil sich der Fall nach dem ersten Fix nicht mehr herstellen lässt: Die Verknüpfung
/// gelingt jetzt. Gedeckt ist, dass eine abgewiesene Übernahme nichts schreibt; die
/// Reihenfolge hält daneben nur der Kommentar im Dienst. Sauber trüge das erst eine
/// Transaktion um den ganzen Vorgang — die beträfe aber auch Policen und Depots und ist
/// deshalb nicht Teil dieser Arbeit.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class PayslipApplyTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 6, 28);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int stelleId;

    /// <summary>Zählt die Einlieferungen, damit jede eigene Bytes bekommt.</summary>
    private int eingeliefert;

    public PayslipApplyTests()
    {
        using var context = database.Context();

        var stelle = new Employment
        {
            Employer = "Musterbetrieb GmbH",
            Position = "Sachbearbeitung",
            StartsOn = new DateOnly(2019, 10, 1),
            IsActive = true,
            GrossMonthly = 4500m,
        };

        context.Employments.Add(stelle);
        context.SaveChanges();
        stelleId = stelle.Id;
    }

    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    private DocumentScanService Scans(PdfContent inhalt, FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);

        var documents = new DocumentService(
            context, paths, new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        return new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new StubReader(inhalt), clock);
    }

    /// <summary>Eine Abrechnung im Aufbau des Originals, mit erfundenen Zahlen.</summary>
    private static PdfContent Abrechnung(
        string monatsbrutto = "4.827,50", string auszahlung = "2.913,64")
        => new()
        {
            PageCount = 1,
            ImageCount = 1,
            TextIsInvisible = true,
            Lines =
            [
                new(1, ["Abrechnun der Brutto/Netto-Bezü e für Juni 2026 24.06.2026 1"]),
                new(1, ["Musterbetrieb GmbH 12345 Musterstadt"]),
                new(1, ["Ges amt-B r utt0"]),
                new(1, [$"Steuer/Sozialversicherung {monatsbrutto}"]),
                new(1, ["Lohnsteuer 412,75 Kirchensteuer 22,70"]),
                new(1, ["Netto-Verdienst"]),
                new(1, [$"DE02 1234 5678 9012 3456 78 1.187,45 {auszahlung}"]),
                new(1, ["DATEV"]),
            ],
        };

    private ScanIntakeService Intake(PdfContent inhalt, FinanzAppDbContext context)
    {
        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        return new ScanIntakeService(
            context, Scans(inhalt, context), documents,
            new ScanInboxService(context, documents, clock), new AssetSheetParser(),
            new CamtStatementParser(), TestDatabase.NoActions(context, clock), clock);
    }

    /// <summary>
    /// Liefert die Abrechnung ein, so wie der Ordnerdienst es täte.
    /// </summary>
    /// <remarks>
    /// Jede Einlieferung bekommt eigene Bytes: Die Einlieferung weist byteweise gleiche Dateien
    /// als Dublette ab, und jeder Test hier liefert ein.
    /// </remarks>
    private async Task<ScanIntakeResultDto> EinliefernAsync(PdfContent inhalt)
    {
        using var context = database.Context();

        var datei = $"%PDF-1.4 Abrechnung {++eingeliefert}";

        return await Intake(inhalt, context).TakeInAsync(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(datei)),
            "Gehaltsabrechnung_06_2026.pdf", @"C:\Scans\gehalt.pdf");
    }

    // ── Die Ursache des Fehlschlags ────────────────────────────────────────────────────

    /// <summary>
    /// <b>Der Arbeitgeber muss sich als Verknüpfungsziel auflösen lassen.</b> Genau das fehlte:
    /// <c>LinkTargetType.Employer</c> stand im Enum und trug ein Etikett, fiel in
    /// <c>ResolveAsync</c> aber auf <c>null</c> — und damit weist jede Verknüpfung darauf ab.
    /// </summary>
    [Fact]
    public async Task Ein_Arbeitgeber_laesst_sich_als_Ziel_aufloesen()
    {
        using var context = database.Context();
        var labels = new ObjectLabelService(context);

        Assert.Equal("Musterbetrieb GmbH",
            await labels.ResolveAsync(LinkTargetType.Employer, stelleId));
    }

    /// <summary>Ein Ziel, das es nicht gibt, bleibt unauflösbar — sonst prüfte die Prüfung nichts.</summary>
    [Fact]
    public async Task Eine_fremde_Kennung_bleibt_unaufloesbar()
    {
        using var context = database.Context();
        var labels = new ObjectLabelService(context);

        Assert.Null(await labels.ResolveAsync(LinkTargetType.Employer, stelleId + 999));
    }

    // ── Der ganze Bogen ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Der ganze Bogen.</b> Nach der Bestätigung stehen die Werte an der Stelle, und der
    /// Beleg hängt daran.
    /// </summary>
    /// <remarks>
    /// Dass der Vorgang überhaupt durchläuft, ist der Kern: Genau hier brach er ab, weil der
    /// Etikettendienst den Arbeitgeber nicht auflösen konnte. Die Verknüpfung selbst entsteht
    /// schon bei der Einlieferung — geprüft wird sie trotzdem, weil sie auf dieses
    /// Arbeitsverhältnis zeigen muss und nicht irgendwohin.
    /// </remarks>
    [Fact]
    public async Task Die_Uebernahme_setzt_die_Werte_und_haengt_den_Beleg_an()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            await Scans(Abrechnung(), context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        var stelle = pruef.Employments.Single(e => e.Id == stelleId);
        Assert.Equal(4827.50m, stelle.GrossMonthly);
        Assert.Equal(2913.64m, stelle.NetMonthly);

        var link = pruef.DocumentLinks.Single(
            l => l.DocumentId == eingang.DocumentId && l.TargetType == LinkTargetType.Employer);

        Assert.Equal(stelleId, link.TargetId);
    }

    /// <summary>
    /// <b>Ein abgewiesener Vorgang lässt nichts halb erledigt zurück.</b> Ohne laufendes
    /// Arbeitsverhältnis gibt es keine Stelle, an die etwas gehörte — und dann darf auch nichts
    /// geschrieben werden. Genau an dieser Reihenfolge lag der zweite Fehler.
    /// </summary>
    [Fact]
    public async Task Ohne_laufende_Stelle_wird_nichts_geschrieben()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            context.Employments.Single(e => e.Id == stelleId).IsActive = false;
            context.SaveChanges();
        }

        using (var context = database.Context())
        {
            await Assert.ThrowsAsync<RuleViolationException>(
                () => Scans(Abrechnung(), context)
                    .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId }));
        }

        using var pruef = database.Context();

        // Unberührt: der Betrag von vorher, und weiterhin kein Nettowert. Darum geht es — die
        // Verknüpfung bleibt hier absichtlich ungeprüft, denn die entsteht schon beim
        // Einliefern und sagt über die Uebernahme nichts.
        var stelle = pruef.Employments.Single(e => e.Id == stelleId);
        Assert.Equal(4500m, stelle.GrossMonthly);
        Assert.Null(stelle.NetMonthly);
    }

    /// <summary>
    /// Was die Übernahme meldet, nennt beide Zahlen — und in Teilen, damit „Beträge verbergen“
    /// auch hier greift.
    /// </summary>
    [Fact]
    public async Task Die_Meldung_nennt_was_sich_aendert()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using var context = database.Context();

        var ergebnis = await Scans(Abrechnung(), context)
            .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });

        Assert.True(ergebnis.Saved);
        Assert.Contains(ergebnis.Effect, t => t.Money == 4827.50m);
        Assert.Contains(ergebnis.Effect, t => t.Money == 2913.64m);

        // Der alte Bruttowert steht als Vergleich dabei; ein Nettowert gab es vorher nicht.
        Assert.Contains(ergebnis.Effect, t => t.Money == 4500m);
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
