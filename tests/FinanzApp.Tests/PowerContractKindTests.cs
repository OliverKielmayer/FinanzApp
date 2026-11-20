using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Die Leseregel für den Stromvertrag.
/// </summary>
/// <remarks>
/// <para><b>Die Einheit steht in der Beschriftung, nicht beim Wert.</b> „Verbrauchspreis
/// Cent/kWh 31,20“ — die Zahl allein sagt nicht, ob sie Cent oder Euro meint, und zwischen
/// beiden liegt der Faktor hundert. Das ist die Falle, gegen die hier das meiste steht.</para>
/// <para><b>Ein Versorgerschreiben ist eine Spalte in einem Blatt voller Spalten.</b> Neben
/// der Vertragstabelle stehen Geschäftsführer, Handelsregister und Bankverbindung, und die
/// Textebene mischt sie zeilenweise zusammen. Eine Regel, die die erstbeste Zahl nimmt,
/// findet die Umsatzsteuer.</para>
/// <para>Die Zahlen und Namen sind erfunden; der Aufbau ist dem echten Schreiben
/// nachgebaut.</para>
/// </remarks>
public sealed class PowerContractKindTests
{
    private readonly DocumentFieldExtractor extractor = new();

    // ── Die Vorlage ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Lieferschreiben im Aufbau des Originals.
    /// </summary>
    /// <param name="preiszeile">
    /// Die Zeile mit dem Arbeitspreis. Sie heißt bei jedem Versorger anders und trägt ihre
    /// Einheit im Namen — deshalb ist sie der Parameter dieser Vorlage.
    /// </param>
    private static PdfContent Schreiben(
        string preiszeile = "Verbrauchspreis Cent/kWh 24,00",
        string grundpreiszeile = "Grundpreis Euro/Monat 12,00",
        string abschlag = "Ihr Abschlag: 88,00 Euro/Monat (inklusive 19 % Umsatzsteuer)",
        string tarif = "Musterstrom Basis",
        bool mitLieferstelle = true)
    {
        List<PdfLine> zeilen =
        [
            Line(1, "Musterstrom Vertrieb GmbH, 11111 Musterstadt", "Musterstrom"),
            Line(1, "Erika Mustermann", "Sitz der Gesellschaft"),
            Line(1, "Musterweg 3", "Musterplatz 1"),
            Line(1, "12345 Musterstadt", "Datum"),
            Line(1, "11.03.2026"),
            Line(1, "Vertragskonto: 111 222 333 444", "IBAN"),
            Line(1, "Zählernummer: 1B QQ0 999 000 111", "BIC"),

            // Der Lieferbeginn steckt im Fließtext und nicht in der Tabelle. Rechts daneben
            // steht der Aufsichtsrat — die Textebene hängt ihn an dieselbe Zeile.
            Line(1, "Ab dem 01.04.2026 liefern wir Ihnen Strom zum Tarif " + tarif + ".",
                "Vorsitzender des Aufsichtsrates"),

            Line(1, abschlag),
            Line(1, "Den Abschlag buchen wir jeweils zum 5. jedes Monats ab."),

            // Seite zwei: die Vertragsdaten als Tabelle.
            Line(2, "Ausführliche Informationen zu Ihren Vertragsdaten:"),
            Line(2, "Geschäftspartnernummer: 9 888 777 666"),
            Line(2, mitLieferstelle ? "Lieferstelle: Musterweg 3" : "Abnahmestelle: Musterweg 3"),
            Line(2, "Tarif: " + tarif),
            Line(2, "Zählernummer: 1B QQ0 999 000 111"),
            Line(2, "Zählerstand: 9.100,0 kWh"),
            Line(2, "Strompreis:"),
            Line(2, grundpreiszeile),
            Line(2, preiszeile),
            Line(2, "Umsatzsteuer % 19"),
            Line(2, "Mindestvertragslaufzeit"),
            Line(2, "12 Monate"),
            Line(2, "Der Vertrag ist nächstmöglich zum 30.06.2027 unter Beachtung der "
                    + "Kündigungsfrist kündbar."),
        ];

        return Content(zeilen);
    }

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(IReadOnlyList<PdfLine> zeilen) => new()
    {
        PageCount = 2,
        ImageCount = 0,

        // Ein Versorgerschreiben kommt mit Textebene — kein Scan, keine Erkennung.
        TextIsInvisible = false,
        Lines = zeilen,
    };

    private Dictionary<string, ReadValue> Lies(PdfContent inhalt)
        => extractor.Read(DocumentKindLibrary.PowerContract, inhalt)
            .Values.ToDictionary(w => w.Rule.Key, w => w);

    // ── Die Erkennung ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Ein_Lieferschreiben_wird_als_solches_erkannt()
    {
        Assert.Equal("stromvertrag", DocumentKindLibrary.Detect(Schreiben())?.Key);
    }

    /// <summary>
    /// Ohne Lieferstelle ist es keines.
    /// </summary>
    /// <remarks>
    /// Das Kennzeichen, das den Versorgungsvertrag von allem anderen trennt. Gemessen am
    /// gewachsenen Bestand steht es in genau einem von 125 Belegen mit Text — „Grundpreis“
    /// dagegen auch in einem Schreiben des Bundesfinanzministeriums.
    /// </remarks>
    [Fact]
    public void Ohne_Lieferstelle_greift_die_Regel_nicht()
    {
        Assert.Null(DocumentKindLibrary.Detect(Schreiben(mitLieferstelle: false)));
    }

