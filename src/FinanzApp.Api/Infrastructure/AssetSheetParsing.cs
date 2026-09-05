using System.Globalization;
using System.Text;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Eine Zeile der Vermögensübersicht, so wie sie in der Datei steht.
/// </summary>
/// <param name="Line">Zeilennummer in der Datei, Kopfzeile als 1 mitgezählt.</param>
/// <param name="Problem">
/// Gesetzt, wenn aus der Zeile kein Stand wird. Sie verschwindet dann nicht, sondern steht mit
/// diesem Grund im Ergebnis — stillschweigend übersprungen wird nichts.
/// </param>
public sealed record AssetSheetRow(
    int Line,
    string Category,
    string ContractKind,
    string ContractNumber,
    string Company,
    decimal? Value,
    decimal? MonthlyRate,
    DateOnly? AsOf,
    string? Note,
    string? Problem);

/// <summary>
/// Liest die Vermögensübersicht — die Datei, die monatlich von MLP kommt.
/// </summary>
/// <remarks>
/// <para>Semikolongetrennt, alle Felder in Anführungszeichen, deutsche Zahlen und Datumsangaben,
/// Windows-1252. Gesucht wird über die Spaltenüberschriften und nicht über feste Positionen:
/// eine zusätzliche Spalte in der nächsten Fassung der Datei verschöbe sonst alles dahinter.</para>
/// <para>Dieselbe Datei trägt Konten, Depots und Versicherungen. Der Leser sortiert nichts aus —
/// er gibt jede Zeile zurück, auch die ohne Wert und ohne Stichtag. <em>Was</em> davon ein
/// Vertragsstand wird, entscheidet der Bestand und nicht das Dateiformat, und das weiß hier
/// niemand.</para>
/// </remarks>
public sealed class AssetSheetParser
{
    /// <summary>Eine Vermögensübersicht ist eine Textdatei; alles jenseits davon ist keine.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>Die Kategorie, unter der die Datei Vorsorgeverträge führt.</summary>
    public const string PolicyCategory = "Lebens- und Rentenversicherungen";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Die Spalten, ohne die die Datei keine Vermögensübersicht ist.</summary>
    private static readonly string[] Required = ["kategorie", "vertragsnummer", "stand"];

