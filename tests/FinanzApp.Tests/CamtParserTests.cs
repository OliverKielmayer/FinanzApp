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
