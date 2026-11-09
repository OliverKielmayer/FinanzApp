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
    private static BankAccessOptions Produkt()
        => new() { ProductId = "ABC123DEF456GHI789JKL0123" };

    private static Account Konto() => new()
    {
        Name = "Musterbank Giro",
        ShortName = "Giro",
        BankName = "Musterbank",
        Kind = AccountKind.Checking,
        Iban = "DE66999999991234567890",
        Blz = "99999999",
        Bic = "MUSTDEFFXXX",
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

    /// <summary>
    /// Eine Bankleitzahl mit der falschen Stellenzahl fällt auf.
    /// </summary>
    /// <remarks>
    /// <para>Beim Abtippen entsteht das leicht — eine Ziffer zu viel, und niemand zählt nach.
    /// Gefunden wurde es an echten Daten: Ein Konto trug eine elfstellige Bankleitzahl, und
    /// der Abruf wäre daran mit einer Meldung zerbrochen, die von der Bankleitzahl kein Wort
    /// gesagt hätte — sie wird weiter unten als Zahl gebraucht, und eine elfstellige passt in
    /// keine.</para>
    /// <para>Die Meldung nennt die Stellenzahl und sagt, wo die richtige steht.</para>
    /// </remarks>
    [Theory]
    [InlineData("99999999000")]
    [InlineData("1234567")]
    [InlineData("1234 5678")]
    [InlineData("ABCDEFGH")]
    public void Eine_falsche_Bankleitzahl_faellt_auf(string blz)
    {
        var konto = Konto();
        konto.Blz = blz;

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));

        Assert.Contains("Bankleitzahl", problem, StringComparison.Ordinal);
        Assert.Contains("acht Ziffern", problem, StringComparison.Ordinal);
        Assert.Contains("Musterbank Giro", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne BIC lässt sich nicht abrufen.
    /// </summary>
    /// <remarks>
    /// <para><b>Am echten Konto gelernt, nicht aus der Spezifikation.</b> Der Abruf scheiterte
    /// mit „Nachrichtenaufbau fehlerhaft (Mussfeld). Nachricht nicht verarbeitbar.“ — einem
    /// Satz, der auf alles zeigt. Im mitgeschnittenen Dialog stand, worauf er zeigte: Das
    /// gesendete Segment benannte das Konto mit der IBAN und einem leeren zweiten Feld.</para>
    /// <para>Die Saldenabfrage nach FinTS 3.0 gibt das Konto als Kontoverbindung international
    /// an, und die ist IBAN <em>und</em> BIC. Dass FinTS die Bank über die Bankleitzahl
    /// anspricht, hat damit nichts zu tun — das eine benennt das Haus, das andere das
    /// Konto.</para>
    /// <para>Deshalb steht sie in der Mängelliste: Wer den Zugang einrichtet, soll sie
    /// eintragen, bevor der erste Abruf an einem Satz scheitert, der nichts erklärt.</para>
    /// </remarks>
    [Fact]
    public void Ohne_BIC_laesst_sich_nicht_abrufen()
    {
        var konto = Konto();
        konto.Bic = null;

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));

        Assert.Contains("fehlt die BIC", problem, StringComparison.Ordinal);
        Assert.Contains("Kontoauszug", problem, StringComparison.Ordinal);
        Assert.Contains("Musterbank Giro", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine BIC mit der falschen Zeichenzahl fällt auf.
    /// </summary>
    /// <remarks>
    /// <para><b>Am echten Konto passiert, und es hat einen Abend gekostet.</b> Dort stand eine
    /// BIC mit zwölf Zeichen — ein Buchstabe zu viel beim Abtippen. Die Bank wies den Abruf
    /// daraufhin mit „Ungültige Auftragsnachricht: Unbekannter Aufbau“ ab, einem Satz, der von
    /// der BIC kein Wort sagt; das Feld in der FinTS-Nachricht fasst elf Zeichen, und ein
    /// zwölftes sprengt den Aufbau. Gesucht wurde daraufhin in der Bibliothek, in der
    /// Segmentfassung und im Dialogablauf — überall, nur nicht im Feld, das kurz zuvor neu
    /// eingetragen worden war.</para>
    /// <para>Nach ISO 9362 hat eine BIC acht oder elf Zeichen. Neun, zehn oder zwölf gibt es
    /// nicht. Dieselbe Prüfung gab es für die Bankleitzahl längst, aus demselben Anlass.</para>
    /// </remarks>
    [Theory]
    [InlineData("MUSTDEFFXXXX")]
    [InlineData("MUSTDEFFX")]
    [InlineData("MUSTDEF")]
    [InlineData("MUST DEFF")]
    [InlineData("MUSTDEFF-XX")]
    public void Eine_falsche_BIC_faellt_auf(string bic)
    {
        var konto = Konto();
        konto.Bic = bic;

        var problem = Assert.Single(BankAccessReadiness.Problems(konto, Produkt()));

        Assert.Contains("BIC", problem, StringComparison.Ordinal);
        Assert.Contains("acht oder elf", problem, StringComparison.Ordinal);
        Assert.Contains("Musterbank Giro", problem, StringComparison.Ordinal);
    }

    /// <summary>Acht Zeichen reichen — die Filiale ist freiwillig.</summary>
    [Theory]
    [InlineData("MUSTDEFF")]
    [InlineData("MUSTDEFFXXX")]
    public void Eine_richtige_BIC_geht_durch(string bic)
    {
        var konto = Konto();
        konto.Bic = bic;

        Assert.Empty(BankAccessReadiness.Problems(konto, Produkt()));
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

    /// <summary>
    /// Eine zu lange Nummer fällt hier auf und nicht erst bei der Bank.
    /// </summary>
    /// <remarks>
    /// Das Feld in der Dialogeröffnung fasst 25 Zeichen. Wer eine längere einträgt, bekommt von
    /// der Bank eine Meldung über ein ungültiges Segment — und sucht den Fehler überall, nur
    /// nicht in der Länge einer Nummer, die er abgetippt hat.
    /// </remarks>
    [Fact]
    public void Eine_zu_lange_Registrierungsnummer_faellt_auf()
    {
        var zuLang = new BankAccessOptions { ProductId = new string('A', 26) };

        var problem = Assert.Single(BankAccessReadiness.Problems(Konto(), zuLang));

        Assert.Contains("26 Zeichen", problem, StringComparison.Ordinal);
        Assert.Contains("höchstens 25", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ein Leerzeichen in der Nummer ebenso.
    /// </summary>
    /// <remarks>
    /// Beim Abtippen aus einem Brief entsteht es von selbst — die Nummern stehen dort in
    /// Vierergruppen. Die Bank weist den Dialog ab, ohne zu sagen, warum.
    /// </remarks>
    [Fact]
    public void Ein_Leerzeichen_in_der_Nummer_faellt_auf()
    {
        var mitLuecke = new BankAccessOptions { ProductId = "ABC1 23DE F456 GHI7 89JK" };

        var problem = Assert.Single(BankAccessReadiness.Problems(Konto(), mitLuecke));

        Assert.Contains("Leerzeichen", problem, StringComparison.Ordinal);
    }

    /// <summary>Die Nummer selbst steht in keiner Meldung.</summary>
    /// <remarks>
    /// Sie ist kein Geheimnis, aber sie gehört dem Betreiber und niemandem sonst vor die Augen.
    /// Die Meldung nennt die Länge; das genügt, um den Fehler zu finden.
    /// </remarks>
    [Fact]
    public void Die_Meldung_zeigt_die_Nummer_nicht()
    {
        var zuLang = new BankAccessOptions { ProductId = new string('A', 30) };

        var problem = Assert.Single(BankAccessReadiness.Problems(Konto(), zuLang));

        Assert.DoesNotContain(zuLang.ProductId, problem, StringComparison.Ordinal);
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
