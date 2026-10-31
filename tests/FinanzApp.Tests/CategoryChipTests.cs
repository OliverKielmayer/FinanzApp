using FinanzApp.Client.Services;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Zwei Kategorien mit demselben Namen müssen sich unterscheiden lassen.
/// </summary>
/// <remarks>
/// Am echten Bestand stehen „Wohnen“, „Freizeit“, „Sonstiges“ und „Auslagen Mama“ je zweimal in
/// der Filterzeile — einmal als Einnahme, einmal als Ausgabe. Zwei Knöpfe mit demselben Wort:
/// wer den falschen trifft, sieht eine leere Liste und erfährt nicht, warum.
/// </remarks>
public sealed class CategoryChipTests
{
    [Fact]
    public void Ein_eindeutiger_Name_bleibt_wie_er_ist()
    {
        IReadOnlyCollection<CategoryDto> alle = [Ausgabe(1, "Lebensmittel"), Ausgabe(2, "Auto")];

        Assert.Equal("Lebensmittel", CategoryChips.Label(alle.First(), alle));
    }

    [Fact]
    public void Ein_doppelter_Name_bekommt_seine_Richtung()
    {
        var ausgabe = Ausgabe(1, "Sonstiges");
        var einnahme = Einnahme(2, "Sonstiges");
        IReadOnlyCollection<CategoryDto> alle = [ausgabe, einnahme, Ausgabe(3, "Auto")];

        Assert.Equal("Sonstiges · Ausgabe", CategoryChips.Label(ausgabe, alle));
        Assert.Equal("Sonstiges · Einnahme", CategoryChips.Label(einnahme, alle));
        Assert.Equal("Auto", CategoryChips.Label(alle.Last(), alle));
    }

    /// <summary>
    /// Groß und klein geschrieben ist derselbe Name.
    /// </summary>
    /// <remarks>
    /// „Sonstiges“ und „sonstiges“ sind für das Auge dasselbe Wort, und genau darum geht es hier.
    /// Nur dem Vergleich im Code wären sie verschieden — und die beiden Knöpfe blieben wieder
    /// nicht auseinanderzuhalten.
    /// </remarks>
    [Fact]
    public void Die_Schreibweise_entscheidet_nicht()
    {
        var eine = Ausgabe(1, "Sonstiges");
        IReadOnlyCollection<CategoryDto> alle = [eine, Einnahme(2, "sonstiges")];

        Assert.Equal("Sonstiges · Ausgabe", CategoryChips.Label(eine, alle));
    }

    /// <summary>
    /// Eindeutig ist eine Frage der Liste, in der er steht.
    /// </summary>
    /// <remarks>
    /// Im Zuordnungsfenster stehen nur die Kategorien einer Richtung. Dort ist „Sonstiges“
    /// eindeutig, und ein angehängtes „· Ausgabe“ wäre eine Auskunft über einen Unterschied, den
    /// man dort gar nicht sieht.
    /// </remarks>
    [Fact]
    public void In_einer_Liste_einer_Richtung_steht_der_Name_allein()
    {
        var eine = Ausgabe(1, "Sonstiges");
        IReadOnlyCollection<CategoryDto> nurAusgaben = [eine, Ausgabe(2, "Auto")];

        Assert.Equal("Sonstiges", CategoryChips.Label(eine, nurAusgaben));
    }

    [Fact]
    public void Die_Richtung_heisst_wie_im_Rest_der_Anwendung()
    {
        Assert.Equal("Einnahme", CategoryChips.DirectionLabel(CategoryDirection.Income));
        Assert.Equal("Ausgabe", CategoryChips.DirectionLabel(CategoryDirection.Expense));
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private static CategoryDto Ausgabe(int id, string name)
        => new() { Id = id, Name = name, Direction = CategoryDirection.Expense };

    private static CategoryDto Einnahme(int id, string name)
        => new() { Id = id, Name = name, Direction = CategoryDirection.Income };
}
