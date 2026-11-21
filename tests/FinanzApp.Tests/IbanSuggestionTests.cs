using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// IBANs aus einem Fließtext holen.
/// </summary>
/// <remarks>
/// <para><b>Auf einer Rechnung steht mehr, was wie eine IBAN aussieht, als IBANs.</b> Die
/// Gläubiger-Identifikation einer Lastschrift beginnt mit Ländercode und zwei Ziffern
/// (<c>DE98ZZZ…</c>), eine maskierte eigene IBAN sieht ebenso aus. Beide fallen an der
/// Prüfziffer durch — und das ist der einzige Filter, der hier arbeitet.</para>
/// <para>Die IBANs sind erfunden und tragen aufgehende Prüfziffern; sie gehören keinem.</para>
/// </remarks>
public sealed class IbanSuggestionTests
{
    /// <summary>Erfundene IBANs mit richtiger Prüfziffer.</summary>
    private const string Eine = "DE02120300000000202051";

    private const string Zweite = "DE02100500000054540402";

    // ── Die Prüfziffer als Filter ────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Gläubiger-Identifikation ist keine IBAN.
    /// </summary>
    /// <remarks>
    /// Sie steht auf jeder Rechnung mit Lastschrifteinzug, beginnt wie eine IBAN und hat
    /// achtzehn Zeichen. Eine Regel, die den ersten Treffer nimmt, überweist dorthin.
    /// </remarks>
    [Fact]
    public void Eine_Glaeubiger_ID_wird_nicht_vorgeschlagen()
    {
        var text = "Gläubiger-ID: DE98ZZZ09999999999. Sorgen Sie bitte für Kontodeckung.";

        Assert.Empty(IbanCheck.FindAll(text));
    }

    /// <summary>Eine maskierte IBAN ebenso wenig.</summary>
    /// <remarks>
    /// Auf dem Beleg steht sie als Nachweis, von welchem Konto eingezogen wird — mit X statt
    /// der mittleren Stellen. Als Ziel einer Überweisung wäre sie unbrauchbar.
    /// </remarks>
    [Fact]
    public void Eine_maskierte_IBAN_wird_nicht_vorgeschlagen()
    {
        Assert.Empty(IbanCheck.FindAll("Konto XX1234500000XXXXXX789 wird belastet."));
    }

    [Fact]
    public void Eine_richtige_IBAN_wird_gefunden()
    {
        var text = $"Bitte überweisen Sie auf {Eine} bei der Musterbank.";

        Assert.Equal([Eine], IbanCheck.FindAll(text));
    }

    /// <summary>Auch in Vierergruppen, so wie sie auf dem Papier steht.</summary>
    [Fact]
    public void Eine_IBAN_in_Vierergruppen_wird_gefunden()
    {
        Assert.Equal([Eine], IbanCheck.FindAll("IBAN DE02 1203 0000 0000 2020 51"));
    }

    // ── Mehrere, und keine doppelt ───────────────────────────────────────────────────────

    /// <summary>
    /// Zwei verschiedene IBANs bleiben zwei — in der Reihenfolge des Papiers.
    /// </summary>
    /// <remarks>
    /// Eine andere Sortierung wäre eine Behauptung darüber, welche die richtige ist. Wer zwei
    /// Vorschläge sieht, soll sie dort finden, wo sie auf dem Blatt stehen.
    /// </remarks>
    [Fact]
    public void Zwei_IBANs_bleiben_zwei_in_der_Reihenfolge_des_Blattes()
    {
        var text = $"Zahlungen an {Eine}; Rücklastschriften an {Zweite}.";

        Assert.Equal([Eine, Zweite], IbanCheck.FindAll(text));
    }

    [Fact]
    public void Dieselbe_IBAN_zweimal_ist_ein_Vorschlag()
    {
        var text = $"IBAN {Eine} — bitte bei Zahlungen {Eine} angeben.";

        Assert.Equal([Eine], IbanCheck.FindAll(text));
    }

    /// <summary>
    /// Die IBAN wird gefunden, auch wenn die BIC direkt daneben steht.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall, an dem es aufgefallen ist. Ein Briefbogen führt die Bankverbindung als
    /// Tabellenzeile: Bank, IBAN, BIC — nebeneinander. Die BIC besteht auch nur aus
    /// Großbuchstaben, und die Texterkennung setzt Leerzeichen hinein; das Muster läuft
    /// hinüber und die Prüfziffer fällt durch.</para>
    /// <para>Gegen eine echte Handwerkerrechnung gemessen wurde so <em>keine einzige</em> IBAN
    /// gefunden — bei zwei Bankverbindungen auf dem Blatt.</para>
    /// </remarks>
    [Fact]
    public void Eine_IBAN_neben_ihrer_BIC_wird_gefunden()
    {
        var zeile = $"Musterbank eG {Eine} GENO DE S1 MUS";

        Assert.Equal([Eine], IbanCheck.FindAll(zeile));
    }

