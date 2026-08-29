using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

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
    /// Der Text liegt unsichtbar hinter Seitenbildern.
    /// </summary>
    /// <remarks>
    /// Ein „durchsuchbares“ Scan-PDF. Lesbar, aber nicht dasselbe wie eine sichtbare Textebene:
    /// was man sieht, ist das Bild, und der Text daneben kann davon abweichen.
    /// </remarks>
    public required bool TextIsInvisible { get; init; }

    public required int ImageCount { get; init; }

    /// <summary>Wie der Vorschlagsschritt die Beschaffenheit nennt.</summary>
    /// <remarks>
    /// Drei Befunde, nicht zwei: eine Datei ohne Seiten ist nicht bildlastig, sondern gar nicht
    /// erst zu öffnen. „Nur Bild“ zu melden, wo das Format kaputt ist, schickte den Nutzer auf
    /// die Suche nach einer Texterkennung, die ihm nicht hilft.
    /// </remarks>
    public string Note => PageCount == 0
        ? "die Datei ließ sich nicht als PDF lesen"
        : !HasTextLayer
            ? "nur Bild — ohne Texterkennung nichts auszulesen"
            : TextIsInvisible
                ? "Textebene hinter Seitenbildern — lesbar, aber nicht das Sichtbare"
                : "Textebene vorhanden";
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
    /// Die Zeilen einer Seite, aus den Koordinaten gesetzt.
    /// </summary>
    /// <remarks>
    /// <para>Nicht in Inhaltsreihenfolge: die schreibt der Erzeuger, wie es ihm passt. Die echte
    /// Quartalsaufstellung liefert so <c>„EUR95.558,12763 A0RPWHStück“</c> — vier Spalten einer
    /// Tabellenzeile, ineinandergeschoben. Wer darauf ein Feld abbildet, bildet es auf Zufall ab.</para>
    /// <para>Also: Wörter nach ihrer Höhe zu Zeilen bündeln, innerhalb der Zeile nach links/rechts
    /// ordnen und dort trennen, wo ein Zwischenraum breiter ist als ein paar Zeichen. Was dabei
    /// entsteht, ist die Tabellenstruktur des Blattes.</para>
    /// </remarks>
    private static IEnumerable<PdfLine> Lines(Page page)
    {
        var woerter = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(w => Clean(w.Text).Length > 0)
            .ToList();

        if (woerter.Count == 0)
        {
            yield break;
        }

        // Von oben nach unten. Im PDF wächst Y nach oben, gelesen wird andersherum.
        foreach (var zeile in Rows(woerter))
        {
            yield return new PdfLine(page.Number, Cells(zeile));
        }
    }

    /// <summary>
    /// Wörter zu Zeilen bündeln.
    /// </summary>
    /// <remarks>
    /// Über die Mitte der Grundlinie, nicht über die Unterkante: Ziffern, Großbuchstaben und
    /// Buchstaben mit Unterlänge sitzen unterschiedlich tief, stehen aber in derselben Zeile.
    /// Die Toleranz richtet sich nach der Schrifthöhe des Wortes selbst, damit eine Überschrift
    /// nicht dieselbe Toleranz bekommt wie eine Fußnote.
    /// </remarks>
    private static IEnumerable<List<Word>> Rows(List<Word> woerter)
    {
        var sortiert = woerter.OrderByDescending(Middle).ThenBy(w => w.BoundingBox.Left).ToList();

        var laufend = new List<Word> { sortiert[0] };
        var hoehe = Middle(sortiert[0]);

        foreach (var wort in sortiert.Skip(1))
        {
            var toleranz = Math.Max(1.5, wort.BoundingBox.Height * 0.4);

            if (Math.Abs(Middle(wort) - hoehe) <= toleranz)
            {
                laufend.Add(wort);
                continue;
            }

            yield return laufend;
            laufend = [wort];
            hoehe = Middle(wort);
        }

        yield return laufend;
    }

    /// <summary>
    /// Eine Zeile in ihre Spalten zerlegen.
    /// </summary>
    /// <remarks>
    /// Ein Zwischenraum von mehr als etwa zwei Zeichenbreiten ist keine Wortlücke mehr, sondern
    /// ein Spaltenabstand. Die Zeichenbreite wird aus dem vorangehenden Wort geschätzt — eine
    /// feste Punktzahl träfe bei 7 pt und bei 14 pt nie beide.
    /// </remarks>
    private static List<string> Cells(List<Word> zeile)
    {
        var sortiert = zeile.OrderBy(w => w.BoundingBox.Left).ToList();

        var spalten = new List<string>();
        var laufend = new List<string> { Clean(sortiert[0].Text) };
        var vorher = sortiert[0];

        foreach (var wort in sortiert.Skip(1))
        {
            var zeichen = vorher.Text.Length == 0
                ? vorher.BoundingBox.Height * 0.5
                : vorher.BoundingBox.Width / vorher.Text.Length;

            var luecke = wort.BoundingBox.Left - vorher.BoundingBox.Right;

            if (luecke > Math.Max(2.0, zeichen * 2.0))
            {
                spalten.Add(string.Join(" ", laufend));
                laufend = [];
            }

            laufend.Add(Clean(wort.Text));
            vorher = wort;
        }

        spalten.Add(string.Join(" ", laufend));
        return [.. spalten.Where(s => s.Length > 0)];
    }

    /// <summary>Die Mitte eines Wortes in der Höhe.</summary>
    private static double Middle(Word wort)
        => (wort.BoundingBox.Bottom + wort.BoundingBox.Top) / 2.0;

    /// <summary>
    /// Geschützte Leerzeichen weg, Trennstriche am Wortende weg.
    /// </summary>
    /// <remarks>
    /// Der Statusreport der Heidelberger trennt am Zeilenende nicht mit einem Bindestrich,
    /// sondern mit dem Nicht-Zeichen <c>U+00AC</c> — siebzehnmal im Dokument. Bliebe es stehen,
    /// hieße das Wort „Ge¬“ und keine Suche fände es. Nur am Wortende entfernt: mitten im Text
    /// wäre ein <c>¬</c> ein Zeichen und keine Trennung.
    /// </remarks>
    private static string Clean(string text)
        => text.Replace(' ', ' ')
            .Replace("­", string.Empty)
            .TrimEnd('¬')
            .Trim();
}
