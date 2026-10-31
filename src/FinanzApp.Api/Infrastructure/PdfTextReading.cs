using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Eine Textzeile mit ihrer Herkunft.
/// </summary>
/// <param name="Page">Seitenzahl, ab 1. Sie steht später an jedem übernommenen Wert.</param>
/// <param name="Cells">
/// Die Zeile in Spalten zerlegt, links nach rechts. Ein breiter Zwischenraum trennt in einem
/// Formular Beschriftung von Wert; genau daran hängt die Zuordnung, und der geht verloren,
/// sobald man alles zu einer Zeichenkette zusammenzieht.
/// </param>
public sealed record PdfLine(int Page, IReadOnlyList<string> Cells)
{
    /// <summary>Die ganze Zeile, Spalten durch ein Leerzeichen getrennt.</summary>
    /// <remarks>Zum Suchen nach Überschriften und Schlüsselwörtern.</remarks>
    public string Text { get; } = string.Join(" ", Cells);
}



/// <summary>
/// Was ein PDF hergibt.
/// </summary>
/// <remarks>
/// <see cref="HasTextLayer"/> entscheidet, wie sehr man den Werten trauen kann — v5-Handoff,
/// Abschnitt 14.1. <see cref="TextIsInvisible"/> unterscheidet dabei zwei Fälle, die gleich
/// aussehen und es nicht sind: sichtbarer Text ist das, was auf dem Papier steht; unsichtbarer
/// liegt hinter einem Seitenbild und stammt aus einer Erkennung oder einer Druckstrecke.
/// </remarks>
public sealed record PdfContent
{
    public required int PageCount { get; init; }
    public required IReadOnlyList<PdfLine> Lines { get; init; }

    /// <summary>Ob überhaupt Text zu holen war.</summary>
    public bool HasTextLayer => Lines.Count > 0;

    /// <summary>
    /// Der ganze Text am Stück, Zeile für Zeile — für Suche und Vorschlag.
    /// </summary>
    /// <remarks>
    /// Die Spalten sind hier wieder zusammengesetzt. Für die Leseregeln sind sie das
    /// Entscheidende; wer ein Wort sucht, stört sich daran nur, weil „Rechnungsnummer 4711“ sonst
    /// über zwei Zellen verteilt läge und keine Suche beides zugleich fände.
    /// </remarks>
    public string PlainText => string.Join('\n', Lines.Select(z => z.Text));

    /// <summary>
    /// Der Text liegt unsichtbar hinter Seitenbildern.
    /// </summary>
    /// <remarks>
    /// Ein „durchsuchbares“ Scan-PDF. Lesbar, aber nicht dasselbe wie eine sichtbare Textebene:
    /// was man sieht, ist das Bild, und der Text daneben kann davon abweichen.
    /// </remarks>
    public required bool TextIsInvisible { get; init; }

    public required int ImageCount { get; init; }

    /// <summary>Woher der Text kommt. Vorgabe ist die Textebene eines PDF.</summary>
    public TextSource Source { get; init; } = TextSource.PdfTextLayer;

    /// <summary>Wie der Vorschlagsschritt die Beschaffenheit nennt.</summary>
    /// <remarks>
    /// <para>Drei Befunde am PDF, nicht zwei: eine Datei ohne Seiten ist nicht bildlastig,
    /// sondern gar nicht erst zu öffnen. „Nur Bild“ zu melden, wo das Format kaputt ist,
    /// schickte den Nutzer auf die Suche nach einer Texterkennung, die ihm nicht hilft.</para>
    /// <para>Und drei am Bild: keine Erkennung eingerichtet, Erkennung ohne Fund, Erkennung mit
    /// Text. Die ersten beiden sehen auf dem Schirm gleich aus — leere Maske — und verlangen
    /// Verschiedenes. Beim einen fehlt eine Einrichtung, beim anderen eine bessere Aufnahme.</para>
    /// </remarks>
    public string Note => Source switch
    {
        TextSource.Image =>
            "ein Bild — ohne eingerichtete Texterkennung ist daraus nichts auszulesen",

        TextSource.Recognition when !HasTextLayer =>
            "ein Bild — die Texterkennung hat darauf keinen Text gefunden",

        TextSource.Recognition => "aus dem Bild erkannter Text",

        _ => PageCount == 0
            ? "die Datei ließ sich nicht als PDF lesen"
            : !HasTextLayer
                ? "nur Bild — ohne Texterkennung nichts auszulesen"
                : TextIsInvisible
                    ? "Seitenbilder mit Textebene darunter"
                    : "durchsuchbarer Text",
    };
}

