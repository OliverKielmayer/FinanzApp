using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Tests;

/// <summary>
/// Ausgeführte Orders und was daraus folgt — v5-Handoff, Abschnitt 11.
/// </summary>
/// <remarks>
/// Zwei Dinge tragen diesen Bau: dieselbe Datei zweimal einzulesen darf das Depot nicht
/// verdoppeln, und der Depotwert hat genau eine Quelle. Der Prototyp führte gepflegte
/// Positionen neben importierten Ausführungen und wies dieselbe ISIN mit drei Stückzahlen aus;
/// der falsche Wert lief über das Finanzvermögen bis ins Gesamtvermögen netto.
/// </remarks>
public sealed class DepotTradeTests : IDisposable
{
    private readonly TestDatabase database = new();
    private int depotId;

    public DepotTradeTests()
    {
        using var context = database.Context();

        var depot = new Depot { Name = "finanzen.net ZERO", Broker = "Baader Bank" };
        context.Depots.Add(depot);
        context.SaveChanges();

        depotId = depot.Id;
    }

    private DepotTradeService Service() => new(database.Context(), new OrderCsvParser());

    private PortfolioService Portfolio() => new(database.Context());

    private const string Header =
        "Name;ISIN;WKN;Anzahl;Anzahl storniert;Status;Orderart;Limit;Stop;Erstellt Datum;"
        + "Erstellt Zeit;Gültig bis;Richtung;Wert;Wert storniert;Mindermengenzuschlag;"
        + "Ausführung Datum;Ausführung Zeit;Ausführung Kurs;Anzahl ausgeführt;Anzahl offen;"
        + "Gestrichen Datum;Gestrichen Zeit";

    private static string Row(
        string datum, string zeit, string kurs, string stueck, string wert,
        string zuschlag = "0,00", string richtung = "Kauf", string status = "ausgeführt",
        string isin = "IE00B4L5Y983")
        => $"iShares Core MSCI World UCITS ETF;{isin};A0RPWH;{stueck};;{status};Markt;;;"
           + $"01.03.2024;12:00:00;30.04.2024;{richtung};{wert};;{zuschlag};"
           + $"{datum};{zeit};{kurs};{stueck};0;;";

