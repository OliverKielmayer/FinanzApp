using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Ob ein Konto über FinTS abgefragt werden kann.
/// </summary>
/// <remarks>
/// <para>Der Zugang steht am Konto, nicht in der Konfigurationsdatei: Bank und IBAN stehen dort
/// ohnehin, und ein Zugang beschreibt genau dieses Konto bei genau dieser Bank. In der
/// Konfiguration bleibt nur, was das <em>Programm</em> bezeichnet — die
/// Produkt-Registrierungsnummer, für alle Konten dieselbe.</para>
/// <para>Geprüft wird, wenn jemand abruft, und nicht beim Start. Ein Konto ohne Bankzugang ist
/// kein Mangel: Kontoauszüge lassen sich als Datei einlesen, und so lief es immer.</para>
/// <para>Die Werte hier sind erfunden.</para>
/// </remarks>
public sealed class BankAccessOptionsTests
{
    private static BankAccessOptions Produkt() => new() { ProductId = "ABC123" };

    private static Account Konto() => new()
    {
        Name = "Musterbank Giro",
        ShortName = "Giro",
        BankName = "Musterbank",
        Kind = AccountKind.Checking,
        Iban = "DE02120300000000202051",
        Blz = "12345678",
        FinTsUrl = "https://banking.example.test/fints30",
        FinTsUserId = "mustermann1",
        FinTsTanMethod = 921,
    };

    // ── Kein Zugang ist kein Mangel ────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Konto ohne Bankzugang wird nur gemeldet, wenn jemand es abrufen will.
    /// </summary>
    /// <remarks>
    /// Die Meldung sagt dann auch, wo die Angaben herkommen — aus dem Zugangsdatenblatt der
    /// Bank. Ohne diesen Hinweis sucht man sie im Online-Banking, wo sie nicht stehen.
    /// </remarks>
    [Fact]
    public void Ein_Konto_ohne_Zugang_sagt_woher_die_Angaben_kommen()
    {
        var konto = Konto();
        konto.Blz = null;
        konto.FinTsUrl = null;
        konto.FinTsUserId = null;
        konto.FinTsTanMethod = null;

        Assert.False(konto.HasBankAccess);

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));
        Assert.Contains("kein Bankzugang hinterlegt", problem, StringComparison.Ordinal);
        Assert.Contains("Zugangsdatenblatt", problem, StringComparison.Ordinal);
    }

    // ── Halb eingetragen ───────────────────────────────────────────────────────────────────

    /// <summary>Jedes fehlende Feld einzeln, mit dem Namen des Kontos.</summary>
    /// <remarks>
    /// Wer drei Meldungen bekommt, trägt drei Dinge nach. Wer eine bekommt, trägt eines nach
    /// und versucht es wieder.
    /// </remarks>
    [Fact]
    public void Mehrere_Luecken_ergeben_mehrere_Meldungen()
    {
        var konto = Konto();
        konto.FinTsUrl = null;
        konto.FinTsUserId = null;

        var probleme = BankAccessReadiness.Problems(konto, Produkt());

        Assert.Equal(2, probleme.Count);
        Assert.Contains(probleme, p => p.Contains("FinTS-Adresse", StringComparison.Ordinal));
        Assert.Contains(probleme, p => p.Contains("Benutzerkennung", StringComparison.Ordinal));
        Assert.All(probleme, p => Assert.Contains("Musterbank Giro", p, StringComparison.Ordinal));
    }

    /// <summary>
    /// Ohne IBAN geht es nicht — auch wenn sie sonst freiwillig ist.
    /// </summary>
    /// <remarks>
    /// An ihr erkennt die Bank, welches ihrer Konten gemeint ist. Ein Zugang ohne sie wäre eine
    /// Anmeldung ohne Frage.
    /// </remarks>
    [Fact]
    public void Ohne_IBAN_laesst_sich_nicht_abrufen()
    {
        var konto = Konto();
        konto.Iban = null;

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));
        Assert.Contains("fehlt die IBAN", problem, StringComparison.Ordinal);
    }

    /// <summary>Ein TAN-Verfahren von null ist keines.</summary>
    [Fact]
    public void Ein_fehlendes_TAN_Verfahren_faellt_auf()
    {
        var konto = Konto();
        konto.FinTsTanMethod = 0;

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));
        Assert.Contains("TAN-Verfahren", problem, StringComparison.Ordinal);
        Assert.Contains("921", problem, StringComparison.Ordinal);
    }

    // ── Die Produktangabe ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fehlt die Registrierungsnummer, nützt der schönste Kontozugang nichts.
    /// </summary>
    /// <remarks>
    /// Sie steht in der Konfigurationsdatei und gilt für alle Konten — die Meldung sagt das,
    /// damit niemand sie am Konto sucht.
    /// </remarks>
    [Fact]
    public void Ohne_Registrierungsnummer_geht_gar_nichts()
    {
        var problem = Assert.Single(
            BankAccessReadiness.Problems(Konto(), new BankAccessOptions()));

        Assert.Contains("Bankzugang:ProductId", problem, StringComparison.Ordinal);
        Assert.Contains("appsettings.json", problem, StringComparison.Ordinal);
        Assert.Contains("für alle Konten", problem, StringComparison.Ordinal);
    }

    // ── Vollständig ────────────────────────────────────────────────────────────────────────

    /// <summary>Ist alles da, schweigt die Prüfung.</summary>
    [Fact]
    public void Ein_vollstaendiger_Zugang_laesst_sich_abrufen()
    {
        Assert.True(BankAccessReadiness.CanQuery(Konto(), Produkt()));
        Assert.Empty(BankAccessReadiness.Problems(Konto(), Produkt()));
    }

    /// <summary>
    /// Ohne eigene Kunden-ID gilt die Benutzerkennung.
    /// </summary>
    /// <remarks>
    /// Bei den meisten Banken sind beide gleich. Das Feld leer zu lassen ist deshalb richtig —
    /// die Kennung zu verdoppeln wäre der Fehler, denn wer sie später ändert, änderte sonst nur
    /// eine von beiden.
    /// </remarks>
    [Fact]
    public void Ohne_Kunden_ID_gilt_die_Benutzerkennung()
    {
        var konto = Konto();

        Assert.Null(konto.FinTsCustomerId);
        Assert.Equal("mustermann1", konto.EffectiveFinTsCustomerId);
    }

    /// <summary>Steht eine eigene da, gilt sie.</summary>
    [Fact]
    public void Eine_eigene_Kunden_ID_gilt()
    {
        var konto = Konto();
        konto.FinTsCustomerId = "kunde-7788";

        Assert.Equal("kunde-7788", konto.EffectiveFinTsCustomerId);
    }

    /// <summary>Die Produktangabe allein trägt keinen Abruf.</summary>
    /// <remarks>
    /// Sie sagt, welche Software anklopft — nicht, bei wem. Ohne Konto bleibt sie folgenlos,
    /// und das ist der Normalfall: Die Nummer steht in der Datei, der Zugang vielleicht nie.
    /// </remarks>
    [Fact]
    public void Die_Produktangabe_allein_ist_folgenlos()
    {
        Assert.True(Produkt().IsConfigured);
        Assert.False(new BankAccessOptions().IsConfigured);
    }
}
