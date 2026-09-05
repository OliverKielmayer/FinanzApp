using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Das Dokumenttyp-Modell und die Feldzuordnung — v5-Handoff, Abschnitte 14.2 bis 14.4.
/// </summary>
/// <remarks>
/// <para>Die Vorlagen bilden den Aufbau der echten Dokumente nach — Abschnitte, Beschriftungen,
/// Fußnotenzeichen, Tabellenzeilen. Namen und Beträge sind erfunden: was in den Verträgen des
/// Nutzers steht, gehört nicht in ein Quellarchiv.</para>
/// <para>Der wichtigste Test ist der auf die Rechenprobe. Ein Formular verrät nicht, ob ein
/// Betrag in der richtigen Zeile gelandet ist; seine eigene Arithmetik schon. Eine Zuordnung,
/// die immer „passt“ sagt, wäre schlimmer als keine.</para>
/// </remarks>
public sealed class DocumentKindTests
{
    private readonly DocumentFieldExtractor extractor = new();

    // ── Vorlagen ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Statusreport im Aufbau des Originals: drei Leistungsszenarien mit teils gleichen
    /// Beträgen, der Vermögenswert erst auf Seite 2 unter „Wert der Versicherung“.
    /// </summary>
    private static PdfContent Statusreport(
        string rueckkauf = "12.345,67 EUR",
        string ansammlung = "1.234,56 EUR",
        string gesamt = "13.580,23 EUR",
        bool mitGesamt = true)
    {
        List<PdfLine> zeilen =
        [
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Hamburg, 12.08.2025"),
            Line(1, "Seite 1 von 3"),
            Line(1, "Versicherungsnummer:", "77001122-01"),
            Line(1, "Versicherungsnehmer:", "Erika Mustermann"),
            Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport zum Vertragsstand Ihrer"),
            Line(1, "Ihr Vertragsstand zum 31.07.2025"),

            Line(1, "Leistung im Erlebensfall zum Ablauf 01.08.2031"),
            Line(1, "garantierte Erlebensfallleistung", "20.000,00 EUR"),
            Line(1, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
            Line(1, "Gesamtleistung*", "21.234,56 EUR"),

            Line(1, "Leistung im Erlebensfall bei Beitragsfreistellung"),
            Line(1, "garantierte Erlebensfallleistung", "15.000,00 EUR"),
            Line(1, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
            Line(1, "Gesamtleistung*", "16.234,56 EUR"),

            Line(1, "Leistung im Todesfall"),
            Line(1, "garantierte Todesfallleistung", "20.000,00 EUR"),
            Line(1, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
            Line(1, "Gesamtleistung", "21.234,56 EUR"),

            Line(2, "Seite 2 von 3"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Rückkaufswert", rueckkauf),
            Line(2, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", ansammlung),
        ];

        if (mitGesamt)
        {
            zeilen.Add(Line(2, "Gesamtleistung*", gesamt));
        }

        zeilen.AddRange(
        [
            Line(2, "Für die Zukunft nicht garantierte Bewertungsreserven**", "23,45 EUR"),
            Line(2, "Für die Zukunft nicht garantierte Schlussüberschüsse***", "678,90 EUR"),
            Line(2, "Bei vorzeitiger Vertragsbeendigung zum 31.07.2025 erhalten Sie eine finanzielle Leistung"),

            Line(2, "Leistung bei Berufsunfähigkeit zum 01.08.2025"),
            Line(2, "Beitragsbefreiung bei Berufsunfähigkeit", "vereinbart"),
            Line(2, "monatliche Berufsunfähigkeitsrente", "2.500,00 EUR"),

            Line(3, "Seite 3 von 3"),
            Line(3, "Informationen zur Überschussbeteiligung für Ihren Vertrag"),
        ]);

        return Content(zeilen);
    }

    /// <summary>
    /// Eine Quartalsaufstellung im Aufbau des Originals: die Bestandszeile ist eine
    /// Tabellenzeile mit sechs Spalten, die Bezeichnung steht unter der ISIN.
    /// </summary>
    private static PdfContent Quarterly(
        string stueck = "500", string kurs = "100,500", string kurswert = "50.250,00",
        string? gesamt = null)
        => Quarterly([("IE00TEST0001", "Weltfonds-Muster UCITS ETF", stueck, kurs, kurswert)], gesamt);

    /// <summary>
    /// Eine Aufstellung mit beliebig vielen Bestandszeilen.
    /// </summary>
    /// <remarks>
    /// Jede Position steht im Original als Block über sechs Zeilen — Stückzahl, ISIN,
    /// Bezeichnung, Verwahrart. Ein Depot mit drei Fonds wiederholt diesen Block dreimal, und
    /// genau daran ist die Zuordnung vorher gescheitert.
    /// </remarks>
    private static PdfContent Quarterly(
        IReadOnlyList<(string Isin, string Name, string Stueck, string Kurs, string Wert)> positionen,
        string? gesamt = null)
    {
        List<PdfLine> zeilen =
        [
            Line(1, "Musterbank AG", "85716 Musterstadt"),
            Line(1, "Musterstadt"),
            Line(1, "15.07.2026"),
            Line(1, "Depot-Nr.:", "9988776655"),
            Line(1, "Referenz-Nr.:", "1032904213"),
            Line(1, "Quartalsaufstellung nach Art. 63 Delegierte Verordnung"),
            Line(1, "MIFID II per 30.06.2026"),
            Line(1, "Nominale", "Bezeichnung", "Kurs", "Kurswert in Depot WHG"),
            Line(1, "Fonds"),
        ];

        foreach (var p in positionen)
        {
            zeilen.AddRange(
            [
                Line(1, "Stück", p.Stueck, "WKN: TEST99", $"EUR {p.Kurs}", p.Wert, "EUR"),
                Line(1, "ISIN:", p.Isin),
                Line(1, p.Name),
                Line(1, "Registered Shs USD (Acc) o.N."),
                Line(1, "Verwahrart:", "Wertpapierrechnung"),
                Line(1, "Lagerstelle:", "1419"),
                Line(1, "Lagerland:", "Luxemburg"),
            ]);
        }

        zeilen.Add(Line(1, "Depotwert", gesamt ?? positionen[0].Wert, "EUR"));
        zeilen.Add(Line(2, "Seite 2/2"));

        return Content(zeilen);
    }

    /// <summary>
    /// Ein Statusreport einer fondsgebundenen Lebensversicherung im Aufbau des Originals.
    /// </summary>
    /// <remarks>
    /// <para>Kein Rückkaufswert, keine Überschussbeteiligung: der Wert steht als
    /// <em>Anteilsguthaben</em> unter einer Fondstabelle, und dahinter wiederholt ein Satz
    /// denselben Betrag für den Fall der Kündigung. Namen, WKN und Beträge sind erfunden.</para>
    /// <para><paramref name="alterJahrgang"/> schaltet auf die Schreibweise bis 2017:
    /// „Ihr aktueller Vertragsstand“, „Todesfallschutz“ statt der zwei Todesfallzeilen, Beträge
    /// in „Euro“ und eine Spalte Fondswährung in der Tabelle.</para>
    /// </remarks>
    private static PdfContent FundStatusreport(
        IReadOnlyList<(string Wkn, string Name, string Anteile, string Preis, string Wert)>? fonds = null,
        string anteilsguthaben = "10.000,00",
        bool alterJahrgang = false,
        string stichtag = "31.12.2024")
    {
        fonds ??=
        [
            ("TEST01", "Musterfonds Welt", "31,4706", "197,3600", "6.211,03"),
            ("TEST02", "Musterfonds Anleihen", "21,1912", "136,7500", "3.788,97"),
        ];

        var währung = alterJahrgang ? "Euro" : "EUR";

        List<PdfLine> zeilen =
        [
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Hamburg, 05.03.2025"),
            Line(1, "Seite 1 von 3"),
            Line(1, "Versicherungsnummer:", "77001122-02"),
            Line(1, "Versicherungsnehmer:", "Erika Mustermann"),
            Line(1, $"Ihr Statusreport zum {stichtag}"),
            Line(1, "hiermit übersenden wir Ihnen den Statusreport zum Stand Ihrer fondsgebundenen"),

            Line(1, alterJahrgang
                ? $"Ihr aktueller Vertragsstand zum {stichtag}"
                : $"Ihr Vertragsstand zum {stichtag}"),
            Line(1, "Versicherungsform", "Muster topinvest fondsgebundene Lebensversicherung"),
            Line(1, "Anlagestrategie", "Portfolio IV Wachstumsorientiert"),
        ];

        if (alterJahrgang)
        {
            // Die Beschriftung bricht um: der Betrag steht in der zweiten Zeile.
            zeilen.Add(Line(1, "Beitragssumme"));
            zeilen.Add(Line(1, "(entspricht den über die Laufzeit zu zahlenden Beiträgen)", $"36.000,00 {währung}"));
            zeilen.Add(Line(1, "Todesfallschutz", $"36.000,00 {währung}"));
        }
        else
        {
            zeilen.Add(Line(1, "Beitragssumme (entspricht den über die Laufzeit", $"43.000,00 {währung}"));
            zeilen.Add(Line(1, "zu zahlenden Beiträgen)"));
            zeilen.Add(Line(1, "Mindesttodesfallschutz:", $"43.000,00 {währung}"));
            zeilen.Add(Line(1, "Aktuelle Leistung im Todesfall:", $"45.000,00 {währung}"));
        }

        zeilen.Add(Line(1, "Ihre Leistungen bei Berufsunfähigkeit zum 01.01.2025"));
        zeilen.Add(Line(1, "Leistungen bei Berufsunfähigkeit", "Beitragsbefreiung"));

        zeilen.Add(Line(2, "Seite 2 von 3"));
        zeilen.Add(Line(2, "Fondsübersicht"));
        zeilen.Add(alterJahrgang
            ? Line(2, "WKN", "Fonds", "Anteile", "Anteilspreise", "Fonds", "Zeitwert")
            : Line(2, "WKN", "Fonds", "Anteile", "Anteilspreis in", "Wert der"));

        foreach (var f in fonds)
        {
            zeilen.Add(alterJahrgang
                ? Line(2, f.Wkn, f.Name, f.Anteile, f.Preis, "EUR", f.Wert)
                : Line(2, f.Wkn, f.Name, f.Anteile, f.Preis, f.Wert));
        }

        zeilen.AddRange(
        [
            Line(2, "Anteilsguthaben:", anteilsguthaben),
            Line(2, "Garantierte finanzielle Leistungen zum Ablauf:"),
            Line(2, "Für Ihre fondsgebundene Lebensversicherung sind keine der Höhe nach garantierten"),
            Line(2, "Leistung bei Kündigung:"),
            Line(2, $"reichten Anteilsguthabens {anteilsguthaben} EUR ggf. reduziert um steuerliche Abzüge."),

            // Die Fußzeile eröffnete früher einen Block ohne Wert und legte die Summenprobe lahm.
            Line(2, "665463", "Fax: +49 40 21995 6999", "Registergericht: Offenbach", "5014"),
            Line(3, "Seite 3 von 3"),
        ]);

        return Content(zeilen);
    }

    private ExtractionResult ReadQuarterly(PdfContent inhalt)
        => extractor.Read(DocumentKindLibrary.QuarterlyStatement, inhalt);

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(IReadOnlyList<PdfLine> zeilen) => new()
    {
        PageCount = zeilen.Count == 0 ? 0 : zeilen.Max(z => z.Page),
        Lines = zeilen,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    private Dictionary<string, ReadValue> Read(DocumentKind art, PdfContent inhalt)
        => extractor.Extract(art, inhalt).ToDictionary(w => w.Rule.Key);

    // ── Typerkennung ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Typ kommt aus dem Text.
    /// </summary>
    /// <remarks>
    /// <see cref="DocumentKindLibrary.Detect"/> bekommt den Dateinamen gar nicht erst zu sehen.
    /// Das ist Absicht: die echte Datei des Nutzers heißt „statusreport 2024“ und meint den
    /// Stand zum 31.07.2025.
    /// </remarks>
    [Fact]
    public void Der_Typ_wird_am_Inhalt_erkannt()
    {
        Assert.Equal(DocumentKindLibrary.Statusreport, DocumentKindLibrary.Detect(Statusreport()));
        Assert.Equal(DocumentKindLibrary.QuarterlyStatement, DocumentKindLibrary.Detect(Quarterly()));
    }

    /// <summary>
    /// Zwei Statusreporte, zwei Typen.
    /// </summary>
    /// <remarks>
    /// Beide Papiere heißen „Statusreport“ und kommen vom selben Absender. Getrennt werden sie an
    /// dem, was sie führen: der klassische Vertrag einen Abschnitt „Wert der Versicherung“, der
    /// fondsgebundene eine Fondstabelle mit Anteilsguthaben. Eine Verwechslung ließe jedes
    /// Wertfeld leer — genau so hat der fondsgebundene Bericht vorher keinen Typ bekommen.
    /// </remarks>
    [Fact]
    public void Der_fondsgebundene_Statusreport_ist_ein_eigener_Typ()
    {
        Assert.Equal(
            DocumentKindLibrary.FundStatusreport,
            DocumentKindLibrary.Detect(FundStatusreport()));

        Assert.Equal(
            DocumentKindLibrary.Statusreport,
            DocumentKindLibrary.Detect(Statusreport()));
    }

    [Fact]
    public void Ein_fremdes_Dokument_bekommt_keinen_Typ()
    {
        var fremd = Content([Line(1, "Stromrechnung"), Line(1, "Verbrauch", "2.400 kWh")]);

        Assert.Null(DocumentKindLibrary.Detect(fremd));
    }

    // ── Statusreport ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Der_Statusreport_liest_seine_zehn_Werte()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.Equal(12345.67m, werte["rueckkauf"].Number);
        Assert.Equal(1234.56m, werte["ansammlung"].Number);
        Assert.Equal(13580.23m, werte["gesamt"].Number);
        Assert.Equal(20000.00m, werte["garantie"].Number);
        Assert.Equal(21234.56m, werte["ablaufwert"].Number);
        Assert.Equal(new DateOnly(2031, 8, 1), werte["ablauf"].Date);
        Assert.Equal(21234.56m, werte["todesfall"].Number);
        Assert.Equal(2500.00m, werte["bu"].Number);
        Assert.Equal(23.45m, werte["reserven"].Number);
        Assert.Equal(678.90m, werte["schluss"].Number);
    }

    /// <summary>
    /// Der Vermögenswert kommt aus „Wert der Versicherung“.
    /// </summary>
    /// <remarks>
    /// Das Dokument führt „Gesamtleistung“ viermal mit vier Beträgen. Ohne Abschnittsanker
    /// träfe die Suche den erstbesten — hier 21.234,56 €, eine Prognose auf das Jahr 2031, die
    /// dann als heutiger Vermögenswert im Dashboard stünde.
    /// </remarks>
    [Fact]
    public void Der_erreichte_Wert_kommt_aus_dem_richtigen_Abschnitt()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.Equal(13580.23m, werte["gesamt"].Number);
        Assert.Equal(2, werte["gesamt"].Page);
    }

    /// <summary>
    /// Jeder Wert weiß, auf welcher Seite er stand.
    /// </summary>
    [Fact]
    public void Jeder_Wert_traegt_seine_Herkunftsseite()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.Equal(1, werte["garantie"].Page);
        Assert.Equal(2, werte["rueckkauf"].Page);
        Assert.Equal(2, werte["bu"].Page);
    }

    /// <summary>
    /// Was nicht garantiert ist, ist als solches gekennzeichnet und nie ein Leitwert.
    /// </summary>
    /// <remarks>
    /// Das Dokument erklärt in drei Fußnoten, warum Bewertungsreserven und Schlussüberschüsse
    /// schwanken oder ganz entfallen können. Sie in eine Vermögenssumme zu nehmen hieße, etwas
    /// zu versprechen, was niemand versprochen hat.
    /// </remarks>
    [Fact]
    public void Nicht_Garantiertes_ist_soft_und_nie_lead()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.True(werte["reserven"].Rule.Soft);
        Assert.True(werte["schluss"].Rule.Soft);

        Assert.DoesNotContain(
            DocumentKindLibrary.Statusreport.Fields.Where(f => f.Soft), f => f.Lead);
    }

    /// <summary>
    /// Genau ein Feld wird übernommen — und es ist der erreichte Wert gesamt.
    /// </summary>
    [Fact]
    public void Uebernommen_wird_der_erreichte_Wert_gesamt()
    {
        var leitwerte = DocumentKindLibrary.Statusreport.Fields.Where(f => f.Lead).ToList();

        Assert.Single(leitwerte);
        Assert.Equal("gesamt", leitwerte[0].Key);
    }

    /// <summary>
    /// Die Rechenprobe erkennt eine verrutschte Wertspalte.
    /// </summary>
    /// <remarks>
    /// Der reale Anlass: in der Textebene des Originals steht die Wertspalte stellenweise um
    /// eine Zeile gegen die Beschriftungen versetzt. Ein Auslesen, das Zeilen zählt, liest dort
    /// die Abschnittsüberschrift als Rückkaufswert. Passt die Summe nicht, wird das gesagt und
    /// nicht gespeichert.
    /// </remarks>
    [Fact]
    public void Eine_verrutschte_Wertspalte_faellt_der_Rechenprobe_auf()
    {
        var werte = Read(
            DocumentKindLibrary.Statusreport,
            Statusreport(rueckkauf: "9.999,99 EUR"));

        var gesamt = werte["gesamt"];

        Assert.NotNull(gesamt.Warning);
        Assert.Contains("11.234,55", gesamt.Warning);
        Assert.True(gesamt.Confidence < 0.8);
    }

    /// <summary>
    /// Nennt das Dokument die Summe nicht, wird sie gerechnet — und sagt das.
    /// </summary>
    [Fact]
    public void Ohne_ausgewiesene_Summe_wird_sie_abgeleitet()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport(mitGesamt: false));

        var gesamt = werte["gesamt"];

        Assert.Equal(13580.23m, gesamt.Number);
        Assert.True(gesamt.Derived);
        Assert.Contains("gerechnet", gesamt.Warning);
    }

