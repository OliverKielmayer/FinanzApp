using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Die Texterkennung für abfotografierte und eingescannte Belege.
/// </summary>
/// <remarks>
/// <para><see cref="Command"/> leer heißt: keine Erkennung. Dann wird ein Bild abgelegt und
/// nicht gelesen — derselbe Zustand wie vor dieser Einrichtung, und der Beleg sagt ihn an.
/// Dieselbe Regel wie beim Mailversand und beim Kursabruf: was nicht eingerichtet ist, läuft
/// nicht und behauptet auch nicht, es liefe.</para>
/// <para>Ein eigenes Programm und keine Bibliothek, weil die Erkennung von Windows in WinRT
/// liegt. Die API bliebe sonst nicht auf <c>net10.0</c>.</para>
/// </remarks>
public sealed class OcrOptions
{
    public const string SectionName = "Texterkennung";

    /// <summary>Das Erkennungsprogramm. Leer heißt: keine Texterkennung.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Die Sprache des Papiers, nicht die des Rechners.</summary>
    public string Language { get; set; } = "de";

    /// <summary>
    /// Wie lange auf eine Antwort gewartet wird.
    /// </summary>
    /// <remarks>
    /// Eine Erkennung über eine Seite dauert Sekunden. Was danach noch läuft, hängt — und ein
    /// hängender Aufruf hielte die ganze Analyse an, nicht nur sich selbst.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Ein Wort, wie die Erkennung es liefert.</summary>
public sealed record OcrWord(
    [property: JsonPropertyName("zeile")] int Line,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("links")] double Left,
    [property: JsonPropertyName("rechts")] double Right,
    [property: JsonPropertyName("oben")] double Top,
    [property: JsonPropertyName("hoehe")] double Height);

/// <summary>Die Antwort des Erkennungsprogramms.</summary>
public sealed record OcrResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("grund")] string? Reason,
    [property: JsonPropertyName("breite")] int Width,
    [property: JsonPropertyName("hoehe")] int Height,
    [property: JsonPropertyName("sprache")] string? Language,
    [property: JsonPropertyName("woerter")] IReadOnlyList<OcrWord>? Words);

/// <summary>
/// Macht aus den Wörtern einer Erkennung dieselben Zeilen, die ein PDF liefert.
/// </summary>
/// <remarks>
/// Der ganze Sinn: ab hier unterscheidet nichts mehr ein Foto von einer Textebene. Dieselben
/// Leseregeln greifen, dieselben Rechenproben prüfen nach — und eine verlesene Zahl bringt die
/// Fondstabelle nicht mehr auf den ausgewiesenen Gesamtwert, statt still im Vertrag zu landen.
/// </remarks>
public static class OcrLayout
{
    /// <summary>
    /// Zeichen, die für sich allein kein Wort sind.
    /// </summary>
    /// <remarks>
    /// Die Erkennung zerlegt <c>31.12.2025</c> in <c>31</c>, <c>.</c> und <c>12.2025</c>. Blieben
    /// das drei Wörter, stünde in der Zelle „31 . 12.2025“, und kein Datumsleser fände darin
    /// einen Stichtag. Am Abstand ist es nicht zu erkennen: der Punkt stand genauso weit vom
    /// Nachbarn entfernt wie zwei echte Wörter voneinander.
    /// </remarks>
    private const string Glue = ".,:;/-";

    /// <summary>
    /// Klebt Satzzeichen an ihre Nachbarn und gibt Wörter in Leserichtung zurück.
    /// </summary>
    /// <remarks>
    /// Geklebt wird nur innerhalb einer erkannten Zeile: über Zeilengrenzen hinweg steht ein
    /// Punkt am Ende des einen und eine Zahl am Anfang des nächsten Satzes, und die gehören
    /// nicht zusammen.
    /// </remarks>
    public static IReadOnlyList<TextWord> Words(IReadOnlyList<OcrWord> recognised)
    {
        var ergebnis = new List<TextWord>();

        foreach (var zeile in recognised.GroupBy(w => w.Line))
        {
            var sortiert = zeile.OrderBy(w => w.Left).ToList();
            var laufend = sortiert[0];

            foreach (var wort in sortiert.Skip(1))
            {
                if (Sticks(laufend, wort))
                {
                    laufend = laufend with
                    {
                        Text = laufend.Text + wort.Text,
                        Right = Math.Max(laufend.Right, wort.Right),
                        Top = Math.Min(laufend.Top, wort.Top),
                        Height = Math.Max(laufend.Height, wort.Height),
                    };

                    continue;
                }

                ergebnis.Add(Convert(laufend));
                laufend = wort;
            }

            ergebnis.Add(Convert(laufend));
        }

        return ergebnis;
    }

