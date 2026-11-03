using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Die Einstellungen des Bankabrufs.
/// </summary>
/// <remarks>
/// <para>Eine Optionsklasse, die man testet, weil ihre Meldungen gelesen werden — und zwar beim
/// Einrichten, von jemandem mit dem Zugangsdatenblatt der Bank in der Hand. Eine Meldung, die
/// „der Bankzugang ist unvollständig“ sagt, hilft dort nicht.</para>
/// <para>Die Werte hier sind erfunden.</para>
/// </remarks>
public sealed class BankAccessOptionsTests
{
    private static BankLoginOptions Zugang() => new()
    {
        Name = "Musterbank",
        Blz = "12345678",
        Url = "https://banking.example.test/fints30",
        UserId = "mustermann1",
        TanMethod = 921,
        Pin = "steht-in-user-secrets",
        Ibans = ["DE02120300000000202051"],
    };

    // ── Nicht eingerichtet ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein leerer Abschnitt ist kein Fehler.
    /// </summary>
    /// <remarks>
    /// Die Anwendung lief immer ohne Bankabruf und tut es weiter: Kontoauszüge lassen sich als
    /// Datei einlesen. Wer den Abruf nicht will, soll bei jedem Start nicht daran erinnert
    /// werden.
    /// </remarks>
    [Fact]
    public void Ohne_Eintrag_gibt_es_nichts_zu_melden()
    {
        var options = new BankAccessOptions();

        Assert.False(options.IsConfigured);
        Assert.Empty(options.Problems());
    }

    /// <summary>Ein Eintrag, in dem nichts steht, zählt nicht als Zugang.</summary>
    /// <remarks>
    /// Eine leere Vorlage in der Datei ist eine Einladung zum Ausfüllen, keine halbe
    /// Einrichtung. Sie zu melden hieße, jemanden für etwas zu rügen, das er noch gar nicht
    /// angefangen hat.
    /// </remarks>
    [Fact]
    public void Ein_leerer_Bankeintrag_zaehlt_nicht()
    {
        var options = new BankAccessOptions { Banken = [new BankLoginOptions()] };

        Assert.False(options.IsConfigured);
        Assert.Empty(options.Problems());
        Assert.Empty(options.Konfigurierte);
    }

    // ── Halb eingerichtet ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fehlt ein Feld, wird es einzeln und mit vollem Schlüssel genannt.
    /// </summary>
    /// <remarks>
    /// „Bankzugang:Banken:0:Url“ sagt, wo zu suchen ist. „Der Zugang ist unvollständig“ sagt
    /// es nicht, und dann sucht man am falschen Ende.
    /// </remarks>
    [Fact]
    public void Ein_fehlendes_Feld_wird_mit_seinem_Schluessel_genannt()
    {
        var zugang = Zugang();
        zugang.Url = string.Empty;

        var options = new BankAccessOptions { ProductId = "ABC123", Banken = [zugang] };

        var problem = Assert.Single(options.Problems());
        Assert.Contains("Bankzugang:Banken:0:Url", problem, StringComparison.Ordinal);
        Assert.Contains("Musterbank", problem, StringComparison.Ordinal);
    }

    /// <summary>Jedes fehlende Feld einzeln, nicht eines für alle.</summary>
    /// <remarks>
    /// Wer drei Meldungen bekommt, trägt drei Dinge nach. Wer eine bekommt, trägt eines nach
    /// und startet wieder.
    /// </remarks>
    [Fact]
    public void Mehrere_Luecken_ergeben_mehrere_Meldungen()
    {
        var zugang = Zugang();
        zugang.Blz = string.Empty;
        zugang.UserId = string.Empty;
        zugang.Ibans = [];

        var options = new BankAccessOptions { ProductId = "ABC123", Banken = [zugang] };
        var probleme = options.Problems();

        Assert.Equal(3, probleme.Count);
        Assert.Contains(probleme, p => p.Contains(":Blz", StringComparison.Ordinal));
        Assert.Contains(probleme, p => p.Contains(":UserId", StringComparison.Ordinal));
        Assert.Contains(probleme, p => p.Contains(":Ibans", StringComparison.Ordinal));
    }