    private Task<DepotImportResultDto> ImportAsync(params string[] rows)
        => Service().ImportAsync(
            depotId,
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows]))),
            "ZERO-orders.csv");

    /// <summary>Drei Käufe: 300 + 16 + 5 Stück, zusammen 26.529 + 1.427,81 + 456,64 plus 1 € Gebühr.</summary>
    private Task<DepotImportResultDto> DreiKaeufeAsync() => ImportAsync(
        Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"),
        Row("13.03.2024", "12:41:18", "89,238", "16", "-1.427,81"),
        Row("14.08.2026", "12:31:39", "129,50", "5", "-647,50", zuschlag: "1,00"));

    // ── Duplikate ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dieselbe Datei zweimal verdoppelt das Depot nicht.
    /// </summary>
    /// <remarks>
    /// Die Datei führt keine Ordernummer; wiedererkannt wird über Ausführungszeitpunkt, Stück
    /// und Kurs. Ohne das wäre der zweite Import ein stiller Vermögenszuwachs von hundert
    /// Prozent.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Datei_zweimal_bringt_beim_zweiten_Mal_nichts_Neues()
    {
        var erst = await DreiKaeufeAsync();
        var zweit = await DreiKaeufeAsync();

        Assert.Equal(3, erst.ImportedCount);
        Assert.Equal(0, erst.DuplicateCount);

        Assert.Equal(3, zweit.ReadCount);
        Assert.Equal(0, zweit.ImportedCount);
        Assert.Equal(3, zweit.DuplicateCount);

        using var context = database.Context();
        Assert.Equal(3, await context.DepotTrades.CountAsync());
    }

    /// <summary>Auch innerhalb einer Datei bleibt derselbe Satz einer.</summary>
    [Fact]
    public async Task Eine_Zeile_doppelt_in_derselben_Datei_wird_einmal_gebucht()
    {
        var ergebnis = await ImportAsync(
            Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"),
            Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"));

        Assert.Equal(1, ergebnis.ImportedCount);
        Assert.Equal(1, ergebnis.DuplicateCount);
    }

    /// <summary>
    /// Zwei echte Ausführungen zur selben Sekunde bleiben zwei.
    /// </summary>
    /// <remarks>
    /// Eine Teilausführung kann in derselben Sekunde zu verschiedenen Kursen laufen. Wer nur
    /// auf den Zeitstempel prüft, wirft die zweite weg — und das Depot ist zu klein.
    /// </remarks>
    [Fact]
    public async Task Zwei_Ausfuehrungen_zur_selben_Sekunde_mit_anderem_Kurs_bleiben_zwei()
    {
        var ergebnis = await ImportAsync(
            Row("11.03.2024", "08:05:46", "88,43", "100", "-8.843,00"),
            Row("11.03.2024", "08:05:46", "88,45", "200", "-17.690,00"));

        Assert.Equal(2, ergebnis.ImportedCount);
        Assert.Equal(0, ergebnis.DuplicateCount);
    }

    /// <summary>Eine nachgelieferte Datei bringt nur die neuen Sätze.</summary>
    [Fact]
    public async Task Eine_erweiterte_Datei_bringt_nur_die_neuen_Saetze()
    {
        await ImportAsync(Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"));

        var ergebnis = await ImportAsync(
            Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"),
            Row("13.03.2024", "12:41:18", "89,238", "16", "-1.427,81"));

        Assert.Equal(1, ergebnis.ImportedCount);
        Assert.Equal(1, ergebnis.DuplicateCount);
    }

    [Fact]
    public async Task Nicht_ausgefuehrte_Zeilen_werden_gemeldet_statt_verschwiegen()
    {
        var ergebnis = await ImportAsync(
            Row("11.03.2024", "08:05:46", "88,43", "300", "-26.529,00"),
            Row("12.03.2024", "09:00:00", "88,00", "10", "-880,00", status: "storniert"));

        Assert.Equal(1, ergebnis.ImportedCount);
        Assert.Single(ergebnis.Skipped);
        Assert.Contains("storniert", ergebnis.Skipped[0].Reason);
    }

    // ── Der Kopf ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Der_Einstand_enthaelt_die_Gebuehr_und_weist_sie_getrennt_aus()
    {
        await DreiKaeufeAsync();

        var kopf = (await Service().GetAsync(depotId)).Head;

        Assert.Equal(321m, kopf.Quantity);
        Assert.Equal(26_529.00m + 1_427.81m + 647.50m + 1.00m, kopf.CostBasis);
        Assert.Equal(1.00m, kopf.Fees);
        Assert.Equal(3, kopf.ExecutionCount);
        Assert.Equal(3, kopf.BuyCount);
        Assert.Equal(0, kopf.SellCount);
    }

    /// <summary>Der Kurs stammt aus der letzten Ausführung — belegbar, kein Live-Kurs.</summary>
    [Fact]
    public async Task Der_letzte_Kurs_stammt_aus_der_letzten_Ausfuehrung()
    {
        await DreiKaeufeAsync();

        var kopf = (await Service().GetAsync(depotId)).Head;

        Assert.Equal(129.50m, kopf.LastPrice);
        Assert.Equal(new DateTime(2026, 8, 14, 12, 31, 39), kopf.LastPriceAt);
        Assert.Equal(321m * 129.50m, kopf.CurrentValue);
    }

    /// <summary>
    /// Ein Verkauf mindert den Einstand anteilig und erzeugt einen realisierten Gewinn.
    /// </summary>
    /// <remarks>
    /// Anteilig zum durchschnittlichen Anschaffungspreis, nicht zum Verkaufskurs. Sonst
    /// verschöbe jeder Verkauf den Einstand des Rests und damit jeden Gewinn danach.
    /// </remarks>
    [Fact]
    public async Task Ein_Verkauf_mindert_den_Einstand_anteilig()
    {
        await ImportAsync(
            Row("11.03.2024", "08:05:46", "100,00", "100", "-10.000,00"),
            Row("11.03.2025", "10:00:00", "150,00", "40", "6.000,00", richtung: "Verkauf"));

        var kopf = (await Service().GetAsync(depotId)).Head;

        Assert.Equal(60m, kopf.Quantity);

        // 40 von 100 Stück gehen ab, also 40 % des Einstands: 10.000 − 4.000 = 6.000.
        Assert.Equal(6_000m, kopf.CostBasis);
        Assert.Equal(6_000m - 4_000m, kopf.RealisedGain);
    }

    // ── Die Positionen ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Liegen Ausführungen vor, gewinnen sie gegen jede gepflegte Position.
    /// </summary>
    /// <remarks>
    /// Genau hier ist der Prototyp gescheitert: die Positionsliste führte weiter vier
    /// Beispielwertpapiere, während die Transaktionen etwas anderes auswiesen — dieselbe ISIN
    /// mit zwei Wahrheiten auf einem Screen.
    /// </remarks>
    [Fact]
    public async Task Importierte_Ausfuehrungen_schlagen_gepflegte_Positionen()
    {
        using (var context = database.Context())
        {
            context.PortfolioPositions.Add(new PortfolioPosition
            {
                DepotId = depotId, Name = "Erfundenes Papier", Isin = "IE00B4L5Y983",
                Quantity = 386m, Price = 102.15m, CostBasis = 30_000m,
                PriceAsOf = new DateTime(2026, 8, 22),
            });

            context.SaveChanges();
        }

        await DreiKaeufeAsync();

        var depot = await Portfolio().GetAsync();

        Assert.NotNull(depot);
        Assert.Single(depot.Positions);
        Assert.Equal(321m, depot.Positions[0].Quantity);
        Assert.Equal(321m * 129.50m, depot.TotalValue);
        Assert.True(depot.PricesFromTrades);
    }

    /// <summary>Ohne Ausführungen bleibt es bei den gepflegten Positionen.</summary>
    [Fact]
    public async Task Ohne_Ausfuehrungen_zaehlen_die_gepflegten_Positionen()
    {
        using (var context = database.Context())
        {
            context.PortfolioPositions.Add(new PortfolioPosition
            {
                DepotId = depotId, Name = "Von Hand", Isin = "IE00B4L5Y983",
                Quantity = 10m, Price = 100m, CostBasis = 900m,
                PriceAsOf = new DateTime(2026, 8, 22),
            });

            context.SaveChanges();
        }

        var depot = await Portfolio().GetAsync();

        Assert.NotNull(depot);
        Assert.Equal(1_000m, depot.TotalValue);
        Assert.False(depot.PricesFromTrades);
    }

    /// <summary>
    /// Der Depotwert im Vermögen ist dieselbe Zahl wie im Depot-Hero.
    /// </summary>
    /// <remarks>
    /// „Der Depotwert hat eine Quelle“ — sonst floss ein falscher Wert über das Finanzvermögen
    /// bis ins Gesamtvermögen netto, und niemand sah, wo er herkam.
    /// </remarks>
    [Fact]
    public async Task Depot_Hero_und_Vermoegensaufstellung_nennen_dieselbe_Zahl()
    {
        await DreiKaeufeAsync();

        var depot = await Portfolio().GetAsync();

        Assert.NotNull(depot);
        Assert.Equal(depot.TotalValue, await Portfolio().GetTotalValueAsync());
    }

    /// <summary>Ein vollständig verkauftes Papier steht in keiner Positionsliste mehr.</summary>
    [Fact]
    public async Task Ein_vollstaendig_verkauftes_Papier_verschwindet_aus_den_Positionen()
    {
        await ImportAsync(
            Row("11.03.2024", "08:05:46", "100,00", "100", "-10.000,00"),
            Row("11.03.2025", "10:00:00", "150,00", "100", "15.000,00", richtung: "Verkauf"));

        var depot = await Portfolio().GetAsync();

        Assert.NotNull(depot);
        Assert.Empty(depot.Positions);
        Assert.Equal(0m, depot.TotalValue);
    }

    // ── Der Jahresfilter ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Der_Jahresfilter_zeigt_nur_sein_Jahr_und_rechnet_den_Kopf_trotzdem_ganz()
    {
        await DreiKaeufeAsync();

        var alles = await Service().GetAsync(depotId);
        var nur2024 = await Service().GetAsync(depotId, 2024);

        Assert.Equal(3, alles.Trades.Count);
        Assert.Equal(2, nur2024.Trades.Count);

        // Der Kopf bleibt der des ganzen Depots — sonst hätte jeder Filter seinen eigenen
        // Einstand, und die Prozente daneben verglichen Verschiedenes.
        Assert.Equal(alles.Head.CostBasis, nur2024.Head.CostBasis);
        Assert.Equal(321m, nur2024.Head.Quantity);
    }

    public void Dispose() => database.Dispose();
}
