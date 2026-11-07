using FinanzApp.Api.Application;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Eckdaten einer Rechnung aus ihrem Text.
/// </summary>
/// <remarks>
/// <para>Der Unterschied zwischen Lesen und Raten ist hier die Beschriftung: Ein Betrag gilt,
/// weil „Rechnungsbetrag“ danebensteht, und nicht, weil er unten rechts steht. Was keine
/// Beschriftung trägt, bleibt leer — ein leeres Feld fragt, ein falsch gefülltes antwortet.</para>
/// <para>Die Belege hier sind erfunden, in der Gestalt echter: dieselben Beschriftungen,
/// dieselben Kästen, dieselben Macken der Texterkennung — aber ausgedachte Namen und Zahlen.</para>
/// </remarks>
public sealed class BillTextReadingTests
{
    [Fact]
    public void Eine_beschriftete_Rechnung_gibt_alle_vier_Werte_her()
    {
        var gelesen = BillTextReader.Read("""
            Musterstädter Klinikum gGmbH
            Musterstraße 10, 12345 Musterstadt
            Rechnungsnummer: 4711
            Rechnungsdatum: 04.11.2026
            Pos Bezeichnung Betrag
            1 Untersuchung 318,40
            Rechnungsbetrag: 318,40
            """);

        Assert.True(gelesen.HasContent);
        Assert.Equal("Musterstädter Klinikum gGmbH", gelesen.Provider);
        Assert.Equal("4711", gelesen.BillNumber);
        Assert.Equal(new DateOnly(2026, 11, 4), gelesen.BillDate);
        Assert.Equal(318.40m, gelesen.GrossAmount);
    }

    /// <summary>Jeder Wert nennt die Zeile, aus der er stammt.</summary>
    /// <remarks>
    /// Ohne Quelle ist ein gelesener Betrag eine Behauptung, die niemand mehr nachsieht. Bei
    /// einer Erstattung wandert er in eine Forderung gegen die Kasse — die falsche Stelle für
    /// ein ungeprüftes Ergebnis.
    /// </remarks>
    [Fact]
    public void Jeder_Wert_nennt_seine_Zeile()
    {
        var gelesen = BillTextReader.Read("""
            Musterpraxis Dr. Mustermann GbR
            Rechnungsnummer: 4711
            Rechnungsbetrag: 318,40
            """);

        Assert.Contains("4711", gelesen.BillNumberSource);
        Assert.Contains("Rechnungsbetrag", gelesen.GrossAmountSource);
        Assert.Contains("Musterpraxis", gelesen.ProviderSource);
    }

    /// <summary>
    /// Steht die Beschriftung als Spaltenkopf da, trägt die Zeile darunter die Werte.
    /// </summary>
    /// <remarks>
    /// Ein Kasten „R-Nummer/Debitor · R-Datum“ mit den Werten darunter ist auf Klinikrechnungen
    /// die Regel. Der Schrägstrich trennt Rechnungsnummer und Debitorennummer; gemeint ist die
    /// erste.
    /// </remarks>
    [Fact]
    public void Ein_Spaltenkopf_traegt_seine_Werte_in_der_Zeile_darunter()
    {
        var gelesen = BillTextReader.Read("""
            Musterstädter Klinikum gGmbH
            Bei Zahlung und Schriftverkehr bitte angeben:
            R-Nummer/Debitor R-Datum
            4711/880022 04.11.2026
            Rechnungsbetrag: 318,40
            """);

        Assert.Equal("4711", gelesen.BillNumber);
        Assert.Equal(new DateOnly(2026, 11, 4), gelesen.BillDate);
    }

    /// <summary>
    /// Ein Fließsatz ist kein Spaltenkopf — unter ihm steht die Bank, nicht der Wert.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall stammt von einem echten Laborbeleg. „Bitte überweisen Sie den
    /// Rechnungsbetrag unter Angabe der Rechnungsnummer auf unser Konto.“ enthält beide
    /// Beschriftungen und keinen einzigen Wert; darunter stand die Bankverbindung. Eine Regel,
    /// die blind die nächste Zeile nimmt, las daraus die Rechnungsnummer <c>DE88</c>.</para>
    /// <para><b>Eine falsch gelesene Nummer ist schlimmer als keine.</b> Keine fragt; eine
    /// falsche steht am Vorgang und wird beim Einreichen zurückgewiesen.</para>
    /// </remarks>
    [Fact]
    public void Aus_einem_Fliesssatz_wird_keine_IBAN_zur_Rechnungsnummer()
    {
        var gelesen = BillTextReader.Read("""
            Musterlabor MVZ GmbH
            Bitte überweisen Sie den Rechnungsbetrag unter Angabe der Rechnungsnummer auf unser Konto.
            Musterbank, Musterlabor MVZ GmbH IBAN: DE02 1234 5678 9012 3456 78, BIC: MUSTDE66XXX
            """);

        Assert.Null(gelesen.BillNumber);
    }

