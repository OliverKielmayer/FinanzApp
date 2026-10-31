using FinanzApp.Shared.Contracts;

namespace FinanzApp.Client.Services;

/// <summary>
/// Beschriftet Kategorien so, dass zwei gleichnamige auseinanderzuhalten sind.
/// </summary>
/// <remarks>
/// <para>Ein Haushalt darf „Sonstiges“ zweimal führen — einmal als Einnahme, einmal als Ausgabe.
/// Im Zuordnungsfenster fällt das nicht auf, weil dort nur die passende Richtung steht. In der
/// Filterzeile stehen beide nebeneinander, und dann sind es zwei Knöpfe mit demselben Wort: wer
/// den falschen trifft, sieht eine leere Liste und keinen Grund dafür.</para>
/// <para>Die Richtung steht deshalb <b>nur an den doppelten</b> Namen. Sie an jeden zu schreiben
/// wäre dieselbe Auskunft dreißigmal, und sie verschwände im Rauschen — gerade dort, wo sie
/// gebraucht wird.</para>
/// </remarks>
public static class CategoryChips
{
    public static string DirectionLabel(CategoryDirection direction)
        => direction == CategoryDirection.Income ? "Einnahme" : "Ausgabe";

    /// <summary>
    /// Die Beschriftung einer Kategorie innerhalb dieser Liste.
    /// </summary>
    /// <remarks>
    /// „Innerhalb dieser Liste“ ist der Punkt: ob ein Name eindeutig ist, hängt davon ab, wogegen
    /// er steht. Dieselbe Kategorie heißt in der gefilterten Auswahl eines Sheets schlicht
    /// „Sonstiges“ und in der vollen Filterzeile „Sonstiges · Ausgabe“.
    /// </remarks>
    public static string Label(CategoryDto category, IReadOnlyCollection<CategoryDto> all)
        => IsAmbiguous(category, all)
            ? $"{category.Name} · {DirectionLabel(category.Direction)}"
            : category.Name;

    /// <summary>Ob dieser Name in der Liste mehr als einmal vorkommt.</summary>
    private static bool IsAmbiguous(CategoryDto category, IReadOnlyCollection<CategoryDto> all)
        => all.Count(c => string.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase)) > 1;
}