    /// <summary>
    /// Die fehlende PIN sagt gleich, wo sie hingehört.
    /// </summary>
    /// <remarks>
    /// Sie steht nicht in der Datei, und wer sie dort sucht, sucht vergeblich. Eine Meldung, die
    /// nur „Pin ist leer“ sagt, führt geradewegs dazu, dass jemand sie doch hineinschreibt.
    /// </remarks>
    [Fact]
    public void Die_fehlende_Pin_nennt_den_richtigen_Ort()
    {
        var zugang = Zugang();
        zugang.Pin = string.Empty;

        var options = new BankAccessOptions { ProductId = "ABC123", Banken = [zugang] };

        var problem = Assert.Single(options.Problems());
        Assert.Contains("user-secrets", problem, StringComparison.Ordinal);
        Assert.Contains("Bankzugang__Banken__0__Pin", problem, StringComparison.Ordinal);
        Assert.Contains("nicht in appsettings.json", problem, StringComparison.Ordinal);
    }

    /// <summary>Ohne Registrierungsnummer weist die Bank den Dialog ab — das wird gesagt.</summary>
    [Fact]
    public void Die_fehlende_Registrierungsnummer_wird_gemeldet()
    {
        var options = new BankAccessOptions { Banken = [Zugang()] };

        var problem = Assert.Single(options.Problems());
        Assert.Contains("Bankzugang:ProductId", problem, StringComparison.Ordinal);
    }

    // ── Vollständig ────────────────────────────────────────────────────────────────────────

    /// <summary>Ist alles da, schweigt die Prüfung.</summary>
    [Fact]
    public void Ein_vollstaendiger_Zugang_meldet_nichts()
    {
        var options = new BankAccessOptions { ProductId = "ABC123", Banken = [Zugang()] };

        Assert.True(options.IsConfigured);
        Assert.Empty(options.Problems());
        Assert.Single(options.Konfigurierte);
    }

    /// <summary>
    /// Ohne eigene Kunden-ID gilt die Benutzerkennung.
    /// </summary>
    /// <remarks>
    /// Bei den meisten Banken sind beide gleich. Das Feld leer zu lassen ist deshalb richtig und
    /// kein Mangel — die Kennung zu verdoppeln wäre der Fehler, denn wer sie später ändert,
    /// änderte sonst nur eine von beiden.
    /// </remarks>
    [Fact]
    public void Ohne_Kunden_ID_gilt_die_Benutzerkennung()
    {
        var zugang = Zugang();
        zugang.CustomerId = string.Empty;

        Assert.Equal("mustermann1", zugang.EffectiveCustomerId);
        Assert.Empty(new BankAccessOptions { ProductId = "ABC123", Banken = [zugang] }.Problems());
    }

    /// <summary>Steht eine eigene da, gilt sie.</summary>
    [Fact]
    public void Eine_eigene_Kunden_ID_gilt()
    {
        var zugang = Zugang();
        zugang.CustomerId = "kunde-4711";

        Assert.Equal("kunde-4711", zugang.EffectiveCustomerId);
    }

    /// <summary>Der zweite Zugang wird mit seiner eigenen Nummer genannt.</summary>
    /// <remarks>
    /// Zwei Banken sind der Normalfall, und eine Meldung ohne Index schickte zum ersten Eintrag.
    /// </remarks>
    [Fact]
    public void Der_zweite_Zugang_traegt_seinen_eigenen_Index()
    {
        var zweiter = Zugang();
        zweiter.Name = "Musterkasse";
        zweiter.TanMethod = 0;

        var options = new BankAccessOptions
        {
            ProductId = "ABC123",
            Banken = [Zugang(), zweiter],
        };

        var problem = Assert.Single(options.Problems());
        Assert.Contains("Bankzugang:Banken:1:TanMethod", problem, StringComparison.Ordinal);
        Assert.Contains("Musterkasse", problem, StringComparison.Ordinal);
    }
}
