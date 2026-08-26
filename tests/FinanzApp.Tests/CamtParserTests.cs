using System.Text;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Der camt-Leser gegen den Beispielauszug aus <c>docs/beispiele</c>.
/// </summary>
/// <remarks>
/// Geprüft wird dieselbe Datei, die auch zum Ausprobieren in der Oberfläche gedacht ist. Eine
/// zweite, nur für den Test gebaute Fassung würde irgendwann von der ersten abweichen, und dann
/// prüfte der Test etwas, das niemand benutzt.
/// </remarks>
public sealed class CamtParserTests
{
    private static readonly CamtStatementParser Parser = new();

    private static async Task<ParsedStatement> ExampleAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Beispiele", "camt052-sparkasse.xml");
        await using var content = File.OpenRead(path);

        return await Parser.ParseAsync(content, "camt052-sparkasse.xml");
    }

    private static Task<ParsedStatement> ParseAsync(string xml, string fileName = "auszug.xml")
        => Parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(xml)), fileName);

    [Fact]
    public async Task Kopfdaten_kommen_aus_der_Datei()
    {
        var statement = await ExampleAsync();

        Assert.Equal("CAMT.052.001.08", statement.Format);
        Assert.Equal("DE44672500200034889102", statement.Iban);
        Assert.Equal("Sparkasse Heidelberg", statement.BankName);
        Assert.Equal(6091.28m, statement.ClosingBalance);
    }

    [Fact]
    public async Task Eine_Abbuchung_ist_negativ_und_eine_Gutschrift_positiv()
    {
        var statement = await ExampleAsync();

        // Der Betrag steht in der Datei immer ohne Vorzeichen — die Richtung trägt CdtDbtInd.
        // Wer das übersieht, bucht jede Abbuchung als Eingang.
        var rewe = statement.Records.Single(r => r.Payee == "REWE Markt Heidelberg");
        var lohn = statement.Records.Single(r => r.Payee.StartsWith("Kielmayer", StringComparison.Ordinal));

        Assert.Equal(-68.42m, rewe.Amount);
        Assert.Equal(2480.00m, lohn.Amount);
    }

    [Fact]
    public async Task Bei_einer_Gutschrift_steht_der_Zahler_da_und_nicht_man_selbst()
    {
        var statement = await ExampleAsync();

        // Die Datei nennt beide Seiten. Immer den Gläubiger zu nehmen ergäbe hier den eigenen
        // Namen — in der Liste stünde dann "Oliver Kielmayer" als Empfänger des eigenen Gehalts.
        Assert.Contains(statement.Records, r => r.Payee == "Kielmayer Systemtechnik GmbH");
        Assert.DoesNotContain(statement.Records, r => r.Payee == "Oliver Kielmayer");
    }

    [Fact]
    public async Task Ein_Sammler_wird_in_seine_Einzelposten_zerlegt()
    {
        var statement = await ExampleAsync();

        var stadtwerke = statement.Records.Single(r => r.Payee == "Stadtwerke Heidelberg");
        var telekom = statement.Records.Single(r => r.Payee == "Telekom Deutschland");

        Assert.Equal(-98.90m, stadtwerke.Amount);
        Assert.Equal(-43.95m, telekom.Amount);

        // Und nicht zusätzlich der Sammelbetrag — der wäre doppelt gebucht.
        Assert.DoesNotContain(statement.Records, r => r.Amount == -142.85m);
    }

    [Fact]
    public async Task Ein_nur_vorgemerkter_Umsatz_steht_da_mit_Grund()
    {
        var statement = await ExampleAsync();

        var hornbach = statement.Records.Single(r => r.Payee == "Baumarkt Hornbach");

        // Er verschwindet nicht — sonst fehlte am Ende ein Satz, den niemand erklärt hat. Das
        // Problem macht ihn unwählbar; gebucht wird er dadurch nicht.
        Assert.Contains("vorgemerkt", hornbach.Problem);
        Assert.Contains("PDNG", hornbach.Problem);
    }

    [Fact]
    public async Task Ein_unlesbarer_Betrag_bleibt_stehen_statt_zu_verschwinden()
    {
        var statement = await ExampleAsync();

        var apotheke = statement.Records.Single(r => r.Payee == "Apotheke am Markt");

        Assert.Null(apotheke.Amount);
        Assert.Equal(new DateOnly(2026, 8, 26), apotheke.BookingDate);
    }

    [Fact]
    public async Task Ein_Zeitstempel_als_Buchungstag_wird_auf_den_Tag_gekuerzt()
    {
        var statement = await ExampleAsync();

        var netflix = statement.Records.Single(r => r.Payee == "Netflix Abo");

        Assert.Equal(new DateOnly(2026, 8, 26), netflix.BookingDate);
    }

    [Fact]
    public async Task Die_Bankreferenz_traegt_die_Wiedererkennung()
    {
        var statement = await ExampleAsync();

        var rewe = statement.Records.Single(r => r.Payee == "REWE Markt Heidelberg");

        // AcctSvcrRef, nicht EndToEndId: die steht hier auf NOTPROVIDED.
        Assert.Equal("CAMT:SPK2026082400001", rewe.Reference);
    }

    [Fact]
    public async Task Ohne_Referenz_entsteht_ein_stabiler_Fingerabdruck()
    {
        var erste = await ExampleAsync();
        var zweite = await ExampleAsync();

        var a = erste.Records.Single(r => r.Payee == "Netflix Abo").Reference;
        var b = zweite.Records.Single(r => r.Payee == "Netflix Abo").Reference;

        Assert.StartsWith("CAMT:~", a, StringComparison.Ordinal);

        // Derselbe Satz muss beim zweiten Einlesen dieselbe Referenz ergeben, sonst erkennt die
        // Duplikatprüfung ihn nicht wieder und der Auszug lässt sich doppelt importieren.
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Auch_ein_camt_053_wird_gelesen()
    {
        // Unterhalb von Ntry sind beide Formate gleich; nur der Bericht heißt anders.
        var statement = await ParseAsync("""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <GrpHdr><MsgId>X</MsgId></GrpHdr>
                <Stmt>
                  <Id>1</Id>
                  <Acct><Id><IBAN>DE12670623660009114007</IBAN></Id></Acct>
                  <Ntry>
                    <Amt Ccy="EUR">12.34</Amt>
                    <CdtDbtInd>DBIT</CdtDbtInd>
                    <BookgDt><Dt>2026-08-20</Dt></BookgDt>
                    <AcctSvcrRef>R1</AcctSvcrRef>
                    <NtryDtls><TxDtls><RltdPties><Cdtr><Nm>Kiosk</Nm></Cdtr></RltdPties></TxDtls></NtryDtls>
                  </Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """);

        Assert.Equal("CAMT.053.001.02", statement.Format);
        Assert.Equal(-12.34m, statement.Records.Single().Amount);
    }

    [Fact]
    public async Task Ein_aelteres_Sts_ohne_Cd_wird_auch_verstanden()
    {
        // camt.052.001.02 schreibt den Code direkt in Sts, spätere Fassungen in Sts/Cd.
        var statement = await ParseAsync("""
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.02">
              <BkToCstmrAcctRpt>
                <Rpt>
                  <Id>1</Id>
                  <Ntry>
                    <Amt Ccy="EUR">5.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Sts>PDNG</Sts>
                    <BookgDt><Dt>2026-08-20</Dt></BookgDt>
                  </Ntry>
                </Rpt>
              </BkToCstmrAcctRpt>
            </Document>
            """);

        Assert.Equal("nur vorgemerkt (PDNG), noch nicht gebucht", statement.Records.Single().Problem);
    }

    /// <summary>
    /// Bei einer Kartenzahlung nennt das Gläubigerfeld oft nicht den Laden.
    /// </summary>
    /// <remarks>
    /// An einer echten Bankdatei nachgewiesen: 46 Einkäufe an einem einzigen Laden lagen dort
    /// unter „PAYONE GmbH“ und „Lastschrift aus Kartenzahlung“ — zwei Namen, die nichts über den
    /// Einkauf sagen. Der Laden steht im Verwendungszweck.
    ///
    /// Die drei Schreibweisen stammen alle aus derselben Datei.
    /// </remarks>
    [Theory]
    [InlineData("Setzer 24/7 Vell./Wolpertshausen/DE 31.12.2025 um 19:08:01 Uhr 682/228/ECTL/",
        "Lastschrift aus Kartenzahlung", "Setzer 24/7 Vell./Wolpertshausen")]
    [InlineData("DIAK Klinikum Landkrei/Am Mutterhaus 1/Schwaebisch H/D02.01.2026 / 18:58 Ortszeit",
        "DZ BANK AG", "DIAK Klinikum Landkrei/Am Mutterhaus 1/Schwaebisch H")]
    [InlineData("NYX.DeinAutomat/Diakoniestrasse/SchwaebischHa/DE/0 16.01.2026 / 16:01 Ortszeit",
        "DZ BANK AG", "NYX.DeinAutomat/Diakoniestrasse/SchwaebischHa")]
    public async Task Bei_einer_Kartenzahlung_zaehlt_der_Laden_aus_dem_Zweck(
        string zweck, string glaeubiger, string erwartet)
    {
        var statement = await ParseAsync(Karte(zweck, glaeubiger));

        Assert.Equal(erwartet, statement.Records.Single().Payee);
    }

    [Fact]
    public async Task Ein_echter_Haendlername_bleibt_stehen()
    {
        // „REWE Martin Sitter“ ist besser lesbar als „REWE SAGT DANKE. 45655449/Heidenheim“ — und
        // beide ergeben dasselbe Regelmuster. Wo der Gläubiger schon der Laden ist, bleibt er.
        var statement = await ParseAsync(Karte(
            "REWE SAGT DANKE. 45655449/Heidenheim/DE 04.01.2026 um 10:11:00 Uhr",
            "REWE Martin Sitter"));

        Assert.Equal("REWE Martin Sitter", statement.Records.Single().Payee);
    }

    /// <summary>
    /// PayPal bucht unter eigenem Namen und nennt den Laden im Zweck.
    /// </summary>
    /// <remarks>
    /// Sonst lägen Apotheke, Bahnfahrt und Tierbedarf unter einem einzigen Empfänger. In einer
    /// echten Datei betraf das 15 von 36 Sätzen — bei den übrigen 21 lässt PayPal die Stelle leer.
    /// </remarks>
    [Fact]
    public async Task Bei_PayPal_zaehlt_der_Laden_aus_dem_Zweck()
    {
        var statement = await ParseAsync(Karte(
            "1047425604925/PP.7060.PP/. LaVita GmbH, Ihr Einkauf bei LaVita GmbH EREF: 104742 MREF: 459",
            "PayPal Europe S.a.r.l. et Cie S.C.A"));

        Assert.Equal("LaVita GmbH", statement.Records.Single().Payee);
    }

    [Fact]
    public async Task Ohne_Ladennamen_bleibt_es_bei_PayPal()
    {
        // Die Stelle ist leer. Ein geratener Name wäre schlechter als der Dienstleister.
        var statement = await ParseAsync(Karte(
            "1047387317764/PP.7474.PP/. , Ihr Einkauf bei EREF: 1047387317764 MREF: 52PJ224NRUSSJ",
            "PayPal Europe S.a.r.l. et Cie S.C.A"));

        Assert.Equal("PayPal Europe S.a.r.l. et Cie S.C.A", statement.Records.Single().Payee);
    }

    /// <summary>
    /// Ein Name, der den Tag der Buchung trägt, ist keiner.
    /// </summary>
    /// <remarks>
    /// Manche Häuser schreiben „Ihr Einkauf bei EDEKA Möller vom 30.12.2025“. Bliebe das Datum
    /// stehen, wäre jeder Einkauf ein eigener Empfänger — in einer echten Datei wären aus einer
    /// Gruppe mit 42 Sätzen 42 Gruppen geworden, und dieselbe Frage wäre 42-mal gestellt worden.
    /// </remarks>
    [Fact]
    public async Task Ein_angehaengtes_Datum_gehoert_nicht_in_den_Namen()
    {
        var erste = await ParseAsync(Karte(
            "KJNUUX Ihr Einkauf bei EDEKA Möller vom 30.12.2025 EREF: T005115664 MREF: 101", "EDEKABANK AG"));
        var zweite = await ParseAsync(Karte(
            "CKVW1R Ihr Einkauf bei EDEKA Möller vom 31.12.2025 EREF: T005130842 MREF: 101", "EDEKABANK AG"));

        Assert.Equal("EDEKA Möller", erste.Records.Single().Payee);
        Assert.Equal(erste.Records.Single().Payee, zweite.Records.Single().Payee);
    }

    /// <summary>
    /// Der Buchungstext der Bank wird mitgeführt — als Auskunft, nicht als Kategorie.
    /// </summary>
    /// <remarks>
    /// An echten Daten geprüft trennt er keine Gruppe, die Empfänger und Vorzeichen nicht schon
    /// trennen: von neun Empfängern mit mehr als einem Text unterschieden acht nur Ein- von
    /// Ausgang. Eine Kategorie daraus abzuleiten wäre geraten; als Angabe an der Gruppe sagt er,
    /// um welche Art Umsatz es geht.
    /// </remarks>
    [Fact]
    public async Task Der_Buchungstext_der_Bank_wird_mitgefuehrt()
    {
        var statement = await ParseAsync(Karte("Miete August", "Heike Immel")
            .Replace("</Ntry>", "<AddtlNtryInf>Dauerauftrag</AddtlNtryInf></Ntry>"));

        Assert.Equal("Dauerauftrag", statement.Records.Single().BookingText);
    }

    private static string Karte(string zweck, string glaeubiger)
        => """
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.08"><BkToCstmrAcctRpt><Rpt><Id>1</Id><Ntry><Amt Ccy="EUR">12.34</Amt><CdtDbtInd>DBIT</CdtDbtInd><BookgDt><Dt>2026-08-20</Dt></BookgDt><AcctSvcrRef>K1</AcctSvcrRef><NtryDtls><TxDtls><RltdPties><Cdtr><Pty><Nm>CDTR</Nm></Pty></Cdtr></RltdPties><RmtInf><Ustrd>ZWECK</Ustrd></RmtInf></TxDtls></NtryDtls></Ntry></Rpt></BkToCstmrAcctRpt></Document>
            """.Replace("ZWECK", zweck).Replace("CDTR", glaeubiger);

    [Fact]
    public async Task Eine_fremde_XML_Datei_wird_benannt_und_nicht_geraten()
    {
        var problem = await Assert.ThrowsAsync<StatementFormatException>(
            () => ParseAsync("<Rechnung><Posten>1</Posten></Rechnung>"));

        Assert.Contains("camt.052", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kaputtes_XML_meldet_sich_als_solches()
    {
        var problem = await Assert.ThrowsAsync<StatementFormatException>(
            () => ParseAsync("<Document><offen>"));

        Assert.Contains("kein gültiges XML", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eine_Datei_mit_DTD_wird_abgewiesen()
    {
        // Sonst zöge eine externe Entität beliebige Serverdateien in die Antwort.
        await Assert.ThrowsAsync<StatementFormatException>(() => ParseAsync("""
            <?xml version="1.0"?>
            <!DOCTYPE Document [<!ENTITY hier SYSTEM "file:///c:/windows/win.ini">]>
            <Document><BkToCstmrAcctRpt><Rpt><Id>&hier;</Id></Rpt></BkToCstmrAcctRpt></Document>
            """));
    }
}
