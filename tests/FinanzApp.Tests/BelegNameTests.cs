using FinanzApp.Api.Application;

namespace FinanzApp.Tests;

/// <summary>
/// Wie ein Beleg heißt, wenn man ihn in eine Liste stellt.
/// </summary>
/// <remarks>
/// <para>Ein Scanner zählt seine Seiten durch. Dieser Titel landet unverändert am Beleg,
/// während der <em>Dateiname</em> von der Ablage gebaut wird und die Art, das Objekt und das
/// Datum kennt. In der Vorgangsliste stand deshalb „Zu erledigen: Scannen0003“ — eine Zeile,
/// die nur sagt, dass es eine dritte Seite war.</para>
/// <para><b>Hergeleitet, nicht gespeichert.</b> Der Titel am Beleg bleibt, wie der Scanner ihn
/// vergeben hat. Wer ihn später selbst pflegt, hat sofort wieder seinen eigenen Namen vor sich
/// — es gibt nichts zurückzunehmen.</para>
/// </remarks>
public sealed class BelegNameTests
{
    // ── Wann der Dateiname einspringt ──────────────────────────────────────────────────────

    /// <summary>
    /// Ein Gerätename wird durch das ersetzt, was die Ablage weiß.
    /// </summary>
    [Theory]
    [InlineData("Scannen")]
    [InlineData("Scannen0003")]
    [InlineData("Scan_20260820_0001")]
    [InlineData("IMG 4711")]
    [InlineData("dokument")]
    public void Ein_Geraetename_weicht_dem_Dateinamen(string titel)
        => Assert.Equal(
            "Arztrechnung Musterklinik Nordstadt · 03.04.2026",
            BelegName.Fuer(titel, "Arztrechnung_Musterklinik_Nordstadt_2026-04-03.pdf",
                dateinameErzeugt: true, new DateOnly(2026, 4, 3)));

    /// <summary>
    /// Was ein Mensch geschrieben hat, bleibt stehen.
    /// </summary>
    /// <remarks>
    /// Die Grenze muss eng sein: „Scan der Police“ sieht nur im ersten Wort nach Gerät aus und
    /// ist ein Titel, den jemand gewählt hat. Ihn zu überschreiben wäre schlimmer als ein
    /// Stummel, denn der Stummel sagt wenigstens nichts Falsches.
    /// </remarks>
    [Theory]
    [InlineData("Scan der Police")]
    [InlineData("Gebührenbescheid 2026")]
    [InlineData("Scannen 0003 Zahnarzt")]
    public void Ein_gepflegter_Titel_bleibt(string titel)
        => Assert.Equal(
            titel,
            BelegName.Fuer(titel, "Arztrechnung_Musterklinik_Nordstadt_2026-04-03.pdf",
                dateinameErzeugt: true, new DateOnly(2026, 4, 3)));

    /// <summary>
    /// Ohne erzeugten Dateinamen gewinnt der Umweg nichts.
    /// </summary>
    /// <remarks>
    /// Dann stammt auch die Datei vom Gerät und heißt genauso. Der Titel bleibt, und niemand
    /// hat etwas verloren.
    /// </remarks>
    [Fact]
    public void Ein_Geraetename_ohne_erzeugte_Datei_bleibt()
        => Assert.Equal(
            "Scannen0003",
            BelegName.Fuer("Scannen0003", "Scannen0003.pdf",
                dateinameErzeugt: false, new DateOnly(2026, 4, 3)));

    /// <summary>Auch der Dateiname kann ein Gerätename sein — dann hilft er nicht.</summary>
    [Fact]
    public void Ein_Dateiname_vom_Geraet_hilft_nicht()
        => Assert.Equal(
            "Scannen0003",
            BelegName.Fuer("Scannen0003", "Scannen0007.pdf",
                dateinameErzeugt: true, new DateOnly(2026, 4, 3)));

