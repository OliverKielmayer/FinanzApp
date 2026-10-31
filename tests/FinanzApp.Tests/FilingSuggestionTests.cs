using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Vorschlag, wohin ein Schreiben gehört, das keine Leseregel erkannt hat.
/// </summary>
/// <remarks>
/// <para>Ein Vorschlag ist etwas anderes als eine Leseregel. Die trägt Werte in Verträge ein und
/// muss genau sein; hier geht es um die Schublade, und ein falscher Griff kostet einen Klick.
/// Trotzdem gilt: <b>kein Vorschlag ist besser als ein schlechter.</b> Wer einmal „Arztrechnung“
/// über einer Werkstattrechnung gelesen hat, traut dem nächsten nicht mehr.</para>
/// <para>Die Texte hier sind erfunden — Bruchstücke, wie sie auf solchen Schreiben stehen, mit
/// ausgedachten Namen und Zahlen.</para>
/// </remarks>
public sealed class FilingSuggestionTests
{
    [Fact]
    public void Eine_Arztrechnung_wird_als_solche_vorgeschlagen()
    {
        var vorschlag = FilingSuggestions.For(
            "Liquidation für ärztliche Leistungen\n"
            + "Praxis Dr. Mustermann\n"
            + "Diagnose: Kontrolluntersuchung\n"
            + "Steigerungsfaktor 2,3 nach GOÄ");

        Assert.NotNull(vorschlag);
        Assert.Equal("Arztrechnung", vorschlag.Value.Label);
        Assert.Equal(DocumentArea.Health, vorschlag.Value.Area);
    }

    [Fact]
    public void Eine_Handwerkerrechnung_landet_bei_Wohnen()
    {
        var vorschlag = FilingSuggestions.For(
            "Rechnung Nr. 2026/112\n"
            + "Arbeitslohn 480,00 EUR\n"
            + "Material 212,50 EUR\n"
            + "Anfahrt 45,00 EUR\n"
            + "Der Lohnanteil ist nach § 35a EStG steuerlich absetzbar.");

        Assert.NotNull(vorschlag);
        Assert.Equal("Handwerkerrechnung", vorschlag.Value.Label);
        Assert.Equal(DocumentArea.Housing, vorschlag.Value.Area);
    }

    [Theory]
    [InlineData("Entgeltabrechnung September\nSteuerklasse 1\nGesamtbrutto 4.200,00",
        "Gehaltsabrechnung", DocumentArea.Work)]
    [InlineData("Kontoauszug Nr. 10/2026\nAlter Kontostand 1.200,00\nNeuer Kontostand 980,00",
        "Kontoauszug", DocumentArea.Finance)]
    [InlineData("Nachtrag zum Versicherungsschein\nVersicherungsnummer 77-4711\nVersicherungsbeginn 01.01.2026",
        "Versicherungsschein", DocumentArea.Insurance)]
    [InlineData("Bescheid für 2025 über Einkommensteuer\nFinanzamt Musterstadt\nSteuernummer 11/222/33333",
        "Steuerbescheid", DocumentArea.Finance)]
    public void Die_uebrigen_Arten_werden_erkannt(string text, string label, DocumentArea bereich)
    {
        var vorschlag = FilingSuggestions.For(text);

        Assert.NotNull(vorschlag);
        Assert.Equal(label, vorschlag.Value.Label);
        Assert.Equal(bereich, vorschlag.Value.Area);
    }

    /// <summary>
    /// Die allgemeine Rechnung tritt nur an, wenn nichts Genaueres greift.
    /// </summary>
    /// <remarks>
    /// Auf einer Arztrechnung steht auch „Rechnungsnummer“. Stünde die allgemeine Regel vorn,
    /// landete jedes Fachschreiben in derselben Schublade — und der Vorschlag wäre wertlos,
    /// obwohl er technisch stimmt.
    /// </remarks>
    [Fact]
    public void Die_allgemeine_Rechnung_verdraengt_nichts_Genaueres()
    {
        var text = "Rechnungsnummer 4711\nRechnungsdatum 12.10.2026\n"
                   + "Liquidation nach GOÄ\nPraxis Dr. Mustermann\nZahlbar binnen 14 Tagen";

        Assert.Equal("Arztrechnung", FilingSuggestions.For(text)!.Value.Label);
    }