    /// <summary>
    /// Ein Punkt neben einer Währung ist ein Komma.
    /// </summary>
    /// <remarks>
    /// „EUR 12.34“ stand so auf einem Beleg — die Erkennung hatte das Komma als Punkt gelesen.
    /// Nur unmittelbar neben der Währung: Sonst wäre „1.234“ plötzlich 1,23 und ein
    /// vierstelliger Betrag zu einem einstelligen geworden.
    /// </remarks>
    [Theory]
    [InlineData("Rechnungsbetrag EUR 12.34", 12.34)]
    [InlineData("Rechnungsbetrag 12.34 EUR", 12.34)]
    [InlineData("Rechnungsbetrag: 1.234,56", 1234.56)]
    public void Ein_Punkt_neben_der_Waehrung_ist_ein_Komma(string zeile, double erwartet)
        => Assert.Equal((decimal)erwartet, BillTextReader.Read("Musterlabor GmbH\n" + zeile).GrossAmount);

    /// <summary>
    /// Die Rechnungsnummer wird auch gefunden, wenn der Scan das Wort zerlegt hat.
    /// </summary>
    /// <remarks>
    /// Auf dem Blatt stand groß „RECHNUNG Nr.“, die Erkennung lieferte „RECHNU G Nr.“. Es
    /// trifft gerade das größte Wort der Seite, weil es oft gesperrt gesetzt ist. Der Rückfall
    /// greift erst, wenn keine ausdrückliche Beschriftung etwas gefunden hat.
    /// </remarks>
    [Fact]
    public void Eine_zerlesene_Rechnungsnummer_wird_trotzdem_gefunden()
    {
        var gelesen = BillTextReader.Read("""
            Musterholz GmbH & Co. KG
            Auftrags-Nr. 01234567
            RECHNU G Nr.00099887
            Kunden-Nr. 77341
            Datum 04.11.2026
            """);

        Assert.Equal("00099887", gelesen.BillNumber);
        Assert.Equal(new DateOnly(2026, 11, 4), gelesen.BillDate);
    }

    /// <summary>
    /// Der Absender hängt an der Rechtsform, nicht an der ersten Zeile.
    /// </summary>
    /// <remarks>
    /// <para>Ein Briefkopf hat keine Beschriftung — die Rechtsform ist der beste Anker, den ein
    /// Blatt von sich aus hergibt. Sie steht am vollständigen Namen und sonst nirgends.</para>
    /// <para>Als ganzes Wort: „ se“ für die SE fand als Teilstring das „Se“ in „Seite“, und der
    /// Absender hieß daraufhin „Endrechnung/ ambulant Seite“.</para>
    /// </remarks>
    [Fact]
    public void Der_Absender_haengt_an_der_Rechtsform()
    {
        var gelesen = BillTextReader.Read("""
            MUSTER KLINIKUM
            Landkreis Musterstadt
            Endrechnung / ambulant Seite 1
            Musterstädter Klinikum Landkreis Musterstadt gGmbH
            Musterstraße 10
            """);

        Assert.Equal("Musterstädter Klinikum Landkreis Musterstadt gGmbH", gelesen.Provider);
    }

    /// <summary>Die Anschrift gehört nicht zum Namen, auch wenn sie in derselben Zeile steht.</summary>
    [Fact]
    public void Die_Anschrift_wird_vom_Namen_getrennt()
    {
        var gelesen = BillTextReader.Read("Musterlabor MVZ GmbH Musterstraße 14 12345 Musterstadt");

        Assert.Equal("Musterlabor MVZ GmbH", gelesen.Provider);
    }

    /// <summary>
    /// Was keine Rechnung ist, gibt auch keine Rechnungsdaten her.
    /// </summary>
    /// <remarks>
    /// <c>HasContent</c> ist die ehrliche Antwort auf ein Blatt ohne Beschriftungen. Die Maske
    /// bleibt dann leer und fragt — statt Zahlen anzubieten, die niemand geprüft hat.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ohne_Text_gibt_es_nichts_zu_lesen(string? text)
        => Assert.False(BillTextReader.Read(text).HasContent);

