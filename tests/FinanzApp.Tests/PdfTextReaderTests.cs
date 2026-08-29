using System.Globalization;
using System.Text;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Der PDF-Leser — v5-Handoff, Abschnitt 14.1.
/// </summary>
/// <remarks>
/// <para>Geprüft wird an echten PDF-Dateien, die der Test selbst baut. Ein Leser, der nur gegen
/// nachgebaute Datenstrukturen getestet ist, sagt nichts darüber aus, ob er ein Dokument lesen
/// kann.</para>
/// <para>Der entscheidende Punkt ist die <b>Leserichtung</b>. In der echten
/// Quartalsaufstellung stehen die Zeichen einer Tabellenzeile in einer anderen Reihenfolge im
/// Datenstrom, als sie auf dem Papier erscheinen — der Kurswert vor der Stückzahl. Wer den
/// Inhalt in seiner Speicherreihenfolge liest, bekommt <c>„EUR95.558,12763 A0RPWHStück“</c> und
/// baut darauf eine Feldzuordnung, die auf Zufall beruht.</para>
/// </remarks>
public sealed class PdfTextReaderTests
{
    private readonly PdfPigTextReader reader = new();

    private PdfContent Read(byte[] pdf) => reader.Read(new MemoryStream(pdf));

    // ── Leserichtung ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Zeile entsteht aus den Koordinaten, nicht aus der Reihenfolge im Datenstrom.
    /// </summary>
    [Fact]
    public void Zeilen_folgen_der_Seite_und_nicht_dem_Datenstrom()
    {
        // Absichtlich verdreht geschrieben: erst rechts, dann links.
        var pdf = Pdf(
            new(430, 700, "95.558,12"),
            new(60, 700, "Kurswert"),
            new(430, 680, "125,240"),
            new(60, 680, "Kurs"));

        var inhalt = Read(pdf);

        Assert.Equal(["Kurswert", "95.558,12"], inhalt.Lines[0].Cells);
        Assert.Equal(["Kurs", "125,240"], inhalt.Lines[1].Cells);
    }

    /// <summary>
    /// Ein breiter Zwischenraum trennt Spalten, ein schmaler nicht.
    /// </summary>
    /// <remarks>
    /// Ohne diese Unterscheidung wäre „Wert der Versicherung“ drei Spalten und „Rückkaufswert
    /// 18.373,87 EUR“ eine — beides falsch herum.
    /// </remarks>
    [Fact]
    public void Woerter_bleiben_zusammen_Spalten_werden_getrennt()
    {
        var pdf = Pdf(
            new(60, 700, "Wert der Versicherung"),
            new(60, 680, "Rueckkaufswert"),
            new(430, 680, "18.373,87 EUR"));

        var inhalt = Read(pdf);

        Assert.Equal(["Wert der Versicherung"], inhalt.Lines[0].Cells);
        Assert.Equal(["Rueckkaufswert", "18.373,87 EUR"], inhalt.Lines[1].Cells);
    }

    /// <summary>
    /// Jede Zeile weiß, auf welcher Seite sie stand.
    /// </summary>
    [Fact]
    public void Die_Seitenzahl_kommt_mit()
    {
        var pdf = Pages([new Piece(60, 700, "Seite eins")], [new Piece(60, 700, "Seite zwei")]);

        var inhalt = Read(pdf);

        Assert.Equal(2, inhalt.PageCount);
        Assert.Equal(1, inhalt.Lines[0].Page);
        Assert.Equal(2, inhalt.Lines[1].Page);
    }

    // ── Beschaffenheit ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsichtbarer Text wird als solcher erkannt.
    /// </summary>
    /// <remarks>
    /// Der Textdarstellungsmodus 3 legt Text hinter ein Seitenbild — so arbeiten durchsuchbare
    /// Scans, und so arbeitet auch die Druckstrecke, aus der der Statusreport des Nutzers
    /// stammt. Lesbar ist er trotzdem; er zählt nur weniger.
    /// </remarks>
    [Fact]
    public void Text_hinter_dem_Seitenbild_wird_erkannt()
    {
        var sichtbar = Read(Pdf(new Piece(60, 700, "Rueckkaufswert")));
        var versteckt = Read(Pdf(new Piece(60, 700, "Rueckkaufswert") { Invisible = true }));

        Assert.True(sichtbar.HasTextLayer);
        Assert.False(sichtbar.TextIsInvisible);

        Assert.True(versteckt.HasTextLayer);
        Assert.True(versteckt.TextIsInvisible);
        Assert.Contains("hinter Seitenbildern", versteckt.Note);
    }

    /// <summary>
    /// Ein einzelnes verstecktes Wort macht aus einem Dokument keinen Scan.
    /// </summary>
    [Fact]
    public void Einzelne_versteckte_Zeichen_kippen_das_Urteil_nicht()
    {
        var pdf = Pdf(
            new(60, 700, "Eine ganz gewoehnliche Seite mit sichtbarem Text darauf"),
            new(60, 680, "und noch einer Zeile, die man auch wirklich sehen kann"),
            new(60, 660, "x") { Invisible = true });

        Assert.False(Read(pdf).TextIsInvisible);
    }

