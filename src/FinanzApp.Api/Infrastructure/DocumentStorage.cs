using System.Text;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    /// <summary>
    /// Wurzelordner aller Dokumentdateien. Relativ zum Anwendungsverzeichnis oder absolut.
    /// </summary>
    public string Root { get; set; } = "App_Data/Dokumente";

    /// <summary>Höchstgröße einer hochgeladenen Datei in Megabyte.</summary>
    public int MaxFileSizeMegabytes { get; set; } = 25;

    /// <summary>Zugelassene Erweiterungen, klein geschrieben mit Punkt.</summary>
    public string[] AllowedExtensions { get; set; } =
        [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".heic", ".txt", ".xml", ".csv"];
}

/// <summary>
/// Setzt relative Dokumentpfade zu absoluten zusammen und legt hochgeladene Dateien ab.
/// </summary>
/// <remarks>
/// <para>In der Datenbank steht nur der relative Pfad. Ein absoluter würde die Daten an einen
/// Rechner binden: nach einem Umzug oder auf einem zweiten Gerät zeigte jeder Eintrag ins Leere.
/// Zusammengesetzt wird deshalb erst hier, gegen den aktuell konfigurierten Wurzelordner.</para>
/// <para>Jede Auflösung prüft, ob das Ergebnis noch <em>innerhalb</em> der Wurzel liegt. Ein
/// gespeicherter Pfad ist eine Eingabe wie jede andere — ohne diese Prüfung ließe sich mit
/// <c>../../</c> jede Datei des Servers ausliefern.</para>
/// </remarks>
public sealed class DocumentPathService
{
    private readonly DocumentStorageOptions options;
    private readonly ILogger<DocumentPathService> log;

    public DocumentPathService(
        DocumentStorageOptions options, IHostEnvironment environment, ILogger<DocumentPathService> log)
    {
        this.options = options;
        this.log = log;

        Root = Path.GetFullPath(Path.IsPathRooted(options.Root)
            ? options.Root
            : Path.Combine(environment.ContentRootPath, options.Root));
    }

    /// <summary>Absoluter Wurzelordner, unter dem alle Dokumente liegen.</summary>
    public string Root { get; }

    public long MaxFileSizeBytes => options.MaxFileSizeMegabytes * 1024L * 1024L;

    public bool IsAllowedExtension(string? extension)
        => extension is { Length: > 0 }
           && options.AllowedExtensions.Contains(extension.ToLowerInvariant());

    public string AllowedExtensionList => string.Join(", ", options.AllowedExtensions);