    /// <summary>
    /// Ob zwei erkannte Teile ein Wort sind.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Fälle, und beide hängen an den Zeichen und nicht am Abstand — der ist bei
    /// einem Trennzeichen genauso groß wie zwischen zwei Wörtern:</para>
    /// <para><b>Ein Trennzeichen für sich</b> gehört an seinen linken Nachbarn. Aus „Summe“ und
    /// „.“ wird „Summe.“.</para>
    /// <para><b>Eine Ziffer nach einem Trennzeichen</b> setzt fort, was davor stand: aus „31.“
    /// und „12.2025“ wird ein Stichtag. Ein <em>Wort</em> nach einem Punkt tut das nicht — dort
    /// endet ein Satz, und „Betrag.Der“ wäre falsch zusammengezogen.</para>
    /// <para>Dazu muss es sich berühren. Sonst klebte eine Seitenzahl am rechten Blattrand an
    /// dem „Seite.“ ganz links.</para>
    /// </remarks>
    private static bool Sticks(OcrWord links, OcrWord rechts)
    {
        if (!Beruehrt(links, rechts))
        {
            return false;
        }

        return IsGlue(rechts.Text)
               || (links.Text.Length > 0
                   && Glue.Contains(links.Text[^1])
                   && rechts.Text.Length > 0
                   && char.IsDigit(rechts.Text[0]));
    }

    /// <summary>Weniger als eine Zeichenbreite Abstand — geschätzt am linken Teil.</summary>
    private static bool Beruehrt(OcrWord links, OcrWord rechts)
    {
        var zeichen = links.Text.Length == 0
            ? links.Height * 0.5
            : (links.Right - links.Left) / links.Text.Length;

        return rechts.Left - links.Right <= Math.Max(2.0, zeichen);
    }

    private static bool IsGlue(string text)
        => text.Length > 0 && text.All(c => Glue.Contains(c));

    /// <summary>
    /// Im Bild wächst Y nach unten — das ist schon die Leserichtung.
    /// </summary>
    /// <remarks>
    /// Anders als im PDF, wo oben die größere Zahl ist. <see cref="TextWord.Middle"/> verlangt
    /// „kleiner heißt weiter oben“, und genau so liegt es hier.
    /// </remarks>
    private static TextWord Convert(OcrWord wort)
        => new(wort.Text, wort.Left, wort.Right, wort.Top + (wort.Height / 2.0), wort.Height);
}

/// <summary>
/// Erkennt Bilder an ihren ersten Bytes.
/// </summary>
/// <remarks>
/// Die Endung entscheidet nicht — dieselbe Regel wie beim Kontoauszug. Eine mit <c>.pdf</c>
/// benannte Handyaufnahme liefe sonst in den PDF-Leser und käme als „ließ sich nicht als PDF
/// lesen“ zurück, obwohl sie bestens lesbar ist.
/// </remarks>
public static class ImageFormats
{
    public static bool LooksLikeImage(ReadOnlySpan<byte> start)
        => IsJpeg(start) || IsPng(start) || IsGif(start) || IsBmp(start)
           || IsTiff(start) || IsWebp(start) || IsHeif(start);

    private static bool IsJpeg(ReadOnlySpan<byte> b)
        => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool IsPng(ReadOnlySpan<byte> b)
        => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    private static bool IsGif(ReadOnlySpan<byte> b)
        => b.Length >= 3 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F';

    private static bool IsBmp(ReadOnlySpan<byte> b)
        => b.Length >= 2 && b[0] == (byte)'B' && b[1] == (byte)'M';

    private static bool IsTiff(ReadOnlySpan<byte> b)
        => b.Length >= 4
           && ((b[0] == 0x49 && b[1] == 0x49 && b[2] == 0x2A && b[3] == 0x00)
               || (b[0] == 0x4D && b[1] == 0x4D && b[2] == 0x00 && b[3] == 0x2A));