    /// <summary>
    /// Fließtext wird nicht zum Wert.
    /// </summary>
    /// <remarks>
    /// Auf Seite 3 des Originals beginnt ein Satz mit „Die Gesamtleistung Ihrer Versicherung
    /// setzt sich …“. Eine Beschriftung am Zeilenanfang und eine Zahl am Zeilenende sind zwei
    /// Bedingungen; erfüllt der Satz nur die erste, entsteht kein Wert.
    /// </remarks>
    [Fact]
    public void Ein_Satz_mit_dem_Beschriftungswort_wird_kein_Wert()
    {
        var mitSatz = Content(
        [
            Line(1, "Statusreport"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Gesamtleistung Ihrer Versicherung setzt sich aus zwei Teilen zusammen"),
            Line(2, "Rückkaufswert", "1.000,00 EUR"),
        ]);

        var werte = Read(DocumentKindLibrary.Statusreport, mitSatz);

        Assert.False(werte.ContainsKey("gesamt"));
        Assert.Equal(1000.00m, werte["rueckkauf"].Number);
    }

    /// <summary>
    /// Stichtag und Dokumentdatum sind zweierlei und werden auch so gelesen.
    /// </summary>
    [Fact]
    public void Stichtag_und_Dokumentdatum_bleiben_getrennt()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.Equal(new DateOnly(2025, 7, 31), werte["stichtag"].Date);
        Assert.Equal(new DateOnly(2025, 8, 12), werte["dokumentdatum"].Date);
    }

