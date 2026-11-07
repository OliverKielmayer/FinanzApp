using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Das Sprachmodell findet, es entscheidet nicht.
/// </summary>
/// <remarks>
/// <para>Ein Modell erfindet Beträge und Daten, und zwar überzeugend. Deshalb wird es nie nach
/// dem Wert gefragt, sondern nach der <em>Fundstelle</em> — und eine erfundene Fundstelle steht
/// nicht im Beleg. Das ist nachprüfbar, und hier wird es geprüft.</para>
/// <para>Diese Tests sind die eigentliche Absicherung der zweiten Schicht. Ob ein bestimmtes
/// Modell gut rät, kann kein Test festhalten; dass nichts gespeichert wird, was nicht im Papier
/// steht, schon.</para>
/// </remarks>
public sealed class DocumentActionFindingTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 6);

    private const string Beleg =
        "Stadtwerke Musterstadt GmbH\n"
        + "Musterstadt, den 02.11.2026\n"
        + "Sehr geehrte Damen und Herren,\n"
        + "Ihr Vertrag wurde zum 01.11.2026 auf den neuen Betreiber übertragen.\n"
        + "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue\n"
        + "Konto um.\n"
        + "Mit freundlichen Grüßen";

    // ── Der Prüfer ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ein_Satz_aus_dem_Beleg_wird_angenommen()
    {
        var gefunden = ActionEvidence.Verify(
            Beleg, "Ihr Vertrag wurde zum 01.11.2026 auf den neuen Betreiber übertragen.",
            clock.Today);

        Assert.NotNull(gefunden);
        Assert.Contains("neuen Betreiber", gefunden.Value.Line);
    }

    /// <summary>
    /// Ein erfundener Satz wird verworfen.
    /// </summary>
    /// <remarks>
    /// Der Kern der ganzen Schicht. Er klingt richtig, er nennt einen plausiblen Termin, und er
    /// steht nirgends. Ohne diese Prüfung landete er als Vorgang im Vorgänge-Tab, und niemand
    /// könnte sagen, woher er stammt.
    /// </remarks>
    [Fact]
    public void Ein_erfundener_Satz_wird_verworfen()
    {
        var gefunden = ActionEvidence.Verify(
            Beleg, "Bitte überweisen Sie 149,90 Euro bis zum 15.12.2026.", clock.Today);

        Assert.Null(gefunden);
    }

    /// <summary>
    /// Ein Satz über einen Zeilenumbruch hinweg wird angenommen.
    /// </summary>
    /// <remarks>
    /// Ein PDF bricht Zeilen dort, wo das Papier zu Ende ist, und kein Modell gibt das
    /// zeichengetreu zurück. Verglichen wird deshalb mit zusammengezogenen Leerräumen — die
    /// Eigenschaft, auf die es ankommt, bleibt dabei erhalten: Die Wörter stammen aus diesem
    /// Beleg.
    /// </remarks>
    [Fact]
    public void Ein_Satz_ueber_einen_Zeilenumbruch_hinweg_wird_angenommen()
    {
        var gefunden = ActionEvidence.Verify(
            Beleg, "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue Konto um.",
            clock.Today);

        Assert.NotNull(gefunden);
        Assert.Equal(new DateOnly(2026, 11, 30), gefunden.Value.Due);
    }

    /// <summary>
    /// Der ganze Beleg als Antwort beweist nichts und wird verworfen.
    /// </summary>
    /// <remarks>
    /// Er käme durch jede Vorkommensprüfung — er steht ja im Beleg. Eine Forderung ist er
    /// trotzdem nicht, und als Zeile in einem Vorgang wäre er unlesbar.
    /// </remarks>
    [Fact]
    public void Der_ganze_Beleg_als_Antwort_wird_verworfen()
    {
        Assert.Null(ActionEvidence.Verify(Beleg + Beleg + Beleg, Beleg + Beleg, clock.Today));
    }

    [Fact]
    public void Ein_zu_kurzer_Schnipsel_wird_verworfen()
    {
        Assert.Null(ActionEvidence.Verify(Beleg, "Konto", clock.Today));
    }

    /// <summary>
    /// Zurückgegeben wird der Ausschnitt aus dem Beleg, nicht die Antwort des Modells.
    /// </summary>
    /// <remarks>
    /// Sie sind nach dem Zusammenziehen gleich. Was abgelegt wird, soll trotzdem aus dem Papier
    /// stammen — sonst stünde am Ende die Schreibweise des Modells in der Ablage.
    /// </remarks>
    [Fact]
    public void Abgelegt_wird_die_Schreibweise_des_Belegs()
    {
        var gefunden = ActionEvidence.Verify(
            Beleg, "ihr vertrag wurde zum 01.11.2026 auf den neuen betreiber übertragen.",
            clock.Today);

        Assert.NotNull(gefunden);
        Assert.StartsWith("Ihr Vertrag", gefunden.Value.Line);
    }

    /// <summary>Ein Termin weit außerhalb jeder Frist wird nicht übernommen.</summary>
    [Fact]
    public void Ein_unplausibler_Termin_wird_nicht_uebernommen()
    {
        const string text = "Die Anlage läuft bis zum 01.01.2099 und wird dann abgerechnet.";

        var gefunden = ActionEvidence.Verify(text, text, clock.Today);

        Assert.NotNull(gefunden);
        Assert.Null(gefunden.Value.Due);
    }

    // ── Die Vorauswahl ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vorgelegt wird nur, was überhaupt eine Forderung sein kann.
    /// </summary>
    /// <remarks>
    /// Ein eingescanntes Schreiben hat hundert Zeilen und mehr, die meisten davon Anschrift,
    /// Tabellenreste und Fußzeile. An echten Belegen gemessen: Legt man alle vor, zählt ein
    /// kleines Modell ins Blaue und greift die Objektzeile statt der Fälligkeit zwei Zeilen
    /// weiter.
    /// </remarks>
    [Fact]
    public void Die_Vorauswahl_laesst_nur_moegliche_Forderungen_durch()
    {
        var kandidaten = ActionEvidence.Candidates(Beleg);

        Assert.Contains(kandidaten, z => z.StartsWith("Bitte stellen Sie", StringComparison.Ordinal));
        Assert.DoesNotContain(kandidaten, z => z.Contains("Mit freundlichen", StringComparison.Ordinal));
        Assert.DoesNotContain(kandidaten, z => z.Contains("Sehr geehrte", StringComparison.Ordinal));
    }

    /// <summary>Eine Zeile mit Datum kommt immer mit, auch ohne Forderungswort.</summary>
    /// <remarks>
    /// Eine Frist ohne Datum gibt es nicht, und die Formulierungen, die ein Datum begleiten,
    /// sind zu viele zum Aufzählen. Was hier herausfiele, sähe das Modell nie.
    /// </remarks>
    [Fact]
    public void Eine_Zeile_mit_Datum_kommt_immer_mit()
    {
        var kandidaten = ActionEvidence.Candidates(
            "Guten Tag\nDer Umbau beginnt am 03.03.2027 im Erdgeschoss.\nViele Gruesse");

        Assert.Contains(kandidaten, z => z.Contains("03.03.2027", StringComparison.Ordinal));
    }

    [Fact]
    public void Ohne_Text_gibt_es_keine_Kandidaten()
        => Assert.Empty(ActionEvidence.Candidates(null));

    // ── Die Antwort des Modells ────────────────────────────────────────────────────────────

    [Fact]
    public void Eine_Zahl_zeigt_auf_ihre_Zeile()
    {
        string[] kandidaten = ["erste Zeile hier", "zweite Zeile hier", "dritte Zeile hier"];

        Assert.Equal("zweite Zeile hier", ActionEvidence.LineFrom("2", kandidaten));
    }

    /// <summary>
    /// Eine Zahl außerhalb der Liste ist kein Treffer.
    /// </summary>
    /// <remarks>
    /// Das Modell hat sich verzählt. Geraten wird hier nicht — eine danebenliegende Zeile wäre
    /// schlechter als keine, weil sie plausibel aussieht.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("")]
    [InlineData("keine Ahnung")]
    [InlineData(null)]
    public void Eine_Zahl_ausserhalb_der_Liste_zeigt_auf_nichts(string? antwort)
        => Assert.Null(ActionEvidence.LineFrom(antwort, ["erste Zeile", "zweite Zeile"]));

    /// <summary>Ein erklärendes Wort vor der Zahl stört nicht.</summary>
    [Fact]
    public void Ein_Wort_vor_der_Zahl_stoert_nicht()
        => Assert.Equal("zweite Zeile", ActionEvidence.LineFrom("Antwort: 2", ["erste Zeile", "zweite Zeile"]));

    // ── Der Dienst ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_gefundener_Satz_steht_danach_am_Beleg()
    {
        var id = Abgelegt(Beleg);
        var sucher = new StubFinder(
            "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue Konto um.");

        Assert.True(await Dienst(sucher).ExamineAsync(id));

        var dokument = Geladen(id);
        Assert.Contains("Lastschrifteinzug", dokument.ActionLine);
        Assert.Equal(new DateOnly(2026, 11, 30), dokument.DueDate);
        Assert.True(dokument.ActionFromModel);
    }

    /// <summary>
    /// Ein erfundener Satz landet nicht am Beleg — der Beleg gilt trotzdem als durchgesehen.
    /// </summary>
    /// <remarks>
    /// Sonst ginge er bei jedem Schub erneut durch das Modell, bekäme jedes Mal dieselbe
    /// erfundene Antwort, und die Zahl der offenen Belege stünde für immer still.
    /// </remarks>
    [Fact]
    public async Task Ein_erfundener_Satz_landet_nicht_am_Beleg()
    {
        var id = Abgelegt(Beleg);
        var sucher = new StubFinder("Bitte überweisen Sie 149,90 Euro bis zum 15.12.2026.");

        Assert.False(await Dienst(sucher).ExamineAsync(id));

        var dokument = Geladen(id);
        Assert.Equal(string.Empty, dokument.ActionLine);
        Assert.Null(dokument.DueDate);
        Assert.False(dokument.ActionFromModel);
    }

    /// <summary>
    /// Ohne Modell wird nichts vermerkt.
    /// </summary>
    /// <remarks>
    /// Sonst bekäme der ganze Bestand beim ersten Durchlauf den Vermerk „angesehen, nichts
    /// dabei“, obwohl nie jemand hingesehen hat — und ein später eingerichtetes Modell sähe
    /// keinen Beleg mehr.
    /// </remarks>
    [Fact]
    public async Task Ohne_Modell_bleibt_der_Beleg_unvermerkt()
    {
        var id = Abgelegt(Beleg);
        var sucher = new StubFinder(null, available: false);

        Assert.False(await Dienst(sucher).ExamineAsync(id));

        Assert.Null(Geladen(id).ActionLine);
        Assert.Equal(0, sucher.Aufrufe);
    }

    [Fact]
    public async Task Ein_zweiter_Schub_sieht_denselben_Beleg_nicht_noch_einmal_an()
    {
        Abgelegt(Beleg);
        var sucher = new StubFinder(null);

        var erster = await Dienst(sucher).ScanAsync();
        var zweiter = await Dienst(sucher).ScanAsync();

        Assert.Equal(1, erster.Durchgesehen);
        Assert.Equal(0, zweiter.Durchgesehen);
        Assert.Equal(0, zweiter.Offen);
        Assert.Equal(1, sucher.Aufrufe);
    }

    /// <summary>
    /// Was die Leseregel schon gefunden hat, geht nicht durch das Modell.
    /// </summary>
    /// <remarks>
    /// Ein Modell für etwas zu laden, das ein Wortvergleich in Millisekunden erledigt, hieße
    /// einen Nagel mit dem Vorschlaghammer zu treffen — und das Ergebnis wäre nicht besser,
    /// nur unsicherer.
    /// </remarks>
    [Fact]
    public async Task Was_die_Leseregel_fand_geht_nicht_durch_das_Modell()
    {
        var id = Abgelegt(Beleg);

        using (var context = database.Context())
        {
            var dokument = context.Documents.Single(d => d.Id == id);
            dokument.ActionLine = "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.";
            context.SaveChanges();
        }

        var sucher = new StubFinder("Bitte stellen Sie Ihren Lastschrifteinzug um.");

        Assert.False(await Dienst(sucher).ExamineAsync(id));
        Assert.Equal(0, sucher.Aufrufe);
        Assert.StartsWith("Dieser Betrag", Geladen(id).ActionLine);
    }

    /// <summary>Der Schub hat eine Obergrenze und meldet, wie viel offen bleibt.</summary>
    [Fact]
    public async Task Der_Schub_hat_eine_Obergrenze_und_meldet_den_Rest()
    {
        for (var i = 0; i < 5; i++)
        {
            Abgelegt(Beleg);
        }

        var ergebnis = await Dienst(new StubFinder(null)).ScanAsync(limit: 2);

        Assert.True(ergebnis.Verfuegbar);
        Assert.Equal(2, ergebnis.Durchgesehen);
        Assert.Equal(0, ergebnis.Gefunden);
        Assert.Equal(3, ergebnis.Offen);
    }

    // ── Mitteilungen ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine erkannte Mitteilung geht gar nicht erst durch das Modell.
    /// </summary>
    /// <remarks>
    /// <para>An der gewachsenen Ablage gemessen und dort schmerzhaft gelernt: Von
    /// dreiundfünfzig Funden kamen zweiundvierzig aus Statusreporten, und alle zweiundvierzig
    /// waren falsch. Solche Belege sind voll von Daten und Beträgen in Sätzen, die wie
    /// Forderungen klingen — „Garantierte finanzielle Leistungen zum Ablauf bei
    /// Beitragsfreistellung zum 01.01.2031“ — und keine sind.</para>
    /// <para>Gemessen hatte ich vorher an den zwanzig neuesten Belegen. Darunter war kein
    /// einziger Statusreport, und deshalb sah die Trefferquote gut aus.</para>
    /// </remarks>
    [Theory]
    [InlineData("statusreport-lv")]
    [InlineData("statusreport-fonds-lv")]
    [InlineData("statusreport-riester")]
    [InlineData("quartalsaufstellung")]
    [InlineData("kontoauszug")]
    public async Task Eine_erkannte_Mitteilung_geht_nicht_durch_das_Modell(string art)
    {
        var id = Abgelegt(Beleg, scanKind: art);
        var sucher = new StubFinder(
            "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue Konto um.");

        Assert.False(await Dienst(sucher).ExamineAsync(id));

        Assert.Equal(0, sucher.Aufrufe);
        Assert.Equal(string.Empty, Geladen(id).ActionLine);
    }

    /// <summary>
    /// Ohne erkannte Art geht der Beleg durch.
    /// </summary>
    /// <remarks>
    /// Das ist der Normalfall für Post, die etwas verlangt: Eine Rechnung, ein Bescheid, ein
    /// Anschreiben trägt keine der Arten, für die es Leseregeln gibt.
    /// </remarks>
    [Fact]
    public async Task Ohne_erkannte_Art_geht_der_Beleg_durch_das_Modell()
    {
        var id = Abgelegt(Beleg, scanKind: null);
        var sucher = new StubFinder(
            "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue Konto um.");

        Assert.True(await Dienst(sucher).ExamineAsync(id));
        Assert.Equal(1, sucher.Aufrufe);
    }

    /// <summary>Eine unbekannte Kennung hält nichts auf.</summary>
    [Fact]
    public async Task Eine_unbekannte_Art_haelt_den_Beleg_nicht_auf()
    {
        var id = Abgelegt(Beleg, scanKind: "rechnung-handwerker");
        var sucher = new StubFinder(
            "Bitte stellen Sie Ihren Lastschrifteinzug bis zum 30.11.2026 auf das neue Konto um.");

        Assert.True(await Dienst(sucher).ExamineAsync(id));
    }

    [Fact]
    public async Task Ein_Beleg_ohne_Text_wird_nicht_durchgesehen()
    {
        Abgelegt(text: null);

        var ergebnis = await Dienst(new StubFinder(null)).ScanAsync();

        Assert.Equal(0, ergebnis.Durchgesehen);
        Assert.Equal(0, ergebnis.Offen);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private sealed class StubFinder(string? antwort, bool available = true) : IDocumentActionFinder
    {
        public int Aufrufe { get; private set; }

        public bool Available => available;

        public Task<string?> FindActionLineAsync(string text, CancellationToken ct = default)
        {
            Aufrufe++;
            return Task.FromResult(antwort);
        }
    }

    private DocumentActionService Dienst(IDocumentActionFinder sucher)
        => new(database.Context(), sucher, clock, NullLogger<DocumentActionService>.Instance);

    private int Abgelegt(string? text, string? scanKind = null)
    {
        using var context = database.Context();

        var dokument = new Document
        {
            Title = "Schreiben",
            RelativePath = $"Wohnen/{Guid.NewGuid():N}.pdf",
            FileName = "Schreiben.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Housing,
            ContentText = text,
            ScanKind = scanKind,
            DocumentDate = new DateOnly(2026, 11, 2),
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private Document Geladen(int id)
    {
        using var context = database.Context();
        return context.Documents.Single(d => d.Id == id);
    }

    public void Dispose() => database.Dispose();
}
