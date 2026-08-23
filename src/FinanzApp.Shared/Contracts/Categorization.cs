namespace FinanzApp.Shared.Contracts;

/// <summary>Regeln der Kategorisierung, die Client und Server gleich sehen müssen.</summary>
public static class Categorization
{
    /// <summary>
    /// Präfix, auf das eine gemerkte Regel greift — das erste Wort des Empfängers.
    /// Der Client zeigt es im Bottom-Sheet an („Regel für ... merken“), der Server legt die
    /// Regel darauf an. Beide müssen zum selben Ergebnis kommen.
    /// </summary>
    public static string RulePatternFor(string payee)
    {
        var head = payee.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(head) ? payee : head;
    }
}