    /// <summary>
    /// Absoluter Pfad zu einem gespeicherten Dokument, oder <c>null</c>, wenn der relative Pfad
    /// aus dem Wurzelordner herausführt.
    /// </summary>
    public string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(Root, relativePath));
        var fence = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(fence, StringComparison.OrdinalIgnoreCase))
        {
            log.LogWarning("Dokumentpfad zeigt aus dem Wurzelordner heraus und wurde abgewiesen: {Pfad}",
                relativePath);
            return null;
        }

        return combined;
    }

    /// <summary>Ob die Datei zum hinterlegten Pfad tatsächlich existiert.</summary>
    public bool Exists(string relativePath)
        => Resolve(relativePath) is { } absolute && File.Exists(absolute);

    /// <summary>
    /// Legt eine hochgeladene Datei unter dem Bereichsordner ab und gibt den relativen Pfad zurück.
    /// Ein bereits vergebener Name bekommt einen Zähler — überschrieben wird nie.
    /// </summary>
    /// <param name="subFolder">
    /// Vorgeschlagener Unterordner unterhalb des Bereichs, etwa
    /// <c>Lebensversicherung/Heidelberger Leben/2025</c>. Jedes Segment wird entschärft; leere
    /// Angabe legt direkt in den Bereichsordner.
    /// </param>
    /// <param name="preferredName">
    /// Gewünschter Dateiname ohne Erweiterung. Die des Originals bleibt erhalten — sie sagt, was
    /// die Datei ist, und das entscheidet nicht der Vorschlag.
    /// </param>
    public async Task<string> StoreAsync(
        Stream content,
        DocumentArea area,
        string originalFileName,
        string? subFolder = null,
        string? preferredName = null,
        CancellationToken ct = default)
    {
        var folder = Combine(FolderFor(area), subFolder);
        var directory = Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var safeName = Sanitize(preferredName is { Length: > 0 }
            ? preferredName + Path.GetExtension(originalFileName)
            : originalFileName);
        var candidate = safeName;
        var counter = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var extension = Path.GetExtension(safeName);
            candidate = $"{stem}_{counter++}{extension}";
        }

        var target = Path.Combine(directory, candidate);
        await using (var file = File.Create(target))
        {
            await content.CopyToAsync(file, ct);
        }

        return folder + "/" + candidate;
    }

    /// <summary>
    /// Verschiebt eine abgelegte Datei an einen anderen Platz im Dokumentordner.
    /// </summary>
    /// <remarks>
    /// <para>Gibt den tatsächlichen neuen Pfad zurück — er kann vom gewünschten abweichen, wenn
    /// dort schon eine Datei gleichen Namens liegt. Überschrieben wird nie: zwei Berichte
    /// desselben Stichtags sind zwei Berichte.</para>
    /// <para><c>null</c> heißt: nicht verschoben. Fehlt die Datei oder scheitert das Verschieben,
    /// bleibt der Eintrag auf seinem alten Pfad stehen — ein Eintrag, der auf ein Nichts zeigt,
    /// wäre schlimmer als einer, der auf den alten Ordner zeigt.</para>
    /// </remarks>
    public string? Move(string relativePath, string targetRelativePath)
    {
        if (Resolve(relativePath) is not { } quelle || !File.Exists(quelle))
        {
            return null;
        }

        if (Resolve(targetRelativePath) is not { } ziel)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);

            var ordner = targetRelativePath[..targetRelativePath.LastIndexOf('/')];
            var name = Path.GetFileName(targetRelativePath);
            var stamm = Path.GetFileNameWithoutExtension(name);
            var endung = Path.GetExtension(name);
            var zaehler = 1;

            while (File.Exists(ziel))
            {
                name = $"{stamm}_{zaehler++}{endung}";
                ziel = Path.Combine(Path.GetDirectoryName(ziel)!, name);
            }

            File.Move(quelle, ziel);
            Aufraeumen(Path.GetDirectoryName(quelle)!);

            return ordner + "/" + name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(
                ex, "Datei {Quelle} ließ sich nicht nach {Ziel} verschieben.",
                relativePath, targetRelativePath);

            return null;
        }
    }

    /// <summary>
    /// Räumt die Ordner weg, die durch das Verschieben leer geworden sind.
    /// </summary>
    /// <remarks>
    /// <para>Aufwärts, solange leer: <c>Unbekannt/2020</c> und das <c>Unbekannt</c> darüber
    /// verschwinden gemeinsam. Bliebe der obere stehen, sagte er im Dateimanager weiter
    /// „unbekannt“ über einen Vertrag, der längst zugeordnet ist — der halbe Sinn des
    /// Umhängens.</para>
    /// <para>Gelöscht wird nur, was <em>vollständig</em> leer ist, und nie der Wurzelordner. Ein
    /// Ordner mit Inhalt beendet den Aufstieg.</para>
    /// </remarks>
    private void Aufraeumen(string folder)
    {
        var wurzel = Path.TrimEndingDirectorySeparator(Root);
        var aktuell = folder;

        try
        {
            while (!string.Equals(
                       Path.TrimEndingDirectorySeparator(aktuell), wurzel, StringComparison.OrdinalIgnoreCase)
                   && Directory.Exists(aktuell)
                   && !Directory.EnumerateFileSystemEntries(aktuell).Any())
            {
                var eltern = Path.GetDirectoryName(aktuell);
                Directory.Delete(aktuell);

                if (eltern is not { Length: > 0 })
                {
                    return;
                }

                aktuell = eltern;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ein leerer Ordner, der stehen bleibt, ist kein Schaden.
        }
    }

    /// <summary>
    /// Hängt einen vorgeschlagenen Unterordner an den Bereichsordner.
    /// </summary>
    /// <remarks>
    /// Segmentweise entschärft, und der Vorschlag kommt aus Dokumentinhalten — also aus fremdem
    /// Text. <c>..</c> darin wäre ein Weg aus dem Wurzelordner heraus; <see cref="Sanitize"/>
    /// macht daraus einen harmlosen Namen, weil Punkte nur zwischen Zeichen überleben.
    /// </remarks>
    public static string Combine(string folder, string? subFolder)
    {
        if (string.IsNullOrWhiteSpace(subFolder))
        {
            return folder;
        }

        var segmente = subFolder
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Sanitize)
            .Where(s => s.Length > 0);

        return string.Join("/", segmente.Prepend(folder));
    }

    /// <summary>Ordnername je Bereich — die Ablage bleibt auch im Dateimanager lesbar.</summary>
    public static string FolderFor(DocumentArea area) => area switch
    {
        DocumentArea.Insurance => "Versicherungen",
        DocumentArea.Health => "Gesundheit",
        DocumentArea.Housing => "Wohnen",
        DocumentArea.Work => "Arbeit",
        DocumentArea.Finance => "Finanzen",
        _ => "Sonstiges",
    };

    /// <summary>
    /// Macht aus einem hochgeladenen Dateinamen einen, der gefahrlos im Dateisystem landen darf:
    /// ohne Verzeichnisanteile, ohne Sonderzeichen, mit Umlauten in Klarschrift.
    /// </summary>
    public static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Dokument";
        }

        name = name
            .Replace("ä", "ae", StringComparison.Ordinal).Replace("Ä", "Ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal).Replace("Ö", "Oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal).Replace("Ü", "Ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_');
        }

        var cleaned = builder.ToString().Trim('.', '_');
        return cleaned.Length == 0 ? "Dokument" : cleaned;
    }
}
