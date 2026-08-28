using System.IO.Compression;
using FinanzApp.Api.Data;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Liest ein Archiv voller Auszüge und legt sie zu einem zusammen.
/// </summary>
/// <remarks>
/// <para>Die Banken stellen Tagesauszüge als ZIP bereit — eine Datei je Buchungstag. Sie einzeln
/// hochzuladen hieße, denselben Ablauf achtmal zu durchlaufen: lesen, Konto wählen, Duplikate
/// ansehen, übernehmen. Deshalb entsteht aus dem Archiv <b>eine</b> Vorschau mit allen Sätzen
/// und <b>eine</b> Übernahme.</para>
/// <para>Was keine Auszugsdatei ist, verschwindet nicht stillschweigend: es steht als Zeile mit
/// Grund in der Liste und lässt sich nicht zuschalten. Nur der Beifang der Packprogramme —
/// Ordnereinträge, <c>__MACOSX</c>, <c>.DS_Store</c> — bleibt draußen; ihn zu melden wäre
/// Lärm über etwas, das der Benutzer nie hineingelegt hat.</para>
/// </remarks>
public sealed class ZipStatementReader(IStatementParser parser)
{
    /// <summary>So viele Einträge sieht sich der Leser an.</summary>
    /// <remarks>
    /// Ein Jahr Tagesauszüge sind rund 250 Dateien. Alles weit darüber ist kein Kontoauszug
    /// mehr, sondern ein Archiv, das jemand versehentlich erwischt hat.
    /// </remarks>
    public const int MaxEntries = 400;

    /// <summary>
    /// Die Grenze gilt <b>entpackt</b>, nicht gepackt.
    /// </summary>
    /// <remarks>
    /// Ein Archiv von wenigen Kilobyte kann sich zu Gigabyte entfalten. Gezählt wird deshalb
    /// beim Lesen mit, nicht die Größenangabe im Verzeichnis des Archivs — die stammt aus der
    /// Datei selbst und kann jede Zahl behaupten.
    /// </remarks>
    public const long MaxTotalBytes = CamtStatementParser.MaxBytes;

    public static bool IsArchive(string fileName)
        => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedStatement> ReadAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        using var archive = Open(content);

        // Nach Namen, denn so heißen Tagesauszüge: 2026.06.09, 2026.06.30, … Die Reihenfolge
        // im Archiv ist die des Packprogramms und sagt über den Inhalt nichts.
        var entries = archive.Entries
            .Where(e => !IsJunk(e))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (entries.Count == 0)
        {
            throw new StatementFormatException($"„{fileName}“ ist leer.");
        }

        if (entries.Count > MaxEntries)
        {
            throw new StatementFormatException(
                $"„{fileName}“ enthält {entries.Count} Dateien — mehr als {MaxEntries} liest "
                + "dieser Weg nicht ein.");
        }

        var statements = new List<ParsedStatement>();
        var records = new List<ImportRecord>();
        var budget = MaxTotalBytes;

        foreach (var entry in entries)
        {
            if (!parser.CanRead(entry.Name))
            {
                records.Add(Unreadable(entry, "keine camt-Datei"));
                continue;
            }

            var (buffer, ueberzogen) = await CopyAsync(entry, budget, ct);

            if (ueberzogen)
            {
                throw new StatementFormatException(
                    $"Die Dateien in „{fileName}“ sind entpackt zusammen größer als "
                    + $"{MaxTotalBytes / (1024 * 1024)} MB.");
            }

            budget -= buffer.Length;
            buffer.Position = 0;

            try
            {
                var statement = await parser.ParseAsync(buffer, entry.Name, ct);

                statements.Add(statement);
                records.AddRange(statement.Records);
            }
            catch (StatementFormatException ex)
            {
                // Eine kaputte Datei im Archiv kippt nicht den ganzen Import. Sie steht als
                // Zeile da und nennt den Grund — die anderen sieben lassen sich trotzdem lesen.
                records.Add(Unreadable(entry, ex.Message));
            }
            finally
            {
                await buffer.DisposeAsync();
            }
        }