    public static bool CanRead(string fileName)
        => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ob der Anfang der Datei nach einer Vermögensübersicht aussieht.
    /// </summary>
    /// <remarks>
    /// Für die Einlieferung aus dem überwachten Ordner: dort kommt jede Datei an, und eine
    /// Orderdatei des Brokers ist ebenfalls eine semikolongetrennte CSV. Geprüft wird die
    /// Kopfzeile und nicht der Dateiname — der sagt über den Inhalt nichts.
    /// </remarks>
    public static bool LooksLikeSheet(Stream content)
    {
        var stand = content.Position;

        try
        {
            using var reader = Reader(content);
            var kopf = reader.ReadLine();

            return kopf is not null && Required.All(Columns(kopf).ContainsKey);
        }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            content.Position = stand;
        }
    }

    public async Task<IReadOnlyList<AssetSheetRow>> ParseAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        using var reader = Reader(content);

        var kopfzeile = await reader.ReadLineAsync(ct)
                        ?? throw new StatementFormatException($"„{fileName}“ ist leer.");

        var spalten = Columns(kopfzeile);

        foreach (var pflicht in Required)
        {
            if (!spalten.ContainsKey(pflicht))
            {
                throw new StatementFormatException(
                    $"„{fileName}“ sieht nicht nach einer Vermögensübersicht aus — die Spalte "
                    + $"„{pflicht}“ fehlt.");
            }
        }

        var zeilen = new List<AssetSheetRow>();
        var nummer = 1;

        while (await reader.ReadLineAsync(ct) is { } zeile)
        {
            nummer++;

            if (zeile.Trim().Length == 0)
            {
                continue;
            }

            zeilen.Add(Read(nummer, Split(zeile), spalten));
        }

        return zeilen.Count == 0
            ? throw new StatementFormatException($"In „{fileName}“ steht keine einzige Zeile.")
            : zeilen;
    }

    /// <summary>
    /// Ein Leser, der die Zeichensatzfrage selbst beantwortet.
    /// </summary>
    /// <remarks>
    /// Die Datei von MLP ist Windows-1252, die umgewandelte Historie ist UTF-8 mit Signatur.
    /// Beide sollen durch dieselbe Strecke gehen. Mit Signatur gewinnt sie; ohne bleibt es bei
    /// Latin-1, und das ist die richtige Vorgabe: als UTF-8 gelesen wird aus „Grüntal“ ein
    /// Wort mit Ersatzzeichen, und die Vertragsnummer daneben stimmt trotzdem — der Fehler fiele
    /// erst im Vertragsnamen auf.
    /// </remarks>
    private static StreamReader Reader(Stream content)
        => new(content, Encoding.Latin1, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

    // ── Die Spalten ────────────────────────────────────────────────────────────────────────

    /// <summary>Die Überschriften auf Feldnamen abgebildet.</summary>
    /// <remarks>
    /// Verglichen wird ohne Rücksicht auf Groß- und Kleinschreibung, Umlaute und Satzzeichen:
    /// die Datei schreibt „Rate mtl. Basis“ und „Vermögen in €“, und ob daraus morgen „Rate
    /// monatlich“ wird, soll den Import nicht kippen.
    /// </remarks>
    private static Dictionary<string, int> Columns(string header)
        => Split(header)
            .Select((name, index) => (Key: Normalise(name), index))
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.Ordinal);

    private static string Normalise(string name)
        => new(name.Trim().ToLowerInvariant()
            .Replace("ü", "ue").Replace("ö", "oe").Replace("ä", "ae").Replace("ß", "ss")
            .Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// Zerlegt eine Zeile in ihre Felder.
    /// </summary>
    /// <remarks>
    /// Anführungszeichen zählen: die Spalte „Konten &amp; Kreditkarten“ enthält kein Semikolon,
    /// aber die Bezeichnung eines Vertrags darf eines enthalten, und ein blindes
    /// <c>Split(';')</c> zerrisse dann die Zeile. Verdoppelte Anführungszeichen stehen für eines.
    /// </remarks>
    private static string[] Split(string line)
    {
        var felder = new List<string>();
        var feld = new StringBuilder();
        var inZitat = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inZitat)
            {
                if (c != '"')
                {
                    feld.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    feld.Append('"');
                    i++;
                }
                else
                {
                    inZitat = false;
                }
            }
            else if (c == '"')
            {
                inZitat = true;
            }
            else if (c == ';')
            {
                felder.Add(feld.ToString());
                feld.Clear();
            }
            else
            {
                feld.Append(c);
            }
        }

        felder.Add(feld.ToString());
        return [.. felder];
    }

    // ── Eine Zeile ─────────────────────────────────────────────────────────────────────────

    private static AssetSheetRow Read(int line, string[] felder, Dictionary<string, int> spalten)
    {
        string? Text(string key)
            => spalten.TryGetValue(key, out var i) && i < felder.Length && felder[i].Trim().Length > 0
                ? felder[i].Trim()
                : null;

        var nummer = Text("vertragsnummer");

        // „Vermögen in €“ ohne Umlaut und ohne Satzzeichen. Das Eurozeichen fällt dabei mit weg
        // — es ist weder Buchstabe noch Ziffer, und in Windows-1252 steht es an einer Stelle,
        // die in Latin-1 gar kein Zeichen ist.
        var wert = Number(Text("vermoegenin"));

        var stichtag = Date(Text("stand"));

        return new AssetSheetRow(
            Line: line,
            Category: Text("kategorie") ?? string.Empty,
            ContractKind: Text("vertragsart") ?? string.Empty,

            // „?“ steht in der Datei für „keine Nummer hinterlegt“. Als Vertragsnummer
            // durchgereicht träfe sie beim nächsten Import eine andere Zeile mit demselben
            // Platzhalter, und zwei Konten teilten sich einen Verlauf.
            ContractNumber: nummer is null or "?" ? string.Empty : nummer,

            Company: Text("gesellschaft") ?? string.Empty,
            Value: wert,
            MonthlyRate: Number(Text("ratemtlbasis")),
            AsOf: stichtag,
            Note: Text("hinweis"),
            Problem: Problem(nummer, wert, stichtag));
    }

    /// <summary>Warum aus dieser Zeile kein Stand wird — oder <c>null</c>.</summary>
    private static string? Problem(string? nummer, decimal? wert, DateOnly? stichtag)
    {
        if (nummer is null or "?")
        {
            return "ohne Vertragsnummer";
        }

        if (wert is null)
        {
            return "ohne Wert";
        }

        return stichtag is null ? "ohne Stichtag" : null;
    }

    private static DateOnly? Date(string? text)
        => DateOnly.TryParseExact(text, "dd.MM.yyyy", German, DateTimeStyles.None, out var tag)
            ? tag
            : null;

    private static decimal? Number(string? text)
        => text is null
            ? null
            : decimal.TryParse(text, NumberStyles.Number, German, out var wert) ? wert : null;
}
