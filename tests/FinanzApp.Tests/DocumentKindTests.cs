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
        string stueck = "500", string kurs = "100,500", string kurswert = "50.250,00")
        => Content(
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
            Line(1, "Stück", stueck, "WKN: TEST99", $"EUR {kurs}", kurswert, "EUR"),
            Line(1, "ISIN:", "IE00TEST0001"),
            Line(1, "Weltfonds-Muster UCITS ETF"),
            Line(1, "Registered Shs USD (Acc) o.N."),
            Line(1, "Verwahrart:", "Wertpapierrechnung"),
            Line(1, "Lagerstelle:", "1419"),
            Line(1, "Lagerland:", "Luxemburg"),
            Line(1, "Depotwert", kurswert, "EUR"),
            Line(2, "Seite 2/2"),
        ]);

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

    // ── Quartalsaufstellung ────────────────────────────────────────────────────────────────

    [Fact]
    public void Die_Quartalsaufstellung_liest_ihre_acht_Angaben()
    {
        var werte = Read(DocumentKindLibrary.QuarterlyStatement, Quarterly());

        Assert.Equal(500m, werte["nominale"].Number);
        Assert.Equal(100.500m, werte["kurs"].Number);
        Assert.Equal(50250.00m, werte["kurswert"].Number);
        Assert.Equal("IE00TEST0001", werte["isin"].Raw);
        Assert.Equal("TEST99", werte["wkn"].Raw);
        Assert.Equal("Weltfonds-Muster UCITS ETF", werte["papier"].Raw);
        Assert.Equal("Wertpapierrechnung", werte["verwahrart"].Raw);
        Assert.Equal("Luxemburg", werte["lagerland"].Raw);
        Assert.Equal("1419", werte["lagerstelle"].Raw);
        Assert.Equal("1032904213", werte["referenz"].Raw);
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
        var stimmt = Read(DocumentKindLibrary.QuarterlyStatement, Quarterly());
        Assert.Null(stimmt["kurswert"].Warning);

        var falsch = Read(
            DocumentKindLibrary.QuarterlyStatement,
            Quarterly(kurswert: "49.000,00"));

        Assert.NotNull(falsch["kurswert"].Warning);
        Assert.True(falsch["kurswert"].Confidence < 0.8);
    }

    /// <summary>
    /// Aus dem Depot wird der Kurswert übernommen, nicht der Depotwert der Zusammenfassung.
    /// </summary>
    [Fact]
    public void Uebernommen_werden_Nominale_und_Kurswert()
    {
        var leitwerte = DocumentKindLibrary.QuarterlyStatement.Fields
            .Where(f => f.Lead).Select(f => f.Key).ToList();

        Assert.Equal(["nominale", "kurswert"], leitwerte);
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
        Assert.Contains("hinter Seitenbildern", versteckt.Note);
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