    [Fact]
    public void Die_Vertragsnummer_wird_gelesen()
    {
        var werte = Read(DocumentKindLibrary.Statusreport, Statusreport());

        Assert.Equal("77001122-01", werte["vertragsnummer"].Raw);
        Assert.Equal("Nordstern Lebensversicherung AG", werte["absender"].Raw);
    }

    // ── Statusreport fondsgebunden ─────────────────────────────────────────────────────────

    /// <summary>
    /// Das Anteilsguthaben ist der erreichte Wert.
    /// </summary>
    /// <remarks>
    /// Es steht unter der Fondstabelle und wiederholt sich im Satz zur Kündigung. Gelesen wird
    /// die Tabellenzeile: der Satz nennt denselben Betrag, aber in einem Fließtext, dessen
    /// Zeilenumbruch nicht vorhersehbar ist.
    /// </remarks>
    [Fact]
    public void Das_Anteilsguthaben_traegt_den_Wert()
    {
        var werte = Read(DocumentKindLibrary.FundStatusreport, FundStatusreport());

        Assert.Equal(10000.00m, werte["anteilsguthaben"].Number);
        Assert.True(werte["anteilsguthaben"].Rule.Lead);
        Assert.Equal(2, werte["anteilsguthaben"].Page);
    }

    /// <summary>
    /// Mindestschutz und aktuelle Leistung im Todesfall sind zwei Größen.
    /// </summary>
    /// <remarks>
    /// Der Mindestschutz entspricht der Beitragssumme, die aktuelle Leistung wächst mit dem
    /// Vertragskapital. In einem Feld zusammengelegt zeigte der Bericht die eine und ließe die
    /// andere verschwinden — und „Mindesttodesfallschutz“ beginnt nicht mit „Todesfallschutz“,
    /// weshalb die Beschriftung des älteren Jahrgangs hier nicht dazwischengreift.
    /// </remarks>
    [Fact]
    public void Todesfallschutz_und_Leistung_im_Todesfall_bleiben_getrennt()
    {
        var neu = Read(DocumentKindLibrary.FundStatusreport, FundStatusreport());

        Assert.Equal(43000.00m, neu["mindesttodesfall"].Number);
        Assert.Equal(45000.00m, neu["todesfall"].Number);
        Assert.Equal(43000.00m, neu["beitragssumme"].Number);
    }

