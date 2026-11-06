namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Regeln der gelernten Ablage, die Client und Server gleich sehen müssen.
/// </summary>
/// <remarks>
/// Das Gegenstück zu <see cref="Categorization"/>, eine Ebene weiter: Dort geht es um die
/// Kategorie einer Buchung, hier um das Objekt eines Schreibens.
/// </remarks>
public static class Filing
{
    /// <summary>
    /// Das Muster, auf das eine gemerkte Ablageregel greift — der <b>ganze</b> Absender.
    /// </summary>
    /// <remarks>
    /// <para>Hier weicht die Ablage bewusst von der Kategorisierung ab.
    /// <see cref="Categorization.RulePatternFor"/> nimmt das erste Wort des Empfängers, weil
    /// Bankzeilen denselben Händler in zehn Schreibweisen führen und „REWE“ sie alle einfängt.
    /// Ein Briefkopf ist nicht so: „Landkreis“ träfe jeden Landkreis, „Sparkasse“ jede
    /// Sparkasse, „Vodafone“ immerhin noch Vodafone GmbH und Vodafone West — zwei Verträge an
    /// zwei Konten.</para>
    /// <para>Und die Folgen sind andere. Eine falsche Kategorie steht in einer Auswertung und
    /// fällt beim Durchsehen auf. Ein falsch abgelegter Beleg hängt still an einem fremden
    /// Objekt, und dort sucht ihn niemand. Breiter machen lässt sich ein Muster später; eine
    /// Zuordnung, die niemand bemerkt hat, findet man nicht wieder.</para>
    /// </remarks>
    public static string RulePatternFor(string sender) => Categorization.Normalize(sender);

    /// <summary>
    /// Ob eine Ablageregel auf einen Absender greift.
    /// </summary>
    /// <remarks>
    /// Derselbe Vergleich wie bei der Kategorisierung — normalisiert, als Präfix an der
    /// Wortgrenze. Damit greift eine Regel für „Landkreis Musterkreis“ auch auf „Landkreis
    /// Musterkreis Abfallwirtschaftsamt“, aber nicht auf „Landkreis Musterkreiser
    /// Zweckverband“.
    /// </remarks>
    public static bool Matches(string sender, string pattern)
        => Categorization.Matches(sender, pattern);
}
