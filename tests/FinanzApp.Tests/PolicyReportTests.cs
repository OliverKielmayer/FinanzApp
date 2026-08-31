using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Berichte entfernen, ihre gelesenen Werte einblenden, und der erreichte Wert folgt dem
/// neuesten Bericht.
/// </summary>
/// <remarks>
/// <para>Der Kopfwert eines Vertrags ist keine eigene Größe mehr, sondern der jüngste gemeldete
/// Stand. Sonst stünde nach dem Entfernen eines Berichts weiter dessen Zahl da, und niemand
/// könnte sagen, woher sie kommt.</para>
/// <para><b>Nach Stichtag, nicht nach Einlesezeitpunkt.</b> Ein nachgetragener alter Bericht
/// ergänzt die Reihe und setzt den aktuellen Wert nicht zurück — ein Stand von 2023 ist keine
/// Aussage über heute.</para>
/// </remarks>
public sealed class PolicyReportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 30);

    private PolicyService Service()
    {
        var context = database.Context();

        return new PolicyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(Path.Combine(Path.GetTempPath(), "finanzapp-tests", "reports")),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance),
            clock);
    }

    private int Policy()
    {
        using var context = database.Context();
        var vertrag = new Policy
        {
            Name = "Muster",
            Provider = "Musteranbieter",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();
        return vertrag.Id;
    }

    private async Task<int> ReportAsync(
        int policyId, string stichtag, decimal wert,
        decimal? basis = null, decimal? bonus = null, int? belegId = null, string quelle = "Statusreport")
    {
        using var context = database.Context();
        await PolicyService.RecordReportAsync(
            context, clock, policyId, DateOnly.Parse(stichtag), wert, quelle, default,
            basis, bonus, belegId);

        await context.SaveChangesAsync();

        return context.PolicyReports.Single(r => r.PolicyId == policyId && r.AsOf == DateOnly.Parse(stichtag)).Id;
    }

    /// <summary>Ein Beleg samt seiner gelesenen Zeilen.</summary>
    private int Document(params (string Key, string Label, string Value, int Page)[] zeilen)
    {
        using var context = database.Context();
        var beleg = new Document
        {
            Title = "Statusreport 2025",
            RelativePath = "Lebensversicherung/Muster/2025/Statusreport_2025-07-31.pdf",
            FileName = "Statusreport_2025-07-31.pdf",
            ScanKind = "statusreport-lv",
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(beleg);
        context.SaveChanges();

        foreach (var zeile in zeilen)
        {
            context.DocumentExtractions.Add(new DocumentExtraction
            {
                DocumentId = beleg.Id,
                FieldKey = zeile.Key,
                Label = zeile.Label,
                Value = zeile.Value,
                SourcePage = zeile.Page,
                Confidence = 1.0,
                Confirmed = true,
                CreatedAt = clock.Now,
            });
        }

        context.SaveChanges();
        return beleg.Id;
    }

    // ── Der erreichte Wert folgt dem neuesten Bericht ──────────────────────────────────────

    /// <summary>Ein gemeldeter Stand setzt Wert, Stichtag und Bestandteile des Vertrags.</summary>
    [Fact]
    public async Task Der_Bericht_setzt_den_erreichten_Wert()
    {
        var id = Policy();
        await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m);

        using var context = database.Context();
        var vertrag = context.Policies.Single();

        Assert.Equal(20481.52m, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2025, 7, 31), vertrag.ValuationDate);
        Assert.Equal(18373.87m, vertrag.BaseValue);
        Assert.Equal(2107.65m, vertrag.AccruedBonus);
    }

    /// <summary>
    /// Ein nachgetragener alter Bericht ändert den erreichten Wert nicht.
    /// </summary>
    /// <remarks>
    /// Sonst setzte das Einlesen eines Berichts von 2023 den Vertrag auf den Stand von 2023
    /// zurück — die Reihe wächst dann rückwärts, die Kopfzahl darf das nicht mitmachen.
    /// </remarks>
    [Fact]
    public async Task Ein_nachgetragener_alter_Bericht_aendert_den_Wert_nicht()
    {
        var id = Policy();
        await ReportAsync(id, "2025-07-31", 20481.52m);
        await ReportAsync(id, "2023-07-31", 17400m);

        using var context = database.Context();

        Assert.Equal(20481.52m, context.Policies.Single().CurrentValue);
        Assert.Equal(2, context.PolicyReports.Count());
    }

    /// <summary>Derselbe Stichtag ein zweites Mal berichtigt den Stand — und die Kopfzahl.</summary>
    [Fact]
    public async Task Derselbe_Stichtag_berichtigt_den_Stand()
    {
        var id = Policy();
        await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m);
        await ReportAsync(id, "2025-07-31", 20500m, 18400m, 2100m);

        using var context = database.Context();

        Assert.Equal(20500m, Assert.Single(context.PolicyReports).Value);
        Assert.Equal(20500m, context.Policies.Single().CurrentValue);
        Assert.Equal(18400m, context.Policies.Single().BaseValue);
    }

    // ── Entfernen ─────────────────────────────────────────────────────────────────────────

    /// <summary>Nach dem Entfernen des neuesten zählt der davor.</summary>
    [Fact]
    public async Task Nach_dem_Entfernen_zaehlt_der_vorherige_Stand()
    {
        var id = Policy();
        await ReportAsync(id, "2024-07-31", 19637.12m, 17600m, 2037.12m);
        var neuester = await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m);

        Assert.True(await Service().DeleteReportAsync(neuester));

        using var context = database.Context();
        var vertrag = context.Policies.Single();

        Assert.Equal(19637.12m, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2024, 7, 31), vertrag.ValuationDate);
        Assert.Equal(17600m, vertrag.BaseValue);
        Assert.Equal(2037.12m, vertrag.AccruedBonus);
        Assert.Single(context.PolicyReports);
    }

    /// <summary>Ein älterer Stand verschwindet, ohne die Kopfzahl anzufassen.</summary>
    [Fact]
    public async Task Ein_aelterer_Stand_laesst_den_erreichten_Wert_stehen()
    {
        var id = Policy();
        var alter = await ReportAsync(id, "2024-07-31", 19637.12m);
        await ReportAsync(id, "2025-07-31", 20481.52m);

        await Service().DeleteReportAsync(alter);

        using var context = database.Context();

        Assert.Equal(20481.52m, context.Policies.Single().CurrentValue);
        Assert.Equal(new DateOnly(2025, 7, 31), context.Policies.Single().ValuationDate);
    }

    /// <summary>
    /// Mit dem letzten Bericht geht auch der erreichte Wert.
    /// </summary>
    /// <remarks>
    /// Der Vertrag zählt danach in keiner Vermögenssumme mehr mit. Das ist die Wahrheit über die
    /// verbliebenen Belege: eine Zahl ohne Beleg stehenzulassen wäre schlimmer als die Lücke.
    /// </remarks>
    [Fact]
    public async Task Mit_dem_letzten_Bericht_geht_der_Wert()
    {
        var id = Policy();
        var einziger = await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m);

        await Service().DeleteReportAsync(einziger);

        using var context = database.Context();
        var vertrag = context.Policies.Single();

        Assert.Null(vertrag.CurrentValue);
        Assert.Null(vertrag.ValuationDate);
        Assert.Null(vertrag.BaseValue);
        Assert.Empty(context.PolicyReports);
    }

    [Fact]
    public async Task Ein_Bericht_der_nicht_mehr_da_ist_meldet_das()
        => Assert.False(await Service().DeleteReportAsync(4711));

    // ── Die gelesenen Werte ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Werte des Belegs kommen mit dem Bericht zurück.
    /// </summary>
    /// <remarks>
    /// Beträge als Zahl mit Kennzeichen, alles andere als Text — ein hier formatierter Betrag
    /// ließe sich von „Beträge verbergen“ nicht mehr maskieren.
    /// </remarks>
    [Fact]
    public async Task Der_Bericht_bringt_die_gelesenen_Werte_mit()
    {
        var id = Policy();
        var beleg = Document(
            ("rueckkauf", "Rückkaufswert", "18.373,87 EUR", 2),
            ("ansammlung", "Ansammlungsguthaben", "2.107,65 EUR", 2),
            ("ablauf", "Ablauf", "1.12.2031", 1));

        await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m, beleg);

        var bericht = Assert.Single((await Service().GetAsync(id))!.Reports);

        Assert.Equal(beleg, bericht.DocumentId);
        Assert.Equal("Statusreport 2025", bericht.DocumentTitle);
        Assert.Equal(3, bericht.Values.Count);

        var rueckkauf = bericht.Values[0];
        Assert.Equal("Rückkaufswert", rueckkauf.Label);
        Assert.True(rueckkauf.IsMoney);
        Assert.Equal(18373.87m, rueckkauf.Number);
        Assert.Equal(2, rueckkauf.SourcePage);

        // Ein Datum ist kein Betrag: es geht als Text hinaus und wird nicht maskiert.
        var ablauf = bericht.Values[2];
        Assert.False(ablauf.IsMoney);
        Assert.Equal("1.12.2031", ablauf.Display);
    }

    /// <summary>Ein von Hand erfasster Stand hat nichts einzublenden.</summary>
    [Fact]
    public async Task Ein_erfasster_Stand_hat_keine_gelesenen_Werte()
    {
        var id = Policy();
        await ReportAsync(id, "2025-07-31", 20481.52m, quelle: "erfasst");

        var bericht = Assert.Single((await Service().GetAsync(id))!.Reports);

        Assert.Null(bericht.DocumentId);
        Assert.Empty(bericht.Values);
        Assert.Equal("erfasst", bericht.Source);
    }

    /// <summary>Die Reihe kommt in Stichtagsreihenfolge und mit ihren Bestandteilen zurück.</summary>
    [Fact]
    public async Task Die_Reihe_traegt_ihre_Bestandteile()
    {
        var id = Policy();
        await ReportAsync(id, "2025-07-31", 20481.52m, 18373.87m, 2107.65m);
        await ReportAsync(id, "2024-07-31", 19637.12m, 17600m, 2037.12m);

        var reihe = (await Service().GetAsync(id))!.Reports;

        Assert.Equal(
            [new DateOnly(2024, 7, 31), new DateOnly(2025, 7, 31)],
            reihe.Select(r => r.AsOf));

        Assert.Equal(17600m, reihe[0].BaseValue);
        Assert.Equal(2107.65m, reihe[1].AccruedBonus);
    }

    public void Dispose() => database.Dispose();
}