    /// <summary>
    /// Der Jahrgang bis 2017 schreibt anders — und wird gelesen.
    /// </summary>
    /// <remarks>
    /// „Ihr aktueller Vertragsstand“, „Todesfallschutz“ ohne Mindestschutz, Beträge in „Euro“ und
    /// eine Beitragssumme, deren Beschriftung eine Zeile über ihrem Betrag steht.
    /// </remarks>
    [Fact]
    public void Der_aeltere_Jahrgang_wird_auch_gelesen()
    {
        var werte = Read(
            DocumentKindLibrary.FundStatusreport,
            FundStatusreport(alterJahrgang: true, stichtag: "31.12.2015"));

        Assert.Equal(10000.00m, werte["anteilsguthaben"].Number);
        Assert.Equal(36000.00m, werte["beitragssumme"].Number);
        Assert.Equal(36000.00m, werte["todesfall"].Number);
        Assert.False(werte.ContainsKey("mindesttodesfall"));
        Assert.Equal(new DateOnly(2015, 12, 31), werte["stichtag"].Date);
    }

    /// <summary>
    /// Die Leistung bei Berufsunfähigkeit ist ein Wort, kein Betrag.
    /// </summary>
    /// <remarks>
    /// Der Vertrag befreit von den Beiträgen und zahlt keine Rente. Als Geldfeld bliebe die
    /// Angabe leer, und niemand wüsste, ob sie fehlt oder keine ist.
    /// </remarks>
    [Fact]
    public void Die_Berufsunfaehigkeit_steht_im_Klartext()
    {
        var werte = Read(DocumentKindLibrary.FundStatusreport, FundStatusreport());

        Assert.Equal("Beitragsbefreiung", werte["bu"].Raw);
        Assert.Null(werte["bu"].Number);
    }

