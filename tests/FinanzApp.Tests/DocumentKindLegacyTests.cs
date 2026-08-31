using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Der Statusreport in der Schreibweise der älteren Jahrgänge.
/// </summary>
/// <remarks>
/// <para>Derselbe Absender, dasselbe Produkt, elf Jahresberichte — und die Textebene sieht in der
/// ersten Hälfte anders aus als in der zweiten. Aus den älteren Berichten kam <b>kein einziger
/// Wert</b> heraus: die Währung steht ausgeschrieben, der Abschnitt heißt kürzer, links vor
/// Überschrift und Beschriftung sitzt eine Druckmarke, und der Stichtag nennt den Monat mit
/// Namen.</para>
/// <para>Die Zahlen hier sind erfunden. Geprüft wird der <em>Aufbau</em>, nicht der Inhalt eines
/// bestimmten Belegs — die echten Berichte gehören dem Benutzer und haben in keinem Repository
/// etwas zu suchen.</para>
/// </remarks>
public sealed class DocumentKindLegacyTests
{
    private readonly DocumentFieldExtractor extractor = new();

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(IReadOnlyList<PdfLine> zeilen) => new()
    {
        PageCount = zeilen.Count == 0 ? 0 : zeilen.Max(z => z.Page),
        Lines = zeilen,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>
    /// Ein Statusreport im Aufbau der Jahrgänge bis 2018.
    /// </summary>
    /// <remarks>
    /// Vier Eigenheiten, jede einzeln ein Grund fürs Scheitern: „Euro“ statt „EUR“, „Leistung im
    /// Erlebensfall“ ohne Szenario, die Druckmarke links („06“, „01191591“) und der Stichtag mit
    /// ausgeschriebenem Monat.
    /// </remarks>
    private static PdfContent Alt(
        string stichtag = "31. Juli 2014",
        string rueckkauf = "6.000,00 Euro",
        string ansammlung = "700,00 Euro",
        string gesamt = "6.700,00 Euro",
        string bu = "monatliche garantierte Berufsunfähigkeitsrente")
        => Content(
        [
            Line(1, "Kundenservice der Musterversicherung"),
            Line(1, "August 2014"),
            Line(1, "Ihr Statusreport zum " + stichtag),
            Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport zum Vertragsstand Ihrer"),
            Line(1, "Ihr aktueller Vertragsstand zum " + stichtag),
            Line(1, "Versicherungsnummer", "01234567-01"),

            // Ohne Szenario im Namen — und mit Druckmarke vor der Beschriftung.
            Line(1, "Leistung im Erlebensfall"),
            Line(1, "01191591", "garantierte Erlebensfallleistung", "20.000,00 Euro"),
            Line(1, "bisher erreichter Wert der Überschussbeteiligung", ansammlung),
            Line(1, "Gesamtleistung", "20.700,00 Euro"),

            // Druckmarke vor der Überschrift.
            Line(1, "06", "Leistung im Todesfall"),
            Line(1, "Todesfallleistung", "20.000,00 Euro"),
            Line(1, "erreichter Wert der Überschussbeteiligung", ansammlung),
            Line(1, "Gesamtleistung", "20.700,00 Euro"),

            Line(1, "Wert der Versicherung"),
            Line(1, "Rückkaufswert (exkl Bewertungsreserve)", rueckkauf),
            Line(1, "erreichter Wert der Überschussbeteiligung", ansammlung),
            Line(1, "Gesamtleistung", gesamt),
            Line(1, "Deckungskapital (exkl Bewertungsreserve)", rueckkauf),

            Line(2, "Leistung bei Berufsunfähigkeit"),
            Line(2, "Beitragsbefreiung bei Berufsunfähigkeit", "vereinbart"),
            Line(2, bu, "3.000,00 Euro"),
            Line(2, "Mit freundlichen Grüßen"),

            Line(2, "Musterversicherung Lebensversicherung AG•Postfach103969"),
        ]);

    private Dictionary<string, ReadValue> Lies(PdfContent inhalt)
        => extractor.Extract(DocumentKindLibrary.Statusreport, inhalt).ToDictionary(w => w.Rule.Key);

    /// <summary>Der Typ wird auch im älteren Aufbau erkannt.</summary>
    [Fact]
    public void Der_alte_Aufbau_wird_als_Statusreport_erkannt()
        => Assert.Equal(DocumentKindLibrary.Statusreport, DocumentKindLibrary.Detect(Alt()));

    /// <summary>
    /// Die ausgeschriebene Währung wird gelesen.
    /// </summary>
    /// <remarks>
    /// Der eigentliche Fehler: das Wort „EUR“ wurde aus dem Text entfernt, und aus „6.000,00
    /// Euro“ blieb „6.000,00 o“ übrig. Kein Betrag war lesbar, kein Wert übernehmbar.
    /// </remarks>
    [Fact]
    public void Betraege_mit_ausgeschriebenem_Euro_werden_gelesen()
    {
        var werte = Lies(Alt());

        Assert.Equal(6000.00m, werte["rueckkauf"].Number);
        Assert.Equal(700.00m, werte["ansammlung"].Number);
        Assert.Equal(6700.00m, werte["gesamt"].Number);
    }

    /// <summary>
    /// Der Abschnitt ohne Szenario im Namen trägt seine Felder trotzdem.
    /// </summary>
    /// <remarks>
    /// Bis 2018 hieß er „Leistung im Erlebensfall“, seit 2019 „Leistung im Erlebensfall zum
    /// Ablauf“. Beide Schreibweisen stehen in der Regel, die genauere zuerst.
    /// </remarks>
    [Fact]
    public void Die_Erlebensfallleistung_kommt_aus_dem_kuerzeren_Abschnitt()
        => Assert.Equal(20000.00m, Lies(Alt())["garantie"].Number);

    /// <summary>
    /// Eine Druckmarke vor der Überschrift beendet den Abschnitt nicht.
    /// </summary>
    /// <remarks>
    /// „06 · Leistung im Todesfall“: wer nur den Zeilenanfang prüft, findet die Überschrift
    /// nicht, und jedes Feld darunter bleibt leer.
    /// </remarks>
    [Fact]
    public void Eine_Druckmarke_vor_der_Ueberschrift_stoert_nicht()
        => Assert.Equal(20700.00m, Lies(Alt())["todesfall"].Number);

    /// <summary>
    /// Der Stichtag mit ausgeschriebenem Monat wird gelesen — mit und ohne Leerzeichen.
    /// </summary>
    /// <remarks>
    /// Ohne Stichtag wird nichts übernommen: ein Jahresstand ohne Datum sähe im Vermögen aus wie
    /// ein Tageskurs. Er ist die Pflichtangabe, und genau sie fehlte.
    /// </remarks>
    [Theory]
    [InlineData("31. Juli 2014", 2014)]
    [InlineData("31.Juli 2015", 2015)]
    [InlineData("31.07.2016", 2016)]
    public void Der_Stichtag_wird_in_jeder_Schreibweise_gelesen(string geschrieben, int jahr)
        => Assert.Equal(new DateOnly(jahr, 7, 31), Lies(Alt(stichtag: geschrieben))["stichtag"].Date);

    /// <summary>
    /// Ein Leerzeichen als Tausenderpunkt wird überlesen.
    /// </summary>
    /// <remarks>
    /// Die Textebene setzt stellenweise ein Leerzeichen, wo der Punkt stand („1 111,41“). Ein
    /// Betrag trägt nie ein Leerzeichen, also darf es weg.
    /// </remarks>
    [Fact]
    public void Ein_Leerzeichen_im_Tausenderblock_wird_ueberlesen()
        => Assert.Equal(1111.41m, Lies(Alt(ansammlung: "1 111,41 Euro"))["ansammlung"].Number);

    /// <summary>
    /// Die jährliche Rente landet nicht im Monatsfeld.
    /// </summary>
    /// <remarks>
    /// Mehrere Jahrgänge weisen die Berufsunfähigkeitsrente jährlich aus. Sie ins Monatsfeld zu
    /// lesen wäre eine falsche Zahl in einem richtigen Feld — zwölfmal zu hoch, und niemand sähe
    /// es der Zeile an.
    /// </remarks>
    [Fact]
    public void Die_jaehrliche_Rente_bekommt_ihr_eigenes_Feld()
    {
        var jaehrlich = Lies(Alt(bu: "jährliche Berufsunfähigkeitsrente"));

        Assert.False(jaehrlich.ContainsKey("bu"));
        Assert.Equal(3000.00m, jaehrlich["bujahr"].Number);

        var monatlich = Lies(Alt(bu: "monatliche Berufsunfähigkeitsrente"));

        Assert.Equal(3000.00m, monatlich["bu"].Number);
        Assert.False(monatlich.ContainsKey("bujahr"));
    }

    /// <summary>Der Absender wird auch erkannt, wenn die Anschrift hinten anstößt.</summary>
    [Fact]
    public void Der_Absender_darf_hinten_anstossen()
        => Assert.Equal("Musterversicherung Lebensversicherung AG", Lies(Alt())["absender"].Raw);

    /// <summary>
    /// Die Rechenprobe schreibt die gelesenen Zahlen und nicht die Schreibweise des Papiers.
    /// </summary>
    /// <remarks>
    /// Sonst stünde dort „6.000,00 Euro + 700,00 Euro = 6.700,00 EUR“ — dieselbe Rechnung in zwei
    /// Sprachen, und die Probe soll die Rechnung zeigen.
    /// </remarks>
    [Fact]
    public void Die_Probe_schreibt_die_Zahlen_einheitlich()
    {
        var probe = Assert.Single(
            extractor.Read(DocumentKindLibrary.Statusreport, Alt()).Proofs);

        Assert.True(probe.Passed);
        Assert.Equal(
            "6.000,00 EUR + 700,00 EUR = 6.700,00 EUR — stimmt mit dem ausgewiesenen Wert überein.",
            probe.Line);
    }
}
