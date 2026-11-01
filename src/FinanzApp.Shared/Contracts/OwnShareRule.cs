using FinanzApp.Shared.Formatting;

namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Die Selbstbeteiligungsregel eines Vertrags, so weit sie gepflegt ist.
/// </summary>
/// <param name="Percent">Anteil am Rechnungsbetrag in Prozent.</param>
/// <param name="AnnualCap">Höchstbetrag im Kalenderjahr; <c>null</c> heißt: ohne Deckel.</param>
/// <param name="DentalPercent">
/// Satz für Zahnbehandlung, ohne Deckel; <c>null</c> heißt: es gilt die allgemeine Regel.
/// </param>
/// <param name="UsedThisYear">
/// Was in <paramref name="Year"/> schon auf den Deckel gezahlt wurde — ohne Zahnbehandlungen,
/// die zählen nicht dagegen.
/// </param>
/// <param name="Year">Das Kalenderjahr, auf das sich <paramref name="UsedThisYear"/> bezieht.</param>
/// <param name="PolicyName">Der Vertrag, aus dem die Regel stammt — für die Begründung.</param>
public sealed record OwnShareRuleDto(
    decimal? Percent,
    decimal? AnnualCap,
    decimal? DentalPercent,
    decimal UsedThisYear,
    int Year,
    string? PolicyName);

/// <summary>Was die Regel für eine Rechnung ergibt.</summary>
/// <param name="Amount">Der vorgeschlagene Eigenanteil; <c>null</c> heißt: kein Vorschlag.</param>
/// <param name="Reason">Die Rechnung dahinter, im Klartext.</param>
public readonly record struct OwnShareAdvice(decimal? Amount, string Reason);

/// <summary>
/// Rechnet den Eigenanteil aus der Vertragsregel aus.
/// </summary>
/// <remarks>
/// <para><b>Gemeinsam gerechnet.</b> Der Server kennt die Regel und das bisher Verbrauchte,
/// die Oberfläche muss aber neu rechnen, sobald jemand den Betrag ändert oder „Zahnbehandlung“
/// setzt. Zwei Rechnungen an zwei Orten gingen irgendwann auseinander — also steht sie hier,
/// im gemeinsamen Teil, und beide rufen dieselbe.</para>
/// <para><b>Ein Vorschlag und keine Festsetzung.</b> Was die Kasse am Ende übernimmt, steht auf
/// ihrer Abrechnung. Dies ist die Erwartung aus dem Vertrag, und sie ist überschreibbar — ein
/// Tarif hat Ausnahmen, die in keinem Feld stehen.</para>
/// </remarks>
public static class OwnShareRule
{
    public static OwnShareAdvice For(decimal gross, bool dental, OwnShareRuleDto? regel)
    {
        if (gross <= 0m)
        {
            return new OwnShareAdvice(null, "Ohne Rechnungsbetrag lässt sich nichts ausrechnen.");
        }

        if (regel is null)
        {
            return new OwnShareAdvice(
                null, "Am Krankenversicherungsvertrag ist keine Selbstbeteiligung hinterlegt.");
        }

        // Zahnbehandlung zuerst: Sie hat ihren eigenen Satz und fällt aus dem Deckel heraus.
        if (dental && regel.DentalPercent is { } zahnsatz)
        {
            return new OwnShareAdvice(
                Anteil(gross, zahnsatz),
                $"Zahnbehandlung: {Prozent(zahnsatz)} von {GermanFormat.Euro(gross)} "
                + "— ohne Jahresdeckel.");
        }

        if (regel.Percent is not { } satz)
        {
            return new OwnShareAdvice(
                null, "Am Krankenversicherungsvertrag ist kein Prozentsatz hinterlegt.");
        }

        var voll = Anteil(gross, satz);

        if (regel.AnnualCap is not { } deckel)
        {
            return new OwnShareAdvice(
                voll, $"{Prozent(satz)} von {GermanFormat.Euro(gross)} — ohne Jahresdeckel.");
        }

        var offen = Math.Max(0m, deckel - regel.UsedThisYear);

        if (voll <= offen)
        {
            return new OwnShareAdvice(
                voll,
                $"{Prozent(satz)} von {GermanFormat.Euro(gross)}. Im Jahr {regel.Year} sind "
                + $"{GermanFormat.Euro(regel.UsedThisYear)} von {GermanFormat.Euro(deckel)} "
                + "verbraucht.");
        }

        // Der Deckel greift. Die Zahl, die er abschneidet, gehört in die Begründung — sonst
        // sieht der Vorschlag aus wie ein krummer Prozentsatz.
        return new OwnShareAdvice(
            offen,
            $"{Prozent(satz)} wären {GermanFormat.Euro(voll)}; der Jahresdeckel lässt nur noch "
            + $"{GermanFormat.Euro(offen)} zu ({GermanFormat.Euro(regel.UsedThisYear)} von "
            + $"{GermanFormat.Euro(deckel)} in {regel.Year} verbraucht).");
    }

    /// <summary>Kaufmännisch auf Cent — ein Eigenanteil wird bezahlt, nicht gerundet geschätzt.</summary>
    private static decimal Anteil(decimal gross, decimal satz)
        => Math.Round(gross * satz / 100m, 2, MidpointRounding.AwayFromZero);

    private static string Prozent(decimal satz)
        => satz == Math.Truncate(satz)
            ? $"{satz:0} %"
            : $"{satz:0.##} %".Replace('.', ',');
}
