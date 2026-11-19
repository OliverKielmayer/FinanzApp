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

    public void Dispose() => database.Dispose();
}
