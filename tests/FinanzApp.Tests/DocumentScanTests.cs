using System.Text;
using FinanzApp.Api.Application;
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
            context, documents, new DepotStatementService(context), new StubReader(inhalt), clock);
    }

    private Task<ScanAnalysisDto> AnalyseAsync(PdfContent inhalt, string dateiname = "beleg.pdf")
        => Service(inhalt).AnalyseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4")), dateiname);

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
        => Content(
            Line(1, "Musterbank AG"),
            Line(1, "15.07.2026"),
            Line(1, "Depot-Nr.:", "9988776655"),
            Line(1, "Referenz-Nr.:", "1032904213"),
            Line(1, "Quartalsaufstellung nach Art. 63 Delegierte Verordnung"),
            Line(1, "MIFID II per " + stichtag),
            Line(1, "Stück", stueck, "WKN: TEST99", "EUR 100,500", "50.250,00", "EUR"),
            Line(1, "ISIN:", "IE00TEST0001"),
            Line(1, "Weltfonds-Muster UCITS ETF"),
            Line(1, "Verwahrart:", "Wertpapierrechnung"),
            Line(1, "Lagerstelle:", "1419"),
            Line(1, "Lagerland:", "Luxemburg"));

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
    /// „20.481,52 € übernommen · +521,38 € gegenüber dem Stand vom 31.07.2024“ statt „Werte
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

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
