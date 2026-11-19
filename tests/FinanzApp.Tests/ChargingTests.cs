using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Ladestrom des Dienstwagens.
/// </summary>
/// <remarks>
/// <para>Zwei Dinge werden gespeichert: der abgelesene Stand und der vereinbarte Satz. Alles
/// andere entsteht beim Lesen. Diese Prüfungen halten genau das fest — und die Regel, ohne die
/// es nicht trägt: <b>ein Zähler zählt aufwärts.</b></para>
/// <para>Ein Stand unter seinem Vorgänger ist kein Rückgang, sondern ein Zahlendreher. Käme er
/// durch, stünde ein negativer Verbrauch in der Abrechnung an den Arbeitgeber.</para>
/// </remarks>
public sealed class ChargingTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 19);

    private ChargingService Dienst(FinanzAppDbContext context) => new(context, clock);

    /// <summary>Ein laufendes Arbeitsverhältnis — ohne eines hängt keine Ablesung irgendwo.</summary>
    private int Arbeitsverhaeltnis()
    {
        using var context = database.Context();

        var verhaeltnis = new Employment
        {
            Employer = "Musterbetrieb GmbH",
            StartsOn = new DateOnly(2020, 1, 1),
            GrossMonthly = 5000m,
            IsActive = true,
        };

        context.Employments.Add(verhaeltnis);
        context.SaveChanges();

        return verhaeltnis.Id;
    }

    private async Task<ChargingReadingDto> AblesenAsync(
        DateOnly tag, decimal stand, int? verhaeltnis = null)
    {
        using var context = database.Context();

        return await Dienst(context).AddReadingAsync(new CreateChargingReadingRequest
        {
            EmploymentId = verhaeltnis,
            ReadOn = tag,
            MeterKwh = stand,
        });
    }

    private async Task SatzAsync(DateOnly ab, decimal euroJeKwh, int? verhaeltnis = null)
    {
        using var context = database.Context();

        await Dienst(context).AddRateAsync(new CreateChargingRateRequest
        {
            EmploymentId = verhaeltnis,
            ValidFrom = ab,
            EuroPerKwh = euroJeKwh,
        });
    }

    private async Task<ChargingOverviewDto> StandAsync(int? verhaeltnis = null)
    {
        using var context = database.Context();
        return await Dienst(context).GetAsync(verhaeltnis);
    }

    // ── Was gerechnet wird ──────────────────────────────────────────────────

    /// <summary>
    /// Der Verbrauch ist die Differenz zweier Stände — und steht nirgends geschrieben.
    /// </summary>
    /// <remarks>
    /// Die ganze Entscheidung des Bereichs in einer Prüfung: Abgelesen wird der Stand, der
    /// Verbrauch wird gerechnet. Wer ihn speicherte, hielte dieselbe Aussage zweimal — und eine
    /// berichtigte Ablesung ließe die gespeicherte Hälfte falsch stehen.
    /// </remarks>
    [Fact]
    public async Task Der_Verbrauch_ist_die_Differenz_zweier_Staende()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1234.567m, id);

        var stand = await StandAsync(id);
        var jüngste = stand.Readings[0];

        Assert.Equal(234.567m, jüngste.Kwh);
        Assert.Equal(new DateOnly(2026, 9, 30), jüngste.Since);
    }

    /// <summary>
    /// Die erste Ablesung einer Reihe hat keinen Verbrauch — und nicht null.
    /// </summary>
    /// <remarks>
    /// Null wäre die Behauptung, es sei nichts geladen worden. Dasteht aber nur, dass es
    /// keinen Vorgänger gibt, von dem sich abziehen ließe.
    /// </remarks>
    [Fact]
    public async Task Die_erste_Ablesung_hat_keinen_Verbrauch()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);

        var stand = await StandAsync(id);

        Assert.Null(stand.Readings[0].Kwh);
        Assert.Null(stand.Readings[0].Amount);
        Assert.Equal(0m, stand.TotalKwh);
    }

    /// <summary>Verbrauch mal Satz ergibt den Betrag, auf Cent gerundet.</summary>
    [Fact]
    public async Task Der_Betrag_ist_Verbrauch_mal_Satz()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2635m, id);
        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1234.567m, id);

        var stand = await StandAsync(id);

        // 234,567 kWh * 0,2635 €/kWh = 61,80841... €
        Assert.Equal(61.81m, stand.Readings[0].Amount);
        Assert.Equal(0.2635m, stand.Readings[0].EuroPerKwh);
    }

    /// <summary>
    /// Ohne vereinbarten Satz bleibt der Betrag offen — und die Summe sagt, dass ihr etwas fehlt.
    /// </summary>
    /// <remarks>
    /// Ein unterstellter Satz sähe aus wie ein vereinbarter. Und eine Summe, der ein Zeitraum
    /// fehlt, ohne dass sie es sagt, ist schlimmer als gar keine.
    /// </remarks>
    [Fact]
    public async Task Ohne_Satz_bleibt_der_Betrag_offen_und_die_Summe_sagt_es()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);

        var stand = await StandAsync(id);

        Assert.Null(stand.Readings[0].Amount);
        Assert.Equal(100.000m, stand.TotalKwh);
        Assert.Equal(0m, stand.TotalAmount);
        Assert.Equal(1, stand.PeriodsWithoutRate);
    }

    /// <summary>
    /// Ein Satz gilt ab seinem Tag, und der Tag der Ablesung entscheidet.
    /// </summary>
    /// <remarks>
    /// Läuft ein Zeitraum über den Wechsel, bekommt er den neuen Satz — die Kilowattstunden auf
    /// die Tage zu verteilen hieße, eine Ladekurve zu erfinden, die niemand gemessen hat. Dass
    /// er darüber lief, muss die Zeile sagen.
    /// </remarks>
    [Fact]
    public async Task Ueber_einen_Satzwechsel_gilt_der_Satz_des_Ablesetags_und_die_Zeile_sagt_es()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2000m, id);
        await SatzAsync(new DateOnly(2027, 1, 1), 0.3000m, id);

        // Beide Ablesungen liegen hinter der Testuhr dieser Klasse — hier zählt der Jahreswechsel,
        // also geht die Uhr für sie weiter.
        var spaeter = TestDatabase.ClockAt(2027, 2, 1);

        using (var dezember = database.Context())
        {
            await new ChargingService(dezember, spaeter).AddReadingAsync(
                new CreateChargingReadingRequest
                {
                    EmploymentId = id,
                    ReadOn = new DateOnly(2026, 12, 20),
                    MeterKwh = 1000.000m,
                });
        }

        using var context = database.Context();
        var januar = await new ChargingService(context, spaeter)
            .AddReadingAsync(new CreateChargingReadingRequest
            {
                EmploymentId = id,
                ReadOn = new DateOnly(2027, 1, 20),
                MeterKwh = 1100.000m,
            });

        Assert.Equal(0.3000m, januar.EuroPerKwh);
        Assert.Equal(30.00m, januar.Amount);
        Assert.True(januar.RateChangedInPeriod);
    }

    /// <summary>Ein Zeitraum ohne Satzwechsel sagt das auch.</summary>
    [Fact]
    public async Task Ohne_Satzwechsel_meldet_die_Zeile_keinen()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2000m, id);
        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);

        var stand = await StandAsync(id);

        Assert.False(stand.Readings[0].RateChangedInPeriod);
    }

    /// <summary>
    /// Ein Satz gilt bis zum Tag vor dem nächsten — abgeleitet, nicht gespeichert.
    /// </summary>
    /// <remarks>
    /// Ein Bis-Datum neben dem Ab-Datum wäre dieselbe Aussage zweimal, und gepflegt würde
    /// irgendwann nur eines. Dann stünde eine Lücke im Kalender, die niemand gewollt hat.
    /// </remarks>
    [Fact]
    public async Task Ein_Satz_gilt_bis_zum_Tag_vor_dem_naechsten()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2000m, id);
        await SatzAsync(new DateOnly(2027, 1, 1), 0.3000m, id);

        var stand = await StandAsync(id);
        var aelterer = stand.Rates.Single(s => s.ValidFrom == new DateOnly(2026, 1, 1));
        var juengerer = stand.Rates.Single(s => s.ValidFrom == new DateOnly(2027, 1, 1));

        Assert.Equal(new DateOnly(2026, 12, 31), aelterer.ValidUntil);
        Assert.Null(juengerer.ValidUntil);
    }

    // ── Was abgewiesen wird ─────────────────────────────────────────────────

    /// <summary>
    /// Ein Stand unter seinem Vorgänger wird abgewiesen.
    /// </summary>
    /// <remarks>
    /// Die tragende Regel. Ein Zähler zählt in eine Richtung; was aussieht wie ein Rückgang, ist
    /// ein Zahlendreher beim Abschreiben. Käme er durch, stünde ein negativer Verbrauch in der
    /// Abrechnung an den Arbeitgeber — und niemand prüft eine Zahl nach, die der Rechner
    /// ausgerechnet hat.
    /// </remarks>
    [Fact]
    public async Task Ein_Stand_unter_dem_vorigen_wird_abgewiesen()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1234.000m, id);

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => AblesenAsync(new DateOnly(2026, 10, 31), 1243.000m - 100m, id));

        Assert.Contains("zählt aufwärts", fehler.Message);
    }

    /// <summary>
    /// Auch mitten in die Reihe muss der Stand passen.
    /// </summary>
    /// <remarks>
    /// Wer eine vergessene Ablesung nachträgt, schiebt sie zwischen zwei vorhandene. Nur gegen
    /// den letzten Stand zu prüfen ließe jede Zahl durch, die über dem ältesten liegt — und die
    /// Reihe wäre nach dem Nachtragen gebrochen, ohne dass beim Eintragen etwas auffiel.
    /// </remarks>
    [Fact]
    public async Task Ein_nachgetragener_Stand_muss_zwischen_seine_Nachbarn_passen()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 11, 15), 1200.000m, id);

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => AblesenAsync(new DateOnly(2026, 10, 31), 1500.000m, id));

        Assert.Contains("zählt aufwärts", fehler.Message);
    }

    /// <summary>Zwei Ablesungen an einem Tag werden abgewiesen.</summary>
    /// <remarks>
    /// Zwei Stände für denselben Zeitpunkt: Der Verbrauch dazwischen ließe sich nicht mehr
    /// ordnen, und welcher der Stand des Tages ist, entschiede die Reihenfolge der Zeilen.
    /// </remarks>
    [Fact]
    public async Task Zwei_Ablesungen_an_einem_Tag_werden_abgewiesen()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => AblesenAsync(new DateOnly(2026, 9, 30), 1001.000m, id));

        Assert.Contains("schon eine Ablesung", fehler.Message);
    }

    /// <summary>Eine Ablesung aus der Zukunft wird abgewiesen.</summary>
    [Fact]
    public async Task Eine_Ablesung_aus_der_Zukunft_wird_abgewiesen()
    {
        var id = Arbeitsverhaeltnis();

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => AblesenAsync(new DateOnly(2026, 11, 20), 1000.000m, id));

        Assert.Contains("Zukunft", fehler.Message);
    }

    /// <summary>Ein Satz von null oder darunter wird abgewiesen.</summary>
    [Fact]
    public async Task Ein_Satz_von_null_wird_abgewiesen()
    {
        var id = Arbeitsverhaeltnis();

        await Assert.ThrowsAsync<ArgumentException>(() => SatzAsync(new DateOnly(2026, 1, 1), 0m, id));
    }

    // ── Was danach noch stimmt ──────────────────────────────────────────────

    /// <summary>
    /// Ein zweiter Satz ab demselben Tag ersetzt den ersten, statt daneben zu stehen.
    /// </summary>
    /// <remarks>
    /// Zwei Sätze ab demselben Tag hieße, dass die Reihenfolge der Zeilen entscheidet, welcher
    /// gilt. Das ist keine Aussage, sondern ein Zufall der Datenbank.
    /// </remarks>
    [Fact]
    public async Task Ein_zweiter_Satz_ab_demselben_Tag_ersetzt_den_ersten()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2000m, id);
        await SatzAsync(new DateOnly(2026, 1, 1), 0.3400m, id);

        var stand = await StandAsync(id);

        Assert.Single(stand.Rates);
        Assert.Equal(0.3400m, stand.Rates[0].EuroPerKwh);
    }

    /// <summary>
    /// Eine gelöschte Ablesung lässt den Zeitraum wieder zusammenwachsen.
    /// </summary>
    /// <remarks>
    /// Die Probe darauf, dass der Verbrauch wirklich nirgends steht: Fällt die Ablesung in der
    /// Mitte weg, ist der Verbrauch über das Ganze die Differenz der beiden äußeren Stände —
    /// ohne dass irgendetwas nachgerechnet werden müsste.
    /// </remarks>
    [Fact]
    public async Task Eine_geloeschte_Ablesung_laesst_den_Zeitraum_zusammenwachsen()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        var mitte = await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);
        await AblesenAsync(new DateOnly(2026, 11, 15), 1250.000m, id);

        using (var context = database.Context())
        {
            Assert.True(await Dienst(context).DeleteReadingAsync(mitte.Id));
        }

        var stand = await StandAsync(id);

        Assert.Equal(2, stand.Readings.Count);
        Assert.Equal(250.000m, stand.Readings[0].Kwh);
        Assert.Equal(new DateOnly(2026, 9, 30), stand.Readings[0].Since);
    }

    /// <summary>
    /// Ohne angegebenes Verhältnis wird das laufende genommen.
    /// </summary>
    /// <remarks>
    /// Es gibt meist genau eines. Danach zu fragen wäre eine Frage, deren Antwort feststeht —
    /// und ein Pflichtfeld, das jede Eingabe um einen Griff verlängert.
    /// </remarks>
    [Fact]
    public async Task Ohne_Angabe_zaehlt_das_laufende_Arbeitsverhaeltnis()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m);

        var stand = await StandAsync();

        Assert.Equal(id, stand.EmploymentId);
        Assert.Equal("Musterbetrieb GmbH", stand.Employer);
        Assert.Single(stand.Readings);
    }

    // ── Die Abrechnung ───────────────────────────────────────────────

    private async Task<ChargingStatementDto> AbrechnenAsync(int? verhaeltnis = null)
    {
        using var context = database.Context();

        return await Dienst(context).CreateStatementAsync(
            new CreateChargingStatementRequest { EmploymentId = verhaeltnis });
    }

    /// <summary>Ein Verhältnis mit Satz und zwei Ablesungen — der kleinste abrechenbare Fall.</summary>
    private async Task<int> VorbereitetAsync(decimal satz = 0.2000m)
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), satz, id);
        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);

        return id;
    }

    /// <summary>
    /// Die Abrechnung schreibt fest, was sie gerechnet hat.
    /// </summary>
    /// <remarks>
    /// Der Kern von allem: Überall sonst in diesem Bereich wird gerechnet und nichts gespeichert.
    /// Hier ist es umgekehrt, und der Grund ist der Empfänger — der Arbeitgeber hat ein Blatt
    /// mit einer Summe bekommen.
    /// </remarks>
    [Fact]
    public async Task Die_Abrechnung_schreibt_ihre_Zahlen_fest()
    {
        var id = await VorbereitetAsync();

        var abrechnung = await AbrechnenAsync(id);

        Assert.Equal(new DateOnly(2026, 9, 30), abrechnung.FromOn);
        Assert.Equal(1000.000m, abrechnung.FromMeterKwh);
        Assert.Equal(new DateOnly(2026, 10, 31), abrechnung.ToOn);
        Assert.Equal(1100.000m, abrechnung.ToMeterKwh);
        Assert.Equal(100.000m, abrechnung.Kwh);
        Assert.Equal(0.2000m, abrechnung.EuroPerKwh);
        Assert.Equal(20.00m, abrechnung.Amount);
    }

    /// <summary>
    /// Ein später geänderter Satz rührt eine Abrechnung nicht mehr an.
    /// </summary>
    /// <remarks>
    /// Die Probe auf das Festschreiben. Ohne sie wäre die Zusage nur ein Kommentar: Der
    /// Arbeitgeber hat 20,00 € gesehen, und daran ändert eine Vereinbarung von morgen nichts.
    /// </remarks>
    [Fact]
    public async Task Ein_spaeterer_Satz_aendert_eine_Abrechnung_nicht_mehr()
    {
        var id = await VorbereitetAsync();
        await AbrechnenAsync(id);

        // Rueckwirkend ab demselben Tag — der haerteste Fall.
        await SatzAsync(new DateOnly(2026, 1, 1), 0.9000m, id);

        var stand = await StandAsync(id);

        Assert.Equal(20.00m, stand.Statements[0].Amount);
        Assert.Equal(0.2000m, stand.Statements[0].EuroPerKwh);
    }

    /// <summary>
    /// Die nächste Abrechnung setzt auf dem Endstand der vorigen auf.
    /// </summary>
    /// <remarks>
    /// Keine Kilowattstunde bleibt liegen, keine geht zweimal hinaus. Beginnt die zweite beim
    /// Anfang statt beim Ende der ersten, wäre derselbe Zeitraum zweimal eingereicht.
    /// </remarks>
    [Fact]
    public async Task Die_naechste_Abrechnung_setzt_auf_der_vorigen_auf()
    {
        var id = await VorbereitetAsync();
        await AbrechnenAsync(id);

        await AblesenAsync(new DateOnly(2026, 11, 15), 1175.000m, id);

        var zweite = await AbrechnenAsync(id);

        Assert.Equal(new DateOnly(2026, 10, 31), zweite.FromOn);
        Assert.Equal(1100.000m, zweite.FromMeterKwh);
        Assert.Equal(75.000m, zweite.Kwh);
        Assert.Equal(15.00m, zweite.Amount);
    }

    /// <summary>
    /// Läuft die Abrechnung über einen Satzwechsel, wird abschnittsweise gerechnet.
    /// </summary>
    /// <remarks>
    /// Ein Mittelwert über den ganzen Zeitraum wäre eine Zahl, die in keiner Vereinbarung
    /// steht. Dass es keinen einen Satz gibt, sagt die Abrechnung, indem sie keinen nennt.
    /// </remarks>
    [Fact]
    public async Task Ueber_einen_Satzwechsel_wird_abschnittsweise_gerechnet()
    {
        var id = Arbeitsverhaeltnis();

        await SatzAsync(new DateOnly(2026, 1, 1), 0.2000m, id);
        await SatzAsync(new DateOnly(2026, 11, 1), 0.3000m, id);

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);
        await AblesenAsync(new DateOnly(2026, 11, 15), 1150.000m, id);

        var abrechnung = await AbrechnenAsync(id);

        // 100 kWh zu 0,20 € plus 50 kWh zu 0,30 € — nicht 150 kWh zu irgendeinem Mittel.
        Assert.Equal(150.000m, abrechnung.Kwh);
        Assert.Equal(35.00m, abrechnung.Amount);
        Assert.Null(abrechnung.EuroPerKwh);
    }

    /// <summary>
    /// Fehlt für einen Abschnitt der Satz, wird nicht festgeschrieben.
    /// </summary>
    /// <remarks>
    /// Das Blatt fürderte zu wenig — und der Zeitraum wäre verbraucht, weil die nächste
    /// Abrechnung dahinter ansetzt. Die fehlende Erstattung käme nie wieder.
    /// </remarks>
    [Fact]
    public async Task Ohne_Satz_wird_nicht_abgerechnet()
    {
        var id = Arbeitsverhaeltnis();

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);
        await AblesenAsync(new DateOnly(2026, 10, 31), 1100.000m, id);

        var fehler = await Assert.ThrowsAsync<ArgumentException>(() => AbrechnenAsync(id));

        Assert.Contains("kein Satz vereinbart", fehler.Message);
    }

    /// <summary>Zweimal dasselbe abzurechnen geht nicht — es ist nichts mehr offen.</summary>
    [Fact]
    public async Task Ohne_neue_Ablesung_gibt_es_nichts_abzurechnen()
    {
        var id = await VorbereitetAsync();
        await AbrechnenAsync(id);

        var fehler = await Assert.ThrowsAsync<ArgumentException>(() => AbrechnenAsync(id));

        Assert.Contains("nichts dazugekommen", fehler.Message);
    }

    /// <summary>
    /// Offen ist, was eingereicht und nicht erstattet ist.
    /// </summary>
    /// <remarks>
    /// Erfasst heißt: Sie liegt da, aber niemand wartet auf Geld. Erst mit dem Einreichen wird
    /// daraus eine Forderung — dieselbe Unterscheidung wie beim PKV-Vorgang.
    /// </remarks>
    [Fact]
    public async Task Offen_ist_erst_was_eingereicht_wurde()
    {
        var id = await VorbereitetAsync();
        var abrechnung = await AbrechnenAsync(id);

        Assert.Equal(0m, (await StandAsync(id)).OpenAmount);

        using (var context = database.Context())
        {
            await Dienst(context).AdvanceAsync(abrechnung.Id, ChargingStatementStatus.Submitted);
        }

        Assert.Equal(20.00m, (await StandAsync(id)).OpenAmount);

        using (var context = database.Context())
        {
            await Dienst(context).AdvanceAsync(abrechnung.Id, ChargingStatementStatus.Paid);
        }

        Assert.Equal(0m, (await StandAsync(id)).OpenAmount);
    }

    /// <summary>
    /// Nur die jüngste Abrechnung lässt sich zurücknehmen.
    /// </summary>
    /// <remarks>
    /// Eine aus der Mitte hinterließe einen Zeitraum, den keine Abrechnung mehr deckt — und die
    /// nächste holt ihn nicht nach, sie beginnt beim Endstand ihrer eigenen Vorgängerin. Die
    /// Lücke fällt erst auf, wenn die Jahressumme nicht mehr zum Zähler passt.
    /// </remarks>
    [Fact]
    public async Task Nur_die_juengste_Abrechnung_laesst_sich_zuruecknehmen()
    {
        var id = await VorbereitetAsync();
        var erste = await AbrechnenAsync(id);

        await AblesenAsync(new DateOnly(2026, 11, 15), 1175.000m, id);
        var zweite = await AbrechnenAsync(id);

        using (var context = database.Context())
        {
            var fehler = await Assert.ThrowsAsync<ArgumentException>(
                () => Dienst(context).DeleteStatementAsync(erste.Id));

            Assert.Contains("jüngste", fehler.Message);
        }

        using (var context = database.Context())
        {
            Assert.True(await Dienst(context).DeleteStatementAsync(zweite.Id));
        }

        var stand = await StandAsync(id);

        Assert.Single(stand.Statements);
        Assert.True(stand.Statements[0].CanDelete);
    }

    /// <summary>
    /// Eine abgerechnete Ablesung lässt sich nicht mehr löschen.
    /// </summary>
    /// <remarks>
    /// Sie steht auf einem Blatt beim Arbeitgeber. Sie zu entfernen hieße, dass der Zähler
    /// hier etwas anderes sagt als das Papier dort.
    /// </remarks>
    [Fact]
    public async Task Eine_abgerechnete_Ablesung_bleibt_stehen()
    {
        var id = await VorbereitetAsync();
        await AbrechnenAsync(id);

        var stand = await StandAsync(id);
        var juengste = stand.Readings[0].Id;

        using var context = database.Context();
        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => Dienst(context).DeleteReadingAsync(juengste));

        Assert.Contains("Abrechnung", fehler.Message);
    }

    /// <summary>Eine Ablesung nach der letzten Abrechnung lässt sich weiter löschen.</summary>
    /// <remarks>
    /// Die Gegenprobe. Ein Schutz, der alles festhält, wäre so unbrauchbar wie keiner — die
    /// eben eingetippte Zahl muss sich berichtigen lassen.
    /// </remarks>
    [Fact]
    public async Task Eine_Ablesung_nach_der_Abrechnung_laesst_sich_loeschen()
    {
        var id = await VorbereitetAsync();
        await AbrechnenAsync(id);

        var spaeter = await AblesenAsync(new DateOnly(2026, 11, 15), 1175.000m, id);

        using var context = database.Context();

        Assert.True(await Dienst(context).DeleteReadingAsync(spaeter.Id));
    }

    /// <summary>
    /// Die Vorschau sagt dasselbe wie die Abrechnung, die daraus wird.
    /// </summary>
    /// <remarks>
    /// Sie rechnet mit demselben Code — diese Prüfung hält fest, dass das so bleibt. Eine
    /// Vorschau, die etwas anderes nennt als die Tat, ist schlimmer als keine.
    /// </remarks>
    [Fact]
    public async Task Die_Vorschau_nennt_dieselben_Zahlen_wie_die_Abrechnung()
    {
        var id = await VorbereitetAsync();

        var vorschau = (await StandAsync(id)).Next;

        Assert.True(vorschau.Possible);
        Assert.Equal(new DateOnly(2026, 9, 30), vorschau.FromOn);
        Assert.Equal(new DateOnly(2026, 10, 31), vorschau.ToOn);
        Assert.Equal(100.000m, vorschau.Kwh);
        Assert.Equal(20.00m, vorschau.Amount);

        var abrechnung = await AbrechnenAsync(id);

        Assert.Equal(vorschau.Kwh, abrechnung.Kwh);
        Assert.Equal(vorschau.Amount, abrechnung.Amount);
    }

    /// <summary>Die Vorschau sagt auch, warum gerade nichts geht.</summary>
    [Fact]
    public async Task Die_Vorschau_nennt_das_Hindernis()
    {
        var id = Arbeitsverhaeltnis();

        Assert.Contains("keine Ablesung", (await StandAsync(id)).Next.Obstacle);

        await AblesenAsync(new DateOnly(2026, 9, 30), 1000.000m, id);

        Assert.Contains("zweite Ablesung", (await StandAsync(id)).Next.Obstacle);
    }

    /// <summary>
    /// Eine zugeordnete Buchung schließt die Abrechnung und trägt ihren Tag ein.
    /// </summary>
    /// <remarks>
    /// Der Zahltag kommt von der Buchung und nicht von der Uhr: Zugeordnet wird oft Wochen
    /// später, und dann stünde dort der Tag des Zuordnens statt der des Geldes.
    /// </remarks>
    [Fact]
    public async Task Eine_zugeordnete_Buchung_schliesst_die_Abrechnung()
    {
        var id = await VorbereitetAsync();
        var abrechnung = await AbrechnenAsync(id);

        int buchungId;
        using (var context = database.Context())
        {
            var konto = new Account
            {
                Name = "Girokonto",
                ShortName = "Giro",
                BankName = "Musterbank",
                Kind = AccountKind.Checking,
            };
            context.Accounts.Add(konto);
            context.SaveChanges();

            var buchung = new Transaction
            {
                AccountId = konto.Id,
                BookingDate = new DateOnly(2026, 11, 28),
                Payee = "Musterbetrieb GmbH",
                Purpose = "Erstattung Ladestrom",
                Kind = TransactionKind.Income,
                Amount = 20.00m,
                CreatedAt = clock.Now,
            };
            context.Transactions.Add(buchung);
            context.SaveChanges();
            buchungId = buchung.Id;
        }

        using (var context = database.Context())
        {
            var geschlossen = await Dienst(context).LinkPaymentAsync(abrechnung.Id, buchungId);

            Assert.NotNull(geschlossen);
            Assert.Equal(ChargingStatementStatus.Paid, geschlossen.Status);
            Assert.Equal(buchungId, geschlossen.TransactionId);
            Assert.Equal(new DateTime(2026, 11, 28), geschlossen.PaidAt);
        }

        Assert.Equal(0m, (await StandAsync(id)).OpenAmount);
    }

    public void Dispose() => database.Dispose();
}