    /// <summary>
    /// Was die Texterkennung beschädigt hat, bleibt draußen.
    /// </summary>
    /// <remarks>
    /// Auf demselben Beleg stand eine zweite IBAN, in der aus zwei Nullen große O geworden
    /// waren. Sie fällt an der Prüfziffer durch — und das ist richtig: Wer ein O zur Null
    /// erklärt, errechnet eine Kontonummer, die auf keinem Papier steht. Abtippen ist dann die
    /// ehrliche Antwort.
    /// </remarks>
    [Fact]
    public void Eine_beschaedigte_IBAN_wird_nicht_repariert()
    {
        Assert.Empty(IbanCheck.FindAll("Deutsche Bank DE021005OOOOOO54540402 DEUT DE SS"));
    }

    /// <summary>
    /// Ein Bruchstück wird nicht zur IBAN aufgefüllt.
    /// </summary>
    /// <remarks>
    /// <para>Die Längenregel kürzt, und nur das. Würde sie auch auffüllen, entstünde aus einer
    /// abgeschnittenen Zeichenfolge eine IBAN mit aufgehender Prüfziffer — die hier steht, ist
    /// so gewählt: vierzehn Zeichen, und mit acht Nullen dahinter geht sie auf.</para>
    /// <para>Vorgeschlagen würde dann eine Kontonummer, die auf keinem Papier steht. Von allen
    /// Fehlern, die diese Maske machen kann, ist das der schlimmste.</para>
    /// </remarks>
    [Fact]
    public void Ein_Bruchstueck_wird_nicht_aufgefuellt()
    {
        Assert.Empty(IbanCheck.FindAll("Konto DE121111111111 bei der Musterbank"));
    }

    [Fact]
    public void Ohne_Text_gibt_es_nichts()
    {
        Assert.Empty(IbanCheck.FindAll(null));
        Assert.Empty(IbanCheck.FindAll(string.Empty));
        Assert.Empty(IbanCheck.FindAll("Sehr geehrte Damen und Herren,"));
    }

    // ── Die Prüfziffer selbst ────────────────────────────────────────────────────────────

    [Fact]
    public void Eine_verdrehte_IBAN_geht_nicht_auf()
    {
        Assert.True(IbanCheck.IsValid(Eine));
        Assert.False(IbanCheck.IsValid("DE02120300000000202015"));
    }

    /// <summary>
    /// Zu kurz und zu lang ist keine IBAN — auch wenn die Prüfziffer aufgeht.
    /// </summary>
    /// <remarks>
    /// <para>Die beiden Zeichenfolgen hier sind mit Absicht so gewählt, dass ihr Rest bei der
    /// Division durch 97 genau eins ergibt. Ohne die Längenschranke kämen sie durch — und die
    /// erste Fassung dieser Prüfung merkte davon nichts, weil sie zwei Zeichenfolgen nahm,
    /// die schon an der Prüfziffer scheiterten.</para>
    /// <para>Kürzeste IBAN: Norwegen mit fünfzehn. Längste: Malta mit vierunddreißig.</para>
    /// </remarks>
    [Fact]
    public void Zu_kurz_und_zu_lang_ist_keine_IBAN()
    {
        Assert.False(IbanCheck.IsValid("DE32123456"));
        Assert.False(IbanCheck.IsValid("DE50" + new string('1', 32)));
    }

    /// <summary>
    /// Was mitten in einer längeren Zeichenfolge steckt, ist keine IBAN.
    /// </summary>
    /// <remarks>
    /// Eine Vertrags- oder Kundennummer kann zufällig eine IBAN enthalten. Ohne Wortgrenzen
    /// schnitte das Muster sie heraus und schlüge sie als Ziel einer Überweisung vor.
    /// </remarks>
    [Fact]
    public void Eine_IBAN_mitten_in_einem_Wort_zaehlt_nicht()
    {
        Assert.Empty(IbanCheck.FindAll($"Vorgang AB{Eine} ist abgeschlossen."));
    }

    // ── Die BIC ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Eine_BIC_hat_acht_oder_elf_Zeichen()
    {
        Assert.True(IbanCheck.BicIsValid("GENODES1SHA"));
        Assert.True(IbanCheck.BicIsValid("GENODES1"));
        Assert.False(IbanCheck.BicIsValid("GENODES1S"));
    }

    /// <summary>Leer ist in Ordnung — die Bank findet das Institut über die IBAN.</summary>
    [Fact]
    public void Eine_leere_BIC_ist_kein_Mangel()
    {
        Assert.True(IbanCheck.BicIsValid(null));
        Assert.True(IbanCheck.BicIsValid("   "));
    }

    /// <summary>Die ersten sechs Stellen einer BIC sind Buchstaben.</summary>
    [Fact]
    public void Eine_BIC_mit_Ziffern_im_Bankteil_stimmt_nicht()
    {
        Assert.False(IbanCheck.BicIsValid("GEN0DES1"));
    }
}
