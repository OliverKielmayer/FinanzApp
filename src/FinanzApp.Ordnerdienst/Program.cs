using FinanzApp.Ordnerdienst;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ── Der Ordnerdienst ───────────────────────────────────────────────────────────────────────
//
// Ein Windows-Dienst, der einen Ordner überwacht und jede neue Datei an die FinanzApp
// weiterreicht. Er analysiert nichts selbst: das kann die Anwendung besser, und zwei Fassungen
// derselben Leseregeln liefen zwangsläufig auseinander. Er sorgt nur dafür, dass keine Datei
// liegen bleibt und keine zweimal hinausgeht.

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,

    // Ein Windows-Dienst startet in C:\Windows\System32. Ohne diese Zeile sucht er seine
    // appsettings.json dort — und findet sie nie.
    ContentRootPath = AppContext.BaseDirectory,
});

// Die Werte dieses Rechners kommen aus einer Datei neben der Anwendung. Wo sie in der Kette
// hängt und warum genau dort, steht in LocalSettings — die Stelle ist zweimal falsch gewesen.
LocalSettings.Insert(builder.Configuration.Sources);

// Als Dienst: der Lebenszyklus hängt am Dienststeuerungsmanager, und das Protokoll geht ins
// Ereignisprotokoll von Windows. Läuft dasselbe Programm von Hand in einer Konsole, ist der
// Aufruf wirkungslos — derselbe Build lässt sich damit ausprobieren, ohne ihn zu installieren.
builder.Services.AddWindowsService(options => options.ServiceName = "FinanzApp Ordnerdienst");

var options = builder.Configuration.GetSection(WatchOptions.SectionName).Get<WatchOptions>()
              ?? new WatchOptions();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<FolderInbox>();
builder.Services.AddSingleton<IIntakeClient, IntakeClient>();
builder.Services.AddHostedService<FolderWorker>();

// Das Postfach ist die zweite Quelle desselben Eingangs und bleibt aus, solange niemand es
// einschaltet. Wer den Dienst nur fuer den Scanordner benutzt, merkt von alldem nichts — kein
// Abschnitt in der Konfiguration, keine Zeile im Protokoll.
var mailbox = builder.Configuration.GetSection(MailboxOptions.SectionName).Get<MailboxOptions>()
              ?? new MailboxOptions();

builder.Services.AddSingleton(mailbox);

if (mailbox.Enabled)
{
    builder.Services.AddSingleton<IMailbox, Mailbox>();
    builder.Services.AddHostedService<MailboxWorker>();
}

var host = builder.Build();

// Geprüft wird vor dem ersten Beleg und nicht bei ihm. Ein Dienst, der läuft und stillschweigend
// nichts tut, ist schlimmer als einer, der sich weigert und sagt, was fehlt — und im Betrieb
// steht dieser Grund im Ereignisprotokoll, wo Windows nach dem Start hinsieht.
// Beide Quellen zusammen: Ein Postfach, dem der Server fehlt, ist derselbe Fall wie ein
// Ordner, den es nicht gibt — der Dienst liefe und taete stillschweigend nichts.
List<string> problems = [.. options.Problems(), .. mailbox.Problems()];

if (problems.Count > 0)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ordnerdienst");

    foreach (var problem in problems)
    {
        logger.LogCritical("{Problem}", problem);
    }

    return 1;
}

host.Run();
return 0;
