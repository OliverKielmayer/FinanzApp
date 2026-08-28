using System.Text;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Die Orderdatei des Brokers — v5-Handoff, Abschnitt 11.1.
/// </summary>
/// <remarks>
/// Der Aufbau stammt aus einer echten Datei (finanzen.net ZERO / Baader Bank); die Zahlen hier
/// sind nachgebaut, denn echte Wertpapiergeschäfte gehören nicht ins Repository. Geprüft wird,
/// was der Handoff verbindlich nennt: nur ausgeführte Sätze, nur die ausgeführte Menge, der
/// Zuschlag als Gebühr, und die Wiedererkennung ohne Ordernummer.
/// </remarks>
public sealed class OrderCsvParserTests
{
    private static readonly OrderCsvParser Parser = new();

    private const string Header =
        "Name;ISIN;WKN;Anzahl;Anzahl storniert;Status;Orderart;Limit;Stop;Erstellt Datum;"
        + "Erstellt Zeit;Gültig bis;Richtung;Wert;Wert storniert;Mindermengenzuschlag;"
        + "Ausführung Datum;Ausführung Zeit;Ausführung Kurs;Anzahl ausgeführt;Anzahl offen;"
        + "Gestrichen Datum;Gestrichen Zeit";

    /// <summary>Eine Zeile im Format der echten Datei.</summary>
    private static string Row(
        string status = "ausgeführt", string orderart = "Markt", string limit = "",
        string richtung = "Kauf", string wert = "-1.427,81", string zuschlag = "0,00",
        string datum = "13.03.2024", string zeit = "12:41:18", string kurs = "89,238",
        string ausgefuehrt = "16", string anzahl = "16", string isin = "IE00B4L5Y983")
        => $"iShares Core MSCI World UCITS ETF;{isin};A0RPWH;{anzahl};;{status};{orderart};"
           + $"{limit};;09.03.2024;12:29:31;30.04.2024;{richtung};{wert};;{zuschlag};"
           + $"{datum};{zeit};{kurs};{ausgefuehrt};0;;";

