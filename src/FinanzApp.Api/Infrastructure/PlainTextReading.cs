using System.Text;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Der dritte Weg: eine Datei, in der der Text einfach dasteht.
/// </summary>
/// <remarks>
/// <para>Bild oder PDF — dazwischen fiel alles durch. Eine Vermögensübersicht als CSV liegt
/// offen lesbar da, und trotzdem stand am Eintrag kein Wort: Ihre Zahlen holte der
/// Tabellenleser, ihr Text kam nie an, und gesucht werden konnte in ihr nicht.</para>
/// <para><b>Entschieden wird an den Bytes und nicht am Namen</b> — dieselbe Regel wie beim
/// Bild. Eine Tabelle, die jemand <c>Beleg.pdf</c> genannt hat, soll gelesen und nicht
/// beklagt werden.</para>
/// </remarks>
public static class PlainTextReading
{
    /// <summary>So viel vom Anfang entscheidet über Beschaffenheit und Kodierung.</summary>
    private const int Probe = 64 * 1024;

    /// <summary>
    /// Was Windows-1252 oberhalb von Latin-1 noch kennt, <c>0x80</c> bis <c>0x9F</c>.
    /// </summary>
    /// <remarks>
    /// Die Stelle, an der es praktisch hängt, ist die erste: <c>0x80</c> ist das Eurozeichen.
    /// Latin-1 macht daraus ein Steuerzeichen, und aus „Vermögen in €“ würde „Vermögen in
    /// <c>U+0080</c>“ — genau ein solches Byte steht in der echten Vermögensübersicht. Die fünf
    /// Stellen, die Windows-1252 nicht belegt, bleiben, was sie sind.
    /// </remarks>
    private const string Windows1252 =
        "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021"
        + "\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F"
        + "\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014"
        + "\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    /// <summary>
    /// Liest die Datei als Text, oder <c>null</c>, wenn sie keiner ist.
    /// </summary>
    /// <remarks>
    /// Eine Zeile der Datei ist eine Zeile des Belegs, und sie bleibt <em>eine</em> Zelle. Die
    /// Spalten eines PDF setzt der Leser erst aus Koordinaten zusammen; hier stünde die Trennung
    /// schon da, aber welches Zeichen sie macht, weiß nur die Datei. Lieber die Zeile, wie sie
    /// geschrieben ist, als eine geratene Zerlegung.
    /// </remarks>
    public static PdfContent? Read(Stream content)
    {
        using var puffer = new MemoryStream();
        content.CopyTo(puffer);
        var bytes = puffer.ToArray();

        var anfang = bytes.AsSpan(0, Math.Min(Probe, bytes.Length));

        if (!LooksLikeText(anfang))
        {
            return null;
        }

        var text = Decode(bytes, IsUtf8(anfang, complete: bytes.Length <= Probe));

        var zeilen = text
            .Split('\n')
            .Select(z => TextLayout.Clean(OhneSteuerzeichen(z)))
            .Where(z => z.Length > 0)
            .Select(z => new PdfLine(1, [z]))
            .ToList();

        return new PdfContent
        {
            PageCount = 1,
            Lines = zeilen,
            TextIsInvisible = false,
            ImageCount = 0,
            Source = TextSource.Text,
        };
    }

    /// <summary>
    /// Ob das überhaupt Text ist — und nicht etwas Binäres ohne Kennung.
    /// </summary>
    /// <remarks>
    /// <para>Ein Nullbyte entscheidet allein: Keine Kodierung für Fließtext setzt es, jedes
    /// Binärformat tut es. Das Nullzeichen, das <see cref="TextLayout.Clean"/> zu einem
    /// Leerzeichen macht, ist etwas anderes — es entsteht beim Lesen einer Schrift und steht
    /// nicht in der Datei.</para>
    /// <para>Daneben ein Maß statt einer Schranke: Ein einzelnes Steuerzeichen macht aus einer
    /// Tabelle keine Binärdatei — alte Werkzeuge hängen etwa <c>0x1A</c> ans Ende. Ab einem
    /// Prozent ist es keine Eigenart mehr, sondern die Beschaffenheit.</para>
    /// </remarks>
    public static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return false;
        }

        var stoerend = 0;

        foreach (var b in bytes)
        {
            if (b == 0)
            {
                return false;
            }

            if (b >= 0x20 || b is 0x09 or 0x0A or 0x0D or 0x0C)
            {
                continue;
            }

            stoerend++;
        }

        return stoerend <= Math.Max(1, bytes.Length / 100);
    }

    /// <summary>
    /// Ob die Bytes als UTF-8 aufgehen.
    /// </summary>
    /// <remarks>
    /// Tun sie es nicht, ist es im deutschen Umfeld praktisch immer Windows-1252. Am Schnitt
    /// eines Stücks kann eine Mehrbyte-Folge halbiert sein; nur am echten Dateiende ist eine
    /// unvollständige Folge wirklich ein Fehler. Sonst entschiede die zufällige Stelle des
    /// Schnitts über den Zeichensatz.
    /// </remarks>
    public static bool IsUtf8(ReadOnlySpan<byte> bytes, bool complete)
    {
        try
        {
            var pruefer = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

            Span<char> zeichen = new char[bytes.Length + 1];
            pruefer.GetDecoder().GetChars(bytes, zeichen, flush: complete);

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Steuerzeichen sind keine Schrift.
    /// </summary>
    /// <remarks>
    /// Was <see cref="LooksLikeText"/> als Eigenart durchgehen lässt, darf deshalb trotzdem
    /// nicht im Text stehen: Ein <c>0x1A</c>, das ein altes Werkzeug ans Dateiende gehängt hat,
    /// ist kein Inhalt, und als letztes Zeichen einer Zelle fände keine Suche das Wort davor.
    /// Der Tabulator bleibt — er trennt Spalten und ist in einer Tabelle das Gegenteil von
    /// Zufall.
    /// </remarks>
    private static string OhneSteuerzeichen(string zeile)
        => zeile.Any(c => char.IsControl(c) && c != '\t')
            ? new string([.. zeile.Where(c => !char.IsControl(c) || c == '\t')])
            : zeile;

    /// <summary>Die Zeichen der Datei, in der Kodierung, in der sie geschrieben ist.</summary>
    private static string Decode(byte[] bytes, bool utf8)
    {
        if (utf8)
        {
            // Die Bytemarke am Anfang ist eine Angabe über die Datei und kein Zeichen darin.
            return new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
        }

        var zeichen = Encoding.Latin1.GetString(bytes).ToCharArray();

        for (var i = 0; i < zeichen.Length; i++)
        {
            if (zeichen[i] is >= '\u0080' and <= '\u009F')
            {
                zeichen[i] = Windows1252[zeichen[i] - '\u0080'];
            }
        }

        return new string(zeichen);
    }
}
