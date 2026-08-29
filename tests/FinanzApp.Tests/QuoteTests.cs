using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Kurszeitreihe und Abruf — v5-Handoff, Abschnitt 16.
/// </summary>
/// <remarks>
/// <para>Die tragende Entscheidung: <b>der Verlauf ist die Datenhaltung, nicht die API.</b> Die
/// hier geprüften Regeln folgen alle daraus — bewertet wird aus der gespeicherten Reihe, ein
/// Ausfall der Quelle ändert daran nichts, und ein zweiter Abruf desselben Tages verdoppelt
/// keinen Punkt.</para>
/// <para>Der zweitwichtigste Test betrifft die Einstandslinie: sie darf nur gezeichnet werden,
/// wenn der Einstand im dargestellten Kursbereich liegt. Am Rand geklemmt behauptet sie eine
/// Größenrelation, die es nicht gibt.</para>
/// </remarks>
public sealed class QuoteTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 29, 18, 5);

    private readonly int depotId;

    public QuoteTests()
    {
        using var context = database.Context();

        var depot = new Depot { Name = "Musterdepot", Broker = "Musterbank" };
        context.Depots.Add(depot);
        context.SaveChanges();

        depotId = depot.Id;
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Quelle, die liefert, was der Test vorgibt.</summary>
    private sealed class StubSource(params QuoteAttempt[] antworten) : IQuoteSource
    {
        private int i;

        public string Name => "Teststelle";

        /// <summary>Wie oft gefragt wurde — die Schonung der Gegenseite ist prüfbar.</summary>
        public int Calls { get; private set; }

        public Task<QuoteAttempt> FetchAsync(string isin, CancellationToken ct = default)
        {
            Calls++;
            var antwort = antworten[Math.Min(i, antworten.Length - 1)];
            i++;

            return Task.FromResult(antwort);
        }
    }

    private QuoteService Service(IQuoteSource? source = null)
        => TestDatabase.Quotes(database.Context(), clock, source);

    private static QuoteAttempt Reading(string isin, string tag, decimal kurs)
        => QuoteAttempt.Found(new QuoteReading(isin, DateOnly.Parse(tag), kurs, "EUR", "Teststelle"));

    private void Trade(string isin, string tag, decimal stueck, decimal kurs, decimal wert)
    {
        using var context = database.Context();
        context.DepotTrades.Add(new DepotTrade
        {
            DepotId = depotId,
            SecurityName = "Musterfonds",
            Isin = isin,
            Kind = DepotTradeKind.Buy,
            ExecutedAt = DateTime.Parse(tag + "T10:00:00"),
            Quantity = stueck,
            Price = kurs,
            Value = wert,
            ImportReference = $"T:{isin}:{tag}:{stueck}",
        });

        context.SaveChanges();
    }

    private void Statement(string isin, string stichtag, decimal stueck, decimal kurs)
    {
        using var context = database.Context();
        var aufstellung = new DepotStatement { DepotId = depotId, AsOf = DateOnly.Parse(stichtag) };
        context.DepotStatements.Add(aufstellung);
        context.SaveChanges();

        context.DepotStatementPositions.Add(new DepotStatementPosition
        {
            StatementId = aufstellung.Id,
            SecurityName = "Musterfonds",
            Isin = isin,
            Quantity = stueck,
            Price = kurs,
            Value = decimal.Round(stueck * kurs, 2),
        });

        context.SaveChanges();
    }

    private void Quote(string isin, string tag, decimal kurs, string quelle = "Teststelle")
    {
        using var context = database.Context();
        context.Quotes.Add(new Quote
        {
            Isin = isin,
            Date = DateOnly.Parse(tag),
            Close = kurs,
            Currency = "EUR",
            Source = quelle,
            FetchedAt = clock.Now,
        });

        context.SaveChanges();
    }

    // ── Der Verlauf gehört der Anwendung ───────────────────────────────────────────────────

    /// <summary>
    /// Ein abgerufener Kurs landet in der Reihe, mit seiner Herkunft.
    /// </summary>
    [Fact]
    public async Task Ein_Abruf_schreibt_die_Reihe_fort()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        var ergebnis = await Service(new StubSource(Reading("IE00TEST0001", "2026-08-28", 127.50m)))
            .RefreshAsync(manual: true);

        Assert.Equal(1, ergebnis.Stored);
        Assert.Contains("Verlauf ergänzt", ergebnis.Message);

        using var context = database.Context();
        var kurs = context.Quotes.Single(q => q.Date == new DateOnly(2026, 8, 28));

        Assert.Equal(127.50m, kurs.Close);
        Assert.Equal("Teststelle", kurs.Source);
        Assert.Equal("EUR", kurs.Currency);
    }

    /// <summary>
    /// Ein zweiter Abruf desselben Tages aktualisiert, statt zu verdoppeln.
    /// </summary>
    /// <remarks>
    /// Abschnitt 16.5. Ohne diese Regel wüchse die Reihe mit jedem Knopfdruck, und der Chart
    /// zeigte für einen Tag ein Bündel Punkte.
    /// </remarks>
    [Fact]
    public async Task Derselbe_Tag_wird_aktualisiert_nicht_verdoppelt()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        await Service(new StubSource(Reading("IE00TEST0001", "2026-08-28", 127.50m)))
            .RefreshAsync(manual: true);

        await Service(new StubSource(Reading("IE00TEST0001", "2026-08-28", 128.10m)))
            .RefreshAsync(manual: true);

        using var context = database.Context();
        var kurse = context.Quotes.Where(q => q.Date == new DateOnly(2026, 8, 28)).ToList();

        Assert.Single(kurse);
        Assert.Equal(128.10m, kurse[0].Close);
    }

    /// <summary>
    /// Fällt die Quelle aus, bleibt die Reihe stehen und die Bewertung rechnet weiter.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Grund für die eigene Datenhaltung. Beide in Frage kommenden Anbieter
    /// sind inoffiziell; eine Anwendung, die ihre Vermögenszahlen daran hängt, verliert sie beim
    /// ersten Umbau der Gegenseite.
    /// </remarks>
    [Fact]
    public async Task Ein_Ausfall_der_Quelle_nimmt_keinen_Kurs_weg()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);
        Quote("IE00TEST0001", "2026-08-27", 126.40m);

        var ergebnis = await Service(new StubSource(QuoteAttempt.Failed("Die Quelle antwortet nicht.")))
            .RefreshAsync(manual: true);

        Assert.Equal(0, ergebnis.Stored);
        Assert.Equal(1, ergebnis.Failed);
        Assert.Equal(QuoteState.Failed, ergebnis.Band.State);

        // Der Kurs steht weiter da, und die Bewertung nimmt ihn.
        Assert.Equal(new DateOnly(2026, 8, 27), ergebnis.Band.LatestDate);

        var bestand = await TestDatabase.Portfolio(database.Context()).GetHoldingsAsync(depotId);
        Assert.Equal(1264.00m, bestand.Value);
    }

    // ── Bewertung ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bewertet wird mit dem jüngsten gespeicherten Kurs, nicht mit dem Ausführungspreis.
    /// </summary>
    [Fact]
    public async Task Der_juengste_gespeicherte_Kurs_bewertet()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);
        Quote("IE00TEST0001", "2026-08-20", 110m);
        Quote("IE00TEST0001", "2026-08-28", 120m);

        var bestand = await TestDatabase.Portfolio(database.Context()).GetHoldingsAsync(depotId);

        Assert.Equal(1200m, bestand.Value);
        Assert.Equal(new DateTime(2026, 8, 28), bestand.PricedAt);
    }

    /// <summary>
    /// Ein älterer Kurs verdrängt keinen frischeren Ausführungspreis.
    /// </summary>
    /// <remarks>
    /// Sonst sähe der Depotwert nach einem Kauf plötzlich älter aus, als er ist — obwohl gerade
    /// ein belegter Preis dazugekommen war.
    /// </remarks>
    [Fact]
    public async Task Ein_alter_Kurs_verdraengt_keine_frische_Ausfuehrung()
    {
        Quote("IE00TEST0001", "2026-07-01", 90m);
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        var bestand = await TestDatabase.Portfolio(database.Context()).GetHoldingsAsync(depotId);

        Assert.Equal(1000m, bestand.Value);
    }

    /// <summary>Ohne jeden Kurs bleibt es beim Preis der letzten Ausführung.</summary>
    [Fact]
    public async Task Ohne_Kurs_gilt_die_letzte_Ausfuehrung()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        var bestand = await TestDatabase.Portfolio(database.Context()).GetHoldingsAsync(depotId);

        Assert.Equal(1000m, bestand.Value);
    }

    // ── Aus dem eigenen Bestand ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ausführungen und Bestandsnachweise sind Kurse — und werden es auch.
    /// </summary>
    /// <remarks>
    /// Die frei zugängliche Quelle gibt keine Vergangenheit heraus. Ohne diesen Rückgriff bliebe
    /// der Chart monatelang leer, obwohl die Anwendung die Punkte hat.
    /// </remarks>
    [Fact]
    public async Task Der_Verlauf_entsteht_aus_Ausfuehrungen_und_Nachweisen()
    {
        Trade("IE00TEST0001", "2026-03-02", 5m, 95m, 475m);
        Trade("IE00TEST0001", "2026-05-04", 5m, 105m, 525m);
        Statement("IE00TEST0001", "2026-06-30", 10m, 112.50m);

        var neu = await Service().BackfillAsync();

        Assert.Equal(3, neu);

        var reihe = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, null);

        Assert.Equal(3, reihe.Points.Count);
        Assert.Equal(new DateOnly(2026, 3, 2), reihe.Points[0].Date);
        Assert.Equal(112.50m, reihe.Points[^1].Close);
        Assert.Equal(["Ausführung", "Bestandsnachweis"], reihe.Sources);
    }

    /// <summary>Der Nachtrag ist wiederholbar und legt nichts doppelt an.</summary>
    [Fact]
    public async Task Der_Nachtrag_laeuft_zweimal_ohne_Schaden()
    {
        Trade("IE00TEST0001", "2026-03-02", 5m, 95m, 475m);

        Assert.Equal(1, await Service().BackfillAsync());
        Assert.Equal(0, await Service().BackfillAsync());

        using var context = database.Context();
        Assert.Single(context.Quotes);
    }

    /// <summary>
    /// Ein abgerufener Kurs wird vom Nachtrag nicht überschrieben.
    /// </summary>
    /// <remarks>
    /// Ein Börsenschlusskurs ist belastbarer als der Preis einer einzelnen Ausführung am selben
    /// Tag.
    /// </remarks>
    [Fact]
    public async Task Der_Nachtrag_ueberschreibt_keinen_abgerufenen_Kurs()
    {
        Quote("IE00TEST0001", "2026-03-02", 96.80m, "Börse Frankfurt");
        Trade("IE00TEST0001", "2026-03-02", 5m, 95m, 475m);

        await Service().BackfillAsync();

        using var context = database.Context();
        var kurs = context.Quotes.Single();

        Assert.Equal(96.80m, kurs.Close);
        Assert.Equal("Börse Frankfurt", kurs.Source);
    }

    // ── Die Einstandslinie ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Einstandslinie erscheint nur, wenn der Einstand im Kursbereich liegt.
    /// </summary>
    /// <remarks>
    /// Die Regel, an der der Prototyp zuerst gebrochen ist: an den Rand geklemmt sah die Kurve
    /// knapp über der Linie aus, tatsächlich lagen 33 % dazwischen.
    /// </remarks>
    [Fact]
    public async Task Die_Einstandslinie_erscheint_nur_im_Bereich()
    {
        Quote("IE00TEST0001", "2026-06-01", 120m);
        Quote("IE00TEST0001", "2026-07-01", 130m);

        var drin = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, 125m);
        var drunter = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, 97.10m);
        var drueber = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, 200m);

        Assert.True(drin.CostInRange);
        Assert.False(drunter.CostInRange);
        Assert.False(drueber.CostInRange);
    }

    /// <summary>Ohne bekannten Einstand gibt es keine Linie und keine Behauptung darüber.</summary>
    [Fact]
    public async Task Ohne_Einstand_keine_Linie()
    {
        Quote("IE00TEST0001", "2026-06-01", 120m);

        var reihe = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, null);

        Assert.False(reihe.CostInRange);
        Assert.Null(reihe.AboveCostSince);
    }

    /// <summary>
    /// „Über Einstand seit“ meint die letzte ununterbrochene Strecke.
    /// </summary>
    /// <remarks>
    /// Irgendein früherer Tag über dem Einstand sagt nichts, wenn die Kurve danach wieder
    /// darunter war.
    /// </remarks>
    [Fact]
    public async Task Ueber_Einstand_seit_zaehlt_nur_die_letzte_Strecke()
    {
        Quote("IE00TEST0001", "2026-01-01", 110m);
        Quote("IE00TEST0001", "2026-02-01", 90m);
        Quote("IE00TEST0001", "2026-03-01", 105m);
        Quote("IE00TEST0001", "2026-04-01", 115m);

        var reihe = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, 100m);

        Assert.Equal(new DateOnly(2026, 3, 1), reihe.AboveCostSince);
    }

    [Fact]
    public async Task Unter_dem_Einstand_gibt_es_kein_Seit()
    {
        Quote("IE00TEST0001", "2026-03-01", 105m);
        Quote("IE00TEST0001", "2026-04-01", 95m);

        var reihe = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.All, 100m);

        Assert.Null(reihe.AboveCostSince);
    }

    // ── Zeitraum ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Zeitraum rechnet vom jüngsten Kurs, nicht von heute.
    /// </summary>
    /// <remarks>
    /// Sonst zeigte „1 Monat“ nach einer Woche ohne Abruf einen leeren Chart, obwohl Kurse da
    /// sind.
    /// </remarks>
    [Fact]
    public async Task Der_Zeitraum_misst_ab_dem_juengsten_Kurs()
    {
        Quote("IE00TEST0001", "2025-01-01", 80m);
        Quote("IE00TEST0001", "2026-05-01", 100m);
        Quote("IE00TEST0001", "2026-05-20", 110m);

        var monat = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.Month, null);

        Assert.Equal(2, monat.Points.Count);
        Assert.Equal(3, monat.StoredCount);
    }

    [Fact]
    public async Task Tief_und_Hoch_beziehen_sich_auf_den_Ausschnitt()
    {
        Quote("IE00TEST0001", "2025-01-01", 50m);
        Quote("IE00TEST0001", "2026-05-01", 100m);
        Quote("IE00TEST0001", "2026-05-20", 110m);

        var monat = await Service().GetSeriesAsync("IE00TEST0001", QuoteRange.Month, null);

        Assert.Equal(100m, monat.Low);
        Assert.Equal(110m, monat.High);
    }

    // ── Das Band ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ohne_Abruf_sagt_das_Band_dass_noch_nie_abgerufen_wurde()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        var band = await Service().GetBandAsync();

        Assert.Equal(QuoteState.Never, band.State);
        Assert.Null(band.FetchedAt);
    }

    /// <summary>
    /// Ein Durchgang ohne einen einzigen Kurs gilt als gescheitert.
    /// </summary>
    /// <remarks>
    /// „Erfolgreich, aber nichts geholt“ wäre für den Leser dasselbe wie ein Fehler und darf
    /// nicht anders aussehen.
    /// </remarks>
    [Fact]
    public async Task Ein_Durchgang_ohne_Ergebnis_gilt_als_gescheitert()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);

        var ergebnis = await Service(new StubSource(QuoteAttempt.Failed("Nichts da.")))
            .RefreshAsync(manual: true);

        Assert.Equal(QuoteState.Failed, ergebnis.Band.State);
        Assert.Equal("Nichts da.", ergebnis.Band.Problem);
    }

    /// <summary>Ein Fehlschlag bei einem Papier nimmt die anderen nicht mit.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Papier_stoppt_den_Durchgang_nicht()
    {
        Trade("IE00TEST0001", "2026-08-01", 10m, 100m, 1000m);
        Trade("IE00TEST0002", "2026-08-01", 5m, 50m, 250m);

        var quelle = new StubSource(
            QuoteAttempt.Failed("Unbekannt."),
            Reading("IE00TEST0002", "2026-08-28", 55m));

        var ergebnis = await Service(quelle).RefreshAsync(manual: true);

        Assert.Equal(2, quelle.Calls);
        Assert.Equal(1, ergebnis.Stored);
        Assert.Equal(1, ergebnis.Failed);
        Assert.Contains("1 ohne Ergebnis", ergebnis.Message);
    }

    /// <summary>
    /// Gefragt wird nur nach Papieren, die auch gehalten werden.
    /// </summary>
    /// <remarks>
    /// Jede überflüssige Anfrage an eine inoffizielle Quelle ist eine zu viel.
    /// </remarks>
    [Fact]
    public async Task Ohne_Bestand_wird_nicht_gefragt()
    {
        var quelle = new StubSource(Reading("IE00TEST0001", "2026-08-28", 127.50m));
        var ergebnis = await Service(quelle).RefreshAsync(manual: true);

        Assert.Equal(0, quelle.Calls);
        Assert.Contains("Kein Wertpapier", ergebnis.Message);
    }

    /// <summary>Ohne eingerichtete Quelle bleibt der Knopf weg.</summary>
    [Fact]
    public async Task Ohne_Quelle_gibt_es_nichts_abzurufen()
    {
        var band = await Service().GetBandAsync();

        Assert.False(band.CanFetch);
    }

    public void Dispose() => database.Dispose();
}
