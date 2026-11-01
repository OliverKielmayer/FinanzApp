using FinanzApp.Shared.Contracts;

namespace FinanzApp.Shared.Formatting;

/// <summary>
/// Die Bereiche der Ablage im Klartext.
/// </summary>
/// <remarks>
/// <para>Bewusst <em>nicht</em> der Ordnername aus <c>DocumentPathService.FolderFor</c>: Der muss
/// auf der Platte stabil bleiben, weil ein umbenannter Ordner jeden hinterlegten Pfad
/// entwertete. Diese Beschriftung darf jederzeit umformuliert werden.</para>
/// <para>Hier und nicht dreimal verteilt. Vorher stand sie in der Typenverwaltung, in der
/// Auswertung und in der Dokumentliste — und war schon auseinandergelaufen: zweimal
/// „Versicherung“, einmal „Absicherung“. Derselbe Bereich hieß also je nach Bildschirm anders,
/// und niemand konnte sagen, welches der richtige Name war.</para>
/// </remarks>
public static class DocumentAreaNames
{
    public static string Label(DocumentArea area) => area switch
    {
        DocumentArea.Insurance => "Versicherung",
        DocumentArea.Health => "Gesundheit",
        DocumentArea.Housing => "Wohnen",
        DocumentArea.Work => "Arbeit",
        DocumentArea.Finance => "Finanzen",
        DocumentArea.Vehicle => "Fahrzeuge",
        DocumentArea.Personal => "Persönlich",
        _ => "Sonstiges",
    };
}
