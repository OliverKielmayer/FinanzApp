using System.Globalization;
using System.Text;
using System.Text.Json;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

// Texterkennung für ein Bild — ein Aufruf, ein Bild, eine Antwort auf der Standardausgabe.
//
// Aufruf:  FinanzApp.Texterkennung <pfad> [sprache]
// Antwort: { "ok": true, "breite": .., "hoehe": .., "woerter": [ { "text", "links", "rechts",
//            "oben", "hoehe" } ] }   oder   { "ok": false, "grund": "..." }
//
// Geliefert werden Wörter mit Kästchen, nicht fertige Zeilen: welche Wörter eine Zeile bilden
// und wo eine Spalte endet, entscheidet die API — für die Textebene eines PDF tut sie es schon,
// und zwei Fassungen derselben Regel liefen auseinander.

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    Fertig(new { ok = false, grund = "Kein Bild angegeben." });
    return 2;
}

var pfad = args[0];
var sprache = args.Length > 1 ? args[1] : "de";

if (!File.Exists(pfad))
{
    Fertig(new { ok = false, grund = $"Die Datei gibt es nicht: {pfad}" });
    return 2;
}

try
{
    // Der Erkenner spricht die Sprache des Belegs, nicht die des Rechners. Eine englische
    // Erkennung liest „Ablaufleistung“ als Buchstabensalat, weil ihr Wörterbuch nichts
    // Ähnliches kennt — und sie meldet dabei keinen Fehler.
    var erkenner = OcrEngine.TryCreateFromLanguage(new Language(sprache))
                   ?? OcrEngine.TryCreateFromUserProfileLanguages();

    if (erkenner is null)
    {
        Fertig(new
        {
            ok = false,
            grund = $"Windows hat keine Texterkennung für „{sprache}“. Sie kommt mit dem "
                    + "Sprachpaket: Einstellungen › Zeit und Sprache › Sprache, dort die Sprache "
                    + "hinzufügen und bei den Sprachoptionen „Texterkennung“ mitinstallieren.",
        });

        return 3;
    }

    var datei = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pfad));
    using IRandomAccessStream strom = await datei.OpenAsync(FileAccessMode.Read);

    var dekoder = await BitmapDecoder.CreateAsync(strom);

    // Ausdrücklich BGRA8: was der Dekoder nativ liefert, nimmt die Erkennung nicht in jedem
    // Format an, und die Meldung dazu nennt das Format nicht.
    using var bild = await dekoder.GetSoftwareBitmapAsync(
        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

    if (bild.PixelWidth > OcrEngine.MaxImageDimension || bild.PixelHeight > OcrEngine.MaxImageDimension)
    {
        Fertig(new
        {
            ok = false,
            grund = $"Das Bild ist mit {bild.PixelWidth}×{bild.PixelHeight} größer, als die "
                    + $"Erkennung verarbeitet ({OcrEngine.MaxImageDimension} Bildpunkte je Kante).",
        });

        return 4;
    }

    var ergebnis = await erkenner.RecognizeAsync(bild);

    // Die Zeilennummer der Erkennung wird mitgegeben, aber nicht als Zeile benutzt: welche
    // Wörter eine Zeile bilden, entscheidet die API nach denselben Regeln wie beim PDF. Gebraucht
    // wird sie für das Zusammenkleben — „31“, „.“, „12.2025“ gehören zu einem Wort, und dass sie
    // nebeneinanderstehen, weiß man nur innerhalb einer Zeile.
    var woerter = ergebnis.Lines
        .SelectMany((zeile, nummer) => zeile.Words.Select(wort => new
        {
            zeile = nummer,
            text = wort.Text,
            links = Zahl(wort.BoundingRect.Left),
            rechts = Zahl(wort.BoundingRect.Right),
            oben = Zahl(wort.BoundingRect.Top),
            hoehe = Zahl(wort.BoundingRect.Height),
        }))
        .ToList();

    Fertig(new
    {
        ok = true,
        breite = bild.PixelWidth,
        hoehe = bild.PixelHeight,
        sprache = erkenner.RecognizerLanguage.LanguageTag,
        woerter,
    });

    return 0;
}
catch (Exception ex)
{
    // Auch der Fehlschlag kommt als Antwort und nicht als Absturz: der Aufrufer liest die
    // Standardausgabe, und ein leerer Strom mit Rückgabewert 1 sagte ihm nur, dass etwas war.
    Fertig(new { ok = false, grund = Beschreiben(ex) });
    return 1;
}

static double Zahl(double wert) => Math.Round(wert, 2);

static void Fertig(object antwort)
    => Console.Out.Write(JsonSerializer.Serialize(antwort));

static string Beschreiben(Exception ex) => ex switch
{
    // 0x88982F50 — der Bilddekoder kennt das Format nicht. Bei .heic heißt das fast immer:
    // die HEIF-Erweiterung aus dem Microsoft Store fehlt.
    { HResult: unchecked((int)0x88982F50) } =>
        "Das Bildformat ließ sich nicht öffnen. Bei HEIC-Dateien vom iPhone fehlt dafür meist "
        + "die HEIF-Bilderweiterung aus dem Microsoft Store.",

    UnauthorizedAccessException =>
        "Kein Zugriff auf die Datei. Läuft der Dienst unter einem eigenen Konto, braucht es "
        + "Leserechte auf den Ablageordner.",

    _ => "Die Texterkennung ist fehlgeschlagen: "
         + ex.Message.Split('\n')[0].Trim().ToString(CultureInfo.InvariantCulture),
};
