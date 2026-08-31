using Microsoft.Extensions.Logging;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Der überwachte Ordner als Warteschlange.
/// </summary>
/// <remarks>
/// <para>Der Ordner <em>ist</em> die Warteschlange — es gibt keine zweite daneben. Das ist die
/// wichtigste Entscheidung dieses Dienstes: eine Liste im Arbeitsspeicher wäre nach einem
/// Neustart weg, eine Liste auf der Platte müsste mit dem Ordner abgeglichen werden, und beide
/// könnten von ihm abweichen. Was noch daliegt, ist noch nicht übergeben; was übergeben ist,
/// liegt nicht mehr da. Ein Neustart mitten im Betrieb kostet damit nichts.</para>
/// <para>Überwacht wird nur die oberste Ebene. Deshalb dürfen <c>_erledigt</c> und
/// <c>_fehlgeschlagen</c> darin liegen, ohne dass der Dienst seine eigenen Ergebnisse wieder
/// einsammelt.</para>
/// </remarks>
public sealed class FolderInbox(WatchOptions options, ILogger<FolderInbox> log)
{
    /// <summary>
    /// Was beim letzten Blick über eine Datei bekannt war.
    /// </summary>
    /// <remarks>
    /// Nur Beobachtung, kein Zustand: geht der Eintrag verloren, wird eine Datei einmal später
    /// übergeben. Das ist der Preis dafür, dass hier nichts zu verwalten ist.
    /// </remarks>
    private readonly Dictionary<string, Sighting> sightings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Die Dateien, die auf der obersten Ebene liegen und angeboten werden dürfen.</summary>
    /// <remarks>
    /// Fehlt der Ordner, ist das keine Ausnahme: ein Netzlaufwerk ist nach einem Neustart
    /// manchmal später da als der Dienst. Gemeldet wird es trotzdem, und zwar jedes Mal — ein
    /// falsch geschriebener Pfad soll auffallen.
    /// </remarks>
    public IReadOnlyList<string> Waiting()
    {
        if (!Directory.Exists(options.WatchFolder))
        {
            log.LogWarning(
                "Der überwachte Ordner {Ordner} ist nicht da. Nichts zu tun, bis er auftaucht.",
                options.WatchFolder);

            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(options.WatchFolder)
                    .Where(p => options.Accepts(p))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Der Ordner {Ordner} ließ sich nicht lesen.", options.WatchFolder);
            return [];
        }
    }

    /// <summary>
    /// Ob die Datei fertig geschrieben ist.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Proben, weil eine nicht reicht. Die erste: Größe und Änderungszeitpunkt haben
    /// sich seit <see cref="WatchOptions.SettleSeconds"/> nicht bewegt. Ein Scanner meldet die
    /// Datei, sobald er sie anlegt, und schreibt danach weiter — ohne diese Wartezeit ginge die
    /// erste Seite als vollständiges Dokument hinaus.</para>
    /// <para>Die zweite: die Datei lässt sich <em>exklusiv</em> öffnen. Das ist unter Windows der
    /// verlässliche Beweis, dass niemand mehr daran schreibt. Sie schlägt auch fehl, während ein
    /// Virenwächter mitliest — das kostet einen Durchgang und ist der geringere Preis gegenüber
    /// einem halben Dokument in der Ablage.</para>
    /// </remarks>
    public bool IsSettled(string path, DateTime now)
    {
        var info = new FileInfo(path);

        if (!info.Exists || info.Length == 0)
        {
            // Länge 0 heißt: der Schreiber hat die Datei gerade erst angelegt. Sie ist noch
            // keine Datei, sondern eine Ankündigung.
            return false;
        }

        var jetzt = new Sighting(info.Length, info.LastWriteTimeUtc, now);

        if (!sightings.TryGetValue(path, out var vorher)
            || vorher.Length != jetzt.Length
            || vorher.Written != jetzt.Written)
        {
            sightings[path] = jetzt;
            return false;
        }

        return now - vorher.Seen >= TimeSpan.FromSeconds(options.SettleSeconds) && IsFree(path);
    }

    /// <summary>Vergisst, was über eine Datei bekannt war — sie ist aus dem Ordner heraus.</summary>
    public void Forget(string path) => sightings.Remove(path);

    /// <summary>
    /// Legt eine übergebene Datei in den Erledigt-Ordner, nach Monat sortiert.
    /// </summary>
    /// <remarks>
    /// Verschoben und nicht gelöscht. Der Dienst hat die Datei weitergereicht, nicht verarbeitet;
    /// sie zu löschen wäre eine Entscheidung über ein Original, die ihm nicht zusteht. Nach Monat,
    /// weil ein Ordner mit dreitausend Scans niemandem hilft.
    /// </remarks>
    public string? MoveToDone(string path, DateTime now)
        => Move(path, Path.Combine(options.ResolvedDoneFolder, now.ToString("yyyy-MM")));

    /// <summary>Legt eine abgelehnte Datei beiseite, damit sie im Eingang nicht im Weg steht.</summary>
    public string? MoveToFailed(string path) => Move(path, options.ResolvedFailedFolder);

    /// <summary>
    /// Verschiebt eine Datei und weicht einem belegten Namen aus.
    /// </summary>
    /// <remarks>
    /// Überschrieben wird nie: zwei Scans desselben Tages heißen beim selben Gerät gern gleich,
    /// und der ältere ist kein Ausschuss. Scheitert das Verschieben, bleibt die Datei liegen —
    /// dann taucht sie im nächsten Durchgang wieder auf, und das ist besser als ein Abbruch.
    /// </remarks>
    private string? Move(string path, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var name = Path.GetFileName(path);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            var target = Path.Combine(folder, name);
            var counter = 1;

            while (File.Exists(target))
            {
                target = Path.Combine(folder, $"{stem}_{counter++}{extension}");
            }

            File.Move(path, target);
            Forget(path);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError(
                ex, "{Datei} ließ sich nicht nach {Ordner} verschieben. Sie bleibt im Eingang.",
                path, folder);

            return null;
        }
    }

    /// <summary>Ob niemand sonst die Datei geöffnet hat.</summary>
    private static bool IsFree(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private readonly record struct Sighting(long Length, DateTime Written, DateTime Seen);
}
