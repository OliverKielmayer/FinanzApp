using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Dasselbe wie der Ordnerdurchgang, nur mit dem Postfach als Quelle.
/// </summary>
/// <remarks>
/// <para><b>Die Mail wird ein Vorgang, ihre Anhänge werden Belege.</b> Beides zusammen, denn
/// einzeln fehlt jedem die Hälfte: Ein Anhang ohne den Satz darüber ist ein abgelegtes Blatt
/// ohne Auftrag, und ein Vorgang ohne den Anhang ist eine Notiz, die auf nichts zeigt.</para>
/// <para><b>Erst abgeben, dann wegräumen.</b> Eine Mail wandert erst in den Erledigt-Ordner,
/// wenn jeder ihrer Anhänge angekommen ist. Andersherum wäre ein Beleg verloren, sobald der
/// Server einmal nicht antwortet — und niemand wüsste, welcher.</para>
/// <para><b>Nichts wird gelöscht.</b> Verschoben, wie beim Ordner. Der Dienst hat die Mail
/// weitergereicht und nicht bearbeitet; sie gehört weiter dem Postfach.</para>
/// </remarks>
public sealed class MailboxWorker(
    MailboxOptions options,
    IMailbox mailbox,
    IIntakeClient client,
    ILogger<MailboxWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Postfach wird angesehen: {Ordner} auf {Host}, erledigt nach {Erledigt}. "
                + "Vorgang aus der Mail: {Vorgang}.",
            options.Folder, options.Host, options.ResolvedDoneFolder,
            options.CreateTask ? "ja" : "nein");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ein Postfach, das gerade nicht erreichbar ist, darf den Dienst nicht beenden —
                // der Ordner läuft daneben weiter. Gemeldet wird es trotzdem bei jedem
                // Durchgang: Ein Postfach, das seit Tagen schweigt, soll im Protokoll stehen.
                log.LogError(ex, "Der Durchgang über das Postfach ist gescheitert.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.SweepSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        log.LogInformation("Der Postfachdurchgang ist beendet.");
    }

    /// <summary>Ein Durchgang: öffnen, Mail für Mail abgeben, wegräumen.</summary>
    private async Task SweepAsync(CancellationToken ct)
    {
        await using var sitzung = await mailbox.OpenAsync(ct);

        foreach (var mail in sitzung.Messages)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (await DeliverAsync(mail, ct))
            {
                await sitzung.DoneAsync(mail, ct);
            }
        }
    }

    /// <summary>
    /// Gibt eine Mail ab. <c>true</c>, wenn sie weggeräumt werden darf.
    /// </summary>
    /// <remarks>
    /// <para>Eine Mail ohne brauchbaren Anhang ist trotzdem erledigt, sobald ihr Vorgang steht —
    /// sonst läge sie bis in alle Ewigkeit im Ordner und käme bei jedem Durchgang wieder. Wer
    /// keinen Vorgang will und eine Mail ohne Anhänge schickt, hat eine Mail geschickt, die
    /// diesen Dienst nichts angeht; auch die wandert weg.</para>
    /// <para>Scheitert dagegen <em>ein</em> Anhang, bleibt die ganze Mail liegen. Sie beim
    /// nächsten Durchgang noch einmal zu schicken ist harmlos: Der Eingang hält den Abdruck
    /// jeder Datei und legt dieselbe kein zweites Mal ab.</para>
    /// </remarks>
    private async Task<bool> DeliverAsync(MailItem mail, CancellationToken ct)
    {
        var angeboten = 0;

        // Der erste Beleg, der ankommt — auf ihn zeigt nachher der Vorgang.
        int? ziel = null;

        foreach (var anhang in mail.Attachments)
        {
            if (!Allowed(anhang))
            {
                continue;
            }

            angeboten++;
            var ergebnis = await client.HandOverAsync(
                anhang.Content, anhang.FileName, mail.Source, ct);

            switch (ergebnis.Status)
            {
                case HandoverStatus.Handed:
                    ziel ??= ergebnis.Intake?.DocumentId;

                    log.LogInformation(
                        "Aus „{Betreff}“: {Datei} übergeben. {Zusammenfassung}",
                        mail.Subject, anhang.FileName, ergebnis.Message);
                    break;

                // Abgelehnt heisst: Der Server will diese Datei nicht, und das ändert sich beim
                // nächsten Durchgang nicht. Sie noch einmal zu schicken wäre eine Schleife.
                case HandoverStatus.Rejected:
                    log.LogWarning(
                        "Aus „{Betreff}“: {Datei} abgelehnt. {Grund}",
                        mail.Subject, anhang.FileName, ergebnis.Message);
                    break;

                default:
                    log.LogWarning(
                        "Aus „{Betreff}“: {Datei} nicht übergeben ({Status}). {Grund} "
                            + "Die Mail bleibt liegen.",
                        mail.Subject, anhang.FileName, ergebnis.Status, ergebnis.Message);
                    return false;
            }
        }

        if (angeboten == 0)
        {
            log.LogInformation(
                "„{Betreff}“ hatte keinen übernehmbaren Anhang.", mail.Subject);
        }

        // Zuletzt, und das ist der ganze Punkt: Jetzt gibt es den Beleg, auf den der Vorgang
        // zeigen kann. Vorher angelegt zeigte er auf nichts, und wer die Zeile antippte,
        // landete bei „Zu dieser Aufgabe gibt es keinen eigenen Screen“ — während der Anhang
        // zwei Klicks weiter in der Ablage lag.
        //
        // Dass der Vorgang jetzt <em>nach</em> der Ablage entsteht, ist unbedenklich:
        // Scheitert er, bleibt die Mail liegen, und beim nächsten Durchgang erkennt der
        // Eingang dieselbe Datei am Abdruck wieder, statt sie ein zweites Mal abzulegen.
        return !options.CreateTask || await CreateTaskAsync(mail, ziel, ct);
    }

    /// <summary>
    /// Der Vorgang zur Mail.
    /// </summary>
    /// <remarks>
    /// Ohne Frist. Der Dienst kennt sie nicht, und ein geratenes Datum — etwa der Tag der Mail —
    /// liesse den Vorgang sofort überfällig aussehen. Dieselbe Überlegung wie beim offenen
    /// Posten aus einem Beleg: Woran sich die Nachverfolgung misst, tippt ein Mensch.
    /// </remarks>
    private async Task<bool> CreateTaskAsync(MailItem mail, int? ziel, CancellationToken ct)
    {
        var anhaenge = mail.Attachments.Count(Allowed);

        var erfolg = await client.CreateTaskAsync(
            new CreateTaskRequest
            {
                Title = mail.Subject,
                Detail = $"Mail von {mail.From} vom {mail.Date:dd.MM.yyyy}"
                         + (anhaenge == 0
                             ? " · ohne Anhang"
                             : $" · {anhaenge} {(anhaenge == 1 ? "Anhang" : "Anhänge")}"),

                // Nur zusammen. Eine Art ohne Nummer zeigt so wenig irgendwohin wie nichts.
                SourceType = ziel is null ? null : LinkTargetType.Document,
                SourceId = ziel,
            },
            ct);

        if (!erfolg)
        {
            log.LogWarning(
                "Der Vorgang zu „{Betreff}“ liess sich nicht anlegen. Die Mail bleibt liegen.",
                mail.Subject);
        }

        return erfolg;
    }

    /// <summary>
    /// Ob ein Anhang überhaupt angeboten wird.
    /// </summary>
    /// <remarks>
    /// <para>Dieselben zwei Fragen wie beim Ordner: die Dateiart und die Grösse. Die Grösse
    /// zählt hier doppelt, weil der Anhang schon im Arbeitsspeicher liegt — ihn auch noch
    /// hochzuladen, um am Ende abgewiesen zu werden, wäre zweimal umsonst.</para>
    /// <para>Eine leere Liste heisst: alle anbieten, der Server entscheidet. Siehe
    /// <see cref="MailboxOptions.Extensions"/>.</para>
    /// </remarks>
    private bool Allowed(MailAttachment anhang)
    {
        if (options.MaxMegabytes > 0
            && anhang.Content.LongLength > options.MaxMegabytes * 1024L * 1024L)
        {
            log.LogWarning(
                "{Datei} ist {Megabyte:0.0} MB gross und wird nicht angeboten (Grenze {Grenze} MB).",
                anhang.FileName, anhang.Content.LongLength / 1024.0 / 1024.0, options.MaxMegabytes);

            return false;
        }

        if (options.Extensions is not { Length: > 0 } erlaubt)
        {
            return true;
        }

        var endung = Path.GetExtension(anhang.FileName);

        return erlaubt.Any(e => string.Equals(
            e.StartsWith('.') ? e : "." + e, endung, StringComparison.OrdinalIgnoreCase));
    }
}