    /// <summary>
    /// Eine Seite ohne Text liefert eine leere Antwort mit Begründung — keinen Fehler.
    /// </summary>
    [Fact]
    public void Ohne_Text_kommt_ein_Befund_und_kein_Absturz()
    {
        var inhalt = Read(Pdf());

        Assert.False(inhalt.HasTextLayer);
        Assert.Contains("nur Bild", inhalt.Note);
    }

    /// <summary>
    /// Was kein PDF ist, wird nicht zum Absturz.
    /// </summary>
    /// <remarks>
    /// Der Nutzer lädt hoch, was er hat. Eine kaputte Datei ist eine leere Maske mit
    /// Begründung — nicht ein Fehler, der die Ablage verhindert.
    /// </remarks>
    [Fact]
    public void Eine_kaputte_Datei_liefert_nichts_statt_zu_werfen()
    {
        var inhalt = Read(Encoding.UTF8.GetBytes("Das ist kein PDF."));

        Assert.False(inhalt.HasTextLayer);
        Assert.Equal(0, inhalt.PageCount);
    }

    // ── Ein PDF bauen ──────────────────────────────────────────────────────────────────────

    /// <summary>Ein Stück Text an einer Stelle der Seite.</summary>
    private sealed record Piece(int X, int Y, string Text)
    {
        /// <summary>Darstellungsmodus 3: vorhanden, aber nicht sichtbar.</summary>
        public bool Invisible { get; init; }
    }

    /// <summary>
    /// Baut ein gültiges PDF mit den angegebenen Textstücken, eine Seite je Liste.
    /// </summary>
    /// <remarks>
    /// Von Hand statt mit einer Bibliothek: eine Erzeuger-Abhängigkeit nur für Tests wäre eine
    /// zweite Abhängigkeit für dieselbe Sache. Das Format ist hier auch klein genug — Katalog,
    /// Seitenbaum, Seiten, Inhaltsströme, Schrift, Querverweistabelle.
    /// </remarks>
    private static byte[] Pdf(params Piece[] stuecke) => Pages(stuecke);

    private static byte[] Pages(params IReadOnlyList<Piece>[] seiten)
    {
        if (seiten.Length == 0)
        {
            seiten = [[]];
        }

        var objekte = new List<string>();

        // 1 Katalog, 2 Seitenbaum, 3 Schrift, danach je Seite ein Seitenobjekt und ein Strom.
        var ersteSeite = 4;
        var seitenIds = Enumerable.Range(0, seiten.Length).Select(i => ersteSeite + i * 2).ToList();

        objekte.Add("<</Type/Catalog/Pages 2 0 R>>");
        objekte.Add($"<</Type/Pages/Kids[{string.Join(" ", seitenIds.Select(i => $"{i} 0 R"))}]"
                    + $"/Count {seiten.Length}>>");
        objekte.Add("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");

        for (var i = 0; i < seiten.Length; i++)
        {
            var inhalt = Stream(seiten[i]);

            objekte.Add($"<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]"
                        + $"/Resources<</Font<</F1 3 0 R>>>>/Contents {seitenIds[i] + 1} 0 R>>");
            objekte.Add($"<</Length {inhalt.Length}>>\nstream\n{inhalt}\nendstream");
        }

        return Assemble(objekte);
    }

    private static string Stream(IReadOnlyList<Piece> stuecke)
    {
        var text = new StringBuilder();

        foreach (var stueck in stuecke)
        {
            text.Append("BT /F1 10 Tf ");

            if (stueck.Invisible)
            {
                text.Append("3 Tr ");
            }

            text.Append(CultureInfo.InvariantCulture, $"1 0 0 1 {stueck.X} {stueck.Y} Tm ");
            text.Append(CultureInfo.InvariantCulture, $"({Escape(stueck.Text)}) Tj ET\n");
        }

        return text.ToString();
    }

    private static string Escape(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    /// <summary>Setzt die Objekte samt Querverweistabelle zu einer Datei zusammen.</summary>
    private static byte[] Assemble(List<string> objekte)
    {
        var datei = new StringBuilder("%PDF-1.4\n");
        var stellen = new List<int>();

        for (var i = 0; i < objekte.Count; i++)
        {
            stellen.Add(datei.Length);
            datei.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objekte[i]}\nendobj\n");
        }

        var xref = datei.Length;

        datei.Append(CultureInfo.InvariantCulture, $"xref\n0 {objekte.Count + 1}\n");
        datei.Append("0000000000 65535 f \n");

        foreach (var stelle in stellen)
        {
            datei.Append(CultureInfo.InvariantCulture, $"{stelle:D10} 00000 n \n");
        }

        datei.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {objekte.Count + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");

        return Encoding.ASCII.GetBytes(datei.ToString());
    }
}
