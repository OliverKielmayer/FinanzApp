namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Ein erkanntes Wort mit seinem Platz auf der Seite.
/// </summary>
/// <param name="Text">Roh, wie der Leser ihn liefert — geputzt wird erst beim Setzen der Zeile.</param>
/// <param name="Middle">
/// Die Mitte der Zeile, in <b>Leserichtung</b>: kleiner heißt weiter oben.
/// </param>
/// <remarks>
/// Die Leserichtung steht ausdrücklich im Vertrag, weil die beiden Quellen sie verschieden
/// zählen: im PDF wächst Y nach oben, in einem Bild nach unten. Ohne diese Festlegung stünde
/// die eine Seite auf dem Kopf, und zwar ohne Fehlermeldung.
/// </remarks>
public readonly record struct TextWord(
    string Text, double Left, double Right, double Middle, double Height);

/// <summary>
/// Setzt aus Wörtern mit Koordinaten Zeilen und Spalten.
/// </summary>
/// <remarks>
/// <para>Gemeinsam für beide Quellen: die Textebene eines PDF und die Ausgabe einer
/// Texterkennung. Beide liefern dasselbe — Wörter mit Kästchen — und für beide gilt dieselbe
/// Frage: welche Wörter stehen in einer Zeile, und wo endet eine Spalte.</para>
/// <para>Zweimal geschrieben wäre es zweimal zu pflegen, und die Fassung, die seltener benutzt
/// wird, bliebe hinter der anderen zurück. Gerade bei der Erkennung wäre das die falsche: sie
/// liefert das unsaubere Material.</para>
/// </remarks>
public static class TextLayout
{
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
    public static IEnumerable<PdfLine> Lines(int page, IEnumerable<TextWord> words)
    {
        var woerter = words.Where(w => Clean(w.Text).Length > 0).ToList();

        if (woerter.Count == 0)
        {
            yield break;
        }

        foreach (var zeile in Rows(woerter))
        {
            yield return new PdfLine(page, Cells(zeile));
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
    private static IEnumerable<List<TextWord>> Rows(List<TextWord> woerter)
    {
        var sortiert = woerter.OrderBy(w => w.Middle).ThenBy(w => w.Left).ToList();

        var laufend = new List<TextWord> { sortiert[0] };
        var hoehe = sortiert[0].Middle;

        foreach (var wort in sortiert.Skip(1))
        {
            var toleranz = Math.Max(1.5, wort.Height * 0.4);

            if (Math.Abs(wort.Middle - hoehe) <= toleranz)
            {
                laufend.Add(wort);
                continue;
            }

            yield return laufend;
            laufend = [wort];
            hoehe = wort.Middle;
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
    private static List<string> Cells(List<TextWord> zeile)
    {
        var sortiert = zeile.OrderBy(w => w.Left).ToList();

        var spalten = new List<string>();
        var laufend = new List<string> { Clean(sortiert[0].Text) };
        var vorher = sortiert[0];

        foreach (var wort in sortiert.Skip(1))
        {
            var zeichen = vorher.Text.Length == 0
                ? vorher.Height * 0.5
                : (vorher.Right - vorher.Left) / vorher.Text.Length;

            var luecke = wort.Left - vorher.Right;

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

    /// <summary>
    /// Geschützte Leerzeichen weg, Trennstriche am Wortende weg.
    /// </summary>
    /// <remarks>
    /// Der Statusreport der Heidelberger trennt am Zeilenende nicht mit einem Bindestrich,
    /// sondern mit dem Nicht-Zeichen <c>U+00AC</c> — siebzehnmal im Dokument. Bliebe es stehen,
    /// hieße das Wort „Ge¬“ und keine Suche fände es. Nur am Wortende entfernt: mitten im Text
    /// wäre ein <c>¬</c> ein Zeichen und keine Trennung.
    /// </remarks>
    public static string Clean(string text)
        => text.Replace(' ', ' ')
            .Replace("­", string.Empty)
            .TrimEnd('¬')
            .Trim();
}
