using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Leseregel für Gehaltsabrechnungen.
/// </summary>
/// <remarks>
/// <para><b>Diese Belege kommen nur als Scan.</b> Die PDFs tragen keine Textebene — kein
/// <c>/Font</c>, ein einzelnes JPEG —, und was gelesen wird, stammt deshalb immer aus einer
/// Texterkennung. Das enge DATEV-Tabellenformular verliert dabei Zeichen. Die Vorlage hier
/// baut genau das nach: zerfallene Beschriftungen, Werte in der Folgezeile, Lesefehler in
/// Wörtern, die kein Feld benutzt.</para>
/// <para><b>Die Falle, um die es geht:</b> „Gesamt-Brutto“ steht zweimal im Papier. Oben für
/// den abgerechneten Monat, unten in der Verdienstbescheinigung <em>aufgelaufen übers Jahr</em>.
/// Eine Regel, die nach der Beschriftung sucht, trifft die falsche und schreibt ein
/// Jahresbrutto als Monatsgehalt fort.</para>
/// <para>Die Zahlen und Namen hier sind erfunden und gegen den echten Bestand gehalten.</para>
/// </remarks>
public sealed class PayslipKindTests
{
    private readonly DocumentFieldExtractor extractor = new();

    // ── Die Vorlage ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Abrechnung im Aufbau des Originals, samt der Lesefehler der Erkennung.
    /// </summary>
    /// <param name="monatsbrutto">
    /// Steht im Original <em>unter</em> der Beschriftung, in der Zeile
    /// „Steuer/Sozialversicherung“ — nicht daneben.
    /// </param>
    /// <param name="jahresbrutto">
    /// Die Wiederholung in der Verdienstbescheinigung, aufgelaufen. Sie trägt dieselbe
    /// Beschriftung und einen ganz anderen Betrag.
    /// </param>
    private static PdfContent Abrechnung(
        string monatsbrutto = "4.827,50",
        string jahresbrutto = "28.965,00",
        string auszahlung = "2.913,64",
        string netto = "3.012,80",
        string? sonderzahlung = null,
        string kopfzeile = "Abrechnun der Brutto/Netto-Bezü e für Juni 2026 24.06.2026 1",
        bool mitIban = true)
    {
        List<PdfLine> zeilen =
        [
            Line(1, "RAA/RCP 12345/1/26"),
            Line(1, kopfzeile),
            Line(1, "Personal-Nr. Geburtsdatum tKl Faktor Ki.Frbtr. Konfession"),

            // Das Eintrittsdatum steht im Original über dem Abrechnungsdatum. Ein Muster, das
            // nur nach „irgendeinem Datum“ sucht, nimmt dieses — und schriebe das Jahr des
            // Arbeitsbeginns als Abrechnungsmonat fort.
            Line(1, "Eintritt Austritt Arb. Std. Urlaub Std."),
            Line(1, "01.10.2019"),
            Line(1, "Musterbetrieb GmbH 12345 Musterstadt"),
            Line(1, "Erika Mustermann"),
            Line(1, "Musterweg 7"),
            Line(1, "12345 Musterstadt"),

            Line(1, "Brutto-Bezüge"),
            Line(1, "Lohnart Bezeichnung Menge Faktor Prozentsatz st V"),
            Line(1, "100 GEHALT L L J 4.500,00"),
            Line(1, "200 Gutschein F J 50,00"),
            Line(1, "300 Privat fahrten L L J 277,50"),
        ];

        if (sonderzahlung is { Length: > 0 } sonstiger)
        {
            zeilen.Add(Line(1, sonstiger));
        }

        zeilen.AddRange(
        [

            // Die Beschriftung zerfällt, der Wert steht in der nächsten Zeile. Genau so liest
            // die Erkennung es in den echten Belegen.
            Line(1, "Ges amt-B r utt0"),
            Line(1, $"Steuer/Sozialversicherung {monatsbrutto}"),

            Line(1, "Steuer-Brutto Lohnsteuer Kirchensteuer Solidaritätszuschlag"),
            Line(1, "L 3.105,80 412,75 00000 22,70"),
            Line(1, "sv# W -Brutto RV-Brutto AV Brutto PW Brutto KV-Beitrag RV•BOit"),

            Line(1, "Netto-Verdienst"),
            Line(1, $"Verdienstbescheinigung Netto-Bezüge/Netto•Abzüge {netto}"),

            // Dieselbe Beschriftung, der Jahreswert. Hier beginnt die Verdienstbescheinigung.
            Line(1, $"Gesam t- Brutto {jahresbrutto} SV-Brutto 24.500,00 Bezeichnung Betrag"),
            Line(1, "Steuer-Brutto 27.950,00 KV-Beitrag 7312 Gutschein 50,00"),
            Line(1, "Lohnsteuer 2.476,50 RV-Beitrag 1.288,50 Geldw. Vorteil PKW 277,50"),

            Line(1, "Musterbank SV-AG-Anteil zus. AG-Kosten Gesamtkosten Auszahlungsbetrag"),
            Line(1, "Bank"),
        ]);

        if (mitIban)
        {
            // Zwei Beträge in der Zeile, und der Auszahlungsbetrag ist der letzte. Die
            // Spalten davor — SV-AG-Anteil, AG-Kosten, Gesamtkosten — tragen im Original
            // ebenfalls Geld; in den echten Belegen verliert die Erkennung dort nur gerade das
            // Komma. Läse sie besser, stünden hier zwei lesbare Beträge, und ohne den
            // Zeilenende-Anker gewanne der erste.
            zeilen.Add(Line(1, $"DE02 1234 5678 9012 3456 78 1.187,45 {auszahlung}"));
        }
        else
        {
            // Der echte Ausfall: Die Erkennung bricht die Zeile hinter den ersten Ziffern ab.
            zeilen.Add(Line(1, "Konto DE02 1234"));
        }

        zeilen.Add(Line(1, "DATEV"));
        zeilen.Add(Line(1, "Form-Nr. LOGN42 -"));

        return Content(zeilen);
    }

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(IReadOnlyList<PdfLine> zeilen) => new()
    {
        PageCount = 1,
        ImageCount = 1,

        // Der Text liegt hinter einem Seitenbild — so kommt dieser Beleg immer herein.
        TextIsInvisible = true,
        Lines = zeilen,
    };

