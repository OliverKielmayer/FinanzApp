using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Quartalsaufstellungen und der Bestandsabgleich — v5-Handoff, Abschnitte 11.2 und 11.3.
/// </summary>
/// <remarks>
/// Der Abgleich ist der einzige Weg, eine Depotbewertung zu prüfen, ohne dem Broker blind zu
/// glauben: stimmen die Stückzahlen zum Stichtag, ist der ausgewiesene Wert belegt. Geprüft
/// wird darum vor allem, dass eine Abweichung auch als solche erkannt wird — ein Abgleich, der
/// immer „stimmt“ sagt, ist schlimmer als keiner.
/// </remarks>
public sealed class DepotStatementTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly int depotId;

    public DepotStatementTests()
    {
        using var context = database.Context();

        var depot = new Depot { Name = "finanzen.net ZERO", Broker = "Baader Bank" };
        context.Depots.Add(depot);
        context.SaveChanges();

        depotId = depot.Id;
    }

    private DepotStatementService Service() => new(database.Context());

    private const string Header =
        "Name;ISIN;WKN;Anzahl;Anzahl storniert;Status;Orderart;Limit;Stop;Erstellt Datum;"
        + "Erstellt Zeit;Gültig bis;Richtung;Wert;Wert storniert;Mindermengenzuschlag;"
        + "Ausführung Datum;Ausführung Zeit;Ausführung Kurs;Anzahl ausgeführt;Anzahl offen;"
        + "Gestrichen Datum;Gestrichen Zeit";

    private Task ImportAsync(params string[] rows)
        => new DepotTradeService(database.Context(), new OrderCsvParser()).ImportAsync(
            depotId,
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows]))),
            "ZERO-orders.csv");

    private static string Trade(
        string datum, string kurs, string stueck, string wert, string isin = "IE00B4L5Y983")
        => $"iShares Core MSCI World UCITS ETF;{isin};A0RPWH;{stueck};;ausgeführt;Markt;;;"
           + $"01.01.2024;10:00:00;01.01.2024;Kauf;{wert};;0,00;{datum};10:00:00;{kurs};{stueck};0;;";

    private Task<DepotStatementDto> StatementAsync(
        string stichtag, decimal stueck, decimal kurs, decimal wert,
        string? erstellt = null, string isin = "IE00B4L5Y983")
        => Service().CreateAsync(depotId, new CreateDepotStatementRequest
        {
            AsOf = DateOnly.Parse(stichtag),
            IssuedOn = erstellt is null ? null : DateOnly.Parse(erstellt),
            DepotNumber = "1234567",
            Custodian = "Baader Bank AG · Clearstream Frankfurt",
            Positions =
            [
                new CreateDepotStatementPosition
                {
                    SecurityName = "iShares Core MSCI World UCITS ETF",
                    Isin = isin,
                    Wkn = "A0RPWH",
                    Quantity = stueck,
                    Price = kurs,
                    Value = wert,
                    SafeCustody = "Girosammelverwahrung",
                    Country = "Deutschland",
                },
            ],
        });

    // ── Die Aufstellung selbst ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Stichtag und Erstellungsdatum sind zwei verschiedene Daten.
    /// </summary>
    /// <remarks>
    /// Ein Schreiben vom Mai über den Bestand vom März sagt etwas über den März. Dieselbe Regel
    /// wie beim Statusreport der Lebensversicherung.
    /// </remarks>
    [Fact]
    public async Task Stichtag_und_Erstellungsdatum_bleiben_getrennt()
    {
        var s = await StatementAsync("2024-03-31", 321m, 91.55m, 29_389.00m, erstellt: "2024-05-14");

        Assert.Equal(new DateOnly(2024, 3, 31), s.AsOf);
        Assert.Equal(new DateOnly(2024, 5, 14), s.IssuedOn);
        Assert.Equal(29_389.00m, s.Value);
    }

    [Fact]
    public async Task Ein_Erstellungsdatum_vor_dem_Stichtag_wird_abgewiesen()
        => await Assert.ThrowsAsync<RuleViolationException>(
            () => StatementAsync("2024-03-31", 321m, 91.55m, 29_389m, erstellt: "2024-03-01"));

    [Fact]
    public async Task Zwei_Aufstellungen_zum_selben_Stichtag_werden_abgewiesen()
    {
        await StatementAsync("2024-03-31", 321m, 91.55m, 29_389m);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => StatementAsync("2024-03-31", 999m, 91.55m, 91_458m));

        Assert.Contains("31.03.2024", fehler.Message);
    }

    [Fact]
    public async Task Eine_Aufstellung_ohne_Position_wird_abgewiesen()
        => await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().CreateAsync(depotId, new CreateDepotStatementRequest
            {
                AsOf = new DateOnly(2024, 3, 31),
                Positions = [],
            }));

    // ── Der Abgleich ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stimmen die Stückzahlen, ist der Depotwert belegt.
    /// </summary>
    /// <remarks>
    /// Die Zahlen sind die des Handoffs: 321 Stück, 29.389 € ausgewiesen gegen 28.413 €
    /// Einstand, Buchgewinn +976 €.
    /// </remarks>
    [Fact]
    public async Task Bei_gleicher_Stueckzahl_ist_der_Wert_belegt()
    {
        await ImportAsync(
            Trade("11.03.2024", "88,43", "300", "-26.529,00"),
            Trade("13.03.2024", "89,238", "16", "-1.427,81"),
            Trade("20.03.2024", "91,12", "5", "-455,60"));

        await StatementAsync("2024-03-31", 321m, 91.55m, 29_389.00m);

        var abgleich = (await Service().GetAsync(depotId)).Reconciliation;

        Assert.NotNull(abgleich);
        Assert.True(abgleich.Matches);
        Assert.Equal(321m, abgleich.Rows.Single().StatementQuantity);
        Assert.Equal(321m, abgleich.Rows.Single().TradeQuantity);
        Assert.Equal(0m, abgleich.Rows.Single().Difference);

        Assert.Equal(29_389.00m, abgleich.StatementValue);
        Assert.Equal(26_529.00m + 1_427.81m + 455.60m, abgleich.TradeCost);
        Assert.Equal(976.59m, abgleich.BookGain);
    }

    /// <summary>
    /// Eine Abweichung wird als solche erkannt.
    /// </summary>
    /// <remarks>
    /// Das ist der Zweck des ganzen Blocks. Meist fehlen Käufe aus einer nicht importierten
    /// Datei — und dann ist jede Zahl darüber unsicher, auch der Depotwert im Gesamtvermögen.
    /// </remarks>
    [Fact]
    public async Task Eine_fehlende_Ausfuehrung_faellt_auf()
    {
        await ImportAsync(Trade("11.03.2024", "88,43", "300", "-26.529,00"));
        await StatementAsync("2024-03-31", 321m, 91.55m, 29_389.00m);

        var abgleich = (await Service().GetAsync(depotId)).Reconciliation;

        Assert.NotNull(abgleich);
        Assert.False(abgleich.Matches);
        Assert.Equal(-21m, abgleich.Rows.Single().Difference);
    }

    /// <summary>
    /// Gezählt wird bis zum Stichtag, einschließlich.
    /// </summary>
    /// <remarks>
    /// Eine Ausführung am Stichtag selbst steht im Bestand der Bank. Wer sie ausließe, fände
    /// zu jedem Quartalswechsel eine Differenz, die es nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Eine_Ausfuehrung_am_Stichtag_zaehlt_noch_dazu()
    {
        await ImportAsync(
            Trade("11.03.2024", "88,43", "300", "-26.529,00"),
            Trade("31.03.2024", "91,12", "21", "-1.913,52"));

        await StatementAsync("2024-03-31", 321m, 91.55m, 29_389.00m);

        Assert.True((await Service().GetAsync(depotId)).Reconciliation!.Matches);
    }

    /// <summary>Was nach dem Stichtag kam, gehört nicht in diesen Bestand.</summary>
    [Fact]
    public async Task Eine_spaetere_Ausfuehrung_verfaelscht_den_Abgleich_nicht()
    {
        await ImportAsync(
            Trade("11.03.2024", "88,43", "300", "-26.529,00"),
            Trade("02.04.2024", "92,00", "50", "-4.600,00"));

        await StatementAsync("2024-03-31", 300m, 91.55m, 27_465.00m);

        var abgleich = (await Service().GetAsync(depotId)).Reconciliation;

        Assert.True(abgleich!.Matches);
        Assert.Equal(1, abgleich.TradeCount);
    }

    /// <summary>
    /// Verglichen wird je Wertpapier, nicht über die Summe der Stücke.
    /// </summary>
    /// <remarks>
    /// Stückzahlen verschiedener Papiere zu addieren ergäbe eine Zahl ohne Bedeutung — und zwei
    /// Fehler, die sich gegenseitig aufheben, blieben unsichtbar.
    /// </remarks>
    [Fact]
    public async Task Zwei_Papiere_werden_einzeln_verglichen()
    {
        await ImportAsync(
            Trade("11.03.2024", "100,00", "100", "-10.000,00"),
            Trade("12.03.2024", "50,00", "100", "-5.000,00", isin: "IE00BK5BQT80"));

        await Service().CreateAsync(depotId, new CreateDepotStatementRequest
        {
            AsOf = new DateOnly(2024, 3, 31),
            Positions =
            [
                new CreateDepotStatementPosition
                {
                    SecurityName = "A", Isin = "IE00B4L5Y983",
                    Quantity = 110m, Price = 100m, Value = 11_000m,
                },
                new CreateDepotStatementPosition
                {
                    SecurityName = "B", Isin = "IE00BK5BQT80",
                    Quantity = 90m, Price = 50m, Value = 4_500m,
                },
            ],
        });

        var abgleich = (await Service().GetAsync(depotId)).Reconciliation;

        // In der Summe wären es 200 gegen 200 — je Papier fehlen zehn und stehen zehn zu viel.
        Assert.False(abgleich!.Matches);
        Assert.Equal(-10m, abgleich.Rows.Single(r => r.Isin == "IE00B4L5Y983").Difference);
        Assert.Equal(10m, abgleich.Rows.Single(r => r.Isin == "IE00BK5BQT80").Difference);
    }

    /// <summary>Ein Papier, das nur die Bank kennt, taucht mit Null auf unserer Seite auf.</summary>
    [Fact]
    public async Task Ein_nur_ausgewiesenes_Papier_steht_mit_Null_Stueck_da()
    {
        await StatementAsync("2024-03-31", 321m, 91.55m, 29_389.00m);

        var abgleich = (await Service().GetAsync(depotId)).Reconciliation;

        Assert.False(abgleich!.Matches);
        Assert.Equal(0m, abgleich.Rows.Single().TradeQuantity);
        Assert.Equal(-321m, abgleich.Rows.Single().Difference);
    }

    [Fact]
    public async Task Ohne_Aufstellung_gibt_es_keinen_Abgleich()
        => Assert.Null((await Service().GetAsync(depotId)).Reconciliation);

    /// <summary>Der Block über der Liste gilt der jüngsten Aufstellung.</summary>
    [Fact]
    public async Task Abgeglichen_wird_die_juengste_Aufstellung()
    {
        await StatementAsync("2024-03-31", 300m, 91.55m, 27_465.00m);
        await StatementAsync("2024-06-30", 321m, 95.00m, 30_495.00m);

        var alle = await Service().GetAsync(depotId);

        Assert.Equal(new DateOnly(2024, 6, 30), alle.Reconciliation!.AsOf);
        Assert.Equal(new DateOnly(2024, 6, 30), alle.Statements[0].AsOf);
    }

    public void Dispose() => database.Dispose();
}