    /// <summary>RIFF-Behälter mit der Kennung WEBP an Stelle 8.</summary>
    private static bool IsWebp(ReadOnlySpan<byte> b)
        => b.Length >= 12
           && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
           && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';

    /// <summary>Ein ISO-Behälter, dessen Marke an Stelle 4 <c>ftyp</c> heißt — HEIC und AVIF.</summary>
    private static bool IsHeif(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12
            || b[4] != (byte)'f' || b[5] != (byte)'t' || b[6] != (byte)'y' || b[7] != (byte)'p')
        {
            return false;
        }

        var marke = System.Text.Encoding.ASCII.GetString(b[8..12]);
        return marke is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1" or "avif";
    }
}

/// <summary>
/// Liest ein Bild, indem sie das Erkennungsprogramm darüberlaufen lässt.
/// </summary>
public sealed class ImageTextReader(OcrOptions options, ILogger<ImageTextReader> log)
{
    /// <summary>Ob überhaupt eine Erkennung eingerichtet ist.</summary>
    public bool Configured => options.Command is { Length: > 0 };

    public PdfContent Read(Stream content)
    {
        if (!Configured)
        {
            return Empty(TextSource.Image);
        }

        // Das Programm liest eine Datei und keinen Strom: ein Bilddekoder springt im Bild
        // umher, und eine Weiterleitung über die Standardeingabe könnte er nicht zurückspulen.
        var temp = Path.Combine(Path.GetTempPath(), "finanzapp-ocr-" + Guid.NewGuid().ToString("N"));

        try
        {
            content.Position = 0;
            using (var datei = File.Create(temp))
            {
                content.CopyTo(datei);
            }

            var antwort = Run(temp);

            if (antwort is null || !antwort.Ok)
            {
                log.LogWarning("Texterkennung ohne Ergebnis: {Grund}", antwort?.Reason ?? "keine Antwort");
                return Empty(TextSource.Recognition);
            }

            var woerter = OcrLayout.Words(antwort.Words ?? []);

            return new PdfContent
            {
                // Ein Bild ist eine Seite. Mehrseitige Aufnahmen gibt es nicht — wer zwei Seiten
                // hat, fotografiert zweimal.
                PageCount = 1,
                Lines = [.. TextLayout.Lines(1, woerter)],
                TextIsInvisible = false,
                ImageCount = 1,
                Source = TextSource.Recognition,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Texterkennung: das Bild ließ sich nicht zwischenlegen.");
            return Empty(TextSource.Recognition);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Eine Datei im Temp-Ordner, die stehen bleibt, ist kein Grund, die Analyse
                // scheitern zu lassen. Das Betriebssystem räumt sie ohnehin weg.
            }
        }
    }