    private Dictionary<string, ReadValue> Lies(PdfContent inhalt)
        => extractor.Read(DocumentKindLibrary.Payslip, inhalt)
            .Values.ToDictionary(w => w.Rule.Key, w => w);

    // ── Die Erkennung ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Eine_Abrechnung_wird_als_solche_erkannt()
    {
        var art = DocumentKindLibrary.Detect(Abrechnung());

        Assert.NotNull(art);
        Assert.Equal("gehaltsabrechnung", art.Key);
        Assert.Equal(DocumentArea.Work, art.Area);
        Assert.Equal(DocumentTargetKind.Employment, art.Target);
    }

    /// <summary>
    /// <b>Beide Kennzeichen sind nötig.</b> „Lohnsteuer“ allein steht auch auf einem
    /// Steuerbescheid; „Netto-Verdienst“ ist die Überschrift, die nur eine Abrechnung führt.
    /// Eines davon genügen zu lassen hieße, fremde Belege einzusammeln.
    /// </summary>
    [Fact]
    public void Ein_Beleg_mit_nur_einem_Kennzeichen_wird_nicht_erkannt()
    {
        PdfContent nurSteuer = Content(
        [
            Line(1, "Bescheid über Einkommensteuer"),
            Line(1, "Lohnsteuer 1.234,00"),
            Line(1, "Kirchensteuer 98,70"),
        ]);

        Assert.Null(DocumentKindLibrary.Detect(nurSteuer));
    }

    /// <summary>Eine Abrechnung berichtet — sie verlangt nichts.</summary>
    /// <remarks>
    /// Das Kennzeichen hält auch das Sprachmodell von ihr fern. Ein Beleg, in dem
    /// „Lohnsteuer“, „Pfändung“ und „Darlehen Rest“ stehen, läse sich sonst wie eine
    /// Forderung.
    /// </remarks>
    [Fact]
    public void Eine_Abrechnung_verlangt_nichts()
        => Assert.True(DocumentKindLibrary.Payslip.IsNotice);

