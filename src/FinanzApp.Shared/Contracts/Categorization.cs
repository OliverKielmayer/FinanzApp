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

    /// <summary>
    /// Ob eine Regel auf einen Empfänger greift.
    /// </summary>
    /// <remarks>
    /// Verglichen wird normalisiert: Groß- und Kleinschreibung, Mehrfachleerzeichen und
    /// Satzzeichen fallen weg. Bankdaten schreiben denselben Empfänger je nach Zahlungsweg
    /// unterschiedlich — „REWE Markt“, „REWE  MARKT“, „REWE-Markt“ —, und ein Vergleich, der
    /// daran scheitert, fragt beim nächsten Import wieder nach derselben Zuordnung.
    ///
    /// Der Präfix ist die Untergrenze, kein Endzustand: Verwendungszwecke variieren stärker,
    /// als ein erstes Wort abbilden kann.
    /// </remarks>
    public static bool Matches(string payee, string pattern)
    {
        var muster = Normalize(pattern);
        var wer = Normalize(payee);

        if (muster.Length == 0 || !wer.StartsWith(muster, StringComparison.Ordinal))
        {
            return false;
        }

        // An der Wortgrenze, nicht mitten im Wort. „R + V Lebensversicherung“ ergibt das Muster
        // „R“; ohne diese Prüfung finge es Rundfunk, REWE, Restaurant und die Raiffeisenbank
        // gleich mit ein — an echten Bankdaten sofort nachweisbar.
        return wer.Length == muster.Length || wer[muster.Length] == ' ';
    }

    /// <summary>Kleinschreibung, ein Leerzeichen, keine Satzzeichen.</summary>
    public static string Normalize(string text)
    {
        var raus = new System.Text.StringBuilder(text.Length);
        var luecke = false;

        foreach (var zeichen in text)
        {
            if (char.IsLetterOrDigit(zeichen))
            {
                if (luecke && raus.Length > 0)
                {
                    raus.Append(' ');
                }

                raus.Append(char.ToLowerInvariant(zeichen));
                luecke = false;
            }
            else
            {
                // Leerzeichen, Bindestrich, Punkt — alles dasselbe: eine Fuge.
                luecke = true;
            }
        }

        return raus.ToString();
    }
}