    /// <summary>Ein Eigenanteil steht nicht auf der Rechnung und wird deshalb nie gelesen.</summary>
    /// <remarks>
    /// Wie viel die Kasse übernimmt, entscheidet die Kasse — auf ihrer Abrechnung, nicht auf
    /// dieser Rechnung. Einen Wert dafür zu erfinden hieße, eine offene Forderung zu erfinden.
    /// </remarks>
    [Fact]
    public void Der_Eigenanteil_wird_nicht_erfunden()
    {
        var gelesen = BillTextReader.Read("""
            Musterpraxis Dr. Mustermann GbR
            Rechnungsbetrag: 318,40
            Eigenanteil: 60,50
            """);

        Assert.Null(gelesen.OwnShare);
    }

    // ── Die Händlerrechnung ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein ausgeschriebener Monat ist auch ein Datum.
    /// </summary>
    /// <remarks>
    /// Arztrechnungen schreiben Ziffern, Händler schreiben den Monat aus. Die Beschriftung
    /// „Belegdatum“ griff deshalb ins Leere: Sie fand die Zeile und daneben kein Datum.
    /// </remarks>
    [Theory]
    [InlineData("Belegdatum 14. Juli 2026", 2026, 7, 14)]
    [InlineData("Rechnungsdatum: 3. Mai 2026", 2026, 5, 3)]
    [InlineData("Belegdatum 17. Sept. 2025", 2025, 9, 17)]
    [InlineData("Belegdatum 1. Marz 2026", 2026, 3, 1)]
    public void Ein_ausgeschriebener_Monat_wird_gelesen(string zeile, int jahr, int monat, int tag)
    {
        var gelesen = BillTextReader.Read($"Musterelektro Handels GmbH\n{zeile}\n");

        Assert.Equal(new DateOnly(jahr, monat, tag), gelesen.BillDate);
    }

    /// <summary>Was wie ein Monat aussieht, aber keiner ist, bleibt ungelesen.</summary>
    /// <remarks>
    /// „12. Rate 2027“ hat die Gestalt eines Datums. Es als eines zu lesen hieße, ein
    /// Rechnungsdatum zu erfinden, das auf dem Blatt nirgends steht.
    /// </remarks>
    [Fact]
    public void Ein_erfundener_Monatsname_ergibt_kein_Datum()
    {
        var gelesen = BillTextReader.Read("""
            Musterelektro Handels GmbH
            Belegdatum 12. Rate 2027
            """);

        Assert.Null(gelesen.BillDate);
    }

