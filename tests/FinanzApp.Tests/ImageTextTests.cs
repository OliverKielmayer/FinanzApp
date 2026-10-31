using System.Text;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Ein abfotografierter Beleg: erkannt als Bild, gelesen oder ehrlich ungelesen.
/// </summary>
/// <remarks>
/// Die Erkennung selbst steckt in einem eigenen Programm und braucht Windows. Was hier geprüft
/// wird, ist alles davor und danach: wer welche Datei bekommt, was aus den erkannten Wörtern
/// wird, und was dasteht, wenn gar keine Erkennung eingerichtet ist.
/// </remarks>
public sealed class ImageTextTests
{
    // ── Welche Datei ist ein Bild ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]                      // JPEG
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]  // PNG
    [InlineData(new byte[] { 0x42, 0x4D, 0x36, 0x00 }, true)]                      // BMP
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, true)]                      // TIFF
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 }, false)]                     // %PDF
    [InlineData(new byte[] { 0x3C, 0x3F, 0x78, 0x6D }, false)]                     // <?xm
    [InlineData(new byte[] { }, false)]
    public void Das_Format_steht_in_den_ersten_Bytes(byte[] kopf, bool bild)
        => Assert.Equal(bild, ImageFormats.LooksLikeImage(kopf));

    [Fact]
    public void Ein_RIFF_ist_nur_mit_WEBP_ein_Bild()
    {
        Assert.True(ImageFormats.LooksLikeImage("RIFF0000WEBP"u8));

        // Dieselben vier Anfangsbytes trägt eine WAV-Datei.
        Assert.False(ImageFormats.LooksLikeImage("RIFF0000WAVE"u8));
    }

    [Fact]
    public void Eine_HEIC_Aufnahme_vom_iPhone_gilt_als_Bild()
    {
        Assert.True(ImageFormats.LooksLikeImage("\0\0\0\u0018ftypheic"u8));
        Assert.False(ImageFormats.LooksLikeImage("\0\0\0\u0018ftypqt  "u8));
    }

    /// <summary>
    /// Die Endung entscheidet nicht.
    /// </summary>
    /// <remarks>
    /// Eine Handyaufnahme, die jemand „Beleg.pdf“ genannt hat, liefe sonst in den PDF-Leser und
    /// käme als „ließ sich nicht als PDF lesen“ zurück — über eine Datei, die bestens lesbar ist.
    /// </remarks>
    [Fact]
    public void Ein_falsch_benanntes_Bild_geht_trotzdem_zur_Erkennung()
    {
        using var jpeg = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);

        var inhalt = Reader(befehl: string.Empty).Read(jpeg);

        Assert.Equal(TextSource.Image, inhalt.Source);
    }

    [Fact]
    public void Ein_PDF_geht_weiter_an_den_PDF_Leser()
    {
        using var pdf = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4 kaputt"));

        var inhalt = Reader(befehl: string.Empty).Read(pdf);

        Assert.Equal(TextSource.PdfTextLayer, inhalt.Source);
        Assert.Equal("die Datei ließ sich nicht als PDF lesen", inhalt.Note);
    }

    // ── Ohne eingerichtete Erkennung ───────────────────────────────────────────────────────

    /// <summary>
    /// Ohne Erkennung sagt der Beleg, dass er ein Bild ist — und nicht, er sei kaputt.
    /// </summary>
    /// <remarks>
    /// Vorher lief ein JPEG in den PDF-Leser und kam als „ließ sich nicht als PDF lesen“ zurück.
    /// Das schickt den Nutzer auf die Suche nach einem Dateifehler, den es nicht gibt. Die
    /// Auskunft, die weiterhilft, ist: es ist ein Bild, und es ist keine Erkennung eingerichtet.
    /// </remarks>
    [Fact]
    public void Ohne_Erkennung_heisst_ein_Bild_ein_Bild()
    {
        using var jpeg = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0]);

        var inhalt = Reader(befehl: string.Empty).Read(jpeg);

        Assert.Empty(inhalt.Lines);
        Assert.False(inhalt.HasTextLayer);
        Assert.Equal(1, inhalt.PageCount);
        Assert.Contains("ohne eingerichtete Texterkennung", inhalt.Note);
    }

    /// <summary>
    /// Ein fehlendes Erkennungsprogramm verhindert die Ablage nicht.
    /// </summary>
    /// <remarks>
    /// Der Beleg ist schon da, wenn gelesen wird. Ihn an einer Einrichtung scheitern zu lassen,
    /// die mit ihm nichts zu tun hat, hieße: ein fotografierter Zettel geht verloren, weil ein
    /// Pfad in einer Einstellungsdatei nicht stimmt.
    /// </remarks>
    [Fact]
    public void Ein_fehlendes_Programm_gibt_eine_leere_Erkennung_zurueck()
    {
        using var jpeg = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0]);

        var inhalt = Reader(befehl: "gibt-es-nicht-hoffentlich.exe").Read(jpeg);

        Assert.Empty(inhalt.Lines);
        Assert.Equal(TextSource.Recognition, inhalt.Source);
        Assert.Contains("keinen Text gefunden", inhalt.Note);
    }

    // ── Aus erkannten Wörtern werden Zeilen ────────────────────────────────────────────────

    /// <summary>
    /// Ein zerlegtes Datum wird wieder eins.
    /// </summary>
    /// <remarks>
    /// Windows gibt „31.12.2025“ als drei Wörter zurück: „31“, „.“, „12.2025“. Am Abstand ist
    /// das nicht zu erkennen — der Punkt stand genauso weit vom Nachbarn entfernt wie zwei
    /// echte Wörter. Zu erkennen ist es daran, dass ein Punkt für sich kein Wort ist.
    /// </remarks>
    [Fact]
    public void Ein_zerlegtes_Datum_wird_wieder_zusammengesetzt()
    {
        var zeilen = Lines([
            Wort(0, "Stichtag", 40, 180, 40, 30),
            Wort(0, "31", 341, 375, 47, 27),
            Wort(0, ".", 385, 389, 71, 3),
            Wort(0, "12.2025", 396, 526, 47, 27),
        ]);

        var zeile = Assert.Single(zeilen);
        Assert.Contains("31.12.2025", zeile.Cells);
    }

    /// <summary>
    /// Ein Satzende klebt nicht am nächsten Wort.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zum Datum steckt im Zeichen danach: auf einen Punkt folgt in „31.12.“
    /// eine Ziffer und in „Betrag. Der Rest“ ein Wort. Am Abstand wäre beides dasselbe, und
    /// „Betrag.Der“ fände keine Regel wieder.
    /// </remarks>
    [Fact]
    public void Ein_Satzende_klebt_nicht_am_naechsten_Wort()
    {
        var zeilen = Lines([
            Wort(0, "Betrag", 40, 140, 40, 30),
            Wort(0, ".", 142, 146, 60, 6),
            Wort(0, "Der", 152, 210, 40, 30),
        ]);

        var zeile = Assert.Single(zeilen);
        Assert.Equal("Betrag. Der", zeile.Text);
    }

    /// <summary>
    /// Was weit auseinandersteht, gehört nicht zusammen.
    /// </summary>
    /// <remarks>
    /// „Seite.“ am linken Rand und die Zahl am rechten: die Zeichenklassen allein würden hier
    /// kleben, und aus zwei Spalten würde eine.
    /// </remarks>
    [Fact]
    public void Was_sich_nicht_beruehrt_wird_nicht_geklebt()
    {
        var zeilen = Lines([
            Wort(0, "Seite.", 40, 140, 40, 30),
            Wort(0, "2", 900, 920, 40, 30),
        ]);

        var zeile = Assert.Single(zeilen);
        Assert.Equal(["Seite.", "2"], zeile.Cells);
    }

    [Fact]
    public void Ueber_Zeilen_hinweg_wird_nichts_geklebt()
    {
        var zeilen = Lines([
            Wort(0, "Summe", 40, 140, 40, 30),
            Wort(0, ".", 150, 154, 60, 6),
            Wort(1, "1234", 40, 140, 200, 30),
        ]);

        Assert.Equal(2, zeilen.Count);
        Assert.Contains("Summe.", zeilen[0].Cells);
        Assert.Contains("1234", zeilen[1].Cells);
    }

    /// <summary>
    /// Beschriftung links, Wert rechts — die Struktur, an der die Leseregeln hängen.
    /// </summary>
    [Fact]
    public void Ein_weiter_Zwischenraum_trennt_die_Spalten()
    {
        var zeilen = Lines([
            Wort(0, "Anteilsguthaben", 46, 312, 247, 34),
            Wort(0, "12.345,67", 706, 880, 247, 34),
            Wort(0, "EUR", 900, 960, 247, 34),
        ]);

        var zeile = Assert.Single(zeilen);
        Assert.Equal(["Anteilsguthaben", "12.345,67 EUR"], zeile.Cells);
    }

    /// <summary>
    /// Im Bild wächst Y nach unten — die obere Zeile kommt zuerst.
    /// </summary>
    /// <remarks>
    /// Im PDF ist es umgekehrt. Stünde die Umrechnung falsch herum, läse die Anwendung jedes
    /// Blatt von unten nach oben, und zwar ohne eine einzige Fehlermeldung.
    /// </remarks>
    [Fact]
    public void Die_obere_Zeile_steht_zuerst()
    {
        var zeilen = Lines([
            Wort(1, "unten", 40, 140, 400, 30),
            Wort(0, "oben", 40, 140, 100, 30),
        ]);

        Assert.Equal("oben", zeilen[0].Text);
        Assert.Equal("unten", zeilen[1].Text);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private static OcrWord Wort(int zeile, string text, double links, double rechts,
        double oben, double hoehe)
        => new(zeile, text, links, rechts, oben, hoehe);

    private static List<PdfLine> Lines(IReadOnlyList<OcrWord> woerter)
        => [.. TextLayout.Lines(1, OcrLayout.Words(woerter))];

    private static ScanTextReader Reader(string befehl)
        => new(
            new PdfPigTextReader(),
            new ImageTextReader(
                new OcrOptions { Command = befehl, TimeoutSeconds = 5 },
                NullLogger<ImageTextReader>.Instance));
}
