using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Was im Formular „Offenen Posten anlegen“ schon dasteht.
/// </summary>
/// <remarks>
/// <para><b>Der Fall, aus dem es entstand:</b> Eine Rechnung lag abgelegt da, der Weg zum
/// offenen Posten war einen Klick entfernt — und das Formular zeigte Empfänger samt halber
/// Anschrift, kein Betragsfeld und keine Frist. Drei verschiedene Ursachen, eine davon
/// ärgerlicher als die anderen: Die Frist <em>kannte</em> die Anwendung bereits.</para>
/// <para>Die Belege hier sind erfunden, in der Gestalt echter — ein Warenwirtschaftssystem, das
/// in Grossbuchstaben in Spalten druckt und die Währung vor die Zahl setzt.</para>
/// </remarks>
public sealed class OpenItemPrefillTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Eine Rechnung in der Gestalt, an der es aufgefallen ist.
    /// </summary>
    /// <remarks>
    /// Drei Eigenheiten, und jede einzelne hat ein Feld leer gelassen: die abgekürzte
    /// Beschriftung <c>GES-BETRAG</c>, der Briefkopf mit Strasse <em>ohne</em> Endung auf
    /// <c>-strasse</c> oder <c>-weg</c>, und das Zahlungsziel in einem Fliesssatz statt hinter
    /// einer Beschriftung.
    /// </remarks>
    private const string Warenrechnung = """
        Musterwerke Energie GmbH I Beispielgasse 26 I 74613 Musterstadt
        R E C H N U N G
        KUNDEN-NR 01-999999
        RECHNR 884501
        LIEFNR 031877
        DATUM 04.08.26
        POS BEZEICHNUNG MENGE EINHEIT EINZELPREIS BETRAG
        0203 Beispielware 1,00 ST 1.830,04 1.830,04
        WARENWERT EUR 1.830,04
        7,00 % MWST EUR 128,10
        GES-BETRAG EUR 1.958,14
        WIR BITTEN UM ÜBERWEISUNG DES GESAMTBETRAGES BIS 14.08.26
        """;

    private CreateFormService Forms(FinanzAppDbContext context)
        => new(context, clock,
            new DocumentService(context, TestDatabase.PathService(root),
                new ObjectLabelService(context), clock, new PdfPigTextReader(),
                NullLogger<DocumentService>.Instance),
            new ObjectLabelService(context), new NoPolicyDocumentAnalyzer());

    /// <summary>Ein abgelegter Beleg, wie ihn die Einlieferung hinterlässt.</summary>
    private int Beleg(
        FinanzAppDbContext context, string text, string? fundzeile = null, DateOnly? frist = null)
    {
        var dokument = new Document
        {
            Title = "Scannen",
            RelativePath = "Sonstiges/Scannen.pdf",
            FileName = "Scannen.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Other,
            ContentText = text,
            ActionLine = fundzeile,
            DueDate = frist,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private static string? Vorgabe(CreateFormDto form, string key)
        => form.Fields.FirstOrDefault(f => f.Key == key)?.DefaultValue;

    // ── Der Betrag an der abgekürzten Beschriftung ─────────────────────────────────────────

    /// <summary>
    /// <c>GES-BETRAG</c> ist eine Betragszeile.
    /// </summary>
    /// <remarks>
    /// Die Abkürzung steht in einer Spalte, in die das ganze Wort nicht passt. Gelesen werden
    /// muss die Brutto-Zeile und nicht der Warenwert darüber — eine Zahl, die um die
    /// Umsatzsteuer danebenliegt, fällt niemandem auf.
    /// </remarks>
    [Fact]
    public void Der_abgekuerzte_Gesamtbetrag_wird_gelesen()
    {
        var gelesen = BillTextReader.Read(Warenrechnung);

        Assert.Equal(1958.14m, gelesen.GrossAmount);
    }

    /// <summary>
    /// Was ausdrücklich <b>keine</b> Betragszeile ist.
    /// </summary>
    /// <remarks>
    /// <para>Die Gegenprobe zur Erweiterung der Liste. Durchgezählt wurden alle Summenzeilen der
    /// echten Ablage, die einen Betrag tragen und heute unerkannt bleiben: Die häufigste ist
    /// die <em>Beitragssumme</em> einer Police — die Summe aller künftigen Beiträge und nicht
    /// ein Betrag, den jemand überweist. Daneben stehen Versicherungssumme, Zwischensumme und
    /// der reine Steuerbetrag.</para>
    /// <para>Jede einzelne davon als offener Posten wäre eine Forderung, die es nicht gibt.
    /// Deshalb kam nur die eine Abkürzung dazu und keines dieser Wörter.</para>
    /// </remarks>
    [Theory]
    [InlineData("Beitragssumme 12.345,67 EUR")]
    [InlineData("Versicherungssumme 48.700,00 EUR")]
    [InlineData("Zwischensumme 410,00 EUR")]
    [InlineData("MwSt.-Betrag 128,10 EUR")]
    public void Keine_Betragszeile_bleibt_ohne_Betrag(string zeile)
    {
        var gelesen = BillTextReader.Read("Musterwerke GmbH" + Environment.NewLine + zeile);

        Assert.Null(gelesen.GrossAmount);
    }

    // ── Der Name hört an der Rechtsform auf ────────────────────────────────────────────────

    /// <summary>
    /// Hinter der Rechtsform ist der Name zu Ende.
    /// </summary>
    /// <remarks>
    /// Die Anschrift abzuschneiden genügte nicht: Ohne Endung auf <c>-strasse</c> oder
    /// <c>-weg</c> greift nur noch die Postleitzahl, und davor blieb die halbe Anschrift stehen.
    /// </remarks>
    [Fact]
    public void Der_Absender_endet_an_der_Rechtsform()
    {
        var gelesen = BillTextReader.Read(Warenrechnung);

        Assert.Equal("Musterwerke Energie GmbH", gelesen.Provider);
    }

    /// <summary>
    /// Die <b>letzte</b> Rechtsform zählt, nicht die erste.
    /// </summary>
    /// <remarks>
    /// Sonst verlöre „Musterbau GmbH &amp; Co. KG“ schon hinter dem ersten Treffer seine
    /// Hälfte — und hiesse fortan „Musterbau GmbH“, was eine andere Firma ist.
    /// </remarks>
    [Fact]
    public void Bei_zwei_Rechtsformen_zaehlt_die_letzte()
    {
        var gelesen = BillTextReader.Read(
            "Musterbau GmbH & Co. KG, Hauptstraße 5, 12345 Musterstadt"
            + Environment.NewLine + "Rechnungsbetrag 1.958,14 EUR");

        Assert.Equal("Musterbau GmbH & Co. KG", gelesen.Provider);
    }

    /// <summary>Ohne Rechtsform bleibt es beim alten Weg.</summary>
    /// <remarks>
    /// Ein Handwerksbetrieb führt oft keine. Der Schnitt an der Rechtsform darf diesen Fall
    /// nicht mitnehmen und den Namen leeren.
    /// </remarks>
    [Fact]
    public void Ohne_Rechtsform_bleibt_der_Name_stehen()
    {
        var gelesen = BillTextReader.Read(
            "Malerbetrieb Muster, Hauptstraße 5, 12345 Musterstadt"
            + Environment.NewLine + "Rechnungsbetrag 1.958,14 EUR");

        Assert.Equal("Malerbetrieb Muster", gelesen.Provider);
    }

    /// <summary>
    /// Steht die Rechtsform hinter der Anschrift, bleibt trotzdem ein Name stehen.
    /// </summary>
    /// <remarks>
    /// <para>Der Rückfall im Schnitt an der Rechtsform, und eine Prüfung, die es erst nach der
    /// Eichung gibt: Ein Probelauf, der statt des Namens eine leere Zeichenkette zurückgab, liess
    /// alle anderen grün.</para>
    /// <para>Erreichbar ist der Fall, weil zuerst die Anschrift abgeschnitten wird: Lag die
    /// Rechtsform dahinter, steht sie im verbliebenen Teil nicht mehr. Was dann bleibt, ist ein
    /// schwacher Name — aber einer. Ein leerer verschwände stillschweigend aus dem Feld
    /// „Empfänger“, und damit hätte die Zahlungssuche nichts mehr, wonach sie greifen kann.</para>
    /// </remarks>
    [Fact]
    public void Hinter_der_Anschrift_bleibt_der_Name_nicht_leer()
    {
        var gelesen = BillTextReader.Read(
            "Rechnung Beispielweg 7, 12345 Musterstadt, Muster Handel GmbH"
            + Environment.NewLine + "Rechnungsbetrag 1.958,14 EUR");

        Assert.False(string.IsNullOrWhiteSpace(gelesen.Provider));
    }

    // ── Die Frist kommt vom Beleg ──────────────────────────────────────────────────────────

    /// <summary>
    /// Steht die Frist in der Fundzeile des Belegs, steht sie im Formular.
    /// </summary>
    /// <remarks>
    /// Der Kern des dritten Mangels: Zwei Leser sehen dasselbe Blatt an. Der eine sucht die
    /// Fälligkeit an einer Beschriftung und findet in einem Fliesssatz keine; der andere ist die
    /// Fundzeile am Dokument und kannte den Tag längst — aus ihr entsteht der Vorgang mit seinem
    /// Termin. Das Formular las nur den ersten.
    /// </remarks>
    [Fact]
    public async Task Die_Frist_vom_Beleg_steht_im_Formular()
    {
        using var context = database.Context();
        var beleg = Beleg(
            context, Warenrechnung,
            "WIR BITTEN UM ÜBERWEISUNG DES GESAMTBETRAGES BIS 14.08.26",
            new DateOnly(2026, 8, 14));

        var form = await Forms(context).GetFormAsync(CreateObjectType.Invoice, null, beleg);

        Assert.NotNull(form);

        Assert.Equal("2026-08-14", Vorgabe(form, "due"));
    }

    /// <summary>
    /// Eine <b>gerechnete</b> Frist bleibt draussen.
    /// </summary>
    /// <remarks>
    /// „Innerhalb von 14 Tagen“ ergibt einen Tag, der so nicht auf dem Papier steht. An der
    /// Fälligkeit misst sich danach, ob ein Posten überfällig ist — einen gerechneten Tag
    /// stillschweigend einzusetzen hiesse, eine Annahme zur Frist zu erklären. Unterschieden
    /// wird an der Fundzeile selbst: Nennt sie kein Datum, kann der Termin nur gerechnet sein.
    /// </remarks>
    [Fact]
    public async Task Eine_gerechnete_Frist_bleibt_leer()
    {
        using var context = database.Context();
        var beleg = Beleg(
            context, Warenrechnung,
            "Bitte begleichen Sie den Betrag innerhalb von zehn Tagen",
            new DateOnly(2026, 8, 14));

        var form = await Forms(context).GetFormAsync(CreateObjectType.Invoice, null, beleg);

        Assert.NotNull(form);

        Assert.Null(Vorgabe(form, "due"));
    }

    /// <summary>
    /// Die Beschriftung auf dem Blatt gewinnt über die Fundzeile.
    /// </summary>
    /// <remarks>
    /// Wo die Rechnung „zahlbar bis“ schreibt, ist das die Quelle — sie steht an der Zahl und
    /// nicht in einem Satz daneben. Die Fundzeile ist der Rückfall und nicht der Vorrang; sonst
    /// überschriebe ein Satz über eine Mahnfrist das gedruckte Zahlungsziel.
    /// </remarks>
    [Fact]
    public async Task Die_Beschriftung_auf_dem_Blatt_gewinnt()
    {
        using var context = database.Context();
        var beleg = Beleg(
            context,
            "Musterwerke Energie GmbH" + Environment.NewLine
            + "Rechnungsbetrag 1.958,14 EUR" + Environment.NewLine
            + "zahlbar bis 25.08.2026",
            "Bitte überweisen Sie bis 14.08.26",
            new DateOnly(2026, 8, 14));

        var form = await Forms(context).GetFormAsync(CreateObjectType.Invoice, null, beleg);

        Assert.NotNull(form);

        Assert.Equal("2026-08-25", Vorgabe(form, "due"));
    }

    /// <summary>
    /// Betrag, Nummer und Datum stehen danach vollständig im Formular.
    /// </summary>
    /// <remarks>
    /// Die Probe über alle drei Mängel zusammen: Dieselbe Rechnung, die vorher drei leere oder
    /// falsche Felder hinterliess, füllt sie jetzt. Einzeln geprüft wird jeder Mangel oben —
    /// hier geht es darum, dass sie sich nicht gegenseitig aufheben.
    /// </remarks>
    [Fact]
    public async Task Das_Formular_steht_vollstaendig_da()
    {
        using var context = database.Context();
        var beleg = Beleg(
            context, Warenrechnung,
            "WIR BITTEN UM ÜBERWEISUNG DES GESAMTBETRAGES BIS 14.08.26",
            new DateOnly(2026, 8, 14));

        var form = await Forms(context).GetFormAsync(CreateObjectType.Invoice, null, beleg);

        Assert.NotNull(form);

        Assert.Equal("Musterwerke Energie GmbH", Vorgabe(form, "payee"));
        Assert.Equal("1958,14", Vorgabe(form, "amount"));
        Assert.Equal("884501", Vorgabe(form, "number"));
        Assert.Equal("2026-08-04", Vorgabe(form, "issued"));
        Assert.Equal("2026-08-14", Vorgabe(form, "due"));
    }

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
