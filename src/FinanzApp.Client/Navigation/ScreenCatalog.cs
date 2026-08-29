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
/// <param name="Group">
/// Gruppe in der Seitennavigation. <c>null</c> heißt: steht dort nicht.
/// </param>
/// <param name="InAreaList">Steht in der Bereichsliste.</param>
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
    NavGroup? Group = null,
    bool InAreaList = false,
    string? DetailTitle = null);

/// <summary>
/// Die vier Gruppen der Seitennavigation — v5-Handoff, Abschnitt 2.
/// </summary>
/// <remarks>
/// Die Navigation trug neunzehn gleichrangige Einträge. Nicht die Zahl war das Problem,
/// sondern dass drei verschiedene Dinge auf einer Ebene standen: Objekte, die man besitzt;
/// Wege, wie Daten hereinkommen; und Stammdaten, die man dreimal im Jahr braucht.
/// </remarks>
public enum NavGroup
{
    /// <summary>Was täglich dran ist.</summary>
    Everyday = 0,

    /// <summary>Was man besitzt — auf dem Telefon eine Liste mit Klassenfilter.</summary>
    Holdings = 1,

    /// <summary>Was man wissen will.</summary>
    Analysis = 2,

    /// <summary>Was man einmal einstellt.</summary>
    System = 3,
}

/// <summary>
/// Kopfzeilen und Navigationsbeschriftungen an einer Stelle. Die Reihenfolge ist zugleich die
/// Reihenfolge in Tab-Bar, Bereichsliste und Seitennavigation.
/// </summary>
/// <remarks>
/// <para>Seit v5 trägt die Tab-Bar vier Zellen: Heute · Vorgänge · Bestand · Erfassen. Die
/// sieben Objektbereiche stehen nicht mehr einzeln in der Navigation — sie sind Klassen einer
/// Liste und bleiben als Detailziele erreichbar. Die Screens selbst wurden dafür nicht
/// angefasst.</para>
/// <para>„Mehr“ ist entfallen: es war ein Sammelbecken, und ein Sammelbecken ist keine
/// Ordnung, sondern ihr Aufschub.</para>
/// </remarks>
public static class ScreenCatalog
{
    public static IReadOnlyList<Screen> All { get; } =
    [
        // Die vier Zellen der Tab-Bar, in dieser Reihenfolge.
        new("/", "Heute", "Übersicht", "Heute", Group: NavGroup.Everyday),
        new("/vorgaenge", "Offen", "Vorgänge", "Vorgänge", Group: NavGroup.Everyday),
        new("/bestand", "Bestand", "Alle Objekte", "Bestand", Group: NavGroup.Holdings),
        new("/erfassen", "Erfassen", "Neue Buchung", "Erfassen",
            IsDetail: true, RequiresWrite: true, IsAction: true),

        // Kein Tab mehr: die Suche in der Kopfzeile führt hierher.
        new("/dokumente", "Ablage", "Dokumente", IsDetail: true, DetailTitle: "Dokument"),

        new("/einstellungen", "System", "Einstellungen", IsDetail: true,
            Group: NavGroup.System, InAreaList: true),

        // Die Objektklassen: ab Tabletbreite als zweite Ebene unter „Bestand“, auf dem Telefon
        // der Klassenfilter derselben Liste. Dieselbe Struktur, nur aufgeklappt.
        new("/konten", "Finanzen", "Konten & Buchungen", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),
        new("/budgets", "Planung", "Budgets", IsDetail: true,
            Group: NavGroup.Analysis, InAreaList: true),
        new("/depot", "Investments", "Depot", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),
        new("/vorsorge", "Finanzen", "Vorsorge & Kapital", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),
        new("/absicherung", "Absicherung", "Versicherungen", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),
        new("/wohnen", "Wohnen", "Wohnen & Immobilien", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true, DetailTitle: "Immobilie"),
        new("/fahrzeuge", "Mobilität", "Fahrzeuge", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true, DetailTitle: "Fahrzeug"),
        new("/darlehen", "Finanzierungen", "Darlehen", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),
        new("/arbeit", "Beruf", "Arbeit & Beruf", IsDetail: true,
            Group: NavGroup.Holdings, InAreaList: true),

        new("/auswertungen", "Analyse", "Auswertungen", IsDetail: true,
            Group: NavGroup.Analysis, InAreaList: true),
        new("/gesundheit", "Gesundheit", "Gesundheit & PKV", IsDetail: true,
            Group: NavGroup.Analysis, InAreaList: true, DetailTitle: "PKV-Vorgang"),

        // Zeilen des Erfassen-Sheets, keine Navigationsziele mehr: der Nutzer sucht
        // die Tür, nicht das Dateiformat.
        new("/import", "Import", "Import­vorschau", IsDetail: true),
        new("/scaneingang", "Eingang", "Scaneingang", IsDetail: true),

        // Zeilen der Einstellungen.
        new("/kategorien", "Ordnung", "Kategorien", IsDetail: true),
        new("/dokumenttypen", "Ordnung", "Dokumenttypen", IsDetail: true),
        new("/kategorieregeln", "Ordnung", "Kategorieregeln", IsDetail: true),
        new("/benutzer", "Konto", "Benutzer & Anmeldung", IsDetail: true),

        // Detailscreens, die aus einem Bereich heraus geöffnet werden.
        new("/gesundheit/scannen", "Erfassen", "Beleg scannen", IsDetail: true, RequiresWrite: true),
        new("/scannen", "Erfassen", "Beleg einlesen", IsDetail: true, RequiresWrite: true),
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

    /// <summary>Die vier Einträge der Tab-Bar.</summary>
    public static IReadOnlyList<Screen> Tabs { get; } = [.. All.Where(s => s.TabLabel is not null)];

    /// <summary>Die Bereichsliste.</summary>
    public static IReadOnlyList<Screen> Areas { get; } = [.. All.Where(s => s.InAreaList)];

    /// <summary>
    /// Die Gruppen der Seitennavigation, in ihrer Reihenfolge.
    /// </summary>
    /// <remarks>
    /// Vier Gruppen statt neunzehn gleichrangiger Einträge. Was unter „Bestand“ steht, ist auf
    /// dem Telefon der Klassenfilter derselben Liste — dieselbe Struktur, nur aufgeklappt.
    /// </remarks>
    public static IReadOnlyList<(NavGroup Group, string Label, IReadOnlyList<Screen> Screens)>
        NavGroups { get; } =
    [
        .. new[]
        {
            (NavGroup.Everyday, "Alltag"),
            (NavGroup.Holdings, "Bestand"),
            (NavGroup.Analysis, "Analyse"),
            (NavGroup.System, "System"),
        }
        .Select(g => (g.Item1, g.Item2,
            (IReadOnlyList<Screen>)[.. All.Where(x => x.Group == g.Item1)])),
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
