namespace FinanzApp.Client.Navigation;

/// <summary>Ein Screen der Anwendung mit seinen Kopfzeilen und seinem Platz in der Navigation.</summary>
/// <remarks>Lange Komposita im Titel tragen ein bedingtes Trennzeichen (\u00ad), damit sie
/// auf schmalen Geräten an der richtigen Stelle umbrechen statt mitten im Wort.</remarks>
/// <param name="Route">Pfad der Seite.</param>
/// <param name="Kicker">Kleine Zeile über dem Titel.</param>
/// <param name="Title">Titel im Kopf.</param>
/// <param name="TabLabel">Beschriftung in der Tab-Bar; <c>null</c>, wenn der Screen kein Tab ist.</param>
/// <param name="IsDetail">Detailscreens bekommen im Kopf einen Zurück-Schalter.</param>
/// <param name="RequiresWrite">Nur für Benutzer mit Schreibrecht sichtbar.</param>
public sealed record Screen(
    string Route,
    string Kicker,
    string Title,
    string? TabLabel = null,
    bool IsDetail = false,
    bool RequiresWrite = false);

/// <summary>
/// Kopfzeilen und Navigationsbeschriftungen an einer Stelle. Die Reihenfolge ist zugleich die
/// Reihenfolge in Tab-Bar und Seitennavigation.
/// </summary>
public static class ScreenCatalog
{
    public static IReadOnlyList<Screen> All { get; } =
    [
        new("/", "Übersicht", "Vermögen", "Vermögen"),
        new("/konten", "Finanzen", "Konten & Buchungen", "Konten"),
        new("/erfassen", "Erfassen", "Neue Buchung", "Erfassen", RequiresWrite: true),
        new("/budgets", "Planung", "Budgets", "Budgets"),
        new("/depot", "Investments", "Depot", "Depot"),
        new("/mehr", "Mehr", "Alle Bereiche", IsDetail: true),
        new("/benutzer", "Konto", "Benutzer & Anmeldung", IsDetail: true),
        new("/darlehen", "Finanzierungen", "Darlehen", IsDetail: true),
        new("/import", "Import", "Import\u00advorschau", IsDetail: true),
    ];

    /// <summary>Die fünf Einträge der Tab-Bar.</summary>
    public static IReadOnlyList<Screen> Tabs { get; } = [.. All.Where(s => s.TabLabel is not null)];

    /// <summary>Die übrigen Bereiche, die ab Tabletbreite in der Seitennavigation stehen.</summary>
    public static IReadOnlyList<Screen> Secondary { get; } = [.. All.Where(s => s.TabLabel is null)];

    private static readonly Screen Fallback = All[0];

    /// <summary>Findet den Screen zu einem Pfad. Unbekannte Pfade fallen auf das Dashboard zurück.</summary>
    public static Screen Resolve(string relativePath)
    {
        var path = "/" + relativePath.Split('?')[0].Split('#')[0].Trim('/');
        return All.FirstOrDefault(s => string.Equals(s.Route, path, StringComparison.OrdinalIgnoreCase))
               ?? Fallback;
    }
}