    // ── Das Datum ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei Rechnungen desselben Hauses unterscheiden sich nur im Datum.
    /// </summary>
    /// <remarks>
    /// Ohne das Datum stünden in der Vorgangsliste zwei Zeilen, die Zeichen für Zeichen gleich
    /// heißen — und niemand wüsste, welche er gerade erledigt.
    /// </remarks>
    [Fact]
    public void Zwei_Belege_derselben_Stelle_bleiben_unterscheidbar()
    {
        var erste = BelegName.Fuer("Scannen0003",
            "Arztrechnung_Musterklinik_Nordstadt_2026-04-03.pdf", true, new DateOnly(2026, 4, 3));
        var zweite = BelegName.Fuer("Scannen0015",
            "Arztrechnung_Musterklinik_Nordstadt_2026-05-23.pdf", true, new DateOnly(2026, 5, 23));

        Assert.NotEqual(erste, zweite);
        Assert.EndsWith("03.04.2026", erste);
        Assert.EndsWith("23.05.2026", zweite);
    }

    /// <summary>Ohne Datum im Namen hilft das Datum am Beleg.</summary>
    [Fact]
    public void Fehlt_das_Datum_im_Namen_zaehlt_das_am_Beleg()
        => Assert.Equal(
            "Betreiberwechsel · 25.04.2026",
            BelegName.Fuer("Scannen", "Betreiberwechsel.pdf", true, new DateOnly(2026, 4, 25)));

    /// <summary>Ohne jedes Datum bleibt der Name für sich.</summary>
    [Fact]
    public void Ohne_jedes_Datum_steht_der_Name_allein()
        => Assert.Equal(
            "Betreiberwechsel",
            BelegName.Fuer("Scannen", "Betreiberwechsel.pdf", true, belegdatum: null));

    /// <summary>
    /// Ein unmögliches Datum im Namen wird nicht abgetrennt.
    /// </summary>
    /// <remarks>
    /// Der 31. Februar steht in keinem Kalender. Dann ist die Ziffernfolge kein Datum, und sie
    /// gehört zum Namen — besser ein Name mit Zahlen darin als ein Absturz.
    /// </remarks>
    [Fact]
    public void Ein_unmoegliches_Datum_bleibt_Teil_des_Namens()
        => Assert.Equal(
            "Vertrag 2026-02-31 · 25.04.2026",
            BelegName.Fuer("Scannen", "Vertrag_2026-02-31.pdf", true, new DateOnly(2026, 4, 25)));

    // ── Die Schreibweise ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unterstriche werden zu Leerzeichen, Bindestriche bleiben.
    /// </summary>
    /// <remarks>
    /// Die Ablage trennt ihre Felder mit Unterstrichen. Was dazwischen einen Bindestrich trägt,
    /// trug ihn schon vorher — „Buche-Regalbrett“ ist ein Wort und keine zwei.
    /// </remarks>
    [Fact]
    public void Unterstriche_werden_Leerzeichen_und_Bindestriche_bleiben()
        => Assert.Equal(
            "Rechnung Buche-Regalbrett · 02.05.2026",
            BelegName.Fuer("Scannen", "Rechnung_Buche-Regalbrett_2026-05-02.pdf",
                true, new DateOnly(2026, 5, 2)));

    /// <summary>Ein Punkt im Namen übersteht das Abtrennen der Erweiterung.</summary>
    [Fact]
    public void Ein_Punkt_im_Namen_bleibt_erhalten()
        => Assert.Equal(
            "Information Haus Musterweg Str. 10 · 25.04.2026",
            BelegName.Fuer("Scannen0013", "Information_Haus_Musterweg_Str._10_2026-04-25.pdf",
                true, new DateOnly(2026, 4, 25)));

    /// <summary>Ohne Titel und ohne Datei bleibt nichts übrig, und das ist kein Fehler.</summary>
    [Fact]
    public void Ohne_alles_bleibt_es_leer()
        => Assert.Equal(string.Empty, BelegName.Fuer(null, null, true, null));
}