    /// <summary>
    /// Die Fondszeilen ergeben das Anteilsguthaben.
    /// </summary>
    /// <remarks>
    /// Die Probe des Dokuments gegen sich selbst. Die Fußzeile mit einer sechsstelligen Nummer
    /// darf dabei keinen Block eröffnen — sie trägt keinen Wert, und die Summenprobe fiel
    /// deshalb vorher ganz aus.
    /// </remarks>
    [Fact]
    public void Die_Fondszeilen_ergeben_das_Anteilsguthaben()
    {
        var gelesen = extractor.Read(DocumentKindLibrary.FundStatusreport, FundStatusreport());

        Assert.Equal(2, gelesen.Rows.Count);
        Assert.Equal("Musterfonds Welt", gelesen.Rows[0]["fonds"]?.Raw);
        Assert.Equal(6211.03m, gelesen.Rows[0]["fondswert"]?.Number);

        var probe = Assert.Single(gelesen.Proofs);
        Assert.True(probe.Passed);
        Assert.Contains("2 Zeilen", probe.Line);
    }

    /// <summary>
    /// Ein paar Cent Unterschied sind keine Unstimmigkeit.
    /// </summary>
    /// <remarks>
    /// Der Bericht summiert die ungerundeten Zeilenwerte und weist die gerundeten aus. Bei sechs
    /// Fonds stehen so zwei Cent Unterschied auf dem Papier — eine Probe, die daran scheitert,
    /// meldet die Rundung des Absenders als Lesefehler.
    /// </remarks>
    [Fact]
    public void Die_Rundung_des_Absenders_gilt_nicht_als_Fehler()
    {
        var gelesen = extractor.Read(
            DocumentKindLibrary.FundStatusreport,
            FundStatusreport(anteilsguthaben: "9.999,98"));

        Assert.True(Assert.Single(gelesen.Proofs).Passed);
        Assert.Equal(9999.98m, gelesen.Values.Single(w => w.Rule.Key == "anteilsguthaben").Number);
    }

    /// <summary>
    /// Eine fehlende Fondszeile fällt auf.
    /// </summary>
    /// <remarks>
    /// Im Jahrgang 2016 ist eine WKN in der Textebene verstümmelt, und die Zeile fehlt darum in
    /// der Tabelle. Die Summe zeigt es: sie ist um genau diese Zeile zu klein, und der Wert
    /// bekommt seinen Warnhinweis.
    /// </remarks>
    [Fact]
    public void Eine_fehlende_Fondszeile_faellt_auf()
    {
        var gelesen = extractor.Read(
            DocumentKindLibrary.FundStatusreport,
            FundStatusreport(
                fonds: [("TEST01", "Musterfonds Welt", "31,4706", "197,3600", "6.211,03")],
                anteilsguthaben: "10.000,00"));

        var probe = Assert.Single(gelesen.Proofs);
        Assert.False(probe.Passed);
        Assert.Contains("ausgewiesen sind", probe.Line);

        var wert = gelesen.Values.Single(w => w.Rule.Key == "anteilsguthaben");
        Assert.Equal(10000.00m, wert.Number);
        Assert.Contains("bitte prüfen", wert.Warning);
    }

    /// <summary>Eine einzelne Zeile heißt „1 Zeile“.</summary>
    [Fact]
    public void Der_Hinweis_zaehlt_die_Zeile_im_Singular()
    {
        var gelesen = extractor.Read(
            DocumentKindLibrary.FundStatusreport,
            FundStatusreport(
                fonds: [("TEST01", "Musterfonds Welt", "31,4706", "197,3600", "6.211,03")],
                anteilsguthaben: "10.000,00"));

        var wert = gelesen.Values.Single(w => w.Rule.Key == "anteilsguthaben");

        Assert.Contains("1 Zeile ergibt", wert.Warning);
        Assert.DoesNotContain("1 Zeilen", wert.Warning);
    }

    // ── Quartalsaufstellung ────────────────────────────────────────────────────────────────

