using System.Net;
using System.Net.Sockets;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Was dasteht, wenn die Bank gar nicht erst antwortet.
/// </summary>
/// <remarks>
/// <para>Der Dialog selbst ist nicht prüfbar — der Satz, den jemand im Fehlerfall liest, schon.
/// Und der ist im Fehlerfall das Einzige, was bleibt.</para>
/// <para>Die Zahlen und Namen sind erfunden.</para>
/// </remarks>
public sealed class BankAccessFailureTests
{
    private const string Adresse = "https://fints.example.invalid/fints30";

    /// <summary>So kommt es aus libfintx heraus, wenn der Rechner nicht auflösbar ist.</summary>
    private static Exception Leitungsfehler(string meldung = "Der angegebene Host ist unbekannt. (fints.example.invalid:443)")
        => new InvalidOperationException(
            "Fehler beim Versenden der HBCI-Nachricht.",
            new WebException(meldung, new SocketException((int)SocketError.HostNotFound)));

    /// <summary>
    /// Eine Bank, die nicht antwortet, wird als Verbindungsfehler benannt — nicht als
    /// Serverfehler und nicht als Sache der PIN.
    /// </summary>
    /// <remarks>
    /// Wer eine Fehlermeldung über einen Abruf liest, denkt zuerst an die PIN und tippt sie
    /// neu. Drei solche Versuche sperren den Zugang — für etwas, das an der Leitung lag.
    /// </remarks>
    [Fact]
    public void Eine_unerreichbare_Bank_wird_als_Leitung_benannt()
    {
        var meldung = BankAccessFailure.Describe(Leitungsfehler(), Adresse);

        Assert.Contains("nicht zu erreichen", meldung, StringComparison.Ordinal);
        Assert.Contains("Der angegebene Host ist unbekannt", meldung, StringComparison.Ordinal);
        Assert.Contains("kein Fehler der PIN", meldung, StringComparison.Ordinal);
    }

    /// <summary>Die Adresse am Konto steht dabei — sie ist das, was sich nachsehen lässt.</summary>
    [Fact]
    public void Die_Adresse_am_Konto_steht_dabei()
    {
        Assert.Contains(Adresse, BankAccessFailure.Describe(Leitungsfehler(), Adresse), StringComparison.Ordinal);
    }

