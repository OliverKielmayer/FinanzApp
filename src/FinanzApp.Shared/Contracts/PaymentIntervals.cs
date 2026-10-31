namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Ein Betrag und sein Takt — auf den Monat und auf das Jahr gerechnet.
/// </summary>
/// <remarks>
/// <para>Die Rechnung stand dreimal im Haus: einmal für den Beitrag einer Police, einmal für
/// den Abschlag eines Vertrags, einmal für die Hochrechnung der Objektkosten. Dreimal dieselben
/// vier Zeilen, die beim nächsten Takt dreimal zu ändern wären.</para>
/// <para>Gerundet wird <b>bei der Umrechnung</b>, nicht erst am Ende: Die Monatszahl ist das,
/// was in der Liste steht, und eine Summe aus ungerundeten Zwischenwerten träfe die Summe der
/// angezeigten Zeilen nicht.</para>
/// </remarks>
public static class PaymentIntervals
{
    /// <summary>Der Betrag auf den Monat gerechnet, egal in welchem Takt er fällt.</summary>
    public static decimal PerMonth(decimal amount, PremiumInterval interval) => interval switch
    {
        PremiumInterval.Monthly => amount,
        PremiumInterval.Quarterly => decimal.Round(amount / 3m, 2),
        PremiumInterval.HalfYearly => decimal.Round(amount / 6m, 2),
        _ => decimal.Round(amount / 12m, 2),
    };

    /// <summary>
    /// Derselbe Betrag auf das Jahr.
    /// </summary>
    /// <remarks>
    /// Aus dem Betrag je Zahlung, nicht aus der gerundeten Monatszahl: Eine Jahresprämie von
    /// 1.000 € ergibt monatlich 83,33 €, und zwölf davon sind 999,96 €. Das Jahr ist die
    /// genauere Angabe und darf nicht aus der ungenaueren entstehen.
    /// </remarks>
    public static decimal PerYear(decimal amount, PremiumInterval interval) => interval switch
    {
        PremiumInterval.Monthly => amount * 12m,
        PremiumInterval.Quarterly => amount * 4m,
        PremiumInterval.HalfYearly => amount * 2m,
        _ => amount,
    };

    /// <summary>Der Takt im Klartext, wie er hinter einem Betrag steht.</summary>
    public static string Label(PremiumInterval interval) => interval switch
    {
        PremiumInterval.Monthly => "monatlich",
        PremiumInterval.Quarterly => "vierteljährlich",
        PremiumInterval.HalfYearly => "halbjährlich",
        _ => "jährlich",
    };
}
