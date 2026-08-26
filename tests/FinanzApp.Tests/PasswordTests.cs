using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Passwortbewertung.
/// </summary>
/// <remarks>
/// Der Anlass war ein echter Fehlgriff: ein bekanntes Wort wurde als <em>Teilzeichenkette</em>
/// gesucht und führte sofort zu „zu schwach“. Damit fiel „Neues-Passwort-2026!“ durch — zwanzig
/// Zeichen, vier Zeichenarten — nur weil das Wort „Passwort“ darin vorkam. Ein bekanntes Wort
/// darf ein Passwort verbilligen, nicht verbieten.
/// </remarks>
public sealed class PasswordTests
{
    [Theory]
    [InlineData("Neues-Passwort-2026!")]
    [InlineData("Mein Passwort ist lang genug!")]
    [InlineData("Demo-Haushalt-2026!")]
    public void Ein_langes_Passwort_faellt_nicht_wegen_eines_Wortes_durch(string password)
        => Assert.True(PasswordPolicy.IsAcceptable(password), password);

    [Theory]
    [InlineData("passwort")]
    [InlineData("passwort123")]
    [InlineData("Passwort1")]
    [InlineData("qwertz123456")]
    public void Wo_das_bekannte_Wort_das_Passwort_ist_bleibt_es_zu_schwach(string password)
        => Assert.Equal(PasswordStrength.TooWeak, PasswordPolicy.Evaluate(password));

    [Fact]
    public void Ein_bekanntes_Wort_kostet_eine_Stufe()
    {
        // Zwei gleich lange Passwörter, die sich nur im bekannten Wort unterscheiden. Bewusst
        // im mittleren Bereich gewählt: ganz oben deckelt die Skala und der Unterschied wäre
        // nicht mehr sichtbar.
        var withWord = PasswordPolicy.Evaluate("hallo-welt-12");
        var without = PasswordPolicy.Evaluate("hedge-welt-12");

        Assert.Equal(without - 1, withWord);

        // Und trotzdem tauglich — verbilligt, nicht verboten.
        Assert.True(PasswordPolicy.IsAcceptable("hallo-welt-12"));
    }

    [Theory]
    [InlineData(null, "Bitte ein Passwort eingeben.")]
    [InlineData("", "Bitte ein Passwort eingeben.")]
    public void Ohne_Eingabe_sagt_die_Meldung_genau_das(string? password, string expected)
        => Assert.Equal(expected, PasswordPolicy.Reject(password));

    [Fact]
    public void Zu_kurz_nennt_die_Laenge_statt_zu_raten()
    {
        var problem = PasswordPolicy.Reject("Ab1!xY");

        // Nicht „zu schwach oder kürzer als 12 Zeichen“ — der Benutzer soll nicht raten müssen,
        // welches von beidem gemeint ist.
        Assert.Contains("6 Zeichen lang", problem);
        Assert.Contains("mindestens 12", problem);
        Assert.DoesNotContain("zu schwach", problem);
    }

    [Fact]
    public void Zu_schwach_nennt_die_Schwaeche_statt_die_Laenge()
    {
        var problem = PasswordPolicy.Reject("aaaaaaaaaaaaaaaa");

        Assert.Contains("zu schwach", problem);
        Assert.DoesNotContain("Zeichen lang", problem);
    }

    [Fact]
    public void Ein_taugliches_Passwort_hat_keinen_Grund()
        => Assert.Null(PasswordPolicy.Reject("Neues-Passwort-2026!"));
}