    // ── Die Preise ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Arbeitspreis in Cent landet im Cent-Feld — und nicht im Euro-Feld.
    /// </summary>
    /// <remarks>
    /// Beide heißen „Verbrauchspreis“. Nur die Einheit dahinter unterscheidet sie, und genau
    /// deshalb stehen in der Regel zwei Felder statt eines mit zwei Beschriftungen: Wo die
    /// Übernahme später durch hundert teilt, muss feststehen, welche Zahl sie vor sich hat.
    /// </remarks>
    [Fact]
    public void Der_Verbrauchspreis_in_Cent_landet_im_Cent_Feld()
    {
        var werte = Lies(Schreiben());

        Assert.Equal(24.00m, werte["arbeitspreis_cent"].Number);
        Assert.False(werte.ContainsKey("arbeitspreis_euro"));
    }

    [Fact]
    public void Der_Arbeitspreis_in_Euro_landet_im_Euro_Feld()
    {
        var werte = Lies(Schreiben(preiszeile: "Arbeitspreis Euro/kWh 0,2400"));

        Assert.Equal(0.2400m, werte["arbeitspreis_euro"].Number);
        Assert.False(werte.ContainsKey("arbeitspreis_cent"));
    }

    /// <summary>
    /// Vier Nachkommastellen überleben das Lesen.
    /// </summary>
    /// <remarks>
    /// <para>Geprüft wird der <em>Leser</em>: Er gibt die Zahl heraus, wie sie dasteht, und
    /// rundet sie nicht unterwegs. Auf Cent gerundet wäre 0,2375 gleich 0,24 — bei
    /// dreitausend Kilowattstunden im Jahr gut vier Euro Unterschied.</para>
    /// <para><b>Dass der Wert auch so <em>abgelegt</em> wird</b>, hängt nicht hier, sondern an
    /// der Art der Kennzahl; das hält <c>ContractFactTests</c> fest. Beide Stellen zusammen
    /// tragen die vier Nachkommastellen vom Papier bis in den Vertrag.</para>
    /// </remarks>
    [Fact]
    public void Ein_Preis_mit_vier_Stellen_bleibt_ganz()
    {
        var werte = Lies(Schreiben(preiszeile: "Arbeitspreis Euro/kWh 0,2375"));

        Assert.Equal(0.2375m, werte["arbeitspreis_euro"].Number);
    }

    /// <summary>Der Grundpreis je Monat und der je Jahr sind zwei verschiedene Zahlen.</summary>
    [Fact]
    public void Grundpreis_je_Jahr_landet_nicht_im_Monatsfeld()
    {
        var werte = Lies(Schreiben(grundpreiszeile: "Grundpreis Euro/Jahr 144,00"));

        Assert.Equal(144.00m, werte["grundpreis_jahr"].Number);
        Assert.False(werte.ContainsKey("grundpreis_monat"));
    }

    [Fact]
    public void Der_Grundpreis_je_Monat_wird_gelesen()
    {
        Assert.Equal(12.00m, Lies(Schreiben())["grundpreis_monat"].Number);
    }

    /// <summary>
    /// Der Abschlag und nicht die Umsatzsteuer.
    /// </summary>
    /// <remarks>
    /// „Ihr Abschlag: 88,00 Euro/Monat (inklusive 19 % Umsatzsteuer)“ — in derselben Zelle
    /// stehen zwei Zahlen. Die Beschriftungssuche nimmt die rechte und fände die 19; das
    /// Muster bindet den Betrag an das Wort „Euro“ dahinter.
    /// </remarks>
    [Fact]
    public void Der_Abschlag_ist_nicht_die_Umsatzsteuer()
    {
        Assert.Equal(88.00m, Lies(Schreiben())["abschlag"].Number);
    }

    // ── Die Daten ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Lieferbeginn, nicht das Briefdatum.
    /// </summary>
    /// <remarks>
    /// Auf dem Blatt stehen mindestens vier Datumsangaben. Der Anker ist deshalb das Verb
    /// „liefern“ hinter dem Datum und nicht das „Ab dem“ davor — dieselbe Lehre wie bei der
    /// Gehaltsabrechnung: an die Nutzlast binden, nicht an die Beschriftung.
    /// </remarks>
    [Fact]
    public void Der_Lieferbeginn_wird_aus_dem_Satz_gelesen()
    {
        var werte = Lies(Schreiben());

        Assert.Equal(new DateOnly(2026, 4, 1), werte["lieferbeginn"].Date);
        Assert.Equal(new DateOnly(2026, 3, 11), werte["briefdatum"].Date);
    }

    [Fact]
    public void Der_naechstmoegliche_Kuendigungstermin_wird_gelesen()
    {
        Assert.Equal(new DateOnly(2027, 6, 30), Lies(Schreiben())["kuendbar_zum"].Date);
    }

    // ── Die Angaben ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tarif_und_Vertragskonto_werden_gelesen()
    {
        var werte = Lies(Schreiben());

        Assert.Equal("Musterstrom Basis", werte["tarif"].Raw);
        Assert.Equal("111 222 333 444", werte["vertragskonto"].Raw);
    }

    /// <summary>
    /// Die Zählernummer und nicht das Wort aus der Nachbarspalte.
    /// </summary>
    /// <remarks>
    /// Auf Seite eins steht rechts von ihr die Überschrift „BIC“ der Bankspalte. Die
    /// Beschriftungssuche geht von rechts nach links und nähme sie — wäre die Nummer nicht
    /// in derselben Zelle wie ihre Beschriftung. Auf Seite zwei steht die Zeile sauber;
    /// gefunden wird die erste, und die trägt den Nachbarn.
    /// </remarks>
    [Fact]
    public void Die_Zaehlernummer_schleppt_keine_Nachbarspalte_mit()
    {
        Assert.Equal("1B QQ0 999 000 111", Lies(Schreiben())["zaehlernummer"].Raw);
    }
}
