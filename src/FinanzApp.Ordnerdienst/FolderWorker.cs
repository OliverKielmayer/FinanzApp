using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Der Dienst selbst: sieht in den Ordner, reicht weiter, räumt auf.
/// </summary>
/// <remarks>
/// <para>Ein Durchgang statt einer Ereigniskette. Die Ordnerüberwachung von Windows verliert
/// Ereignisse, wenn ihr Puffer überläuft, und auf Netzlaufwerken bekommt sie manche nie zu
/// sehen. Ein Dienst, der ihr allein glaubt, verliert genau die Datei, die niemand vermisst,
/// weil niemand von ihr weiß. Deshalb ist die Nachlese der eigentliche Antrieb und die
/// Überwachung nur der Wecker, der ihn vorzieht.</para>
/// <para>Reihum und nicht parallel: die Analyse eines PDF kostet den Server Arbeit, und ein
/// Stapel von zweihundert Seiten aus dem Einzugsscanner soll ihn nicht umwerfen. Der Eingang
/// eines Haushalts ist kein Datenstrom.</para>
/// </remarks>
public sealed class FolderWorker(
    WatchOptions options,
    FolderInbox inbox,
    IIntakeClient client,
    ILogger<FolderWorker> log) : BackgroundService
{
    /// <summary>Der Wecker der Ordnerüberwachung. Höchstens ein Weckruf wartet.</summary>
    private readonly SemaphoreSlim wake = new(0, 1);

    /// <summary>Wie oft eine Datei schon vorübergehend gescheitert ist.</summary>
    private readonly Dictionary<string, int> attempts = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? watcher;
    private volatile bool watcherBroken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Ordnerdienst läuft. Überwacht {Ordner}, liefert an {Adresse} ein, erledigt nach {Erledigt}.",
            options.WatchFolder, options.BaseAddress, options.ResolvedDoneFolder);

        while (!stoppingToken.IsCancellationRequested)
        {
            var pause = TimeSpan.FromSeconds(options.SweepSeconds);

            try
            {
                EnsureWatcher();
                pause = await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ein unerwarteter Fehler darf den Dienst nicht beenden. Ein beendeter Dienst
                // fällt niemandem auf; eine Fehlerzeile im Ereignisprotokoll schon.
                log.LogError(ex, "Der Durchgang ist unerwartet gescheitert. Der nächste läuft normal weiter.");
            }

            if (await WaitAsync(pause, stoppingToken))
            {
                log.LogDebug("Der Ordner hat sich gemeldet — Durchgang vorgezogen.");
            }
        }

        watcher?.Dispose();
        log.LogInformation("Ordnerdienst beendet.");
    }

    /// <summary>
    /// Ein Durchgang über alles, was im Ordner liegt.
    /// </summary>
    /// <returns>
    /// Wie lange bis zum nächsten Durchgang gewartet wird. Wartet noch eine Datei darauf, fertig
    /// geschrieben zu werden, ist das kurz — sonst der eingestellte Takt. Eine Datei, die gerade
    /// hereinkommt, soll nicht eine Minute liegen, weil sie beim ersten Blick noch wuchs.
    /// </returns>
    private async Task<TimeSpan> SweepAsync(CancellationToken ct)
    {
        var files = inbox.Waiting();
        if (files.Count == 0)
        {
            return TimeSpan.FromSeconds(options.SweepSeconds);
        }

        var wachsend = false;

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            if (!inbox.IsSettled(path, DateTime.UtcNow))
            {
                wachsend = true;
                continue;
            }

            if (TooBig(path) is { } zuGross)
            {
                Settle(path, zuGross);
                continue;
            }

            var result = await client.HandOverAsync(path, ct);

            if (result.Status == HandoverStatus.Blocked)
            {
                // Nicht die Datei, sondern der Weg dorthin. Weiterzumachen hieße, jede Datei
                // im Ordner einen Versuch zu kosten, obwohl an keiner etwas fehlt.
                log.LogError(
                    "Einlieferung nicht möglich: {Grund} Der Durchgang endet hier, {Anzahl} Datei(en) bleiben liegen.",
                    result.Message, files.Count);

                return TimeSpan.FromSeconds(options.SweepSeconds);
            }

            Settle(path, result);
        }

        return wachsend
            ? TimeSpan.FromSeconds(Math.Max(1, Math.Min(options.SettleSeconds, options.SweepSeconds)))
            : TimeSpan.FromSeconds(options.SweepSeconds);
    }

    /// <summary>
    /// Die Ablehnung für eine zu große Datei — oder <c>null</c>, wenn sie klein genug ist.
    /// </summary>
    /// <remarks>
    /// Hier und nicht erst beim Server: die Rumpfgrenze des Servers greift <em>während</em> des
    /// Sendens, und ein Abbruch mitten im Rumpf sieht aus wie ein Verbindungsfehler. Ein
    /// 300-MB-Scan aus dem Einzugsscanner ginge sonst mehrfach über die Leitung, nur um jedes Mal
    /// dasselbe misszuverstehen.
    /// </remarks>
    private HandoverResult? TooBig(string path)
    {
        var info = new FileInfo(path);

        return options.IsSmallEnough(info.Length)
            ? null
            : new HandoverResult(
                HandoverStatus.Rejected,
                $"{info.Length / 1024 / 1024} MB — mehr als die erlaubten {options.MaxMegabytes} MB.");
    }

    /// <summary>Was mit der Datei nach dem Versuch geschieht.</summary>
    private void Settle(string path, HandoverResult result)
    {
        var name = Path.GetFileName(path);

        switch (result.Status)
        {
            case HandoverStatus.Handed:
                Report(name, result.Intake, result.Message);
                attempts.Remove(path);

                if (inbox.MoveToDone(path, DateTime.Now) is null)
                {
                    // Die Datei ist übergeben, ließ sich aber nicht wegräumen. Beim nächsten
                    // Durchgang geht sie erneut hinaus — das ist ein doppelter Beleg im
                    // Scaneingang, und der ist dort sichtbar und löschbar. Eine verlorene Datei
                    // wäre es nicht.
                    log.LogWarning(
                        "{Datei} ist übergeben, liegt aber noch im Eingang. Sie wird erneut angeboten.",
                        name);
                }

                break;

            case HandoverStatus.Rejected:
                log.LogWarning("{Datei} abgelehnt: {Grund}", name, result.Message);
                attempts.Remove(path);
                Aside(path, name);
                break;

            default:
                var versuche = attempts.GetValueOrDefault(path) + 1;
                attempts[path] = versuche;

                if (versuche >= options.MaxAttempts)
                {
                    log.LogError(
                        "{Datei} nach {Versuche} Versuchen beiseitegelegt. Zuletzt: {Grund}",
                        name, versuche, result.Message);

                    attempts.Remove(path);
                    Aside(path, name);
                }
                else
                {
                    log.LogWarning(
                        "{Datei}, Versuch {Versuche} von {Grenze} gescheitert: {Grund}",
                        name, versuche, options.MaxAttempts, result.Message);
                }

                break;
        }
    }

    /// <summary>Was die FinanzApp mit dem Beleg gemacht hat — eine Zeile, die man lesen kann.</summary>
    /// <remarks>
    /// Auf zwei Stufen, weil es zwei Nachrichten sind. Vollständig zugeordnet ist eine
    /// Erfolgsmeldung; unvollständig ist eine Aufgabe für einen Menschen, und die gehört
    /// sichtbar ins Protokoll — sonst wartet ein Beleg im Eingang, ohne dass jemand hinsieht.
    /// </remarks>
    private void Report(string name, ScanIntakeResultDto? intake, string message)
    {
        if (intake is null)
        {
            log.LogInformation("{Datei} übergeben: {Meldung}", name, message);
            return;
        }

        if (intake.Outcome == ScanIntakeOutcome.Assigned)
        {
            log.LogInformation(
                "{Datei} übergeben und eingeordnet — Bereich {Bereich}, Beleg {Beleg}: {Meldung}",
                name, intake.Area, intake.DocumentId, intake.Summary);
        }
        else
        {
            log.LogInformation(
                "{Datei} übergeben, wartet auf Zuordnung — Bereich {Bereich}, Beleg {Beleg}: {Meldung}",
                name, intake.Area, intake.DocumentId, intake.Summary);
        }
    }

    /// <summary>Legt eine Datei beiseite und sagt, wo sie liegt.</summary>
    private void Aside(string path, string name)
    {
        if (inbox.MoveToFailed(path) is { } target)
        {
            log.LogInformation("{Datei} liegt jetzt unter {Ziel}.", name, target);
        }
    }

    /// <summary>
    /// Wartet den Takt ab — oder bis der Ordner sich meldet.
    /// </summary>
    /// <returns><c>true</c>, wenn die Ordnerüberwachung geweckt hat.</returns>
    private async Task<bool> WaitAsync(TimeSpan pause, CancellationToken ct)
    {
        try
        {
            return await wake.WaitAsync(pause, ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Legt die Ordnerüberwachung an, wenn sie fehlt oder ausgefallen ist.
    /// </summary>
    /// <remarks>
    /// Sie ist Beschleunigung, nicht Grundlage: ohne sie arbeitet der Dienst im Takt weiter. Ein
    /// Netzlaufwerk, das nach dem Start des Rechners erst später auftaucht, bekommt deshalb kein
    /// eigenes Sonderverfahren — der nächste Durchgang legt sie einfach an.
    /// </remarks>
    private void EnsureWatcher()
    {
        if (watcherBroken)
        {
            watcher?.Dispose();
            watcher = null;
            watcherBroken = false;
        }

        if (watcher is not null || !Directory.Exists(options.WatchFolder))
        {
            return;
        }

        try
        {
            var fresh = new FileSystemWatcher(options.WatchFolder)
            {
                // Nur die oberste Ebene: die eigenen Ergebnisordner liegen darunter, und ihre
                // Dateien wieder einzusammeln wäre eine Endlosschleife.
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,

                // Ein Einzugsscanner legt zwanzig Dateien in wenigen Sekunden ab. Der Standardpuffer
                // läuft dabei über — und übergelaufene Ereignisse sind verlorene Ereignisse.
                InternalBufferSize = 64 * 1024,
            };

            fresh.Created += (_, _) => Wake();
            fresh.Changed += (_, _) => Wake();
            fresh.Renamed += (_, _) => Wake();

            // Ein Puffer, der überläuft, oder ein Laufwerk, das verschwindet. Beides ist kein
            // Datenverlust, solange die Nachlese läuft — die Überwachung wird neu aufgesetzt.
            fresh.Error += (_, e) =>
            {
                log.LogWarning(
                    e.GetException(),
                    "Die Ordnerüberwachung ist ausgefallen. Der Dienst arbeitet im Takt weiter.");

                watcherBroken = true;
                Wake();
            };

            fresh.EnableRaisingEvents = true;
            watcher = fresh;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
                                      or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            log.LogWarning(
                ex, "Ohne Ordnerüberwachung — gearbeitet wird im Takt von {Sekunden} s.",
                options.SweepSeconds);
        }
    }

    /// <summary>Zieht den nächsten Durchgang vor. Mehr als einen Weckruf braucht es nicht.</summary>
    private void Wake()
    {
        try
        {
            wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Es wartet schon einer. Zwei Weckrufe wecken nicht zweimal.
        }
    }

    public override void Dispose()
    {
        watcher?.Dispose();
        wake.Dispose();
        base.Dispose();
    }
}