    /// <summary>Ohne Adresse bleibt der Rest der Meldung trotzdem stehen.</summary>
    [Fact]
    public void Ohne_Adresse_bleibt_die_Meldung_brauchbar()
    {
        var meldung = BankAccessFailure.Describe(Leitungsfehler(), null);

        Assert.Contains("nicht zu erreichen", meldung, StringComparison.Ordinal);
        Assert.DoesNotContain("„“", meldung, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne Netzfehler wird nicht behauptet, die Bank sei unerreichbar gewesen.
    /// </summary>
    /// <remarks>
    /// Eine Bibliothek kann auch an einer Antwort scheitern, die sie nicht versteht. Dann war
    /// die Bank sehr wohl zu erreichen, und wer die Leitung prüft, sucht am falschen Ort.
    /// </remarks>
    [Fact]
    public void Ohne_Netzfehler_wird_keine_Unerreichbarkeit_behauptet()
    {
        var meldung = BankAccessFailure.Describe(
            new InvalidOperationException("Unerwartetes Segment."), Adresse);

        Assert.DoesNotContain("nicht zu erreichen", meldung, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), meldung, StringComparison.Ordinal);
    }

    /// <summary>
    /// Und es wird auch nicht behauptet, die Bank habe nicht geantwortet.
    /// </summary>
    /// <remarks>
    /// <para>Genau das stand hier einmal: „Der Abruf ist abgebrochen, bevor die Bank
    /// geantwortet hat.“ Der Satz galt für jede Ausnahme, die kein Netzfehler war — also auch
    /// dann, wenn die Bank sehr wohl geantwortet und den Dialog zurückgewiesen hatte.</para>
    /// <para>Am echten Konto hat er die Suche in die falsche Richtung geschickt: geprüft
    /// wurden Leitung, Adresse und Zugangsdaten, während die Antwort der Bank im Protokoll
    /// stand und sagte, woran es lag.</para>
    /// </remarks>
    [Fact]
    public void Ohne_Netzfehler_wird_auch_kein_Schweigen_der_Bank_behauptet()
    {
        var meldung = BankAccessFailure.Describe(
            new InvalidOperationException("Unerwartetes Segment."), Adresse);

        Assert.DoesNotContain("bevor die Bank geantwortet", meldung, StringComparison.Ordinal);
    }

    // ── Wenn die Bank das TAN-Verfahren nicht anbietet ─────────────────────────────────────

    /// <summary>
    /// Aus der Zurückweisung wird ein Satz, der sagt, was zu tun ist.
    /// </summary>
    /// <remarks>
    /// <para>Die Bibliothek wirft das als <see cref="InvalidOperationException"/> — dieselbe
    /// Art, die auch ein Verbindungsabbruch trägt. Ohne diesen Blick stünde am Bildschirm, der
    /// Abruf sei gescheitert, und die eine Zahl, die ihn flottmacht, bliebe im Protokoll
    /// liegen.</para>
    /// <para>Die eigentliche Meldung steckt in der inneren Ausnahme; gesucht wird deshalb über
    /// die ganze Kette.</para>
    /// </remarks>
    [Fact]
    public void Ein_abgelehntes_TAN_Verfahren_nennt_das_erlaubte()
    {
        var meldung = BankAccessFailure.Describe(Zurueckgewiesen("901", "972"), Adresse);

        Assert.Contains("901", meldung, StringComparison.Ordinal);
        Assert.Contains("972", meldung, StringComparison.Ordinal);
        Assert.Contains("am Konto", meldung, StringComparison.Ordinal);
        Assert.DoesNotContain("nicht zu erreichen", meldung, StringComparison.Ordinal);
    }

    /// <summary>Mehrere erlaubte Verfahren werden alle genannt.</summary>
    [Fact]
    public void Mehrere_erlaubte_Verfahren_werden_alle_genannt()
    {
        var meldung = BankAccessFailure.Describe(Zurueckgewiesen("901", "972, 944"), Adresse);

        Assert.Contains("972, 944", meldung, StringComparison.Ordinal);
        Assert.Contains("Erlaubt sind", meldung, StringComparison.Ordinal);
    }

    /// <summary>
    /// Auch hier kommt kein fremder Text mit — nur die Ziffern.
    /// </summary>
    /// <remarks>
    /// Das ist die Bedingung, unter der dieser eine Satz überhaupt aus einer fremden Meldung
    /// entstehen darf: Genommen wird, was zwei enge Muster hergeben, und sonst nichts. Hängt
    /// die Bibliothek die Nachricht an, an der sie gescheitert ist, bleibt sie draussen.
    /// </remarks>
    [Fact]
    public void Aus_der_Zurueckweisung_kommt_kein_fremder_Text_mit()
    {
        const string pin = "987654";

        var fehler = new InvalidOperationException(
            "Fehler beim Zerlegen.",
            new Exception(
                $"Invalid HIRMS/Tan-Mode 901 detected. Please choose one of the allowed "
                + $"modes: 972 HNSHA:6:2+1234++{pin}'"));

        var meldung = BankAccessFailure.Describe(fehler, Adresse);

        Assert.Contains("972", meldung, StringComparison.Ordinal);
        Assert.DoesNotContain(pin, meldung, StringComparison.Ordinal);
        Assert.DoesNotContain("HNSHA", meldung, StringComparison.Ordinal);
        Assert.DoesNotContain("Zerlegen", meldung, StringComparison.Ordinal);
    }

    /// <summary>Die Zurückweisung, so wie die Bibliothek sie wirft.</summary>
    private static InvalidOperationException Zurueckgewiesen(string genannt, string erlaubt)
        => new(
            "Fehler beim Zerlegen der Antwort.",
            new Exception(
                $"Invalid HIRMS/Tan-Mode {genannt} detected. Please choose one of the "
                + $"allowed modes: {erlaubt}"));

    /// <summary>
    /// Was die Bibliothek in ihre Ausnahme schreibt, kommt nicht in die Meldung.
    /// </summary>
    /// <remarks>
    /// <b>Das ist der Grund für die ganze Übersetzung.</b> libfintx hängt im Fehlerfall gern
    /// die Nachricht an, an der es gescheitert ist — und in einer FinTS-Nachricht steht die
    /// PIN. Übernommen wird deshalb nur, was von der Leitung spricht: Rechnername, Port,
    /// Zeitüberschreitung. Dort kann keine PIN stehen.
    /// </remarks>
    [Fact]
    public void Die_PIN_aus_der_Ausnahme_steht_nicht_in_der_Meldung()
    {
        const string pin = "987654";

        var mitNetz = new InvalidOperationException(
            $"Fehler beim Versenden: HNHBK:1:3+000000000123+300+DDDDD+1'HKVVB...PIN={pin}'",
            new WebException("Der angegebene Host ist unbekannt. (fints.example.invalid:443)"));

        var ohneNetz = new InvalidOperationException($"Unerwartete Antwort auf PIN={pin}.");

        Assert.DoesNotContain(pin, BankAccessFailure.Describe(mitNetz, Adresse), StringComparison.Ordinal);
        Assert.DoesNotContain(pin, BankAccessFailure.Describe(ohneNetz, Adresse), StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Kette fürs Protokoll nennt die Arten und keinen einzigen Text.
    /// </summary>
    /// <remarks>
    /// Sie steht dort, wo sonst die Ausnahme selbst stünde. Nähme sie deren Texte mit, wäre
    /// nichts gewonnen: Die PIN landete auf der Platte, nur eine Zeile tiefer.
    /// </remarks>
    [Fact]
    public void Die_Kette_fuers_Protokoll_nennt_nur_die_Arten()
    {
        const string pin = "987654";

        var kette = BankAccessFailure.TypeChain(new InvalidOperationException(
            $"HKVVB...PIN={pin}", new WebException("Zeitüberschreitung")));

        Assert.Equal("InvalidOperationException <- WebException", kette);
        Assert.DoesNotContain(pin, kette, StringComparison.Ordinal);
        Assert.DoesNotContain("Zeitüberschreitung", kette, StringComparison.Ordinal);
    }
}