    [Fact]
    public void Die_Quartalsaufstellung_liest_ihre_acht_Angaben()
    {
        var gelesen = ReadQuarterly(Quarterly());
        var zeile = Assert.Single(gelesen.Rows);
        var kopf = gelesen.Values.ToDictionary(w => w.Rule.Key);

        Assert.Equal(500m, zeile["nominale"]!.Number);
        Assert.Equal(100.500m, zeile["kurs"]!.Number);
        Assert.Equal(50250.00m, zeile["kurswert"]!.Number);
        Assert.Equal("IE00TEST0001", zeile["isin"]!.Raw);
        Assert.Equal("TEST99", zeile["wkn"]!.Raw);
        Assert.Equal("Weltfonds-Muster UCITS ETF", zeile["papier"]!.Raw);
        Assert.Equal("Wertpapierrechnung", zeile["verwahrart"]!.Raw);
        Assert.Equal("Luxemburg", zeile["lagerland"]!.Raw);
        Assert.Equal("1419", zeile["lagerstelle"]!.Raw);

        Assert.Equal("1032904213", kopf["referenz"].Raw);
        Assert.Equal(50250.00m, kopf["depotwert"].Number);
    }

    /// <summary>
    /// Drei Fonds ergeben drei Zeilen.
    /// </summary>
    /// <remarks>
    /// Der Grund für die Wiederholgruppe: vorher nahm der Extraktor je Feld den ersten Treffer
    /// im ganzen Dokument, und aus drei Positionen wurde eine — mit dem Wert der ersten.
    /// </remarks>
    [Fact]
    public void Drei_Positionen_ergeben_drei_Zeilen()
    {
        var gelesen = ReadQuarterly(Quarterly(
        [
            ("IE00TEST0001", "Weltfonds Muster", "152", "120,000", "18.240,00"),
            ("IE00TEST0002", "Schwellenland Muster", "175", "54,930", "9.612,75"),
            ("DE000TEST003", "Musterkonzern AG", "18", "341,600", "6.148,80"),
        ], gesamt: "34.001,55"));

        Assert.Equal(3, gelesen.Rows.Count);

        Assert.Equal("IE00TEST0002", gelesen.Rows[1]["isin"]!.Raw);
        Assert.Equal(175m, gelesen.Rows[1]["nominale"]!.Number);
        Assert.Equal("Musterkonzern AG", gelesen.Rows[2]["papier"]!.Raw);
        Assert.Equal(6148.80m, gelesen.Rows[2]["kurswert"]!.Number);
    }

    /// <summary>
    /// Geprüft wird zweifach: je Zeile und in Summe.
    /// </summary>
    /// <remarks>
    /// Je Zeile allein ginge eine fehlende Zeile durch, weil die übrigen für sich stimmen. Die
    /// Summe allein zeigte nicht, welche Zeile verrutscht ist.
    /// </remarks>
    [Fact]
    public void Die_Probe_laeuft_je_Zeile_und_in_Summe()
    {
        var gelesen = ReadQuarterly(Quarterly(
        [
            ("IE00TEST0001", "Weltfonds Muster", "152", "120,000", "18.240,00"),
            ("IE00TEST0002", "Schwellenland Muster", "175", "54,930", "9.612,75"),
            ("DE000TEST003", "Musterkonzern AG", "18", "341,600", "6.148,80"),
        ], gesamt: "34.001,55"));

        // Drei Zeilenproben plus die Summenprobe.
        Assert.Equal(4, gelesen.Proofs.Count);
        Assert.All(gelesen.Proofs, p => Assert.True(p.Passed));

        Assert.StartsWith("Weltfonds Muster:", gelesen.Proofs[0].Line);
        Assert.Contains("3 Zeilen = 34.001,55 EUR", gelesen.Proofs[^1].Line);
    }

    /// <summary>Eine verrutschte Zeile fällt einzeln heraus, nicht nur in der Summe.</summary>
    [Fact]
    public void Eine_verrutschte_Zeile_faellt_einzeln_auf()
    {
        var gelesen = ReadQuarterly(Quarterly(
        [
            ("IE00TEST0001", "Weltfonds Muster", "152", "120,000", "18.240,00"),
            ("IE00TEST0002", "Schwellenland Muster", "175", "54,930", "9.999,99"),
        ], gesamt: "28.239,99"));

        var gescheitert = gelesen.Proofs.Where(p => !p.Passed).ToList();

        Assert.Single(gescheitert);
        Assert.StartsWith("Schwellenland Muster:", gescheitert[0].Line);
    }

    /// <summary>Eine fehlende Zeile fällt nur der Summenprobe auf.</summary>
    [Fact]
    public void Eine_fehlende_Zeile_faellt_der_Summe_auf()
    {
        var gelesen = ReadQuarterly(Quarterly(
        [
            ("IE00TEST0001", "Weltfonds Muster", "152", "120,000", "18.240,00"),
        ], gesamt: "34.001,55"));

        // Die eine Zeile stimmt für sich — nur die Summe zeigt, dass zwei fehlen.
        Assert.True(gelesen.Proofs[0].Passed);
        Assert.False(gelesen.Proofs[^1].Passed);
        Assert.Contains("ausgewiesen sind 34.001,55", gelesen.Proofs[^1].Line);
    }

    /// <summary>
    /// Der Stichtag des Bestands, nicht das Datum des Schreibens.
    /// </summary>
    /// <remarks>
    /// Beide stehen im Dokument und liegen zwei Wochen auseinander. Maßgeblich ist der Stichtag:
    /// er sagt, wann der Bestand so war.
    /// </remarks>
    [Fact]
    public void Der_Stichtag_der_Aufstellung_ist_der_Bestandsstichtag()
    {
        var werte = Read(DocumentKindLibrary.QuarterlyStatement, Quarterly());

        Assert.Equal(new DateOnly(2026, 6, 30), werte["stichtag"].Date);
        Assert.Equal(new DateOnly(2026, 7, 15), werte["dokumentdatum"].Date);
    }

