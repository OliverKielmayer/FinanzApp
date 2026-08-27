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
/// <param name="IsAction">
/// Kein Ziel, sondern eine Aktion: die Tab-Zelle öffnet das Erfassen-Sheet, statt zu navigieren.
/// </param>
/// <param name="InAreaList">Steht auf „Mehr“ in der Bereichsliste.</param>
/// <param name="DetailTitle">
/// Titel für Unterseiten mit Id (<c>/versicherungen/3</c>). Ohne Angabe gilt <paramref name="Title"/>.
/// </param>
public sealed record Screen(
    string Route,
    string Kicker,
    string Title,
    string? TabLabel = null,
    bool IsDetail = false,
    bool RequiresWrite = false,
    bool IsAction = false,
    bool InAreaList = false,
    string? DetailTitle = null);

/// <summary>
/// Kopfzeilen und Navigationsbeschriftungen an einer Stelle. Die Reihenfolge ist zugleich die
/// Reihenfolge in Tab-Bar, Bereichsliste und Seitennavigation.
/// </summary>
/// <remarks>
/// Mit der Erweiterung trägt die Tab-Bar Vermögen · Vorgänge · Erfassen · Dokumente · Mehr.
/// Konten, Budgets und Depot bleiben unverändert erhalten und wandern in die Bereichsliste —
/// die Screens selbst wurden dafür nicht angefasst.
/// </remarks>
public static class ScreenCatalog
{
    public static IReadOnlyList<Screen> All { get; } =
    [
        // Die fünf Zellen der Tab-Bar, in dieser Reihenfolge.
        new("/", "Übersicht", "Vermögen", "Vermögen"),
        new("/vorgaenge", "Offen", "Vorgänge", "Vorgänge"),
        new("/erfassen", "Erfassen", "Neue Buchung", "Erfassen",
            IsDetail: true, RequiresWrite: true, IsAction: true),
        new("/dokumente", "Ablage", "Dokumente", "Dokumente", DetailTitle: "Dokument"),
        new("/mehr", "Mehr", "Alle Bereiche", "Mehr"),

        // Bereiche, die über „Mehr“ und ab Tabletbreite über die Seitennavigation erreichbar sind.
        new("/konten", "Finanzen", "Konten & Buchungen", IsDetail: true, InAreaList: true),
        new("/budgets", "Planung", "Budgets", IsDetail: true, InAreaList: true),
        new("/depot", "Investments", "Depot", IsDetail: true, InAreaList: true),
        new("/darlehen", "Finanzierungen", "Darlehen", IsDetail: true, InAreaList: true),
        new("/auswertungen", "Analyse", "Auswertungen", IsDetail: true, InAreaList: true),
        new("/import", "Import", "Import\u00advorschau", IsDetail: true, InAreaList: true),
        new("/vorsorge", "Finanzen", "Vorsorge & Kapital", IsDetail: true, InAreaList: true),
        new("/absicherung", "Absicherung", "Versicherungen", IsDetail: true, InAreaList: true),
        new("/gesundheit", "Gesundheit", "Gesundheit & PKV", IsDetail: true, InAreaList: true,
            DetailTitle: "PKV-Vorgang"),
        new("/wohnen", "Wohnen", "Wohnen & Immobilien", IsDetail: true, InAreaList: true,
            DetailTitle: "Immobilie"),
        new("/fahrzeuge", "Mobilität", "Fahrzeuge", IsDetail: true, InAreaList: true,
            DetailTitle: "Fahrzeug"),
        new("/scaneingang", "Eingang", "Scaneingang", IsDetail: true, InAreaList: true),
        new("/kategorien", "Ordnung", "Kategorien", IsDetail: true, InAreaList: true),
        new("/kategorieregeln", "Ordnung", "Kategorieregeln", IsDetail: true, InAreaList: true),
        new("/benutzer", "Konto", "Benutzer & Anmeldung", IsDetail: true, InAreaList: true),

        // Detailscreens, die aus einem Bereich heraus geöffnet werden.
        new("/gesundheit/scannen", "Erfassen", "Beleg scannen", IsDetail: true, RequiresWrite: true),
        new("/police", "Vertrag", "Vertrag", IsDetail: true, DetailTitle: "Vertrag"),
        new("/neu", "Neu", "Anlegen", IsDetail: true, RequiresWrite: true, DetailTitle: "Anlegen"),
        new("/bearbeiten", "Bearbeiten", "Bearbeiten", IsDetail: true, RequiresWrite: true,
            DetailTitle: "Bearbeiten"),
        new("/vertraege", "Wohnen", "Vertrag", IsDetail: true, DetailTitle: "Vertrag"),
        new("/rechnungen", "Wohnen", "Rechnung", IsDetail: true, DetailTitle: "Rechnung"),
        new("/liquiditaet", "Übersicht", "Liquidität", IsDetail: true),
        new("/liquiditaet/fluss", "Übersicht", "Wohin fließt es", IsDetail: true),
        new("/liquiditaet/sparen", "Übersicht", "Sparpotential", IsDetail: true),
    ];

    /// <summary>Die fünf Einträge der Tab-Bar.</summary>
    public static IReadOnlyList<Screen> Tabs { get; } = [.. All.Where(s => s.TabLabel is not null)];

    /// <summary>Die Bereichsliste auf „Mehr“ und in der Seitennavigation.</summary>
    public static IReadOnlyList<Screen> Areas { get; } = [.. All.Where(s => s.InAreaList)];

    /// <summary>
    /// Die Seitennavigation ab Tabletbreite: eine flache Liste in der Reihenfolge aus Handoff v4,
    /// Abschnitt 3 — nicht die Reihenfolge der Tab-Bar.
    /// </summary>
    /// <remarks>
    /// Alle Bereiche des Handoffs, in seiner Reihenfolge.
    /// </remarks>
    public static IReadOnlyList<Screen> SideNav { get; } =
    [
        .. new[]
        {
            "/", "/vorgaenge", "/konten", "/budgets", "/depot",
            "/vorsorge", "/absicherung",
            "/dokumente", "/scaneingang", "/gesundheit", "/wohnen", "/fahrzeuge",
            "/darlehen", "/auswertungen", "/import", "/kategorien", "/kategorieregeln",
            "/benutzer",
        }
        .Select(route => All.First(s => s.Route == route)),
    ];

    private static readonly Screen Fallback = All[0];

    /// <summary>
    /// Findet den Screen zu einem Pfad. Detailseiten mit Id (<c>/versicherungen/3</c>) fallen auf
    /// ihren Bereich zurück, unbekannte Pfade auf das Dashboard.
    /// </summary>
    public static Screen Resolve(string relativePath)
    {
        var path = "/" + relativePath.Split('?')[0].Split('#')[0].Trim('/');

        var exact = All.FirstOrDefault(s => string.Equals(s.Route, path, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        // Detailseite eines Bereichs: den längsten passenden Präfix nehmen. Sie bekommt in jedem
        // Fall den Zurück-Schalter — auch wenn ihr Bereich selbst ein Tab ist.
        var prefix = All
            .Where(s => s.Route.Length > 1 && path.StartsWith(s.Route + "/", StringComparison.OrdinalIgnoreCase))
            .MaxBy(s => s.Route.Length);

        return prefix is null
            ? Fallback
            : prefix with { Title = prefix.DetailTitle ?? prefix.Title, IsDetail = true };
    }
}
