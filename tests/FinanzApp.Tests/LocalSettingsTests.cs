using FinanzApp.Ordnerdienst;
using Microsoft.Extensions.Hosting;

namespace FinanzApp.Tests;

/// <summary>
/// Wo <c>appsettings.Local.json</c> in der Konfigurationskette hängt.
/// </summary>
/// <remarks>
/// <para>Eine Reihenfolge ist eine unsichtbare Zusicherung: nichts daran übersetzt falsch, nichts
/// wirft, und beide Fehlstellungen sahen im Protokoll plausibel aus. Angehängt gewann die Datei
/// gegen die Umgebungsvariablen — bemerkt erst, als ein Prüflauf den echten Scanordner überwachte
/// statt des vorgegebenen. Vor die erste Umgebungsquelle gesetzt verlor sie gegen
/// <c>appsettings.json</c> — bemerkt erst, als der Dienst auf dem echten Rechner mit leerem
/// Scanordner abbrach.</para>
/// <para>Geprüft wird deshalb nicht die Position in der Liste, sondern das, was am Ende
/// herauskommt: mit echten Dateien in einem echten Ordner und einem echten
/// <see cref="HostApplicationBuilder"/>, denn die Reihenfolge, gegen die es geht, ist dessen
/// eigene.</para>
/// </remarks>
public sealed class LocalSettingsTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    public LocalSettingsTests()
    {
        Directory.CreateDirectory(root);

        // Die Vorlage, wie sie im Repository liegt: alle Felder vorhanden, alle leer.
        File.WriteAllText(Path.Combine(root, "appsettings.json"),
            """{ "Ordnerdienst": { "WatchFolder": "", "Email": "" } }""");

        File.WriteAllText(Path.Combine(root, LocalSettings.FileName),
            """{ "Ordnerdienst": { "WatchFolder": "C:/aus-der-datei", "Email": "datei@example.net" } }""");
    }

    private string? Read(string key)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = root,
        });

        LocalSettings.Insert(builder.Configuration.Sources);
        return builder.Configuration[key];
    }

    [Fact]
    public void Die_Datei_gewinnt_gegen_die_leere_Vorlage()
    {
        Assert.Equal("C:/aus-der-datei", Read("Ordnerdienst:WatchFolder"));
        Assert.Equal("datei@example.net", Read("Ordnerdienst:Email"));
    }

    [Fact]
    public void Die_Umgebungsvariable_gewinnt_gegen_die_Datei()
    {
        Environment.SetEnvironmentVariable("Ordnerdienst__WatchFolder", "C:/aus-der-umgebung");

        try
        {
            Assert.Equal("C:/aus-der-umgebung", Read("Ordnerdienst:WatchFolder"));

            // Und nur dieses eine Feld: was die Umgebung nicht nennt, bleibt bei der Datei.
            Assert.Equal("datei@example.net", Read("Ordnerdienst:Email"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Ordnerdienst__WatchFolder", null);
        }
    }

    /// <summary>
    /// Ohne die Datei bleibt es bei der Vorlage — sie ist freiwillig, ihr Fehlen kein Fehler.
    /// </summary>
    [Fact]
    public void Ohne_die_Datei_bleibt_die_Vorlage_stehen()
    {
        File.Delete(Path.Combine(root, LocalSettings.FileName));

        Assert.Equal(string.Empty, Read("Ordnerdienst:WatchFolder"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
