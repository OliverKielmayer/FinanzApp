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
        string monatsbrutto = "4.827,50",
        string auszahlung = "2.913,64",
        string netto = "3.012,80",
        string monat = "Juni 2026",
        bool mitMonat = true,
        bool mitNetto = true)
        => new()
        {
            PageCount = 1,
            ImageCount = 1,
            TextIsInvisible = true,
            Lines =
            [
                new(1, [mitMonat
                    ? $"Abrechnun der Brutto/Netto-Bezü e für {monat} 24.06.2026 1"
                    : "Abrechnun der Brutto/Netto-Bezü e"]),
                new(1, ["Musterbetrieb GmbH 12345 Musterstadt"]),
                new(1, ["Ges amt-B r utt0"]),
                new(1, [$"Steuer/Sozialversicherung {monatsbrutto}"]),
                new(1, ["Lohnsteuer 412,75 Kirchensteuer 22,70"]),
                new(1, ["Netto-Verdienst"]),
                new(1, [mitNetto
                    ? $"Verdienstbescheinigung Netto-Bezüge/Netto•Abzüge {netto}"

                    // So liest die Erkennung die Zeile, wenn sie die Zahl verliert: Die
                    // Beschriftung steht da, der Betrag fehlt.
                    : "Verdienstbescheinigung Netto-Bezüge/Netto•Abzüge"]),
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
    public async Task Die_Uebernahme_legt_eine_Lohnabrechnungszeile_an()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            await Scans(Abrechnung(), context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        var zeile = pruef.Payslips.Single(p => p.EmploymentId == stelleId);

        Assert.Equal(new DateOnly(2026, 6, 1), zeile.Month);
        Assert.Equal(4827.50m, zeile.Gross);
        Assert.Equal(3012.80m, zeile.Net);
        Assert.Equal(2913.64m, zeile.Payout);
        Assert.Equal(eingang.DocumentId, zeile.DocumentId);

        var link = pruef.DocumentLinks.Single(
            l => l.DocumentId == eingang.DocumentId && l.TargetType == LinkTargetType.Employer);

        Assert.Equal(stelleId, link.TargetId);
    }

    /// <summary>
    /// <b>Die Kopfwerte bleiben unberührt.</b> <c>GrossMonthly</c> trägt das Grundgehalt aus
    /// der GEHALT-Zeile, nicht das Gesamt-Brutto mit Sachbezügen. Es mit dem Monatsbrutto zu
    /// überschreiben machte aus einer gepflegten Angabe eine andere Größe mit demselben Namen —
    /// genau das tat die erste Fassung.
    /// </summary>
    [Fact]
    public async Task Die_Kopfwerte_des_Arbeitsverhaeltnisses_bleiben_stehen()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            await Scans(Abrechnung(), context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        var stelle = pruef.Employments.Single(e => e.Id == stelleId);
        Assert.Equal(4500m, stelle.GrossMonthly);
        Assert.Null(stelle.NetMonthly);
    }

    /// <summary>
    /// <b>Der Beleg wandert in den Bereich der erkannten Art.</b> Wer vor dieser Leseregel
    /// hereinkam, liegt unter „Sonstiges“ — und blieb dort liegen, weil die Übernahme den
    /// gespeicherten Bereich weiterreichte statt den erkannten. Der erste echte Beleg, den
    /// jemand übernahm, lag danach immer noch falsch.
    /// </summary>
    [Fact]
    public async Task Der_Beleg_wandert_in_den_Bereich_Arbeit()
    {
        var eingang = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            // So kommt ein Beleg herein, den beim Einliefern noch keine Regel erkannte.
            var vorher = context.Documents.Single(d => d.Id == eingang.DocumentId);
            vorher.Area = DocumentArea.Other;
            context.SaveChanges();
        }

        using (var context = database.Context())
        {
            await Scans(Abrechnung(), context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        Assert.Equal(DocumentArea.Work,
            pruef.Documents.Single(d => d.Id == eingang.DocumentId).Area);
    }

    /// <summary>
    /// Zwei Belege desselben Monats sind eine Dublette — und die weist die Übernahme ab.
    /// </summary>
    /// <remarks>
    /// Dieselbe Regel wie beim Erfassen von Hand. Ohne sie stünden zwei Zeilen für einen Monat
    /// in der Tabelle, und die Jahressumme zählte den Monat doppelt.
    /// </remarks>
    [Fact]
    public async Task Ein_zweiter_Beleg_desselben_Monats_wird_abgewiesen()
    {
        var erster = await EinliefernAsync(Abrechnung());

        using (var context = database.Context())
        {
            await Scans(Abrechnung(), context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = erster.DocumentId });
        }

        var zweiter = await EinliefernAsync(Abrechnung(auszahlung: "2.900,00"));

        using (var context = database.Context())
        {
            var fehler = await Assert.ThrowsAsync<RuleViolationException>(
                () => Scans(Abrechnung(auszahlung: "2.900,00"), context)
                    .ConfirmAsync(new ConfirmScanRequest { DocumentId = zweiter.DocumentId }));

            Assert.Contains("schon eine", fehler.Message);
        }

        using var pruef = database.Context();
        Assert.Single(pruef.Payslips.Where(p => p.EmploymentId == stelleId));
    }

    /// <summary>
    /// <b>Ein fehlendes Netto blockiert die Übernahme nicht.</b>
    /// </summary>
    /// <remarks>
    /// <para>Der Fall kam aus der echten Ablage: Ein Beleg der echten Ablage gibt sein
    /// Netto nicht lesbar her, und die Pflichtprüfung wies sie deshalb ganz ab — „Es fehlt:
    /// Netto-Verdienst“ —, obwohl Monat, Brutto und Auszahlungsbetrag dastanden.</para>
    /// <para>Die Ursache war eine Doppelbedeutung: <c>Lead</c> hieß „wird übernommen“
    /// <em>und</em> „muss da sein“. Für einen Statusreport fällt das zusammen, für eine
    /// Abrechnung mit drei Beträgen nicht. Jetzt trennt <c>Optional</c> beides.</para>
    /// </remarks>
    [Fact]
    public async Task Ohne_lesbares_Netto_laesst_sich_trotzdem_uebernehmen()
    {
        var ohneNetto = Abrechnung(mitNetto: false);
        var eingang = await EinliefernAsync(ohneNetto);

        using (var context = database.Context())
        {
            await Scans(ohneNetto, context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        var zeile = pruef.Payslips.Single(p => p.EmploymentId == stelleId);

        Assert.Equal(4827.50m, zeile.Gross);
        Assert.Null(zeile.Net);

        // Die Auszahlung stand da und bleibt stehen — sie wird nicht aus dem Brutto
        // geschätzt, nur weil das Netto fehlt.
        Assert.Equal(2913.64m, zeile.Payout);
    }

    /// <summary>
    /// <b>Und der Knopf bleibt offen.</b>
    /// </summary>
    /// <remarks>
    /// <para>Die Prüfung, die den Nutzer blockierte, sitzt nicht in der Übernahme, sondern im
    /// Vorschlag: <c>Blocker</c> sperrt den Knopf, bevor jemand drückt. Die erste Fassung
    /// dieser Prüfung rief <c>ConfirmAsync</c> direkt auf und ging daran vorbei — sie blieb
    /// grün, während der Nutzer vor einem gesperrten Knopf stand.</para>
    /// <para>Geeicht: Nimmt man <c>Optional</c> zurück, fällt genau diese Prüfung.</para>
    /// </remarks>
    [Fact]
    public async Task Ohne_lesbares_Netto_bleibt_der_Knopf_offen()
    {
        var ohneNetto = Abrechnung(mitNetto: false);
        var eingang = await EinliefernAsync(ohneNetto);

        using var context = database.Context();

        var vorschlag = await Scans(ohneNetto, context).ReviewAsync(eingang.DocumentId);

        Assert.NotNull(vorschlag);
        Assert.Null(vorschlag.Blocker);
    }

    /// <summary>
    /// Fehlt dagegen das Brutto, steht der Knopf zu Recht still — ohne es gibt es nichts zu
    /// erfassen.
    /// </summary>
    [Fact]
    public async Task Ohne_Brutto_sperrt_der_Knopf()
    {
        var ohneBrutto = Abrechnung();

        var zeilen = ohneBrutto.Lines
            .Where(z => !z.Text.StartsWith("Steuer/Sozial", StringComparison.Ordinal))
            .ToList();

        var inhalt = ohneBrutto with { Lines = zeilen };
        var eingang = await EinliefernAsync(inhalt);

        using var context = database.Context();

        var vorschlag = await Scans(inhalt, context).ReviewAsync(eingang.DocumentId);

        Assert.NotNull(vorschlag);
        Assert.Contains("Gesamt-Brutto", vorschlag.Blocker);
    }

    /// <summary>
    /// Fehlen Netto <em>und</em> Auszahlung, wird die Auszahlung aus dem Brutto geschätzt —
    /// dieselbe Regel wie beim Erfassen von Hand.
    /// </summary>
    [Fact]
    public async Task Ohne_beides_wird_die_Auszahlung_geschaetzt()
    {
        var karg = Abrechnung(mitNetto: false, auszahlung: "0,00");

        // Die IBAN-Zeile ohne Betrag: so bricht die Erkennung sie in einem echten Beleg ab.
        var zeilen = karg.Lines
            .Where(z => !z.Text.StartsWith("DE02", StringComparison.Ordinal))
            .Append(new PdfLine(1, ["Konto DE02 1234"]))
            .ToList();

        var inhalt = karg with { Lines = zeilen };
        var eingang = await EinliefernAsync(inhalt);

        using (var context = database.Context())
        {
            await Scans(inhalt, context)
                .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });
        }

        using var pruef = database.Context();

        var zeile = pruef.Payslips.Single(p => p.EmploymentId == stelleId);

        Assert.Equal(4827.50m, zeile.Gross);
        Assert.Null(zeile.Net);
        Assert.Equal(EmploymentService.Estimate(4827.50m), zeile.Payout);
    }

    /// <summary>
    /// <b>Ohne lesbaren Monat keine Zeile.</b> Sie hinge an keinem Zeitpunkt, und die
    /// Jahressumme zählte sie irgendwo mit. In der echten Ablage gibt ein Beleg von zehn den
    /// Monat nicht her.
    /// </summary>
    [Fact]
    public async Task Ohne_Monat_entsteht_keine_Zeile()
    {
        var eingang = await EinliefernAsync(Abrechnung(mitMonat: false));

        using (var context = database.Context())
        {
            await Assert.ThrowsAsync<RuleViolationException>(
                () => Scans(Abrechnung(mitMonat: false), context)
                    .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId }));
        }

        using var pruef = database.Context();
        Assert.Empty(pruef.Payslips);
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

        // Keine Zeile, und die Kopfwerte unberührt. Die Verknüpfung bleibt hier absichtlich
        // ungeprüft: Die entsteht schon beim Einliefern und sagt über die Übernahme nichts.
        Assert.Empty(pruef.Payslips);

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
        Assert.Contains(ergebnis.Effect, t => t.Money == 3012.80m);
        Assert.Contains(ergebnis.Effect, t => t.Money == 2913.64m);
    }

    /// <summary>
    /// Wo ein sonstiger Bezug im Papier steht, nennt ihn die Meldung — er erklärt, warum das
    /// Monatsbrutto gegenüber den übrigen Monaten springt.
    /// </summary>
    [Fact]
    public async Task Die_Meldung_nennt_einen_sonstigen_Bezug()
    {
        var mitSonderzahlung = Abrechnung(monatsbrutto: "6.577,50");

        var zeilen = mitSonderzahlung.Lines.ToList();
        zeilen.Insert(2, new PdfLine(1, ["005 PRAEMIE S E J 1.750,00"]));

        var inhalt = mitSonderzahlung with { Lines = zeilen };

        var eingang = await EinliefernAsync(inhalt);

        using var context = database.Context();

        var ergebnis = await Scans(inhalt, context)
            .ConfirmAsync(new ConfirmScanRequest { DocumentId = eingang.DocumentId });

        Assert.Contains(ergebnis.Effect, t => t.Money == 1750.00m);
        Assert.Contains(ergebnis.Effect, t => t.Text?.Contains("sonstiger Bezug") == true);
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