    /// <summary>
    /// Nominale × Kurs muss den ausgewiesenen Kurswert ergeben.
    /// </summary>
    /// <remarks>
    /// Die Bestandszeile ist eine Tabellenzeile mit sechs Spalten; Stück, Kurs und Kurswert
    /// stehen darin nebeneinander und ohne Beschriftung. Die Probe ist der einzige Hinweis
    /// darauf, dass jede Zahl in ihrer eigenen Spalte gelandet ist.
    /// </remarks>
    [Fact]
    public void Nominale_mal_Kurs_muss_den_Kurswert_ergeben()
    {
        Assert.Null(ReadQuarterly(Quarterly()).Rows[0]["kurswert"]!.Warning);

        var falsch = ReadQuarterly(Quarterly(kurswert: "49.000,00"));

        Assert.NotNull(falsch.Rows[0]["kurswert"]!.Warning);
        Assert.True(falsch.Rows[0]["kurswert"]!.Confidence < 0.8);
    }

    /// <summary>
    /// Aus dem Depot wird der Kurswert übernommen, nicht der Depotwert der Zusammenfassung.
    /// </summary>
    [Fact]
    public void Uebernommen_werden_Nominale_und_Kurswert()
    {
        var art = DocumentKindLibrary.QuarterlyStatement;

        Assert.Equal(["nominale", "kurswert"], art.Repeat!.Fields.Where(f => f.Lead).Select(f => f.Key));
        Assert.Equal(["depotwert"], art.Fields.Where(f => f.Lead).Select(f => f.Key));
    }

    // ── Werte lesen ────────────────────────────────────────────────────────────────────────

    private static DocumentFieldRule Geldfeld
        => DocumentKindLibrary.FundStatusreport.Fields.Single(f => f.Key == "anteilsguthaben");

    private static DocumentFieldRule Datumsfeld
        => DocumentKindLibrary.FundStatusreport.Fields.Single(f => f.Key == "stichtag");

    [Theory]
    [InlineData("10.000,00", 10000)]
    [InlineData("6.099,65 Euro", 6099.65)]
    [InlineData("40.883,40 EUR", 40883.40)]
    [InlineData("24 782,58", 24782.58)]
    [InlineData("1.234", 1234)]
    [InlineData("763", 763)]
    public void Ein_deutscher_Betrag_wird_gelesen(string roh, double erwartet)
        => Assert.Equal((decimal)erwartet, DocumentFieldExtractor.Read(Geldfeld, roh)?.Number);

    /// <summary>
    /// Ein Punkt mit zwei Stellen dahinter ist im Deutschen kein Betrag.
    /// </summary>
    /// <remarks>
    /// Als Tausenderpunkt bräuchte er drei Stellen, als Dezimaltrenner ein Komma. Die deutsche
    /// Kultur liest ihn trotzdem — sie prüft die Gruppengröße nicht — und macht aus dem
    /// eingescannten „43 866.12“ den Betrag 4.386.612: das Hundertfache, ohne einen Hinweis.
    /// Gefunden am Scan eines Jahrgangs, in dem die Texterkennung Punkt und Komma vertauscht hat.
    /// </remarks>
    [Theory]
    [InlineData("43 866.12")]
    [InlineData("43866.12")]
    [InlineData("1.261.37")]
    [InlineData("136.7500")]
    public void Ein_Punkt_mit_falscher_Gruppe_ergibt_keinen_Betrag(string roh)
        => Assert.Null(DocumentFieldExtractor.Read(Geldfeld, roh));

    [Theory]
    [InlineData("31.12.2024")]
    [InlineData("31. Dezember 2012")]
    public void Ein_Datum_wird_in_beiden_Schreibweisen_gelesen(string roh)
        => Assert.Equal(
            new DateOnly(int.Parse(roh[^4..]), 12, 31),
            DocumentFieldExtractor.Read(Datumsfeld, roh)?.Date);

    /// <summary>
    /// Ein Komma im Datum kommt aus der Texterkennung und wird berichtigt.
    /// </summary>
    /// <remarks>
    /// „31,12.2023“ steht so im Scan. Ein Komma trennt in einem deutschen Datum nie, und an
    /// dieser Stelle lässt es keine zweite Lesart zu — anders als beim Betrag, wo der Punkt
    /// wirklich zwei Bedeutungen hat. Der Rohtext bleibt, wie er auf dem Papier steht.
    /// </remarks>
    [Fact]
    public void Ein_Komma_im_Datum_wird_berichtigt()
    {
        var gelesen = DocumentFieldExtractor.Read(Datumsfeld, "31,12.2023");

        Assert.Equal(new DateOnly(2023, 12, 31), gelesen?.Date);
        Assert.Equal("31,12.2023", gelesen?.Raw);
    }