    // ── Die Werte ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Der Fall, um den es geht.</b> Das Monatsbrutto steht oben, das aufgelaufene
    /// Jahresbrutto unten — beide unter derselben Beschriftung. Gelesen wird der Monat.
    /// </summary>
    [Fact]
    public void Gelesen_wird_das_Monatsbrutto_und_nicht_das_Jahresbrutto()
    {
        var werte = Lies(Abrechnung(monatsbrutto: "4.827,50", jahresbrutto: "28.965,00"));

        Assert.Equal(4827.50m, werte["brutto"].Number);
        Assert.NotEqual(28965.00m, werte["brutto"].Number);
    }

    [Fact]
    public void Der_Auszahlungsbetrag_kommt_aus_der_Zeile_mit_der_Iban()
    {
        var werte = Lies(Abrechnung(auszahlung: "2.913,64"));

        Assert.Equal(2913.64m, werte["auszahlung"].Number);
    }

    /// <remarks>
    /// Dass es das <em>richtige</em> Datum ist, deckt
    /// <see cref="Ohne_Kopfzeile_fehlt_nur_das_Datum"/>: Dort fällt die Kopfzeile weg, und ein
    /// Muster ohne Monatsnamen griffe nach dem Eintrittsdatum weiter oben. Hier steht nur, dass
    /// überhaupt gelesen wird — beide Prüfungen zusammen halten das Feld.
    /// </remarks>
    [Fact]
    public void Das_Abrechnungsdatum_wird_gelesen()
    {
        var werte = Lies(Abrechnung());

        Assert.Equal(new DateOnly(2026, 6, 24), werte["abgerechnet"].Date);
    }

    /// <summary>
    /// Drei Leitwerte wandern in die Lohnabrechnungszeile — und nur sie.
    /// </summary>
    /// <remarks>
    /// Monat und Abrechnungsdatum sind keine: Der Monat ist der Schlüssel der Zeile, das
    /// Datum das Dokumentdatum. Der sonstige Bezug ist auch keiner — die Tabelle hat keine
    /// Spalte dafür; er wird gelesen und gezeigt, damit ein Sprung im Brutto erklärt ist.
    /// </remarks>
    [Fact]
    public void Brutto_Auszahlung_und_Netto_sind_die_Leitwerte()
    {
        var leit = DocumentKindLibrary.Payslip.Fields.Where(f => f.Lead).Select(f => f.Key);

        Assert.Equal(["brutto", "auszahlung", "netto"], leit);
    }

    // ── Wo die Erkennung versagt ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>Was nicht dasteht, wird nicht behauptet.</b> In einem der zehn echten Belege bricht
    /// die Erkennung die IBAN-Zeile vor dem Betrag ab. Dann bleibt der Auszahlungsbetrag leer —
    /// kein Muster holt ihn zurück, und die nächstbeste Zahl wäre geraten.
    /// </summary>
    [Fact]
    public void Ohne_lesbare_Ibanzeile_bleibt_der_Auszahlungsbetrag_leer()
    {
        var werte = Lies(Abrechnung(mitIban: false));

        Assert.False(werte.ContainsKey("auszahlung") && werte["auszahlung"].Number is not null);

        // Das Brutto steht davon unberührt da: zwei Felder, zwei Quellen.
        Assert.Equal(4827.50m, werte["brutto"].Number);
    }

    /// <summary>
    /// Fehlt die Kopfzeile, fehlt das Datum — die Werte bleiben trotzdem lesbar.
    /// </summary>
    /// <remarks>
    /// Anders als beim Statusreport ist der Stichtag hier keine Bedingung. Dort trennt er einen
    /// Jahresstand von einem Tageskurs; eine Abrechnung trägt den Monat schon im Dateinamen,
    /// und sie wegen eines verlesenen Kopfes ganz liegen zu lassen hieße, drei von zehn echten
    /// Belegen zu verwerfen.
    /// </remarks>
    [Fact]
    public void Ohne_Kopfzeile_fehlt_nur_das_Datum()
    {
        var werte = Lies(Abrechnung(kopfzeile: "Abrechnun der Brutto/Netto-Bezü e"));

        Assert.False(werte.ContainsKey("abgerechnet") && werte["abgerechnet"].Date is not null);
        Assert.Equal(4827.50m, werte["brutto"].Number);
        Assert.Equal(2913.64m, werte["auszahlung"].Number);
    }