/// <summary>
/// Liest die Textebene eines PDF.
/// </summary>
/// <remarks>
/// Eine Schnittstelle, weil der Leser austauschbar bleiben soll — v5-Handoff, Abschnitt 14.6:
/// „Anbieterabhängige OCR bleibt austauschbar und ist nie Voraussetzung.“ Wo kein Text zu holen
/// ist, kommt eine leere Antwort mit Begründung, und die Maske erscheint unausgefüllt.
/// </remarks>
public interface IPdfTextReader
{
    PdfContent Read(Stream content);
}

/// <summary>
/// Der eingebaute Leser auf Basis von PdfPig.
/// </summary>
/// <remarks>
/// <para>Er braucht keine Texterkennung: beide Beispieldokumente tragen eine Textebene — die
/// Quartalsaufstellung sichtbar, der Statusreport unsichtbar hinter vier Seitenbildern. Der
/// Handoff nahm für den Statusreport OCR an; an der echten Datei geprüft ist sie unnötig.</para>
/// <para>Ausgegeben werden Zeilen in Spalten, aus den Koordinaten gesetzt — nicht in der
/// Reihenfolge, in der die Zeichen im Dokument stehen. Eine Beschriftung links, ihr Betrag
/// rechts: das ist die Struktur, an der die Zuordnung hängt. Und je Seite getrennt, weil jeder
/// übernommene Wert seine Herkunftsseite tragen muss.</para>
/// </remarks>
public sealed class PdfPigTextReader : IPdfTextReader
{
    public PdfContent Read(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        try
        {
            using var pdf = PdfDocument.Open(buffer.ToArray());

            var lines = new List<PdfLine>();
            var bilder = 0;
            var sichtbar = 0;
            var unsichtbar = 0;

            foreach (var page in pdf.GetPages())
            {
                bilder += page.GetImages().Count();
                Count(page, ref sichtbar, ref unsichtbar);

                lines.AddRange(Lines(page));
            }

            return new PdfContent
            {
                PageCount = pdf.NumberOfPages,
                Lines = lines,

                // Unsichtbar nur, wenn praktisch nichts sichtbar ist. Ein einzelnes verstecktes
                // Wort in einem normalen Dokument macht daraus keinen Scan.
                TextIsInvisible = unsichtbar > 0 && sichtbar < unsichtbar / 10,
                ImageCount = bilder,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ein unlesbares PDF ist kein Absturz, sondern eine leere Maske mit Begründung.
            return new PdfContent
            {
                PageCount = 0,
                Lines = [],
                TextIsInvisible = false,
                ImageCount = 0,
            };
        }
    }

    /// <summary>
    /// Wie viel Text sichtbar ist und wie viel nicht.
    /// </summary>
    /// <remarks>
    /// Der Textdarstellungsmodus 3 heißt „unsichtbar“. Genau ihn benutzen durchsuchbare Scans,
    /// um den erkannten Text hinter das Seitenbild zu legen.
    /// </remarks>
    private static void Count(Page page, ref int sichtbar, ref int unsichtbar)
    {
        foreach (var letter in page.Letters)
        {
            if (letter.RenderingMode == UglyToad.PdfPig.Core.TextRenderingMode.Neither)
            {
                unsichtbar++;
            }
            else
            {
                sichtbar++;
            }
        }
    }

    /// <summary>
    /// Die Wörter einer Seite, an den gemeinsamen Zeilensetzer gereicht.
    /// </summary>
    /// <remarks>
    /// Das Vorzeichen ist der ganze Unterschied zwischen einem PDF und einem Bild: hier wächst Y
    /// nach oben, dort nach unten. <see cref="TextWord.Middle"/> zählt in Leserichtung, also
    /// kehrt die Umrechnung es um. Alles Weitere — bündeln, ordnen, in Spalten trennen — gilt
    /// für beide Quellen gleich und steht deshalb in <see cref="TextLayout"/>.
    /// </remarks>
    private static IEnumerable<PdfLine> Lines(Page page)
        => TextLayout.Lines(
            page.Number,
            page.GetWords(NearestNeighbourWordExtractor.Instance)
                .Select(w => new TextWord(
                    w.Text,
                    w.BoundingBox.Left,
                    w.BoundingBox.Right,
                    -(w.BoundingBox.Bottom + w.BoundingBox.Top) / 2.0,
                    w.BoundingBox.Height)));
}
