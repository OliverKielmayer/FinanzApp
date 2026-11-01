using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Eigenanteil, wie ihn der Krankenversicherungsvertrag vorgibt.
/// </summary>
/// <remarks>
/// <para>Ein Prozentsatz bis zu einem Höchstbetrag im Jahr, und für Zahnbehandlung ein eigener
/// Satz ohne Deckel. Die Zahlen stehen am Vertrag und nicht hier: Welcher Tarif welche Regel
/// trägt, ist Sache des Haushalts, und ein zweiter mit anderem Tarif bekäme sonst eine falsche
/// Zahl vorgelegt — gerade die wäre glaubhaft, weil sie von selbst dasteht.</para>
/// <para>Die Sätze in diesen Tests sind deshalb ausgedacht.</para>
/// </remarks>
public sealed class OwnShareRuleTests
{
    /// <summary>Zwanzig Prozent, Deckel 500 € im Jahr, Zahnbehandlung 20 % ohne Deckel.</summary>
    private static OwnShareRuleDto Vertrag(decimal verbraucht = 0m)
        => new(Percent: 20m, AnnualCap: 500m, DentalPercent: 20m,
               UsedThisYear: verbraucht, Year: 2026, PolicyName: "Musterkasse");

    [Fact]
    public void Der_Satz_gilt_solange_der_Deckel_Luft_hat()
    {
        var rat = OwnShareRule.For(gross: 300m, dental: false, Vertrag());

        Assert.Equal(60m, rat.Amount);
        Assert.Contains("20 %", rat.Reason);
    }

    /// <summary>
    /// Der Deckel schneidet ab — und sagt, dass er es war.
    /// </summary>
    /// <remarks>
    /// Ohne die Begründung sähe der Vorschlag aus wie ein krummer Prozentsatz. Wer ihn prüft,
    /// soll sehen, dass nicht die Rechnung krumm ist, sondern der Rest des Jahresdeckels.
    /// </remarks>
    [Fact]
    public void Der_Deckel_schneidet_ab_und_sagt_es()
    {
        // 450 schon verbraucht, also sind von 500 noch 50 offen; 20 % von 1.000 wären 200.
        var rat = OwnShareRule.For(gross: 1000m, dental: false, Vertrag(verbraucht: 450m));

        Assert.Equal(50m, rat.Amount);
        Assert.Contains("Jahresdeckel", rat.Reason);
    }

    [Fact]
    public void Ein_ausgeschoepfter_Deckel_laesst_nichts_mehr_zu()
    {
        var rat = OwnShareRule.For(gross: 300m, dental: false, Vertrag(verbraucht: 500m));

        Assert.Equal(0m, rat.Amount);
    }

    /// <summary>
    /// Zahnbehandlung behält ihren Satz, auch wenn der Deckel längst voll ist.
    /// </summary>
    /// <remarks>
    /// Das ist der Grund, warum am Vorgang steht, ob er zahnärztlich war: Sonst ließe sich
    /// später nicht mehr ausrechnen, wie viel vom Jahresdeckel wirklich verbraucht ist.
    /// </remarks>
    [Fact]
    public void Zahnbehandlung_kennt_keinen_Deckel()
    {
        var rat = OwnShareRule.For(gross: 300m, dental: true, Vertrag(verbraucht: 500m));

        Assert.Equal(60m, rat.Amount);
        Assert.Contains("ohne Jahresdeckel", rat.Reason);
    }

    /// <summary>Ohne eigenen Zahnsatz gilt die allgemeine Regel samt Deckel.</summary>
    [Fact]
    public void Ohne_eigenen_Zahnsatz_gilt_die_allgemeine_Regel()
    {
        var ohne = Vertrag(verbraucht: 500m) with { DentalPercent = null };

        Assert.Equal(0m, OwnShareRule.For(gross: 300m, dental: true, ohne).Amount);
    }

    /// <summary>Kaufmännisch auf Cent — ein Eigenanteil wird bezahlt, nicht geschätzt.</summary>
    [Fact]
    public void Gerundet_wird_auf_Cent()
    {
        // 20 % von 123,45 sind 24,69 — der dritte Platz rundet auf.
        var rat = OwnShareRule.For(gross: 123.45m, dental: false, Vertrag());

        Assert.Equal(24.69m, rat.Amount);
    }

    /// <summary>
    /// Ohne gepflegte Regel wird nichts vorgeschlagen.
    /// </summary>
    /// <remarks>
    /// Null statt einer Zahl: Ein erfundener Eigenanteil wandert in eine Forderung gegen die
    /// Kasse, und dort fällt er erst auf, wenn sie weniger überweist als erwartet.
    /// </remarks>
    [Fact]
    public void Ohne_Regel_gibt_es_keinen_Vorschlag()
    {
        var rat = OwnShareRule.For(gross: 300m, dental: false, null);

        Assert.Null(rat.Amount);
        Assert.Contains("keine Selbstbeteiligung", rat.Reason);
    }

    [Fact]
    public void Ohne_Prozentsatz_gibt_es_keinen_Vorschlag()
    {
        var ohne = Vertrag() with { Percent = null, DentalPercent = null };

        Assert.Null(OwnShareRule.For(gross: 300m, dental: false, ohne).Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Ohne_Rechnungsbetrag_gibt_es_nichts_zu_rechnen(double brutto)
        => Assert.Null(OwnShareRule.For((decimal)brutto, dental: false, Vertrag()).Amount);

    /// <summary>Ohne Deckel bleibt es beim Satz, egal wie viel schon war.</summary>
    [Fact]
    public void Ohne_Deckel_bleibt_es_beim_Satz()
    {
        var ohne = Vertrag(verbraucht: 9999m) with { AnnualCap = null };

        Assert.Equal(60m, OwnShareRule.For(gross: 300m, dental: false, ohne).Amount);
    }
}
