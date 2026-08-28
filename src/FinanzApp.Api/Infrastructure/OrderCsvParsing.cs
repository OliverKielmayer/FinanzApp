using System.Globalization;
using System.Text;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Eine gelesene Zeile der Orderdatei — ausgeführt oder mit Grund verworfen.</summary>
/// <param name="Reference">Woran der Satz beim nächsten Import wiedererkannt wird.</param>
/// <param name="Problem">
/// Gesetzt, wenn aus der Zeile keine Ausführung wird. Sie verschwindet dann nicht, sondern
/// steht mit diesem Grund im Ergebnis — stillschweigend übersprungen wird nichts.
/// </param>
public sealed record ParsedTrade(
    string Reference,
    string SecurityName,
    string Isin,
    string? Wkn,
    bool IsSell,
    bool IsLimit,
    decimal? LimitPrice,
    DateTime ExecutedAt,
    decimal Quantity,
    decimal Price,
    decimal Value,
    decimal Fee,
    string? Problem = null);

/// <summary>
/// Liest die Orderdatei von finanzen.net ZERO / Baader Bank — v5-Handoff, Abschnitt 11.1.
/// </summary>
/// <remarks>
/// <para>Semikolongetrennt, deutsche Zahlen, UTF-8 mit Signatur. Gesucht wird über die
/// Spaltenüberschriften und nicht über feste Positionen: eine zusätzliche Spalte in der
/// nächsten Fassung der Datei verschöbe sonst alles dahinter.</para>
/// <para>Die drei Regeln des Handoffs stecken hier: nur ausgeführte Sätze zählen und nur die
/// ausgeführte Menge, der Mindermengenzuschlag ist eine Gebühr und liegt auf dem Wert, und die
/// Wiedererkennung läuft über Ausführungszeitpunkt, Stück und Kurs — die Datei führt keine
/// Ordernummer.</para>
/// </remarks>
public sealed class OrderCsvParser
{
    /// <summary>Eine Orderdatei ist eine Textdatei; alles jenseits davon ist keine.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static bool CanRead(string fileName)
        => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ParsedTrade>> ParseAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var kopfzeile = await reader.ReadLineAsync(ct)
                        ?? throw new StatementFormatException($"„{fileName}“ ist leer.");

        var spalten = Columns(kopfzeile, fileName);
        var saetze = new List<ParsedTrade>();

        while (await reader.ReadLineAsync(ct) is { } zeile)
        {
            if (zeile.Trim().Length == 0)
            {
                continue;
            }

            saetze.Add(Read(zeile.Split(';'), spalten));
        }

        return saetze.Count == 0
            ? throw new StatementFormatException($"In „{fileName}“ steht keine einzige Order.")
            : saetze;
    }

    // ── Die Spalten ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Überschriften auf Feldnamen abgebildet.
    /// </summary>
    /// <remarks>
    /// Verglichen wird ohne Rücksicht auf Groß- und Kleinschreibung und ohne Leerzeichen: die
    /// Datei schreibt „Ausführung Datum“, und ob der Broker morgen „Ausführungsdatum“ daraus
    /// macht, soll den Import nicht kippen.
    /// </remarks>
    private static Dictionary<string, int> Columns(string header, string fileName)
    {
        var spalten = header.Split(';')
            .Select((name, index) => (Key: Normalise(name), index))
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);

        foreach (var pflicht in new[] { "isin", "status", "ausfuehrungdatum", "ausfuehrungkurs" })
        {
            if (!spalten.ContainsKey(pflicht))
            {
                throw new StatementFormatException(
                    $"„{fileName}“ sieht nicht nach einer Orderdatei aus — die Spalte "
                    + $"„{pflicht}“ fehlt.");
            }
        }

        return spalten;
    }

    private static string Normalise(string name)
        => new(name.Trim().ToLowerInvariant()
            .Replace("ü", "ue").Replace("ö", "oe").Replace("ä", "ae").Replace("ß", "ss")
            .Where(char.IsLetterOrDigit).ToArray());

    // ── Eine Zeile ─────────────────────────────────────────────────────────────────────────

    private static ParsedTrade Read(string[] felder, Dictionary<string, int> spalten)
    {
        string? Text(string key)
            => spalten.TryGetValue(key, out var i) && i < felder.Length && felder[i].Trim().Length > 0
                ? felder[i].Trim()
                : null;

        var isin = Text("isin") ?? string.Empty;
        var name = Text("name") ?? isin;
        var status = Text("status") ?? string.Empty;

        var zeitpunkt = Moment(Text("ausfuehrungdatum"), Text("ausfuehrungzeit"));
        var kurs = Number(Text("ausfuehrungkurs")) ?? 0m;

        // Die ausgeführte Menge, nicht die bestellte. Wer „Anzahl“ nimmt, bucht bei einer
        // teilausgeführten Order Stücke ein, die nie geliefert wurden.
        var stueck = Number(Text("anzahlausgefuehrt")) ?? 0m;

        var wert = Math.Abs(Number(Text("wert")) ?? 0m);
        var gebuehr = Math.Abs(Number(Text("mindermengenzuschlag")) ?? 0m);
        var verkauf = string.Equals(Text("richtung"), "Verkauf", StringComparison.OrdinalIgnoreCase);
        var limit = string.Equals(Text("orderart"), "Limit", StringComparison.OrdinalIgnoreCase);

        var problem = Problem(status, zeitpunkt, stueck, kurs);

        return new ParsedTrade(
            Reference: zeitpunkt is null
                ? string.Empty
                : $"ZERO:{isin}:{zeitpunkt:yyyy-MM-ddTHH:mm:ss}:{stueck:0.######}:{kurs:0.######}",
            SecurityName: name,
            Isin: isin,
            Wkn: Text("wkn"),
            IsSell: verkauf,
            IsLimit: limit,
            LimitPrice: limit ? Number(Text("limit")) : null,
            ExecutedAt: zeitpunkt ?? default,
            Quantity: stueck,
            Price: kurs,
            Value: wert,
            Fee: gebuehr,
            Problem: problem);
    }

    /// <summary>
    /// Warum aus dieser Zeile keine Ausführung wird — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Stornierte und offene Orders gehören in keine Summe. Sie hier wegzuwerfen wäre bequemer,
    /// aber dann fehlten sie im Ergebnis, und niemand wüsste, warum aus 26 Zeilen 24 Sätze
    /// wurden.
    /// </remarks>
    private static string? Problem(string status, DateTime? zeitpunkt, decimal stueck, decimal kurs)
    {
        if (!status.Equals("ausgeführt", StringComparison.OrdinalIgnoreCase))
        {
            return status.Length == 0 ? "ohne Status" : $"Status „{status}“ — nicht ausgeführt";
        }

        if (zeitpunkt is null)
        {
            return "ohne Ausführungszeitpunkt";
        }

        return stueck <= 0m
            ? "keine ausgeführte Stückzahl"
            : kurs <= 0m ? "ohne Ausführungskurs" : null;
    }

    private static DateTime? Moment(string? datum, string? zeit)
    {
        if (!DateTime.TryParseExact(datum, "dd.MM.yyyy", German, DateTimeStyles.None, out var tag))
        {
            return null;
        }

        return TimeSpan.TryParseExact(zeit, @"hh\:mm\:ss", German, out var uhrzeit)
            ? tag.Add(uhrzeit)
            : tag;
    }

    private static decimal? Number(string? text)
        => text is null
            ? null
            : decimal.TryParse(text, NumberStyles.Number, German, out var wert) ? wert : null;
}