    private OcrResponse? Run(string bild)
    {
        var start = new ProcessStartInfo(options.Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add(bild);
        start.ArgumentList.Add(options.Language);

        try
        {
            using var lauf = Process.Start(start);

            if (lauf is null)
            {
                return null;
            }

            var ausgabe = lauf.StandardOutput.ReadToEnd();

            if (!lauf.WaitForExit(TimeSpan.FromSeconds(options.TimeoutSeconds)))
            {
                lauf.Kill(entireProcessTree: true);
                log.LogWarning(
                    "Texterkennung nach {Sekunden} s abgebrochen.", options.TimeoutSeconds);

                return null;
            }

            return ausgabe.Length == 0
                ? null
                : JsonSerializer.Deserialize<OcrResponse>(ausgabe);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or JsonException
                                      or InvalidOperationException)
        {
            // Ein fehlendes oder kaputtes Hilfsprogramm darf die Ablage nicht verhindern: der
            // Beleg wird abgelegt und als ungelesen gemeldet, so wie ohne Erkennung.
            log.LogWarning(
                ex, "Texterkennung „{Befehl}“ ließ sich nicht ausführen.", options.Command);

            return null;
        }
    }

    private static PdfContent Empty(TextSource source) => new()
    {
        PageCount = 1,
        Lines = [],
        TextIsInvisible = false,
        ImageCount = 1,
        Source = source,
    };
}

/// <summary>
/// Schickt jede Datei zu dem Leser, der sie lesen kann.
/// </summary>
/// <remarks>
/// Die Entscheidung fällt an den ersten Bytes und nicht am Namen. Ein Foto, das jemand
/// <c>Beleg.pdf</c> genannt hat, soll gelesen und nicht beklagt werden.
/// </remarks>
public sealed class ScanTextReader(
    PdfPigTextReader pdf, ImageTextReader images, ILogger<ScanTextReader> log) : IPdfTextReader
{
    public PdfContent Read(Stream content)
    {
        using var puffer = new MemoryStream();
        content.CopyTo(puffer);

        puffer.Position = 0;
        var kopf = new byte[16];
        var gelesen = puffer.Read(kopf, 0, kopf.Length);
        puffer.Position = 0;

        if (ImageFormats.LooksLikeImage(kopf.AsSpan(0, gelesen)))
        {
            return images.Read(puffer);
        }

        var inhalt = pdf.Read(puffer);

        // Ein PDF mit Text ist fertig gelesen. Eines ohne kann trotzdem beschrieben sein — als
        // Bild. Ein Flachbettscanner liefert genau das: ein Foto der Seite in einem
        // PDF-Umschlag, null Schriftobjekte. Bis hierher fiel so ein Zettel durch jede Regel,
        // bekam keinen Vorschlag und lag stumm unter „Sonstiges“.
        if (inhalt.HasTextLayer || inhalt.ImageCount == 0 || !images.Configured)
        {
            return inhalt;
        }

        puffer.Position = 0;
        return Seitenbilder(puffer, inhalt);
    }

    /// <summary>
    /// Liest die Seitenbilder eines textlosen PDF durch die Erkennung.
    /// </summary>
    /// <remarks>
    /// <para><b>Je Seite nur das größte Bild.</b> Ein eingescanntes Blatt ist ein Bild; was sonst
    /// noch darauf liegt, sind Logos und Linien. Alle Bilder einer Seite nacheinander zu lesen
    /// hieße, ihre Zeilen nach der Höhe im jeweils eigenen Bild zu mischen — und aus zwei
    /// Bildkoordinatensystemen wird keine Seite.</para>
    /// <para>Scheitert die Erkennung oder liefert sie nichts, bleibt es beim Ergebnis des
    /// PDF-Lesers. Ein leeres Ergebnis ist eine Auskunft; ein erfundenes wäre keine.</para>
    /// </remarks>
    private PdfContent Seitenbilder(MemoryStream puffer, PdfContent inhalt)
    {
        var zeilen = new List<PdfLine>();

        try
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(puffer.ToArray());

            var seite = 0;
            foreach (var blatt in pdf.GetPages())
            {
                seite++;

                if (Groesstes(blatt) is not { } bytes)
                {
                    continue;
                }

                using var strom = new MemoryStream(bytes);
                var gelesen = images.Read(strom);

                // Die Erkennung zählt ihre eigenen Seiten ab eins. Hier zählt das Blatt.
                zeilen.AddRange(gelesen.Lines.Select(z => z with { Page = seite }));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogWarning(ex, "Seitenbilder ließen sich nicht lesen.");
            return inhalt;
        }

        if (zeilen.Count == 0)
        {
            return inhalt;
        }

        return inhalt with { Lines = zeilen, Source = TextSource.Recognition };
    }

    /// <summary>
    /// Das größte Bild einer Seite, als Bytes — oder nichts.
    /// </summary>
    /// <remarks>
    /// PNG zuerst: Es entsteht aus jedem Kodierverfahren, das PdfPig versteht. Gelingt das
    /// nicht, sind die Rohbytes einen Versuch wert — ein JPEG liegt im PDF als JPEG, und dann
    /// sind die Rohbytes bereits die Datei.
    /// </remarks>
    private static byte[]? Groesstes(UglyToad.PdfPig.Content.Page blatt)
    {
        var bild = blatt.GetImages()
            .OrderByDescending(b => (long)b.WidthInSamples * b.HeightInSamples)
            .FirstOrDefault();

        if (bild is null)
        {
            return null;
        }

        if (bild.TryGetPng(out var png) && png is { Length: > 0 })
        {
            return png;
        }

        var roh = bild.RawBytes.ToArray();
        return ImageFormats.LooksLikeImage(roh) ? roh : null;
    }
}
