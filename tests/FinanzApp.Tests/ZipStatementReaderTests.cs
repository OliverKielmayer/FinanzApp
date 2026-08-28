using System.IO.Compression;
using System.Text;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Archiv voller Tagesauszüge — die Form, in der die Banken sie bereitstellen.
/// </summary>
/// <remarks>
/// Acht Dateien einzeln durch Lesen, Kontowahl, Duplikatprüfung und Übernahme zu schicken ist
/// keine Arbeit, die jemand tun sollte. Aus dem Archiv wird deshalb eine Vorschau und eine
/// Übernahme. Geprüft wird vor allem, was dabei <em>nicht</em> passieren darf: dass etwas
/// stillschweigend verschwindet, und dass Sätze zweier Konten in einem Topf landen.
/// </remarks>
public sealed class ZipStatementReaderTests
{
    private static readonly ZipStatementReader Reader = new(new CamtStatementParser());

    /// <summary>Ein Tagesauszug mit genau einer Buchung — so sehen die echten aus.</summary>
    private static string Auszug(string iban, string tag, string betrag, string umsatzId)
        => $"""
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.02">
              <BkToCstmrAcctRpt>
                <GrpHdr><MsgId>X</MsgId></GrpHdr>
                <Rpt>
                  <Id>camt052_ONLINEBA</Id>
                  <Acct><Id><IBAN>{iban}</IBAN></Id></Acct>
                  <Bal>
                    <Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>
                    <Amt Ccy="EUR">{betrag}</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Dt><Dt>{tag}</Dt></Dt>
                  </Bal>
                  <Ntry>
                    <Amt Ccy="EUR">{betrag}</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Sts>BOOK</Sts>
                    <BookgDt><Dt>{tag}</Dt></BookgDt>
                    <AcctSvcrRef>NONREF</AcctSvcrRef>
                    <NtryDtls><TxDtls>
                      <Refs><Prtry><Tp>FI-UMSATZ-ID</Tp><Ref>{umsatzId}</Ref></Prtry></Refs>
                      <RltdPties><Dbtr><Nm>Carmen Sperrle</Nm></Dbtr></RltdPties>
                    </TxDtls></NtryDtls>
                  </Ntry>
                </Rpt>
              </BkToCstmrAcctRpt>
            </Document>
            """;

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        buffer.Position = 0;