        if (statements.Count == 0)
        {
            throw new StatementFormatException(
                $"In „{fileName}“ steckt kein camt-Auszug — gefunden wurden nur "
                + string.Join(", ", entries.Take(3).Select(e => $"„{e.Name}“"))
                + (entries.Count > 3 ? $" und {entries.Count - 3} weitere." : "."));
        }

        return Merge(fileName, statements, records);
    }

    // ── Zusammenlegen ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aus vielen Auszügen wird einer.
    /// </summary>
    /// <remarks>
    /// Die IBAN muss überall dieselbe sein: die Übernahme bucht alle Sätze auf <em>ein</em>
    /// Konto. Ein Archiv mit zwei Konten stillschweigend auf eines zu buchen wäre der
    /// schlimmere Fehler — lieber abweisen und sagen, welche beiden es sind.
    /// </remarks>
    private static ParsedStatement Merge(
        string fileName, List<ParsedStatement> statements, List<ImportRecord> records)
    {
        var ibans = statements
            .Select(s => s.Iban)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ibans.Count > 1)
        {
            throw new StatementFormatException(
                $"„{fileName}“ enthält Auszüge zu mehreren Konten ({string.Join(", ", ibans)}). "
                + "Bitte je Konto ein Archiv einlesen.");
        }

        var formate = statements.Select(s => s.Format).Distinct(StringComparer.Ordinal).ToList();

        return new ParsedStatement
        {
            FileName = fileName,
            Format = string.Join(" · ", formate),
            BankName = statements.Select(s => s.BankName).FirstOrDefault(b => b is not null),
            Iban = ibans.FirstOrDefault(),

            // Der Schlusssaldo des jüngsten Auszugs, nicht der des letzten in der Reihe. Ein
            // Saldo gilt zu einem Stichtag; den ältesten zu nehmen wäre eine Zahl von vorgestern
            // mit dem Anschein von heute.
            ClosingBalance = Newest(statements)?.ClosingBalance,
            SourceCount = statements.Count,
            Records = records,
        };
    }

    private static ParsedStatement? Newest(List<ParsedStatement> statements)
        => statements
            .Select(s => (Statement: s, Last: s.Records
                .Where(r => r.BookingDate is not null)
                .Max(r => r.BookingDate)))
            .Where(x => x.Last is not null)
            .OrderByDescending(x => x.Last)
            .Select(x => x.Statement)
            .FirstOrDefault()
           ?? statements.LastOrDefault();

    // ── Bausteine ──────────────────────────────────────────────────────────────────────────

    private static ZipArchive Open(Stream content)
    {
        try
        {
            return new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new StatementFormatException("Die Datei ist kein lesbares ZIP-Archiv.");
        }
    }

    /// <summary>Ordnereinträge und der Beifang der Packprogramme.</summary>
    private static bool IsJunk(ZipArchiveEntry entry)
        => entry.Name.Length == 0
           || entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
           || entry.Name.StartsWith('.');

    /// <summary>
    /// Kopiert einen Eintrag, aber nur bis zum Rest des Budgets.
    /// </summary>
    /// <returns>Der Inhalt und ob das Budget dabei überzogen wurde.</returns>
    private static async Task<(MemoryStream Buffer, bool Exceeded)> CopyAsync(
        ZipArchiveEntry entry, long budget, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        await using var source = entry.Open();

        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > budget)
            {
                await buffer.DisposeAsync();
                return (new MemoryStream(), true);
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        return (buffer, false);
    }

    private static ImportRecord Unreadable(ZipArchiveEntry entry, string reason)
        => new(
            Reference: "ZIP:" + entry.FullName,
            BookingDate: null,
            Payee: entry.Name,
            Amount: null,
            Problem: reason);
}