    /// <summary>
    /// Zwischen Netto und Brutto gewinnt Brutto — auch wenn Netto zuerst dasteht.
    /// </summary>
    /// <remarks>
    /// Händlerrechnungen stellen beide untereinander und nennen beide „Endsumme“. Ohne die
    /// Unterscheidung wäre der übernommene Preis um die Umsatzsteuer zu niedrig, und eine Zahl,
    /// die fast stimmt, fällt niemandem auf.
    /// </remarks>
    [Fact]
    public void Die_Endsumme_ohne_Mehrwertsteuer_ist_nicht_der_Rechnungsbetrag()
    {
        var gelesen = BillTextReader.Read("""
            Musterelektro Handels GmbH
            Endsumme ohne MwSt 1.011,76 EUR
            MwSt.-Betrag 192,24 EUR
            Endsumme inkl. MwSt 1.204,00 EUR
            """);

        Assert.Equal(1204.00m, gelesen.GrossAmount);
        Assert.Contains("inkl", gelesen.GrossAmountSource!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>„Gesamt brutto“ ist dieselbe Zeile wie „Summe brutto“.</summary>
    /// <remarks>
    /// Eine Handwerkerrechnung schrieb es so, und der Betrag blieb ungelesen, während die
    /// Nettozeile darüber bereitstand. Die Sperre gegen Nettozeilen trägt auch hier.
    /// </remarks>
    [Fact]
    public void Gesamt_brutto_wird_gelesen_und_gesamt_netto_nicht()
    {
        var gelesen = BillTextReader.Read("""
            Musterschreinerei Eberhard Muster
            Gesamt netto 500,35 €
            zzgl. 19,0 % MWSt 95,07 €
            Gesamt brutto 595,42 €
            """);

        Assert.Equal(595.42m, gelesen.GrossAmount);
    }

    /// <summary>
    /// Ein Artikel am Zeilenanfang ist kein Absender.
    /// </summary>
    /// <remarks>
    /// Ein Briefkopf bricht den Firmennamen über mehrere Zeilen um, und die Erkennung liefert
    /// die Bruchstücke einzeln. Steht in der ersten „Die“, war der Absender bisher „Die“ —
    /// die Zeile darunter ist die bessere Antwort und selten eine schlechtere.
    /// </remarks>
    [Fact]
    public void Ein_Artikel_in_der_ersten_Zeile_wird_uebersprungen()
    {
        var gelesen = BillTextReader.Read("""
            Die
            Schreiner
            Eberhard Muster
            Werkstatt
            Rechnungsbetrag: 595,42
            """);

        Assert.Equal("Schreiner", gelesen.Provider);
    }

    /// <summary>Steht nur eine Endsumme da, ist sie es.</summary>
    [Fact]
    public void Eine_einzelne_Endsumme_zaehlt()
    {
        var gelesen = BillTextReader.Read("""
            Musterelektro Handels GmbH
            Endsumme 249,90 EUR
            """);

        Assert.Equal(249.90m, gelesen.GrossAmount);
    }

    // ── Behördenbescheide ──────────────────────────────────────────────────────────────────
    //
    // Eine Behörde stellt keine Rechnung. Sie erlässt einen Bescheid, nennt ihre Nummer
    // „Bescheidnummer“, schreibt ihr Datum neben den Ort und ihre Frist in einen ganzen Satz.
    // Drei Formen, die auf jedem Gebührenbescheid gleich aussehen — und die der Leser bis
    // zuletzt übersah, obwohl sie im Klartext dastanden.

    private const string Gebuehrenbescheid = """
        Landkreis Musterkreis- Abfallwirtschaftsamt
        Musterstraße 44
        12345 Musterstadt
        Herrn/Frau Musterstadt, den 12.03.2026
        Kundennummer: 7654321
        Bescheidnummer: 80231477
        Abfallgebührenbescheid 2026
        Objekt: Musterdorf, Beispielweg 3
        Für den Zeitraum vom 01.04.2026 bis 30.06.2026
        wird die Abfallgebühr festgesetzt auf 63,40 EUR
        Dieser Betrag ist am 24.04.2026 zur Zahlung fällig.
        Sollte der Fälligkeitstermin ein Wochenende oder Feiertag sein, verschiebt sich der
        Zusammenfassung:
        Summe Personenpflichtgebühr 52,10
        Summe Leerungsgebühren Restmüll 11,30
        Gesamtbetrag 2026 63,40
        """;

    [Fact]
    public void Ein_Behoerdenbescheid_gibt_Nummer_Datum_und_Faelligkeit_her()
    {
        var gelesen = BillTextReader.Read(Gebuehrenbescheid);

        Assert.Equal("80231477", gelesen.BillNumber);
        Assert.Equal(new DateOnly(2026, 3, 12), gelesen.BillDate);
        Assert.Equal(new DateOnly(2026, 4, 24), gelesen.DueDate);
        Assert.Equal(63.40m, gelesen.GrossAmount);
    }

    /// <summary>
    /// Das Finanzamt sagt nicht „fällig“, sondern „spätestens am“.
    /// </summary>
    /// <remarks>
    /// <para>Gefunden an zwei echten Steuerbescheiden, die mit einem Termin in der Ablage
    /// lagen, von dem die Anwendung nichts wusste. Das Datumsmuster selbst kam mit dem
    /// Leerzeichen zurecht, das die Texterkennung hinter den zweiten Punkt setzt — es fehlte
    /// nur das Wort.</para>
    /// <para>Der Betrag in derselben Zeile ist kein Datum und darf keines werden: „4.100,00“
    /// sieht am Anfang aus wie ein Tag und ein Monat, hat aber nach dem zweiten Punkt ein
    /// Komma statt eines Jahres.</para>
    /// </remarks>
    [Fact]
    public void Ein_Steuerbescheid_nennt_seinen_Termin_mit_spaetestens()
    {
        var gelesen = BillTextReader.Read("""
            Finanzamt Musterstadt
            Bescheid über Grunderwerbsteuer
            Noch zu zahlen 4.100,00
            spätestens am 03.04. 2026 4.100,00
            Wenn Sie die Steuern nicht bis zum Ablauf des Fälligkeitstages zahlen, ist für
            """);

        Assert.Equal(new DateOnly(2026, 4, 3), gelesen.DueDate);
        Assert.Contains("spätestens am", gelesen.DueDateSource);
    }

    /// <summary>
    /// Ein Bescheid nennt seinen Betrag unter „Noch zu zahlen“.
    /// </summary>
    /// <remarks>
    /// <para>Eine Behörde stellt keine Rechnung. Sie rechnet vor und sagt am Ende, was davon
    /// offen ist — und ohne dieses Wort stand ein Steuerbescheid mit Frist und Forderung in der
    /// Ablage, dessen Betrag nirgends stand.</para>
    /// <para><b>Die Reihenfolge entscheidet hier wirklich.</b> Auf demselben Blatt steht der
    /// „Gesamtbetrag der Gegenleistung“: der Kaufpreis, auf den die Steuer berechnet wird, und
    /// ein Vielfaches von ihr. Stünde das Summenwort vorn, trüge der offene Posten den
    /// Kaufpreis.</para>
    /// </remarks>
    [Fact]
    public void Ein_Bescheid_nennt_seinen_Betrag_unter_Noch_zu_zahlen()
    {
        var gelesen = BillTextReader.Read("""
            Finanzamt Musterstadt
            Bescheid über Grunderwerbsteuer
            Gesamtbetrag der Gegenleistung 164.000,00
            Die Steuer wird festgesetzt auf 5,00 % von 82.000 4.100,00
            Bereits getilgt/ausgezahlt 0,00
            Noch zu zahlen 4.100,00
            """);

        Assert.Equal(4100.00m, gelesen.GrossAmount);
        Assert.Contains("Noch zu zahlen", gelesen.GrossAmountSource);
    }

    /// <summary>
    /// Die Überschrift eines Bescheids trägt sein Datum.
    /// </summary>
    /// <remarks>
    /// <para>„Bescheid vom" steht seit je in der Wortliste und traf trotzdem nicht: Dazwischen
    /// steht, worum es geht. Das ist die übliche Form — eine Behörde überschreibt ihr Blatt mit
    /// dem, was sie entscheidet, und hängt das Datum an.</para>
    /// <para>Der Briefkopf hilft hier nicht. Er trägt das Datum neben dem Ort, aber ohne „den",
    /// und die Texterkennung bricht ihn außerdem mitten im Ortsnamen um.</para>
    /// </remarks>
    [Fact]
    public void Die_Ueberschrift_eines_Bescheids_traegt_sein_Datum()
    {
        var gelesen = BillTextReader.Read("""
            Finanzamt Musterstadt 11111 Musterstadt
            Stadt 12.03.2026
            Steuernummer 11111/22222
            Bescheid über Grunderwerbsteue r vom 12.03. 2026
            Noch zu zahlen 4.100,00
            """);

        Assert.Equal(new DateOnly(2026, 3, 12), gelesen.BillDate);
        Assert.Contains("Bescheid über", gelesen.BillDateSource);
    }

    /// <summary>
    /// Mitten im Satz ist „Bescheid" keine Überschrift.
    /// </summary>
    /// <remarks>
    /// Die Rechtsbehelfsbelehrung steht auf demselben Blatt und spricht in jedem zweiten Satz
    /// vom Bescheid. Nähme die Regel eine dieser Zeilen, trüge der Beleg das Datum eines
    /// Verwaltungsakts von vorgestern.
    /// </remarks>
    [Fact]
    public void Ein_Satz_ueber_den_Bescheid_ist_keine_Ueberschrift()
    {
        var gelesen = BillTextReader.Read("""
            Musterbehörde
            Gegen diesen Bescheid, der den Verwaltungsakt vom 07.07.2017 ändert, ist der Einspruch gegeben.
            """);

        Assert.Null(gelesen.BillDate);
    }

    /// <summary>
    /// „Spätestens“ allein ist kein Zahlungstermin.
    /// </summary>
    /// <remarks>
    /// Das Wort trägt auch Sätze, die etwas ganz anderes meinen. Spränge die Regel darauf an,
    /// nähme sie sich das nächstbeste Datum der Zeile und machte daraus eine Frist, die
    /// niemand gesetzt hat — und die stünde dann als Vorgang da.
    /// </remarks>
    [Fact]
    public void Eine_Kuendigungsbelehrung_mit_spaetestens_ist_kein_Zahlungstermin()
    {
        var gelesen = BillTextReader.Read("""
            Musterversicherung AG
            Ihr Vertrag läuft bis 31.12.2030.
            Eine Kündigung muss uns spätestens drei Monate vor Ablauf erreichen.
            """);

        Assert.Null(gelesen.DueDate);
    }

    /// <summary>
    /// Die ausdrücklichere Beschriftung gewinnt.
    /// </summary>
    /// <remarks>
    /// Die Wortliste wird Wort für Wort durchgegangen, und „spätestens“ steht hinten. Trägt
    /// ein Blatt beides — eine Zahlungsfrist und irgendwo ein „spätestens am“ —, soll die
    /// Zahlungsfrist gelten.
    /// </remarks>
    [Fact]
    public void Eine_ausdrueckliche_Faelligkeit_schlaegt_das_spaetestens()
    {
        var gelesen = BillTextReader.Read("""
            Musterwerke GmbH
            spätestens am 30.06.2026 ist der Zählerstand zu melden
            Der Rechnungsbetrag ist am 15.05.2026 zur Zahlung fällig.
            """);

        Assert.Equal(new DateOnly(2026, 5, 15), gelesen.DueDate);
    }

    /// <summary>
    /// Auch die neuen Werte nennen ihre Zeile.
    /// </summary>
    /// <remarks>
    /// Beim Ortsdatum trägt das die Prüfung: „Musterstadt, den 12.03.2026“ sieht einem
    /// Zeitraum zum Verwechseln ähnlich, und wer die Quelle sieht, erkennt den Unterschied in
    /// einer Sekunde.
    /// </remarks>
    [Fact]
    public void Die_Faelligkeit_und_das_Ortsdatum_nennen_ihre_Quelle()
    {
        var gelesen = BillTextReader.Read(Gebuehrenbescheid);

        Assert.Contains("zur Zahlung fällig", gelesen.DueDateSource);
        Assert.Contains(", den 12.03.2026", gelesen.BillDateSource);
    }

    /// <summary>
    /// Eine Belehrung über die Fälligkeit ist kein Termin.
    /// </summary>
    /// <remarks>
    /// „Sollte der Fälligkeitstermin ein Wochenende sein …“ steht auf jedem Bescheid und nennt
    /// kein Datum. Eine Regel, die auf das Wort allein anspränge, nähme sich das nächste Datum
    /// der Seite — und das ist dort der Beginn des Abrechnungszeitraums.
    /// </remarks>
    [Fact]
    public void Eine_Belehrung_ueber_die_Faelligkeit_ist_kein_Termin()
    {
        var gelesen = BillTextReader.Read("""
            Landkreis Musterkreis- Abfallwirtschaftsamt
            Für den Zeitraum vom 01.04.2026 bis 30.06.2026
            Gesamtbetrag 2026 63,40
            Sollte der Fälligkeitstermin ein Wochenende oder Feiertag sein, verschiebt sich der
            Termin auf den folgenden Werktag.
            """);

        Assert.Null(gelesen.DueDate);
        Assert.Equal(63.40m, gelesen.GrossAmount);
    }

    /// <summary>
    /// Nennt der Beleg keine Frist, bleibt sie leer.
    /// </summary>
    /// <remarks>
    /// Aus dem Rechnungsdatum vierzehn Tage hochzurechnen wäre eine verbreitete Übung und
    /// trotzdem geraten — in einem Feld, an dem später eine Mahnung hängt.
    /// </remarks>
    [Fact]
    public void Ohne_genannte_Frist_bleibt_die_Faelligkeit_leer()
    {
        var gelesen = BillTextReader.Read("""
            Musterstädter Klinikum gGmbH
            Rechnungsnummer: 4711
            Rechnungsdatum: 04.11.2026
            Rechnungsbetrag: 318,40
            """);

        Assert.Equal(new DateOnly(2026, 11, 4), gelesen.BillDate);
        Assert.Null(gelesen.DueDate);
    }

    /// <summary>Die gelesene Frist landet im Formular des offenen Postens.</summary>
    [Fact]
    public void Der_offene_Posten_bekommt_die_Frist_vorgeschlagen()
    {
        var vorschlaege = BillFormProposals.For(
            CreateObjectType.Invoice, BillTextReader.Read(Gebuehrenbescheid));

        Assert.Equal("2026-04-24", vorschlaege["due"]);
        Assert.Equal("2026-03-12", vorschlaege["issued"]);
        Assert.Equal("80231477", vorschlaege["number"]);
        Assert.Equal("63,40", vorschlaege["amount"]);
    }
}