    [Fact]
    public void Ohne_etwas_Genaueres_bleibt_die_allgemeine_Rechnung()
    {
        var vorschlag = FilingSuggestions.For(
            "Rechnungsnummer 2026-88\nRechnungsdatum 03.10.2026\nGesamtbetrag 119,00 EUR");

        Assert.NotNull(vorschlag);
        Assert.Equal("Rechnung", vorschlag.Value.Label);
        Assert.Equal(DocumentArea.Other, vorschlag.Value.Area);
    }

    /// <summary>
    /// Eine eingescannte Rechnung wird erkannt, auch wenn die Erkennung das Wort zerlegt.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall stammt von einem echten Blatt: Auf der Seite stand groß „RECHNUNG Nr.“,
    /// und die Texterkennung lieferte „RECHNU G Nr.“ — ein verschlucktes N. Eine Regel, die auf
    /// das Wort selbst wartet, wartet dann vergeblich, und der Zettel bleibt ohne Vorschlag
    /// liegen, obwohl ein Mensch ihn in einer Sekunde einordnet.</para>
    /// <para>Was ein Scan dagegen heil überliefert, sind die kurzen Feldbeschriftungen ringsum.
    /// Sie tragen den Vorschlag, wenn das große Wort zerfällt. Zahlen und Namen hier sind
    /// erfunden.</para>
    /// </remarks>
    [Fact]
    public void Eine_zerlesene_Rechnung_wird_an_ihren_Feldern_erkannt()
    {
        var text = """
            Musterholz GmbH & Co. KG
            Auftrags-Nr. 01234567
            RECHNU G Nr. 00099887
            Kunden-Nr. 77341
            Datum 04.11.2026
            Pos Bezeichnung Menge EH GP EUR
            Warenwert: 318,40 EUR 19,00 0/0 MwSt 60,50 EUR Summe:
            """;

        var vorschlag = FilingSuggestions.For(text);

        Assert.NotNull(vorschlag);
        Assert.Equal("Rechnung", vorschlag.Value.Label);
        Assert.Contains("kunden-nr", vorschlag.Value.Hits);
        Assert.Contains("mwst", vorschlag.Value.Hits);

        // Und zwar ohne das Wort, an dem es eigentlich hängen sollte.
        Assert.DoesNotContain("rechnung", vorschlag.Value.Hits);
    }

    /// <summary>
    /// Ein einzelnes schwaches Wort entscheidet nichts.
    /// </summary>
    /// <remarks>
    /// „Brutto“ steht auf jeder zweiten Rechnung, „IBAN“ unter jedem Schreiben mit Bankverbindung.
    /// Zwei Punkte sind die Schwelle, und ein einzelnes stützendes Wort bringt einen.
    /// </remarks>
    [Theory]
    [InlineData("Sehr geehrte Damen und Herren, anbei die Unterlagen. Brutto wie besprochen.")]
    [InlineData("Unsere Bankverbindung: IBAN DE02 1234 5678 9012 3456 78")]
    [InlineData("")]
    [InlineData(null)]
    public void Ein_duenner_Text_bekommt_keinen_Vorschlag(string? text)
        => Assert.Null(FilingSuggestions.For(text));

    /// <summary>Die Wörter, die den Ausschlag gaben, stehen im Vorschlag — er ist prüfbar.</summary>
    [Fact]
    public void Der_Vorschlag_nennt_seine_Gruende()
    {
        var vorschlag = FilingSuggestions.For(
            "Entgeltabrechnung Oktober 2026\nLohnsteuer 612,40\nSteuerklasse 3");

        Assert.NotNull(vorschlag);
        Assert.Contains("entgeltabrechnung", vorschlag.Value.Hits);
        Assert.Contains("lohnsteuer", vorschlag.Value.Hits);
    }
}