    /// <summary>
    /// Der Stichtag darf auch aus der Betreffzeile kommen.
    /// </summary>
    /// <remarks>
    /// Im Scan von 2023 steht in der Vertragsstandzeile „Vortragsstand“ — verlesen. Die
    /// Betreffzeile „Ihr Statusreport zum …“ trägt denselben Stichtag und ist unbeschädigt.
    /// Ohne Stichtag wird kein Wert übernommen; deshalb zählt hier die zweite Quelle.
    /// </remarks>
    [Fact]
    public void Der_Stichtag_kommt_notfalls_aus_der_Betreffzeile()
    {
        var verlesen = Content(
        [
            Line(1, "Nordstern Lebensversicherung AG"),
            Line(1, "Versicherungsnummer:", "77001122-02"),
            Line(1, "Ihr Statusreport zum 31,12.2023"),
            Line(1, "Ihr Vortragsstand zum 31.12.2023"),
            Line(2, "Fondsübersicht"),
            Line(2, "TEST01", "Musterfonds Welt", "31,4706", "197,3600", "6.211,03"),
            Line(2, "Anteilsguthaben:", "6.211,03"),
        ]);

        var werte = Read(DocumentKindLibrary.FundStatusreport, verlesen);

        Assert.Equal(new DateOnly(2023, 12, 31), werte["stichtag"].Date);
        Assert.Equal(6211.03m, werte["anteilsguthaben"].Number);
    }

    // ── Beschaffenheit ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Textebene hinter Seitenbildern senkt die Sicherheit.
    /// </summary>
    /// <remarks>
    /// Sie ist lesbar, aber nicht das Sichtbare: was auf dem Papier steht, ist das Bild, und der
    /// Text daneben kann davon abweichen. Das ist kein Grund, ihn abzulehnen, wohl aber einer,
    /// es dazuzuschreiben.
    /// </remarks>
    [Fact]
    public void Text_hinter_Seitenbildern_zaehlt_weniger_als_sichtbarer()
    {
        var sichtbar = Statusreport();
        var versteckt = sichtbar with { TextIsInvisible = true, ImageCount = 3 };

        var offen = Read(DocumentKindLibrary.Statusreport, sichtbar)["rueckkauf"].Confidence;
        var hinter = Read(DocumentKindLibrary.Statusreport, versteckt)["rueckkauf"].Confidence;

        Assert.Equal(1.0, offen);
        Assert.True(hinter < offen);
        Assert.Contains("Seitenbilder mit Textebene darunter", versteckt.Note);
    }

    // ── Die sichtbare Rechenprobe (Abschnitt 15.6) ─────────────────────────────────────────

    /// <summary>
    /// Die Probe wird zum Ergebnis, nicht nur zur stillen Prüfung.
    /// </summary>
    /// <remarks>
    /// Sie steht vor der Übernahme auf dem Schirm. Wer eine Zahl in sein Vermögen schreibt,
    /// soll sehen, woran sie geprüft wurde — eine Prüfung, die nur im Verborgenen stattfindet,
    /// überzeugt niemanden.
    /// </remarks>
    [Fact]
    public void Die_Rechenprobe_kommt_als_Satz_heraus()
    {
        var ergebnis = extractor.Read(DocumentKindLibrary.Statusreport, Statusreport());
        var probe = Assert.Single(ergebnis.Proofs);

        Assert.True(probe.Passed);
        Assert.Equal(
            "12.345,67 EUR + 1.234,56 EUR = 13.580,23 EUR — stimmt mit dem ausgewiesenen Wert überein.",
            probe.Line);

        Assert.Contains("Zeilenversatz", probe.Why);
    }

    [Fact]
    public void Die_Probe_der_Aufstellung_nennt_Nominale_mal_Kurs()
    {
        var probe = ReadQuarterly(Quarterly()).Proofs[0];

        Assert.True(probe.Passed);
        Assert.Contains("500 \u00D7 100,500 = 50.250,00 EUR", probe.Line);
    }

    /// <summary>
    /// Geht sie nicht auf, sagt sie beide Zahlen.
    /// </summary>
    /// <remarks>
    /// „Ergibt X, ausgewiesen ist Y“ — nur so kann der Nutzer entscheiden, welche der beiden
    /// stimmt. Eine Meldung „Probe fehlgeschlagen“ ließe ihn im Dunkeln.
    /// </remarks>
    [Fact]
    public void Eine_gescheiterte_Probe_nennt_beide_Zahlen()
    {
        var ergebnis = extractor.Read(
            DocumentKindLibrary.Statusreport, Statusreport(rueckkauf: "9.999,99 EUR"));

        var probe = Assert.Single(ergebnis.Proofs);

        Assert.False(probe.Passed);
        Assert.Contains("11.234,55", probe.Line);
        Assert.Contains("13.580,23", probe.Line);
    }

    /// <summary>Ohne die Teile gibt es keine Probe — und keine Behauptung darüber.</summary>
    [Fact]
    public void Ohne_die_Summanden_entsteht_keine_Probe()
    {
        var knapp = Content(
        [
            Line(1, "Statusreport"),
            Line(2, "Wert der Versicherung"),
            Line(2, "Gesamtleistung*", "13.580,23 EUR"),
        ]);

        Assert.Empty(extractor.Read(DocumentKindLibrary.Statusreport, knapp).Proofs);
    }

    /// <summary>
    /// Die Beschaffenheit steht als Beschreibung da, nicht als Ja oder Nein.
    /// </summary>
    [Fact]
    public void Der_Befund_beschreibt_die_Textebene()
    {
        var sichtbar = Statusreport();
        var versteckt = sichtbar with { TextIsInvisible = true, ImageCount = 4 };

        Assert.Equal("durchsuchbarer Text", sichtbar.Note);
        Assert.Equal("Seitenbilder mit Textebene darunter", versteckt.Note);
    }

    [Fact]
    public void Ohne_Text_gibt_es_keinen_Befund()
    {
        var leer = Content([]) with { PageCount = 4, ImageCount = 4, Lines = [] };

        Assert.False(leer.HasTextLayer);
        Assert.Contains("nur Bild", leer.Note);
        Assert.Null(DocumentKindLibrary.Detect(leer));
    }
}