        return buffer;
    }

    private static MemoryStream DreiTage() => Zip(
        ("2026.06.09.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")),
        ("2026.07.20.xml", Auszug("DE45622500300002759445", "2026-07-20", "121.77", "U-2")),
        ("2026.08.25.xml", Auszug("DE45622500300002759445", "2026-08-25", "203000.00", "U-3")));

    // ── Der gute Fall ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alle_Dateien_des_Archivs_landen_in_einer_Vorschau()
    {
        var statement = await Reader.ReadAsync(DreiTage(), "auszuege.zip");

        Assert.Equal(3, statement.SourceCount);
        Assert.Equal(3, statement.Records.Count);
        Assert.Equal("auszuege.zip", statement.FileName);
        Assert.Equal("DE45622500300002759445", statement.Iban);
    }

    /// <summary>Die Referenzen bleiben je Buchung verschieden — sonst nützt das Archiv nichts.</summary>
    [Fact]
    public async Task Jede_Buchung_behaelt_ihre_eigene_Referenz()
    {
        var statement = await Reader.ReadAsync(DreiTage(), "auszuege.zip");

        Assert.Equal(3, statement.Records.Select(r => r.Reference).Distinct().Count());
    }

    /// <summary>
    /// Der Schlusssaldo gehört dem jüngsten Auszug.
    /// </summary>
    /// <remarks>
    /// Ein Saldo gilt zu einem Stichtag. Den des ältesten Auszugs zu nehmen wäre eine Zahl von
    /// vorgestern mit dem Anschein von heute — und der Kopf der Vorschau stellt sie neben den
    /// Zeitraum, der bis gestern reicht.
    /// </remarks>
    [Fact]
    public async Task Der_Schlusssaldo_kommt_vom_juengsten_Auszug()
    {
        // Absichtlich verkehrt herum benannt: die Reihenfolge im Archiv darf nicht entscheiden.
        var zip = Zip(
            ("z-alt.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")),
            ("a-neu.xml", Auszug("DE45622500300002759445", "2026-08-25", "203000.00", "U-2")));

        Assert.Equal(203000.00m, (await Reader.ReadAsync(zip, "auszuege.zip")).ClosingBalance);
    }

    // ── Was nicht verschwinden darf ────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Datei, die kein Auszug ist, steht mit Grund in der Liste.
    /// </summary>
    /// <remarks>
    /// Sie zu überspringen hieße, aus zehn Dateien neun zu importieren und nichts davon zu
    /// sagen. Wer nachzählt, fände die Lücke — wer nicht, hielte den Import für vollständig.
    /// </remarks>
    [Fact]
    public async Task Eine_fremde_Datei_im_Archiv_wird_gemeldet_statt_uebersprungen()
    {
        var zip = Zip(
            ("2026.06.09.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")),
            ("Hinweis.txt", "Bitte nicht loeschen."));

        var statement = await Reader.ReadAsync(zip, "auszuege.zip");

        Assert.Equal(1, statement.SourceCount);
        Assert.Equal(2, statement.Records.Count);

        var fremd = statement.Records.Single(r => r.Payee == "Hinweis.txt");

        Assert.NotNull(fremd.Problem);
        Assert.Null(fremd.Amount);
    }

    /// <summary>Eine kaputte Datei kippt nicht die anderen sieben.</summary>
    [Fact]
    public async Task Eine_unlesbare_Datei_laesst_die_uebrigen_durch()
    {
        var zip = Zip(
            ("gut.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")),
            ("kaputt.xml", "<Document><Nichts/></Document>"));

        var statement = await Reader.ReadAsync(zip, "auszuege.zip");

        Assert.Equal(1, statement.SourceCount);
        Assert.Contains(statement.Records, r => r.Payee == "kaputt.xml" && r.Problem is not null);
        Assert.Contains(statement.Records, r => r.Amount == 1500.00m);
    }

    /// <summary>Der Beifang der Packprogramme ist kein Fehler und gehört in keine Liste.</summary>
    [Fact]
    public async Task Ordnereintraege_und_Systemdateien_bleiben_draussen()
    {
        var zip = Zip(
            ("__MACOSX/._2026.06.09.xml", "Muell"),
            (".DS_Store", "Muell"),
            ("2026.06.09.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")));

        var statement = await Reader.ReadAsync(zip, "auszuege.zip");

        Assert.Equal(1, statement.SourceCount);
        Assert.Single(statement.Records);
    }

    // ── Was abgewiesen gehört ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei Konten in einem Archiv werden abgewiesen.
    /// </summary>
    /// <remarks>
    /// Die Übernahme bucht alle Sätze auf <em>ein</em> Konto. Sie stillschweigend zusammenzulegen
    /// hieße, die Hälfte auf das falsche Konto zu buchen — und das fiele erst beim Kontostand auf.
    /// </remarks>
    [Fact]
    public async Task Ein_Archiv_mit_zwei_Konten_wird_abgewiesen()
    {
        var zip = Zip(
            ("a.xml", Auszug("DE45622500300002759445", "2026-06-09", "1500.00", "U-1")),
            ("b.xml", Auszug("DE12670623660009114007", "2026-06-10", "20.00", "U-2")));

        var fehler = await Assert.ThrowsAsync<StatementFormatException>(
            () => Reader.ReadAsync(zip, "auszuege.zip"));

        Assert.Contains("mehreren Konten", fehler.Message);
        Assert.Contains("DE45622500300002759445", fehler.Message);
    }

    [Fact]
    public async Task Ein_Archiv_ganz_ohne_Auszug_wird_abgewiesen()
    {
        var zip = Zip(("Hinweis.txt", "Bitte nicht loeschen."));

        var fehler = await Assert.ThrowsAsync<StatementFormatException>(
            () => Reader.ReadAsync(zip, "auszuege.zip"));

        Assert.Contains("kein camt-Auszug", fehler.Message);
        Assert.Contains("Hinweis.txt", fehler.Message);
    }

    [Fact]
    public async Task Ein_leeres_Archiv_wird_abgewiesen()
        => Assert.Contains(
            "ist leer",
            (await Assert.ThrowsAsync<StatementFormatException>(
                () => Reader.ReadAsync(Zip(), "auszuege.zip"))).Message);

    [Fact]
    public async Task Etwas_das_kein_ZIP_ist_wird_abgewiesen()
    {
        var kein = new MemoryStream(Encoding.UTF8.GetBytes("<Document/>"));

        var fehler = await Assert.ThrowsAsync<StatementFormatException>(
            () => Reader.ReadAsync(kein, "auszuege.zip"));

        Assert.Contains("kein lesbares ZIP-Archiv", fehler.Message);
    }
}
