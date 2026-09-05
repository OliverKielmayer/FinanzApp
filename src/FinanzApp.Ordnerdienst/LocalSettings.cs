using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Die Einstellungen dieses Rechners — Scanordner, Adresse, Zugang — aus einer Datei neben der
/// Anwendung.
/// </summary>
/// <remarks>
/// <para>Sie hängen nicht an der Umgebung: ein Dienst läuft in <c>Production</c>, ein
/// Ausprobieren mit user-secrets in <c>Development</c>, und beide überwachen denselben Ordner.
/// Eine <c>appsettings.Production.json</c> wäre beim Ausprobieren stillschweigend weg. Die Datei
/// ist freiwillig und gehört nicht ins Repository — die versionierte <c>appsettings.json</c>
/// bleibt die Vorlage mit leeren Feldern.
/// </para>
/// <para><b>Die Reihenfolge ist der ganze Punkt</b>, und sie ist zweimal falsch gewesen. Mit
/// <c>AddJsonFile</c> angehängt gewann die Datei gegen die Umgebungsvariablen — dann ließ sich
/// der Dienst für einen einzelnen Lauf nicht mehr umbiegen. Vor die <em>erste</em> Umgebungsquelle
/// gesetzt landete sie vor <c>appsettings.json</c>, denn die erste ist die mit dem Präfix
/// <c>DOTNET_</c> und steht ganz am Anfang: die Vorlage überschrieb die Werte dieses Rechners mit
/// Leerzeichenketten, und der Dienst meldete beim Start einen leeren Scanordner.</para>
/// <para>Deshalb wird die Stelle an <em>Dateinamen</em> gebunden und nicht an eine Position, die
/// von der Reihenfolge des Gerüsts abhängt.</para>
/// </remarks>
public static class LocalSettings
{
    /// <summary>Der Dateiname, neben der Anwendung gesucht.</summary>
    public const string FileName = "appsettings.Local.json";

    /// <summary>
    /// Hängt die Datei hinter die übrigen <c>appsettings</c>-Dateien und vor alles Weitere.
    /// </summary>
    /// <remarks>
    /// Hinter die Vorlagen, sonst gewinnen deren leere Felder. Vor die Umgebungsvariablen und die
    /// Befehlszeile, damit ein einzelner Lauf sich umbiegen lässt. Und vor die user-secrets kommt
    /// sie <b>nicht</b>: dort steht das Passwort, und wer dort etwas setzt, meint es — auch wenn
    /// die Datei dasselbe Feld trägt.
    /// </remarks>
    public static void Insert(IList<IConfigurationSource> sources)
    {
        var lokal = new JsonConfigurationSource
        {
            Path = FileName,
            Optional = true,
            ReloadOnChange = false,
        };

        // Nur die appsettings-Dateien: die user-secrets sind ebenfalls eine JSON-Quelle, heißen
        // aber secrets.json und sollen hinter dieser Datei bleiben.
        var letzteVorlage = sources.ToList().FindLastIndex(q =>
            q is JsonConfigurationSource { Path: { } datei }
            && datei.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase));

        sources.Insert(letzteVorlage < 0 ? 0 : letzteVorlage + 1, lokal);
    }
}