    /// <summary>
    /// Der Netto-Verdienst ist nicht der Auszahlungsbetrag.
    /// </summary>
    /// <remarks>
    /// Zwischen beiden stehen Vorschüsse, Pfändungen und vermögenswirksame Leistungen. In der
    /// echten Ablage gehen sie in jedem Monat auseinander, und die Lohnabrechnungstabelle
    /// führt beide getrennt — eine Regel, die nur eine Zahl läse, füllte die andere falsch.
    /// </remarks>
    [Fact]
    public void Netto_und_Auszahlung_sind_zwei_Zahlen()
    {
        var werte = Lies(Abrechnung(netto: "3.012,80", auszahlung: "2.913,64"));

        Assert.Equal(3012.80m, werte["netto"].Number);
        Assert.Equal(2913.64m, werte["auszahlung"].Number);
    }

    /// <summary>
    /// <b>Der abgerechnete Monat, nicht der Tag der Abrechnung.</b> Die
    /// Lohnabrechnungstabelle führt eine Zeile je Monat; zwei Belege desselben Monats sind
    /// eine Dublette.
    /// </summary>
    [Fact]
    public void Der_abgerechnete_Monat_wird_zum_Monatsersten()
    {
        var werte = Lies(Abrechnung());

        Assert.Equal(new DateOnly(2026, 6, 1), werte["monat"].Date);

        // Daneben, und etwas anderes: der Tag, an dem gerechnet wurde.
        Assert.Equal(new DateOnly(2026, 6, 24), werte["abgerechnet"].Date);
    }

    /// <summary>
    /// <b>Eine Sonderzahlung steht als solche im Papier.</b> Die Lohnart trägt „S“ für
    /// sonstigen Bezug statt „L“ für laufenden — das Formular erklärt es in seiner eigenen
    /// Legende. Daran hängt die Antwort darauf, warum ein Monatsbrutto springt; sie muss nicht
    /// aus einem Vergleich mit anderen Monaten geraten werden.
    /// </summary>
    [Fact]
    public void Ein_sonstiger_Bezug_wird_erkannt()
    {
        var werte = Lies(Abrechnung(
            monatsbrutto: "6.577,50", sonderzahlung: "005 PRAEMIE S E J 1.750,00"));

        Assert.Equal(1750.00m, werte["sonderzahlung"].Number);
        Assert.Equal(6577.50m, werte["brutto"].Number);
    }

    /// <summary>
    /// Ohne sonstigen Bezug steht dort nichts — eine laufende Lohnart wird nicht dafür
    /// gehalten.
    /// </summary>
    /// <remarks>
    /// Die Vorlage führt drei laufende Bezüge mit „L“ und einen steuerfreien mit „F“. Würde
    /// das Muster auf eines davon greifen, stünde in jedem Monat eine Sonderzahlung.
    /// </remarks>
    [Fact]
    public void Ohne_sonstigen_Bezug_steht_dort_nichts()
    {
        var werte = Lies(Abrechnung());

        Assert.False(werte.ContainsKey("sonderzahlung")
                     && werte["sonderzahlung"].Number is not null);
    }

    /// <summary>
    /// <b>Keine Berichtigung beschädigter Trennzeichen.</b> Dieses Papier liefert keine
    /// Gegenprobe mit: Brutto minus Steuern minus Abgaben ergäbe den Auszahlungsbetrag, aber
    /// Steuern und Abgaben stehen in einem Zahlenblock ohne verlässliche Zuordnung. Ohne Probe
    /// bliebe eine Berichtigung eine Behauptung.
    /// </summary>
    [Fact]
    public void Kein_Feld_berichtigt_Trennzeichen()
        => Assert.All(DocumentKindLibrary.Payslip.Fields, f => Assert.False(f.ScanRepair));
}
