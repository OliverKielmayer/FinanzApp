using System.Reflection;

namespace FinanzApp.Client;

/// <summary>Fassungsnummer der Anwendung, wie sie in der Fußzeile der Anmeldung steht.</summary>
public static class AppVersion
{
    /// <summary>Nur Haupt- und Nebennummer, etwa <c>0.4</c>.</summary>
    public static string Short { get; } = Resolve();

    private static string Resolve()
    {
        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version is null ? "—" : $"{version.Major}.{version.Minor}";
    }
}
