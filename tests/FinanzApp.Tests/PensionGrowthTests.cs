using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Vorsorge: Zuwachs gegen Beitrag.
/// </summary>
/// <remarks>
/// <para>Der Wert eines Vertrags beantwortet die Frage nicht, die zählt: Was hat er
/// <em>selbst</em> erwirtschaftet? Sein Guthaben steigt auch dann, wenn von jedem eingezahlten
/// Euro Kosten abgehen — nur langsamer als die Summe der Einzahlungen.</para>
/// <para>Im Mittelpunkt steht hier die Trennung der beiden Beitragszahlen. <b>Hochgerechnet</b>
/// ist der heutige Monatsbeitrag mal Monate; <b>gemessen</b> sind die Buchungen, und die gibt es
/// nur, wenn sie den ganzen Zeitraum abdecken. Eine Teilsumme, die wie eine ganze aussieht,
/// machte jeden Vertrag künstlich gut.</para>
/// <para>Alle Zahlen hier sind erfunden.</para>
/// </remarks>
public sealed class PensionGrowthTests : IDisposable
{
    private readonly TestDatabase database = new();

    private PensionGrowthService Dienst() => new(database.Context());

    /// <summary>Ein Vertrag mit Nummer und heutigem Monatsbeitrag.</summary>
    private int Vertrag(string name, decimal beitrag, string? nummer = "77-4711")
    {
        using var context = database.Context();
        var police = new Policy
        {
            Name = name,
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            Provider = "Musterversicherung",
            PolicyNumber = nummer,
            Premium = beitrag,
            PremiumInterval = PremiumInterval.Monthly,
        };

        context.Policies.Add(police);
        context.SaveChanges();
        return police.Id;
    }

    private void Stand(int police, string tag, decimal wert)
    {
        using var context = database.Context();
        context.PolicyReports.Add(new PolicyReport
        {
            PolicyId = police,
            AsOf = DateOnly.Parse(tag),
            Value = wert,
            Source = "Statusreport",
        });

        context.SaveChanges();
    }

    /// <summary>Eine Beitragsbuchung, die die Vertragsnummer im Verwendungszweck trägt.</summary>
    private void Beitrag(string tag, decimal betrag, string zweck)
    {
        using var context = database.Context();
        var konto = context.Accounts.FirstOrDefault();
        if (konto is null)
        {
            konto = new Account
            {
                Name = "Giro", ShortName = "Giro", BankName = "Bank",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 1, 1),
            };

            context.Accounts.Add(konto);
            context.SaveChanges();
        }

        context.Transactions.Add(new Transaction
        {
            BookingDate = DateOnly.Parse(tag),
            Payee = "Musterversicherung",
            Purpose = zweck,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            AccountId = konto.Id,
            CreatedAt = new DateTime(2026, 1, 1),
        });

