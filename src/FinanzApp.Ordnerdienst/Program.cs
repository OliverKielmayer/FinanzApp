using FinanzApp.Ordnerdienst;
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

// Die Werte dieses Rechners: Scanordner, Adresse, Zugang. Sie hängen nicht an der Umgebung —
// ein Dienst läuft in Production, ein Ausprobieren mit user-secrets in Development, und beide
// überwachen denselben Ordner. Eine appsettings.Production.json wäre beim Ausprobieren
// stillschweigend weg. Die Datei liegt neben der Anwendung, ist freiwillig und gehört nicht ins
// Repository — die versionierte appsettings.json bleibt die Vorlage mit leeren Feldern.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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

var host = builder.Build();

// Geprüft wird vor dem ersten Beleg und nicht bei ihm. Ein Dienst, der läuft und stillschweigend
// nichts tut, ist schlimmer als einer, der sich weigert und sagt, was fehlt — und im Betrieb
// steht dieser Grund im Ereignisprotokoll, wo Windows nach dem Start hinsieht.
if (options.Problems() is { Count: > 0 } problems)
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
