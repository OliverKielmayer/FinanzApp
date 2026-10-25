using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Weg vom PDF ins Objekt — v5-Handoff, Abschnitte 14.5 und 14.6.
/// </summary>
/// <remarks>
/// <para>Die Regel, an der alles hängt: <b>nichts Unbestätigtes verändert eine
/// Vermögenszahl.</b> Zwischen Analyse und Übernahme liegt ein Mensch. Der erste Test hier
/// prüft genau das — dass die Analyse den Vertrag unangetastet lässt.</para>
/// <para>Gelesen wird über einen gestellten Leser: was PdfPig aus einer Datei holt, ist in
/// <see cref="PdfTextReaderTests"/> geprüft, und hier geht es um das, was danach passiert.</para>
/// </remarks>
public sealed class DocumentScanTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 29);
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int vertragId;
    private readonly int depotId;

    public DocumentScanTests()
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

        var depot = new Depot { Name = "Musterdepot", Broker = "Musterbank", Number = "9988776655" };

        context.Policies.Add(vertrag);
        context.Depots.Add(depot);
        context.SaveChanges();

        vertragId = vertrag.Id;
        depotId = depot.Id;
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Leser, der eine vorgegebene Textebene liefert.</summary>
    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    private DocumentScanService Service(PdfContent inhalt)
    {
        var context = database.Context();
        var documents = new DocumentService(
            context,
            TestDatabase.PathService(root),
            new ObjectLabelService(context),
            clock,
            NullLogger<DocumentService>.Instance);

        return new DocumentScanService(
            context,
            documents,
            TestDatabase.PathService(root),
            new DepotStatementService(context),
            new StubReader(inhalt),
            clock);
    }

    /// <param name="bytes">
    /// Die Rohdatei. Gleiche Bytes sind dieselbe Datei — daran hängt die Dublettenprüfung.
    /// </param>
    private Task<ScanAnalysisDto> AnalyseAsync(
        PdfContent inhalt, string dateiname = "beleg.pdf", string bytes = "%PDF-1.4")
        => Service(inhalt).AnalyseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(bytes)), dateiname);

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(params PdfLine[] zeilen) => new()
    {
        PageCount = zeilen.Length == 0 ? 0 : zeilen.Max(z => z.Page),
        Lines = zeilen,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>Ein Statusreport mit erfundenen Zahlen, im Aufbau des Originals.</summary>
    private static PdfContent Statusreport(string rueckkauf = "12.345,67", string gesamt = "13.580,23")
        => Content(
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Hamburg, 12.08.2025"),
            Line(1, "Versicherungsnummer:", "77001122-01"),
            Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport"),
            Line(1, "Ihr Vertragsstand zum 31.07.2025"),
            Line(1, "Leistung im Erlebensfall zum Ablauf 01.08.2031"),
            Line(1, "garantierte Erlebensfallleistung", "20.000,00 EUR"),
            Line(1, "Gesamtleistung*", "21.234,56 EUR"),
            Line(1, "Leistung im Todesfall"),
            Line(1, "Gesamtleistung", "21.234,56 EUR"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Rückkaufswert", rueckkauf + " EUR"),
            Line(2, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
            Line(2, "Gesamtleistung*", gesamt + " EUR"),
            Line(2, "Für die Zukunft nicht garantierte Bewertungsreserven**", "23,45 EUR"),
            Line(2, "Für die Zukunft nicht garantierte Schlussüberschüsse***", "678,90 EUR"),
            Line(2, "Leistung bei Berufsunfähigkeit zum 01.08.2025"),
            Line(2, "monatliche Berufsunfähigkeitsrente", "2.500,00 EUR"));

    /// <summary>Eine Quartalsaufstellung mit erfundenen Zahlen.</summary>
    private static PdfContent Quarterly(string stichtag = "30.06.2026", string stueck = "500")
        => Quarterly(stichtag, [("IE00TEST0001", "Weltfonds-Muster UCITS ETF", stueck, "100,500", "50.250,00")], "50.250,00");

    /// <summary>Eine Aufstellung mit beliebig vielen Positionen.</summary>
    private static PdfContent Quarterly(
        string stichtag,
        IReadOnlyList<(string Isin, string Name, string Stueck, string Kurs, string Wert)> positionen,
        string gesamt)
    {
        List<PdfLine> zeilen =
        [
            Line(1, "Musterbank AG"),
            Line(1, "15.07.2026"),
            Line(1, "Depot-Nr.:", "9988776655"),
            Line(1, "Referenz-Nr.:", "1032904213"),
            Line(1, "Quartalsaufstellung nach Art. 63 Delegierte Verordnung"),
            Line(1, "MIFID II per " + stichtag),
        ];

        foreach (var p in positionen)
        {
            zeilen.AddRange(
            [
                Line(1, "Stück", p.Stueck, "WKN: TEST99", $"EUR {p.Kurs}", p.Wert, "EUR"),
                Line(1, "ISIN:", p.Isin),
                Line(1, p.Name),
                Line(1, "Verwahrart:", "Wertpapierrechnung"),
                Line(1, "Lagerstelle:", "1419"),
                Line(1, "Lagerland:", "Luxemburg"),
            ]);
        }

        zeilen.Add(Line(1, "Depotwert", gesamt, "EUR"));
        return Content([.. zeilen]);
    }

    private Policy Vertrag()
    {
        using var context = database.Context();
        return context.Policies.Single(p => p.Id == vertragId);
    }

    // ── Analyse verändert nichts ───────────────────────────────────────────────────────────

    /// <summary>
    /// Die Analyse legt ab und schlägt vor. Sie ändert keine Zahl.
    /// </summary>
    /// <remarks>
    /// Der Kern von Abschnitt 14.6. Ein Leseergebnis, das den Vertragswert schon verändert hat,
    /// wäre keine Prüfung mehr, sondern eine Mitteilung.
    /// </remarks>
    [Fact]
    public async Task Die_Analyse_veraendert_den_Vertrag_nicht()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Equal("statusreport-lv", vorschlag.KindKey);
        Assert.Null(Vertrag().CurrentValue);
        Assert.Null(Vertrag().ValuationDate);

        using var context = database.Context();
        Assert.All(
            context.DocumentExtractions.Where(x => x.DocumentId == vorschlag.DocumentId),
            x => Assert.False(x.Confirmed));
    }

    /// <summary>
    /// Der Vorschlag findet das Zielobjekt über seine Nummer.
    /// </summary>
    [Fact]
    public async Task Der_Vorschlag_findet_den_Vertrag_ueber_die_Nummer()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Equal(vertragId, vorschlag.TargetId);
        Assert.Equal("Nordstern Leben", vorschlag.TargetName);
        Assert.Null(vorschlag.Blocker);
    }

    /// <summary>
    /// Metadaten kommen aus dem Inhalt, nie aus dem Dateinamen.
    /// </summary>
    /// <remarks>
    /// Der Anlass steht im Handoff und im echten Ordner des Nutzers: die Datei heißt
    /// „statusreport 2024“, der Inhalt sagt Stichtag 31.07.2025. Wer dem Dateinamen glaubt,
    /// legt den Bericht ins falsche Jahr und datiert den Vermögenswert um zwölf Monate falsch.
    /// </remarks>
    [Fact]
    public async Task Der_Dateiname_bestimmt_weder_Datum_noch_Ablage()
    {
        var vorschlag = await AnalyseAsync(Statusreport(), "statusreport 2024.pdf");

        Assert.Equal(new DateOnly(2025, 7, 31), vorschlag.AsOf);
        Assert.Equal(new DateOnly(2025, 8, 12), vorschlag.DocumentDate);
        Assert.Contains("/2025/", vorschlag.RelativePath);
        Assert.DoesNotContain("2024", vorschlag.RelativePath);
    }

    /// <summary>
    /// Der Ablagepfad entsteht aus Bereich, Objekt und Jahr.
    /// </summary>
    [Fact]
    public async Task Der_Ablagepfad_folgt_der_Vorlage()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Equal(
            "Versicherungen/Lebensversicherung/Nordstern_Leben/2025/Statusreport_2025-07-31.pdf",
            vorschlag.RelativePath);

        Assert.True(File.Exists(Path.Combine(root, vorschlag.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// Die Werteliste zeigt die Sachwerte, nicht die Kopfdaten.
    /// </summary>
    /// <remarks>
    /// Stichtag, Absender und Vertragsnummer tragen den Vorschlag darüber. Ein zweites Mal in
    /// der Liste stünden sie nur im Weg — der Handoff nennt zehn Felder, nicht vierzehn.
    /// </remarks>
    [Fact]
    public async Task Die_Werteliste_wiederholt_die_Kopfdaten_nicht()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        var schluessel = vorschlag.Fields.Select(f => f.Key).ToList();

        Assert.DoesNotContain("stichtag", schluessel);
        Assert.DoesNotContain("absender", schluessel);
        Assert.DoesNotContain("vertragsnummer", schluessel);
        Assert.Contains("gesamt", schluessel);
    }

    /// <summary>
    /// Beträge gehen als Zahl hinaus, damit die Maske sie verbergen kann.
    /// </summary>
    [Fact]
    public async Task Betraege_kommen_als_Zahl_und_nicht_als_fertiger_Text()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        var gesamt = vorschlag.Fields.Single(f => f.Key == "gesamt");

        Assert.True(gesamt.IsMoney);
        Assert.Equal(13580.23m, gesamt.Number);
        Assert.Equal(string.Empty, gesamt.Display);
    }

    /// <summary>
    /// Jeder gezeigte Wert nennt seine Herkunftsseite.
    /// </summary>
    [Fact]
    public async Task Jede_Zeile_nennt_ihre_Seite()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.All(vorschlag.Fields, f => Assert.NotNull(f.SourcePage));
        Assert.Contains("Seite 2", vorschlag.Fields.Single(f => f.Key == "gesamt").Source);
        Assert.Contains("nicht garantiert", vorschlag.Fields.Single(f => f.Key == "reserven").Source);
    }

    /// <summary>
    /// Die Rechenprobe erreicht den Prüfschritt.
    /// </summary>
    /// <remarks>
    /// Und die Analysekette endet mit ihr: sie ist der letzte Schritt, der über die Werte
    /// entscheidet. Ohne sie sähe die Analyse fertiger aus, als sie ist.
    /// </remarks>
    [Fact]
    public async Task Die_Rechenprobe_steht_im_Vorschlag()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        var probe = Assert.Single(vorschlag.Proofs);
        Assert.True(probe.Passed);
        Assert.Contains("=", probe.Line);
        Assert.NotEmpty(probe.Why);

        Assert.Equal("Rechenprobe bestanden", vorschlag.Steps[^1]);
    }

    [Fact]
    public async Task Eine_gescheiterte_Probe_steht_am_Ende_der_Kette()
    {
        var vorschlag = await AnalyseAsync(Statusreport(rueckkauf: "9.999,99"));

        Assert.False(vorschlag.Proofs[0].Passed);
        Assert.Equal("Rechenprobe nicht aufgegangen — Werte prüfen", vorschlag.Steps[^1]);
    }

    // ── Dasselbe noch einmal ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Dieselbe Datei, von Hand zweimal eingelesen, wird nicht zweimal abgelegt.
    /// </summary>
    /// <remarks>
    /// <para>Die Prüfung saß nur an der Einlieferung aus dem überwachten Ordner. Von Hand ließ
    /// sich dieselbe Datei beliebig oft einlesen, und jedes Mal entstand ein Dokument: im
    /// gewachsenen Bestand lagen davon drei Abzüge desselben Riester-Statusreports, einer davon
    /// an keinem Vertrag.</para>
    /// <para>Geantwortet wird mit dem <em>vorhandenen</em> Beleg. Wer eine Datei einliest, will
    /// wissen, wo sie liegt — und nicht nur, dass hier nichts passiert ist.</para>
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Datei_wird_von_Hand_nicht_zweimal_abgelegt()
    {
        var erster = await AnalyseAsync(Statusreport());
        var zweiter = await AnalyseAsync(Statusreport());

        Assert.Equal(erster.DocumentId, zweiter.DuplicateOfDocumentId);
        Assert.Equal(erster.DocumentId, zweiter.DocumentId);
        Assert.Equal(erster.RelativePath, zweiter.RelativePath);
        Assert.Contains("Byte für Byte dieselbe Datei", zweiter.Note);

        using var context = database.Context();
        Assert.Single(context.Documents.ToList());
    }

    /// <summary>
    /// Die abgewiesene Datei wird nicht gelesen — und behauptet darum nichts über sich.
    /// </summary>
    /// <remarks>
    /// Eine Seitenzahl oder ein Stichtag in dieser Antwort stammte aus dem vorhandenen Beleg und
    /// stünde doch da, wo nach der eingereichten Datei gefragt ist. Sie ist gar nicht erst
    /// geöffnet worden; das ist die ehrliche Auskunft.
    /// </remarks>
    [Fact]
    public async Task Die_abgewiesene_Datei_zaehlt_keine_Seiten()
    {
        await AnalyseAsync(Statusreport());
        var zweiter = await AnalyseAsync(Statusreport());

        Assert.Equal(0, zweiter.PageCount);
        Assert.Empty(zweiter.Fields);
        Assert.Empty(zweiter.Proofs);
        Assert.Null(zweiter.AsOf);
    }

    /// <summary>
    /// Derselbe Beleg aus einem zweiten Scan wird gemeldet — und hält nichts auf.
    /// </summary>
    /// <remarks>
    /// <para>Dasselbe Schreiben ein zweites Mal eingescannt ergibt andere Bytes; der Abdruck
    /// trifft es nicht. Der Ablagename trifft es, denn den bildet die Analyse aus Art, Objekt
    /// und Stichtag.</para>
    /// <para>Der Verdacht steht neben dem Vorschlag und nicht im Blocker: eine berichtigte
    /// Fassung desselben Schreibens bekäme denselben Namen, und sie zu übernehmen ist richtig.
    /// Entscheiden soll ein Mensch, der den Hinweis gesehen hat.</para>
    /// </remarks>
    [Fact]
    public async Task Derselbe_Beleg_aus_zweitem_Scan_steht_neben_dem_Vorschlag()
    {
        var erster = await AnalyseAsync(Statusreport());
        var zweiter = await AnalyseAsync(Statusreport(), bytes: "%PDF-1.4 zweiter Scan");

        Assert.Null(erster.SameFilingNote);

        Assert.NotEqual(erster.DocumentId, zweiter.DocumentId);
        Assert.Equal(erster.DocumentId, zweiter.SameFilingDocumentId);
        Assert.Contains(erster.RelativePath, zweiter.SameFilingNote);
        Assert.Null(zweiter.Blocker);
    }

    /// <summary>
    /// Zwei verschiedene Belege sind keine Dublette.
    /// </summary>
    /// <remarks>
    /// Ein Hinweis, der immer dasteht, wird nicht mehr gelesen. Geprüft wird darum auch die
    /// andere Richtung: eine Quartalsaufstellung neben einem Statusreport löst nichts aus.
    /// </remarks>
    [Fact]
    public async Task Ein_anderer_Beleg_loest_keinen_Verdacht_aus()
    {
        await AnalyseAsync(Statusreport());
        var zweiter = await AnalyseAsync(Quarterly(), "quartal.pdf", bytes: "%PDF-1.4 Quartal");

        Assert.Null(zweiter.DuplicateOfDocumentId);
        Assert.Null(zweiter.SameFilingDocumentId);
        Assert.Null(zweiter.SameFilingNote);
    }

    // ── Übernahme ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Erst die Bestätigung trägt Wert und Stichtag ein.
    /// </summary>
    [Fact]
    public async Task Die_Bestaetigung_schreibt_Wert_und_Stichtag()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        await Service(Statusreport()).ConfirmAsync(new ConfirmScanRequest
        {
            DocumentId = vorschlag.DocumentId,
        });

        Assert.Equal(13580.23m, Vertrag().CurrentValue);
        Assert.Equal(new DateOnly(2025, 7, 31), Vertrag().ValuationDate);
    }

    /// <summary>
    /// Das Ablaufdatum wird ergänzt, aber nie überschrieben.
    /// </summary>
    /// <remarks>
    /// Was der Nutzer gepflegt hat, weiß er besser als ein Leseergebnis. Ergänzen hilft, Ersetzen
    /// wäre Übergriff.
    /// </remarks>
    [Fact]
    public async Task Ein_gepflegtes_Ablaufdatum_bleibt_stehen()
    {
        using (var context = database.Context())
        {
            context.Policies.Single(p => p.Id == vertragId).MaturesOn = new DateOnly(2030, 1, 1);
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());
        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        Assert.Equal(new DateOnly(2030, 1, 1), Vertrag().MaturesOn);
    }

    /// <summary>
    /// Die Bestätigung nennt die Wirkung, nicht den Vorgang.
    /// </summary>
    /// <remarks>
    /// „21.360,45 € übernommen · +521,38 € gegenüber dem Stand vom 31.07.2024“ statt „Werte
    /// gespeichert“. Der Betrag allein sagt niemandem, ob der Vertrag gewachsen ist.
    /// </remarks>
    [Fact]
    public async Task Die_Wirkung_nennt_die_Veraenderung_zum_letzten_Stand()
    {
        using (var context = database.Context())
        {
            var vertrag = context.Policies.Single(p => p.Id == vertragId);
            vertrag.CurrentValue = 13000.00m;
            vertrag.ValuationDate = new DateOnly(2024, 7, 31);
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());
        var ergebnis = await Service(Statusreport()).ConfirmAsync(
            new() { DocumentId = vorschlag.DocumentId });

        Assert.True(ergebnis.Saved);
        Assert.Equal(13580.23m, ergebnis.LeadNumber);
        Assert.True(ergebnis.LeadIsMoney);

        Assert.Equal(13580.23m, ergebnis.Effect[0].Money);
        Assert.Contains(ergebnis.Effect, t => t.Money == 580.23m && t.Signed);
        Assert.Contains(ergebnis.Effect, t => t.Text?.Contains("31.07.2024") == true);
    }

    /// <summary>
    /// Ohne früheren Stand wird keine Veränderung behauptet.
    /// </summary>
    [Fact]
    public async Task Ohne_Vorwert_heisst_es_erster_Stand()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        var ergebnis = await Service(Statusreport()).ConfirmAsync(
            new() { DocumentId = vorschlag.DocumentId });

        Assert.Contains(ergebnis.Effect, t => t.Text == "erster erfasster Stand");
        Assert.DoesNotContain(ergebnis.Effect, t => t.Signed);
    }

    /// <summary>
    /// Eine Korrektur aus der Maske gilt vor dem gelesenen Wert.
    /// </summary>
    [Fact]
    public async Task Eine_Korrektur_ersetzt_den_gelesenen_Wert()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        await Service(Statusreport()).ConfirmAsync(new ConfirmScanRequest
        {
            DocumentId = vorschlag.DocumentId,
            Values = new Dictionary<string, string> { ["gesamt"] = "14.000,00" },
        });

        Assert.Equal(14000.00m, Vertrag().CurrentValue);
    }

    /// <summary>
    /// Nach der Übernahme sind die gelesenen Werte als bestätigt vermerkt.
    /// </summary>
    [Fact]
    public async Task Nach_der_Uebernahme_gelten_die_Werte_als_bestaetigt()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        using var context = database.Context();
        Assert.All(
            context.DocumentExtractions.Where(x => x.DocumentId == vorschlag.DocumentId),
            x => Assert.True(x.Confirmed));
    }

    /// <summary>
    /// Der Weg zum Ziel führt auf eine Seite, die es gibt.
    /// </summary>
    /// <remarks>
    /// Die Vertragsseite liegt unter <c>/police/{id}</c> und nicht unter <c>/vorsorge/{id}</c> —
    /// der erste Versuch zeigte ins Leere, und ein toter Knopf auf der Bestätigungsseite fällt
    /// erst auf, wenn jemand ihn drückt.
    /// </remarks>
    [Fact]
    public async Task Der_Weg_zum_Vertrag_zeigt_auf_die_Vertragsseite()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        var ergebnis = await Service(Statusreport()).ConfirmAsync(
            new() { DocumentId = vorschlag.DocumentId });

        Assert.Equal($"/police/{vertragId}", ergebnis.TargetHref);
        Assert.Equal($"/police/{vertragId}", vorschlag.TargetHref);
        Assert.Equal("Zum Vertrag", ergebnis.TargetLink);
    }

    /// <summary>
    /// Das Dokument hängt danach am Vertrag.
    /// </summary>
    [Fact]
    public async Task Das_Dokument_wird_mit_dem_Vertrag_verknuepft()
    {
        var vorschlag = await AnalyseAsync(Statusreport());
        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        using var context = database.Context();
        Assert.Contains(
            context.DocumentLinks,
            l => l.DocumentId == vorschlag.DocumentId
                 && l.TargetType == LinkTargetType.Policy
                 && l.TargetId == vertragId);
    }

    // ── Wenn etwas fehlt ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ohne passenden Vertrag bleibt die Datei liegen, und der Vorschlag sagt warum.
    /// </summary>
    /// <remarks>
    /// Vorher gesagt statt hinterher: ein Knopf „Übernehmen“, der beim Drücken scheitert, hat
    /// den Nutzer zweimal Zeit gekostet.
    /// </remarks>
    [Fact]
    public async Task Ohne_Zielobjekt_nennt_der_Vorschlag_den_Grund()
    {
        using (var context = database.Context())
        {
            context.Policies.Single(p => p.Id == vertragId).PolicyNumber = "99999999-99";
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Null(vorschlag.TargetId);
        Assert.Contains("Kein Vertrag gefunden", vorschlag.Blocker);

        // Die gesuchte Nummer steht in der Meldung: mit ihr weiß der Nutzer, was zu tun ist.
        Assert.Contains("77001122-01", vorschlag.Blocker);
        Assert.Contains("Unbekannt", vorschlag.RelativePath);
    }

    /// <summary>
    /// Die Meldung nennt den Vertrag, der dieselben Ziffern trägt.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall aus dem Betrieb: der Riester-Statusreport nennt „R-01511104-04“, im
    /// Vertrag stand „01511104-04“. Alle Werte waren gelesen, der Knopf war tot, und die Meldung
    /// las sich wie „diesen Vertrag gibt es nicht“ — während er einen Bindestrich entfernt
    /// dastand.</para>
    /// <para>Genannt und <b>nicht zugeordnet</b>: das R steht bei diesem Absender für die
    /// geförderte Tranche desselben Stammvertrags, und es kann beide geben. Ein Beleg am falschen
    /// Vertrag ist von allen möglichen Fehlern der teuerste.</para>
    /// </remarks>
    [Fact]
    public async Task Der_Grund_nennt_den_Vertrag_mit_denselben_Ziffern()
    {
        using (var context = database.Context())
        {
            // Der Beleg nennt 77001122-01, der Vertrag trägt dieselbe Nummer mit einem
            // Buchstaben davor: dieselben Ziffern, andere Schreibweise.
            context.Policies.Single(p => p.Id == vertragId).PolicyNumber = "R-77001122-01";
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Null(vorschlag.TargetId);
        Assert.Contains("denselben Ziffern", vorschlag.Blocker);
        Assert.Contains("R-77001122-01", vorschlag.Blocker);
    }

    /// <summary>
    /// Eine Nummer, die nichts trifft, erfindet auch keinen Beinahe-Treffer.
    /// </summary>
    /// <remarks>
    /// Sonst stünde unter jedem unbekannten Beleg ein Vertrag, der nichts mit ihm zu tun hat —
    /// ein Hinweis, der in die Irre führt, ist schlechter als keiner.
    /// </remarks>
    [Fact]
    public async Task Ohne_Beinahe_Treffer_steht_kein_Vertrag_in_der_Meldung()
    {
        using (var context = database.Context())
        {
            context.Policies.Single(p => p.Id == vertragId).PolicyNumber = "88889999-77";
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.DoesNotContain("denselben Ziffern", vorschlag.Blocker);
    }

    /// <summary>
    /// Die Meldung sagt auch, <em>wo</em> gesucht wurde.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall aus dem Betrieb: sieben Verträge liegen im einen Haushalt, eingelesen wird
    /// im anderen. Die Nummer wird gelesen, der Vertrag existiert — und trotzdem findet die
    /// Suche nichts, weil sie über die Haushaltsgrenze nicht hinaussieht. Das ist richtig so;
    /// falsch war die Meldung, die klang wie „diese Nummer kennt niemand“.</para>
    /// <para>Der Name des eigenen Haushalts verrät nichts über den anderen — er sagt nur, auf
    /// welcher Seite die Suche stand.</para>
    /// </remarks>
    [Fact]
    public async Task Der_Grund_nennt_den_Haushalt_in_dem_gesucht_wurde()
    {
        using (var context = database.Context())
        {
            context.Households.Add(new Household
            {
                Id = database.HouseholdId,
                Name = "Haushalt Muster",
                CreatedAt = clock.Now,
            });

            context.Policies.Single(p => p.Id == vertragId).PolicyNumber = "99999999-99";
            context.SaveChanges();
        }

        var vorschlag = await AnalyseAsync(Statusreport());

        Assert.Contains("Haushalt Muster", vorschlag.Blocker);
    }

    /// <summary>
    /// Ohne gelesene Nummer steht kein Haushalt in der Meldung.
    /// </summary>
    /// <remarks>
    /// Dann hat keine Suche stattgefunden, und ein Ort ohne Suche ist eine Auskunft über nichts.
    /// </remarks>
    [Fact]
    public async Task Ohne_Nummer_nennt_der_Grund_keinen_Haushalt()
    {
        using (var context = database.Context())
        {
            context.Households.Add(new Household
            {
                Id = database.HouseholdId,
                Name = "Haushalt Muster",
                CreatedAt = clock.Now,
            });

            context.SaveChanges();
        }

        var ohneNummer = Content(
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Hamburg, 12.08.2025"),
            Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport"),
            Line(1, "Ihr Vertragsstand zum 31.07.2025"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Rückkaufswert", "12.345,67 EUR"),
            Line(2, "Gesamtleistung*", "12.345,67 EUR"));

        var vorschlag = await AnalyseAsync(ohneNummer);

        Assert.Contains("nennt auch keine", vorschlag.Blocker);
        Assert.DoesNotContain("Haushalt Muster", vorschlag.Blocker);
    }

    /// <summary>
    /// Eine von Hand eingetragene Zeile wandert bis in den Bestand.
    /// </summary>
    /// <remarks>
    /// Bei einer Aufstellung sind die Zeilen nicht bloß eine Probe — sie werden zu Positionen.
    /// Eine Berichtigung, die nur den Schirm beruhigt und beim Speichern verschwiegen wird, wäre
    /// die schlimmste Sorte Halbwahrheit: der Nutzer hat sie gesehen, die Datenbank nicht.
    /// </remarks>
    [Fact]
    public async Task Eine_von_Hand_eingetragene_Zeile_wandert_in_den_Bestand()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");

        await Service(Quarterly()).ConfirmAsync(new ConfirmScanRequest
        {
            DocumentId = vorschlag.DocumentId,
            RowValues = new Dictionary<int, string> { [0] = "50.000,00" },
        });

        using var context = database.Context();
        Assert.Equal(50000.00m, context.DepotStatementPositions.Single().Value);
    }

    // ── Gelesen, aber nicht übernommen ─────────────────────────────────────────────────────

    /// <summary>
    /// Am Beleg steht, dass seine Werte noch niemand übernommen hat — und der Weg dorthin.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall aus dem Betrieb: dreizehn Statusreporte eines Vertrags waren eingelesen,
    /// abgelegt und mit dem Vertrag verknüpft — und am Vertrag stand eine Null. Wer sie im
    /// Scaneingang wegräumt, erzeugt genau das. Von außen sah so ein Beleg aus wie ein
    /// übernommener; die Angabe steckte in der Datenbank und in keiner Fläche.</para>
    /// <para>Nach der Übernahme ist nichts mehr offen, und der Weg zum Prüfschritt verschwindet
    /// mit: ein Angebot, das nichts mehr zu tun hat, ist eine Einladung zum Verdoppeln.</para>
    /// </remarks>
    [Fact]
    public async Task Am_Beleg_steht_was_noch_niemand_uebernommen_hat()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        using var context = database.Context();
        var documents = Documents(context);

        var vorher = await documents.GetAsync(vorschlag.DocumentId);

        Assert.NotNull(vorher);
        Assert.True(vorher.UnconfirmedValues > 0);
        Assert.Equal($"/scannen/{vorschlag.DocumentId}", vorher.ValuesHref);

        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        var nachher = await documents.GetAsync(vorschlag.DocumentId);

        Assert.NotNull(nachher);
        Assert.Equal(0, nachher.UnconfirmedValues);
        Assert.Null(nachher.ValuesHref);
    }

    /// <summary>
    /// Ein zweiter Blick nimmt einem Beleg nicht seine Übernahme.
    /// </summary>
    /// <remarks>
    /// <para>Der Prüfschritt schreibt den Leseabdruck jedes Mal neu — er ist die Grundlage der
    /// Übernahme und muss zu dem passen, was auf dem Schirm steht. Dabei setzte er aber auch das
    /// Merkmal „übernommen“ zurück: der Stand stand im Vertrag, und der Beleg behauptete
    /// daneben, seine Werte habe nie jemand genommen.</para>
    /// <para>Aufgefallen ist es am lebenden Bestand — ein Beleg meldete „7 gelesene Angaben noch
    /// nicht übernommen“, während sein Wert längst im Verlauf stand. Wer das sieht, übernimmt
    /// ein zweites Mal.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_zweiter_Blick_nimmt_nicht_die_Uebernahme()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });
        await Service(Statusreport()).ReviewAsync(vorschlag.DocumentId);

        using var context = database.Context();
        var beleg = await Documents(context).GetAsync(vorschlag.DocumentId);

        Assert.NotNull(beleg);
        Assert.Equal(0, beleg.UnconfirmedValues);
    }

    /// <summary>
    /// Liest die Nachschau eine andere Zahl, gilt die alte Bestätigung nicht mehr.
    /// </summary>
    /// <remarks>
    /// Sie galt einer anderen Zahl. Eine verbesserte Leseregel kann aus demselben Papier etwas
    /// anderes holen — und was dann im Vertrag steht, hat niemand zu <em>diesem</em> Wert
    /// gesagt. Der Beleg meldet ihn deshalb wieder als offen.
    /// </remarks>
    [Fact]
    public async Task Eine_andere_Lesung_gilt_wieder_als_offen()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        await Service(Statusreport()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        // Dieselbe Datei, andere Lesung — wie nach einer verbesserten Regel.
        await Service(Statusreport(gesamt: "14.000,00 EUR")).ReviewAsync(vorschlag.DocumentId);

        using var context = database.Context();
        var beleg = await Documents(context).GetAsync(vorschlag.DocumentId);

        Assert.NotNull(beleg);
        Assert.True(beleg.UnconfirmedValues > 0);
    }

    /// <summary>
    /// Ein Beleg ohne gelesene Werte bietet keinen Prüfschritt an.
    /// </summary>
    /// <remarks>
    /// Ein Foto, ein Schreiben ohne Leseregel: dort gibt es nichts zu übernehmen, und ein
    /// Schalter dorthin führte in eine Meldung statt in eine Fläche.
    /// </remarks>
    [Fact]
    public async Task Ein_Beleg_ohne_gelesene_Werte_bietet_keinen_Weg_an()
    {
        var vorschlag = await AnalyseAsync(Content(Line(1, "Ein Zettel ohne Merkmale")), "zettel.pdf");

        using var context = database.Context();
        var beleg = await Documents(context).GetAsync(vorschlag.DocumentId);

        Assert.NotNull(beleg);
        Assert.Equal(0, beleg.UnconfirmedValues);
        Assert.Null(beleg.ValuesHref);
    }

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

    /// <summary>
    /// Ein eingetragener Wert steht im Prüfschritt — und nicht im Leseabdruck.
    /// </summary>
    /// <remarks>
    /// <para>Der Schirm zeigte die Korrektur früher nicht: übernommen wurde die eingetragene
    /// Zahl, zu sehen war die gelesene, und darüber stand „Was hier steht, wird übernommen“. Wer
    /// das sah, hielt seine Eingabe für verloren.</para>
    /// <para>Im Abdruck hat sie trotzdem nichts zu suchen. Der hält fest, was <em>gelesen</em>
    /// wurde; stünde die Eingabe darin, wüsste beim nächsten Öffnen niemand mehr, welche Zahl
    /// vom Papier kam.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_eingetragener_Wert_steht_im_Pruefschritt()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        var geprueft = await Service(Statusreport()).ReviewAsync(
            vorschlag.DocumentId,
            values: new Dictionary<string, string> { ["gesamt"] = "14.000,00" });

        var feld = geprueft.Fields.Single(f => f.Key == "gesamt");

        Assert.Equal(14000.00m, feld.Number);
        Assert.True(feld.ByHand);
        Assert.Contains("von Hand eingetragen", feld.Source);

        using var context = database.Context();

        var abdruck = context.DocumentExtractions
            .Single(x => x.DocumentId == vorschlag.DocumentId && x.FieldKey == "gesamt");

        Assert.Equal("13.580,23 EUR", abdruck.Value);
    }

    /// <summary>
    /// Ein Leitwert, aus dem keine Zahl wird, hält die Übernahme auf.
    /// </summary>
    /// <remarks>
    /// Vorher stand der Knopf bereit und die Übernahme sagte hinterher „ohne Gesamtleistung geht
    /// nichts“ — vorher gesagt statt hinterher, wie überall sonst auf diesem Schirm.
    /// </remarks>
    [Fact]
    public async Task Ein_Leitwert_ohne_Zahl_haelt_die_Uebernahme_auf()
    {
        var vorschlag = await AnalyseAsync(Statusreport());

        var geprueft = await Service(Statusreport()).ReviewAsync(
            vorschlag.DocumentId,
            values: new Dictionary<string, string> { ["gesamt"] = "vierzehntausend" });

        Assert.NotNull(geprueft.Blocker);
        Assert.Contains("Es fehlt", geprueft.Blocker);
    }

    /// <summary>
    /// Die erste Zeile lässt sich berichtigen wie jede andere.
    /// </summary>
    /// <remarks>
    /// Die Zeilennummer null ist gültig und sah trotzdem lange nach „nichts gefunden“ aus: die
    /// Prüfung auf eine Zeile außerhalb der Tabelle suchte mit <c>FirstOrDefault</c>, und das
    /// liefert bei Zahlen die Null. Jede Berichtigung der obersten Zeile flog damit heraus —
    /// gefunden erst im Probelauf, weil kein Test die erste Zeile anfasste.
    /// </remarks>
    [Fact]
    public async Task Die_erste_Zeile_laesst_sich_berichtigen()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");

        var geprueft = await Service(Quarterly()).ReviewAsync(
            vorschlag.DocumentId, new Dictionary<int, string> { [0] = "50.000,00" });

        var zeile = Assert.Single(geprueft.Repeat!.Rows);

        Assert.Equal(50000.00m, zeile.Value);
        Assert.True(zeile.ByHand);
    }

    /// <summary>
    /// Eine Berichtigung für eine Zeile, die es nicht gibt, wird abgewiesen.
    /// </summary>
    /// <remarks>
    /// Sie käme aus einem Schirm, der eine andere Tabelle gesehen hat als die Datei — nach einer
    /// verbesserten Leseregel etwa. Sie still zu übergehen wäre das Schlimmste: der Nutzer hätte
    /// eine Zahl eingetragen und niemand hätte sie verwendet.
    /// </remarks>
    [Fact]
    public async Task Eine_Zeile_die_es_nicht_gibt_wird_zurueckgewiesen()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service(Quarterly()).ReviewAsync(
                vorschlag.DocumentId, new Dictionary<int, string> { [7] = "1,00" }));

        Assert.Contains("Zeile 8", fehler.Message);
    }

    /// <summary>
    /// Ein unbekannter Typ wird trotzdem abgelegt.
    /// </summary>
    /// <remarks>
    /// Eine Datei, die die Analyse nicht versteht, ist kein Fehler des Nutzers. Sie liegt im
    /// Bereich „Sonstiges“ und wartet darauf, von Hand eingeordnet zu werden.
    /// </remarks>
    [Fact]
    public async Task Ein_unbekanntes_Dokument_wird_abgelegt_statt_abgewiesen()
    {
        var vorschlag = await AnalyseAsync(Content(Line(1, "Stromrechnung"), Line(1, "2.400 kWh")));

        Assert.Null(vorschlag.KindKey);
        Assert.NotNull(vorschlag.Note);
        Assert.StartsWith("Sonstiges/", vorschlag.RelativePath);
        Assert.True(File.Exists(Path.Combine(root, vorschlag.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// Was keinen Typ hat, lässt sich nicht übernehmen.
    /// </summary>
    [Fact]
    public async Task Ohne_Typ_gibt_es_keine_Uebernahme()
    {
        var vorschlag = await AnalyseAsync(Content(Line(1, "Stromrechnung")));

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service(Content(Line(1, "Stromrechnung")))
                .ConfirmAsync(new() { DocumentId = vorschlag.DocumentId }));

        Assert.Contains("kein Typ erkannt", fehler.Message);
    }

    // ── Quartalsaufstellung ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Aufstellung wird zum Bestandsnachweis und speist den Abgleich aus Abschnitt 11.3.
    /// </summary>
    [Fact]
    public async Task Die_Quartalsaufstellung_wird_zum_Bestandsnachweis()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");

        Assert.Equal("quartalsaufstellung", vorschlag.KindKey);
        Assert.Equal(depotId, vorschlag.TargetId);
        Assert.Equal(new DateOnly(2026, 6, 30), vorschlag.AsOf);

        var ergebnis = await Service(Quarterly()).ConfirmAsync(
            new() { DocumentId = vorschlag.DocumentId });

        Assert.True(ergebnis.Saved);

        using var context = database.Context();
        var aufstellung = context.DepotStatements.Single();
        var position = context.DepotStatementPositions.Single();

        Assert.Equal(new DateOnly(2026, 6, 30), aufstellung.AsOf);
        Assert.Equal(new DateOnly(2026, 7, 15), aufstellung.IssuedOn);
        Assert.Equal("1032904213", aufstellung.Reference);
        Assert.Equal(vorschlag.DocumentId, aufstellung.DocumentId);

        Assert.Equal("IE00TEST0001", position.Isin);
        Assert.Equal("TEST99", position.Wkn);
        Assert.Equal("Weltfonds-Muster UCITS ETF", position.SecurityName);
        Assert.Equal(500m, position.Quantity);
        Assert.Equal(100.500m, position.Price);
        Assert.Equal(50250.00m, position.Value);
        Assert.Equal("Wertpapierrechnung", position.SafeCustody);
        Assert.Equal("Luxemburg", position.Country);
        Assert.Equal("1419", position.Depository);
    }

    /// <summary>
    /// Die Wirkung der Aufstellung nennt Stück, Kurs und Kurswert zum Stichtag.
    /// </summary>
    [Fact]
    public async Task Die_Wirkung_der_Aufstellung_nennt_den_belegten_Bestand()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");
        var ergebnis = await Service(Quarterly()).ConfirmAsync(
            new() { DocumentId = vorschlag.DocumentId });

        Assert.Contains(ergebnis.Effect, t => t.Quantity == 500m);
        Assert.Contains(ergebnis.Effect, t => t.Price == 100.500m);
        Assert.Contains(ergebnis.Effect, t => t.Money == 50250.00m);
        Assert.Contains(ergebnis.Effect, t => t.Text?.Contains("30.06.2026") == true);
        Assert.Equal("/depot", ergebnis.TargetHref);
    }

    /// <summary>
    /// Drei Fonds ergeben eine Aufstellung mit drei Positionen.
    /// </summary>
    /// <remarks>
    /// Nicht drei Aufstellungen — Abschnitt 17.2. Die Zuordnung zum Depot geht über die
    /// Depotnummer, die der Zeilen über die ISIN.
    /// </remarks>
    [Fact]
    public async Task Drei_Positionen_ergeben_eine_Aufstellung_mit_drei_Zeilen()
    {
        var inhalt = Quarterly("30.06.2026",
        [
            ("IE00TEST0001", "Weltfonds Muster", "152", "120,000", "18.240,00"),
            ("IE00TEST0002", "Schwellenland Muster", "175", "54,930", "9.612,75"),
            ("DE000TEST003", "Musterkonzern AG", "18", "341,600", "6.148,80"),
        ], "34.001,55");

        var vorschlag = await AnalyseAsync(inhalt, "quartal.pdf");

        Assert.Equal(3, vorschlag.Repeat!.Rows.Count);
        Assert.True(vorschlag.Repeat.Matches);
        Assert.Equal(34001.55m, vorschlag.Repeat.Total);

        var ergebnis = await Service(inhalt).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });
        Assert.True(ergebnis.Saved);

        using var context = database.Context();
        Assert.Single(context.DepotStatements);

        var positionen = context.DepotStatementPositions.OrderBy(p => p.Id).ToList();
        Assert.Equal(3, positionen.Count);
        Assert.Equal(["IE00TEST0001", "IE00TEST0002", "DE000TEST003"], positionen.Select(p => p.Isin));
        Assert.Equal(175m, positionen[1].Quantity);
        Assert.Equal(6148.80m, positionen[2].Value);

        Assert.Equal(34001.55m, ergebnis.LeadNumber);
    }

    /// <summary>
    /// Zweimal derselbe Stichtag ist keine zweite Aufstellung.
    /// </summary>
    /// <remarks>
    /// Dieselbe Regel wie beim Kontoauszug: derselbe Beleg zweimal eingelesen darf den Bestand
    /// nicht verdoppeln. Der Abgleich greift schon in <see cref="DepotStatementService"/>; hier
    /// zählt, dass der Weg über den Scan sie nicht umgeht.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Aufstellung_zweimal_wird_abgelehnt()
    {
        var erste = await AnalyseAsync(Quarterly(), "quartal.pdf");
        await Service(Quarterly()).ConfirmAsync(new() { DocumentId = erste.DocumentId });

        var zweite = await AnalyseAsync(Quarterly(), "quartal-nochmal.pdf");
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service(Quarterly()).ConfirmAsync(new() { DocumentId = zweite.DocumentId }));

        Assert.Contains("30.06.2026", fehler.Message);

        using var context = database.Context();
        Assert.Single(context.DepotStatements);
    }

    /// <summary>
    /// Der Depotwert selbst bleibt unangetastet — die Aufstellung belegt ihn, sie setzt ihn nicht.
    /// </summary>
    [Fact]
    public async Task Die_Aufstellung_setzt_keinen_Depotwert()
    {
        var vorschlag = await AnalyseAsync(Quarterly(), "quartal.pdf");
        await Service(Quarterly()).ConfirmAsync(new() { DocumentId = vorschlag.DocumentId });

        using var context = database.Context();
        var depot = context.Depots.Single(d => d.Id == depotId);

        Assert.Null(depot.StatedValue);
        Assert.Null(depot.ValuationDate);
    }

    // ── Ein Vertrag, der keinen Wert tragen kann ──────────────────────────────────────────

    /// <summary>
    /// Ein Statusreport auf einem Absicherungsvertrag wird nicht übernommen.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall entstand aus zwei richtigen Regeln: eine Absicherung trägt <b>nie</b> einen
    /// Vermögenswert, und der Kopfwert eines Vertrags kommt aus seinem neuesten Bericht. Zusammen
    /// hieß das: der Bericht wurde geschrieben, der Wert nicht abgeleitet — und die Übernahme
    /// meldete trotzdem „übernommen“. Der Betrag verschwand.</para>
    /// <para>Jetzt sagt der Prüfschritt es vorher und die Übernahme weist ab. Geschrieben wird
    /// nichts: kein Bericht, kein Wert.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_Absicherungsvertrag_uebernimmt_keinen_erreichten_Wert()
    {
        int abgesichert;

        using (var context = database.Context())
        {
            var vertrag = new Policy
            {
                Name = "Risikoleben",
                Provider = "Nordstern Leben",
                Kind = PolicyKind.TermLife,
                IsCapitalForming = false,
                PolicyNumber = "77009999-01",
            };

            context.Policies.Add(vertrag);
            context.SaveChanges();
            abgesichert = vertrag.Id;
        }

        var beleg = Content(
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Hamburg, 12.08.2025"),
            Line(1, "Versicherungsnummer:", "77009999-01"),
            Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport"),
            Line(1, "Ihr Vertragsstand zum 31.07.2025"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Rückkaufswert", "12.345,67 EUR"),
            Line(2, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
            Line(2, "Gesamtleistung*", "13.580,23 EUR"));

        var analyse = await AnalyseAsync(beleg);

        // Das Ziel steht — es kann den Wert nur nicht tragen, und das sagt der Prüfschritt.
        Assert.Equal(abgesichert, analyse.TargetId);
        Assert.Contains("trägt keinen Vermögenswert", analyse.Blocker);
        Assert.Contains("Vorsorge", analyse.Blocker);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service(beleg).ConfirmAsync(new ConfirmScanRequest { DocumentId = analyse.DocumentId }));

        Assert.Contains("trägt keinen Vermögenswert", fehler.Message);

        using var pruefung = database.Context();
        var danach = pruefung.Policies.Single(p => p.Id == abgesichert);

        Assert.Null(danach.CurrentValue);
        Assert.Empty(pruefung.PolicyReports.Where(r => r.PolicyId == abgesichert));
    }

    /// <summary>
    /// Beim Vorsorgevertrag bleibt es beim Übernehmen.
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zur Sperre: sie darf nur den Fall treffen, den sie meint. Sonst hätte der
    /// Schutz den Weg zugemauert, der funktioniert.
    /// </remarks>
    [Fact]
    public async Task Ein_Vorsorgevertrag_uebernimmt_weiter()
    {
        var analyse = await AnalyseAsync(Statusreport());

        Assert.Equal(vertragId, analyse.TargetId);
        Assert.Null(analyse.Blocker);

        var ergebnis = await Service(Statusreport()).ConfirmAsync(
            new ConfirmScanRequest { DocumentId = analyse.DocumentId });

        Assert.True(ergebnis.Saved);

        using var pruefung = database.Context();
        Assert.Equal(13580.23m, pruefung.Policies.Single(p => p.Id == vertragId).CurrentValue);
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