        context.SaveChanges();
    }

    // ── Der Zuwachs ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei Stände ergeben einen Zuwachs, und der heutige Beitrag die Hochrechnung.
    /// </summary>
    [Fact]
    public async Task Zwei_Staende_und_ein_Beitrag_ergeben_die_Differenz()
    {
        var id = Vertrag("Kapitallebensversicherung", beitrag: 100m);
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-09-30", 11_500m);

        var bericht = await Dienst().GetAsync();
        var zeile = Assert.Single(bericht.Rows);

        Assert.Equal(9, zeile.Months);
        Assert.Equal(1_500m, zeile.Growth);
        Assert.Equal(900m, zeile.Projected);
        Assert.Equal(600m, zeile.DifferenceProjected);
    }

    /// <summary>
    /// Eine negative Differenz ist die eigentliche Auskunft.
    /// </summary>
    /// <remarks>
    /// Der Wert steigt um 400 €, eingezahlt wurden 900 €. Der Vertrag hat also 500 € weniger
    /// gutgeschrieben, als hineinging — Kosten, Risikoanteil, Kursverlust. Welches davon, sagt
    /// die Zahl nicht; dass es so ist, sagt kein anderer Schirm.
    /// </remarks>
    [Fact]
    public async Task Wer_weniger_gutschreibt_als_er_nimmt_zeigt_eine_negative_Differenz()
    {
        var id = Vertrag("Teure Police", beitrag: 100m);
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-09-30", 10_400m);

        var bericht = await Dienst().GetAsync();
        var zeile = Assert.Single(bericht.Rows);

        Assert.Equal(400m, zeile.Growth);
        Assert.Equal(-500m, zeile.DifferenceProjected);
    }

    /// <summary>Gerechnet wird über die beiden <em>jüngsten</em> Stände, nicht über alle.</summary>
    [Fact]
    public async Task Gerechnet_wird_ueber_die_beiden_juengsten_Staende()
    {
        var id = Vertrag("Mit langer Reihe", beitrag: 50m);
        Stand(id, "2014-12-31", 1_000m);
        Stand(id, "2020-12-31", 5_000m);
        Stand(id, "2025-12-31", 9_000m);
        Stand(id, "2026-06-30", 9_800m);

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Equal(new DateOnly(2025, 12, 31), zeile.From);
        Assert.Equal(new DateOnly(2026, 6, 30), zeile.To);
        Assert.Equal(800m, zeile.Growth);
        Assert.Equal(6, zeile.Months);
    }

    // ── Die gemessene Seite ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Buchungen_mit_der_Vertragsnummer_werden_gemessen()
    {
        var id = Vertrag("Mit Buchungen", beitrag: 100m, nummer: "77001122-03");
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-03-31", 10_500m);

        // Vor dem Zeitraum, damit die Buchungen ihn abdecken.
        Beitrag("2025-12-01", 100m, "Beitrag 77001122-03 12/25");

        Beitrag("2026-01-02", 100m, "Beitrag 77001122-03 01/26");
        Beitrag("2026-02-02", 100m, "Beitrag 77001122-03 02/26");
        Beitrag("2026-03-02", 100m, "Beitrag 77001122-03 03/26");

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Equal(300m, zeile.Measured);
        Assert.Equal(3, zeile.MeasuredCount);
        Assert.Equal(200m, zeile.DifferenceMeasured);
        Assert.Empty(zeile.Note);
    }

    /// <summary>
    /// Die Nummer wird normalisiert verglichen.
    /// </summary>
    /// <remarks>
    /// „77001122-03“ und „77001122 03“ sind dieselbe Nummer. Banken schreiben sie je nach
    /// Zahlungsweg verschieden, und ein Vergleich, der daran scheitert, findet nichts.
    /// </remarks>
    [Fact]
    public async Task Die_Nummer_wird_normalisiert_verglichen()
    {
        var id = Vertrag("Mit Schreibweisen", beitrag: 100m, nummer: "77001122-03");
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-02-28", 10_300m);
        Beitrag("2025-12-01", 100m, "Beitrag 77001122 03");
        Beitrag("2026-01-02", 100m, "BEITRAG 77001122 03 JAN");
        Beitrag("2026-02-02", 100m, "Beitrag 77001122.03 Feb");

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Equal(200m, zeile.Measured);
        Assert.Equal(2, zeile.MeasuredCount);
    }

    /// <summary>
    /// Decken die Buchungen den Zeitraum nicht ab, steht keine gemessene Zahl da.
    /// </summary>
    /// <remarks>
    /// Das ist der Kern. Die Berichte reichen Jahre zurück, die Buchungen oft nur Monate. Eine
    /// Summe aus dem letzten Drittel sähe aus wie eine ganze, und jeder Vertrag stünde damit
    /// besser da, als er ist — am stärksten die ältesten, über die man am wenigsten weiß.
    /// </remarks>
    [Fact]
    public async Task Decken_die_Buchungen_den_Zeitraum_nicht_ab_gibt_es_keine_gemessene_Zahl()
    {
        var id = Vertrag("Alte Reihe", beitrag: 100m, nummer: "77001122-03");
        Stand(id, "2024-12-31", 8_000m);
        Stand(id, "2026-09-30", 11_000m);

        // Erste Buchung liegt lange nach dem Beginn des Zeitraums.
        Beitrag("2026-01-02", 100m, "Beitrag 77001122-03");
        Beitrag("2026-02-02", 100m, "Beitrag 77001122-03");

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Null(zeile.Measured);
        Assert.Null(zeile.DifferenceMeasured);
        Assert.Contains("02.01.2026", zeile.Note);
        Assert.Contains("31.12.2024", zeile.Note);

        // Die hochgerechnete Zahl steht trotzdem da — sie hängt nicht an Buchungen.
        Assert.Equal(2_100m, zeile.Projected);
        Assert.Equal(900m, zeile.DifferenceProjected);
    }

    [Fact]
    public async Task Ohne_Vertragsnummer_gibt_es_keine_Zuordnung()
    {
        var id = Vertrag("Ohne Nummer", beitrag: 100m, nummer: null);
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-03-31", 10_500m);
        Beitrag("2025-12-01", 100m, "Irgendein Beitrag");
        Beitrag("2026-01-02", 100m, "Irgendein Beitrag");

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Null(zeile.Measured);
        Assert.Contains("Vertragsnummer", zeile.Note);
    }

    // ── Was keine Zeile trägt ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_Vertrag_mit_einem_Stand_traegt_keinen_Zuwachs()
    {
        var id = Vertrag("Nur ein Stand", beitrag: 80m);
        Stand(id, "2026-09-30", 18_000m);

        var bericht = await Dienst().GetAsync();
        var zeile = Assert.Single(bericht.Rows);

        Assert.Null(zeile.Growth);
        Assert.Equal(1, bericht.SkippedPolicies);
        Assert.Equal(0, bericht.CountedPolicies);
        Assert.Contains("braucht zwei", zeile.Note);
    }

    [Fact]
    public async Task Ohne_hinterlegten_Beitrag_gibt_es_nichts_hochzurechnen()
    {
        var id = Vertrag("Beitragsfrei", beitrag: 0m);
        Stand(id, "2025-12-31", 10_000m);
        Stand(id, "2026-09-30", 10_600m);

        var zeile = Assert.Single((await Dienst().GetAsync()).Rows);

        Assert.Equal(600m, zeile.Growth);
        Assert.Null(zeile.Projected);
        Assert.Null(zeile.DifferenceProjected);
        Assert.Contains("Kein Beitrag hinterlegt", zeile.Note);
    }

    /// <summary>Eine Absicherung hat keinen Wert und gehört nicht in diesen Bericht.</summary>
    [Fact]
    public async Task Eine_Absicherung_steht_nicht_im_Bericht()
    {
        using (var context = database.Context())
        {
            context.Policies.Add(new Policy
            {
                Name = "Hausrat",
                Kind = PolicyKind.HouseholdContents,
                IsCapitalForming = false,
                Provider = "Musterversicherung",
                Premium = 12m,
            });

            context.SaveChanges();
        }

        Assert.Empty((await Dienst().GetAsync()).Rows);
    }

    // ── Die Summe ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Die_Summe_zaehlt_nur_Vertraege_mit_zwei_Staenden()
    {
        var a = Vertrag("Mit Reihe", beitrag: 100m, nummer: "AAA-1");
        Stand(a, "2025-12-31", 10_000m);
        Stand(a, "2026-09-30", 11_500m);

        var b = Vertrag("Ohne Reihe", beitrag: 50m, nummer: "BBB-2");
        Stand(b, "2026-09-30", 5_000m);

        var bericht = await Dienst().GetAsync();

        Assert.Equal(1_500m, bericht.TotalGrowth);
        Assert.Equal(900m, bericht.TotalProjected);
        Assert.Equal(600m, bericht.TotalDifference);
        Assert.Equal(1, bericht.CountedPolicies);
        Assert.Equal(1, bericht.SkippedPolicies);
    }

    public void Dispose() => database.Dispose();
}