    private static Task<IReadOnlyList<ParsedTrade>> ParseAsync(params string[] rows)
        => Parser.ParseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows]))),
            "ZERO-orders.csv");

    // ── Was gelesen wird ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_ausgefuehrte_Order_wird_vollstaendig_gelesen()
    {
        var satz = (await ParseAsync(Row())).Single();

        Assert.Null(satz.Problem);
        Assert.Equal("IE00B4L5Y983", satz.Isin);
        Assert.Equal("A0RPWH", satz.Wkn);
        Assert.False(satz.IsSell);
        Assert.False(satz.IsLimit);
        Assert.Equal(new DateTime(2024, 3, 13, 12, 41, 18), satz.ExecutedAt);
        Assert.Equal(16m, satz.Quantity);
        Assert.Equal(1427.81m, satz.Value);
    }

    /// <summary>
    /// Der Kurs behält seine Nachkommastellen.
    /// </summary>
    /// <remarks>
    /// Broker rechnen mit mehr als zwei. Auf Cent gerundet ergäbe 16 × 89,24 = 1.427,84 statt
    /// der 1.427,81, die wirklich belastet wurden — eine Abweichung, die sich über
    /// sechsundzwanzig Ausführungen aufsummiert.
    /// </remarks>
    [Fact]
    public async Task Der_Kurs_behaelt_seine_Nachkommastellen()
        => Assert.Equal(89.238m, (await ParseAsync(Row())).Single().Price);

    [Fact]
    public async Task Eine_Limit_Order_traegt_ihr_Limit()
    {
        var satz = (await ParseAsync(Row(orderart: "Limit", limit: "90,00"))).Single();

        Assert.True(satz.IsLimit);
        Assert.Equal(90.00m, satz.LimitPrice);
    }

    [Fact]
    public async Task Ein_Verkauf_wird_als_solcher_erkannt()
        => Assert.True((await ParseAsync(Row(richtung: "Verkauf"))).Single().IsSell);

    /// <summary>
    /// Der Mindermengenzuschlag ist eine Gebühr und liegt auf dem Wert, nicht darin.
    /// </summary>
    /// <remarks>
    /// Geprüft an der echten Datei: dort ist Wert exakt Stück × Kurs, der Zuschlag steht
    /// daneben. Ihn in den Kurs zu rechnen machte aus einer Gebühr einen schlechteren Kurs.
    /// </remarks>
    [Fact]
    public async Task Der_Zuschlag_liegt_auf_dem_Wert()
    {
        var satz = (await ParseAsync(
            Row(wert: "-456,64", zuschlag: "1,00", kurs: "91,328", ausgefuehrt: "5", anzahl: "5")))
            .Single();

        Assert.Equal(456.64m, satz.Value);
        Assert.Equal(1.00m, satz.Fee);
        Assert.Equal(satz.Quantity * satz.Price, satz.Value);
    }

    // ── Was nicht zählt ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stornierte und offene Orders gehören in keine Summe — verschwinden dürfen sie trotzdem nicht.
    /// </summary>
    /// <remarks>
    /// Sie stillschweigend wegzuwerfen wäre bequemer, aber dann fehlten sie im Ergebnis, und
    /// niemand wüsste, warum aus sechsundzwanzig Zeilen vierundzwanzig Sätze wurden.
    /// </remarks>
    [Theory]
    [InlineData("storniert")]
    [InlineData("offen")]
    [InlineData("gestrichen")]
    public async Task Was_nicht_ausgefuehrt_ist_steht_mit_Grund_da(string status)
    {
        var satz = (await ParseAsync(Row(status: status))).Single();

        Assert.NotNull(satz.Problem);
        Assert.Contains(status, satz.Problem);
    }

    /// <summary>
    /// Gezählt wird die ausgeführte Menge, nicht die bestellte.
    /// </summary>
    /// <remarks>
    /// Bei einer Teilausführung bucht „Anzahl“ Stücke ein, die nie geliefert wurden — und der
    /// Depotwert stimmte danach nie wieder mit dem Bestandsnachweis überein.
    /// </remarks>
    [Fact]
    public async Task Bei_einer_Teilausfuehrung_zaehlt_die_ausgefuehrte_Menge()
        => Assert.Equal(6m, (await ParseAsync(Row(anzahl: "16", ausgefuehrt: "6"))).Single().Quantity);

    [Fact]
    public async Task Eine_Zeile_ohne_ausgefuehrte_Stueckzahl_steht_mit_Grund_da()
        => Assert.NotNull((await ParseAsync(Row(ausgefuehrt: "0"))).Single().Problem);

    // ── Wiedererkennung ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dieselbe Ausführung ergibt dieselbe Referenz.
    /// </summary>
    /// <remarks>
    /// Die Datei führt keine Ordernummer. Ohne stabile Referenz verdoppelt sich das Depot,
    /// sobald jemand dieselbe Datei ein zweites Mal einliest.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Ausfuehrung_ergibt_dieselbe_Referenz()
    {
        var a = (await ParseAsync(Row())).Single().Reference;
        var b = (await ParseAsync(Row())).Single().Reference;

        Assert.Equal(a, b);
        Assert.NotEmpty(a);
    }

    [Theory]
    [InlineData("13.03.2024", "12:41:19", "89,238", "16")]
    [InlineData("14.03.2024", "12:41:18", "89,238", "16")]
    [InlineData("13.03.2024", "12:41:18", "89,239", "16")]
    [InlineData("13.03.2024", "12:41:18", "89,238", "17")]
    public async Task Ein_anderer_Zeitpunkt_Kurs_oder_Stueck_ergibt_eine_andere_Referenz(
        string datum, string zeit, string kurs, string stueck)
    {
        var a = (await ParseAsync(Row())).Single().Reference;
        var b = (await ParseAsync(Row(datum: datum, zeit: zeit, kurs: kurs, ausgefuehrt: stueck)))
            .Single().Reference;

        Assert.NotEqual(a, b);
    }

    // ── Die Datei selbst ───────────────────────────────────────────────────────────────────

    /// <summary>Die Spalten werden über ihre Überschrift gefunden, nicht über ihre Position.</summary>
    [Fact]
    public async Task Eine_zusaetzliche_Spalte_verschiebt_nichts()
    {
        var kopf = "Zusatz;" + Header;
        var zeile = "egal;" + Row();

        var satz = (await Parser.ParseAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(kopf + "\r\n" + zeile)), "x.csv")).Single();

        Assert.Equal(16m, satz.Quantity);
        Assert.Equal(89.238m, satz.Price);
    }

    [Fact]
    public async Task Eine_Datei_ohne_die_noetigen_Spalten_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<StatementFormatException>(
            () => Parser.ParseAsync(
                new MemoryStream(Encoding.UTF8.GetBytes("Name;Betrag\r\nX;1")), "x.csv"));

        Assert.Contains("Orderdatei", fehler.Message);
    }

    [Fact]
    public async Task Eine_leere_Datei_wird_abgewiesen()
        => await Assert.ThrowsAsync<StatementFormatException>(
            () => Parser.ParseAsync(new MemoryStream(), "x.csv"));
}
