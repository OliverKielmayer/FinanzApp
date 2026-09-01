using FinanzApp.Api.Application;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Gespeicherte Ansichten des Auswertungsbereichs.
/// </summary>
/// <remarks>
/// Sie halten fest, <em>wie</em> gerechnet wird, nie ein Ergebnis — ein festgehaltenes Ergebnis
/// wäre am nächsten Tag falsch, ohne dass jemand es merkt. Und sie gehören einem Benutzer, nicht
/// dem Haushalt: ein Ausschluss ist eine persönliche Entscheidung über eine Auswertung.
/// </remarks>
public sealed class ReportViewTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private ReportService For(int userId) => database.Reports(clock, userId: userId);

    [Fact]
    public async Task Eine_Ansicht_haelt_alle_Einstellungen()
    {
        var gespeichert = await For(1).SaveViewAsync(new SaveReportViewRequest(
            ReportKind.CostTrend, PeriodScope.Quarter, ComparisonBasis.TwelveMonthAverage,
            CostTrendSort.Amount, DepotId: 7, ExcludedTransactionIds: [3, 9]));

        var gelesen = (await For(1).GetViewsAsync()).Single();

        Assert.Equal(gespeichert.Id, gelesen.Id);
        Assert.Equal(PeriodScope.Quarter, gelesen.Period);
        Assert.Equal(ComparisonBasis.TwelveMonthAverage, gelesen.Comparison);
        Assert.Equal(CostTrendSort.Amount, gelesen.Sort);
        Assert.Equal(7, gelesen.DepotId);
        Assert.Equal([3, 9], gelesen.ExcludedTransactionIds);
    }

    /// <summary>
    /// Die Ausschlüsse überstehen das Speichern.
    /// </summary>
    /// <remarks>
    /// Sie liegen kommagetrennt in einer Spalte. Ohne den Wertvergleicher hielte EF die Liste
    /// für unverändert, solange es dieselbe Instanz ist — die Wahl ginge still verloren, und
    /// die wiederhergestellte Ansicht rechnete anders als die gespeicherte.
    /// </remarks>
    [Fact]
    public async Task Eine_leere_Ausschlussliste_bleibt_leer_und_wird_nicht_null()
    {
        await For(1).SaveViewAsync(new SaveReportViewRequest());

        Assert.Empty((await For(1).GetViewsAsync()).Single().ExcludedTransactionIds);
    }

    [Fact]
    public async Task Ohne_Namen_beschreibt_sich_die_Ansicht_selbst()
    {
        var ansicht = await For(1).SaveViewAsync(new SaveReportViewRequest(
            ReportKind.CostTrend, PeriodScope.Month, ComparisonBasis.PreviousYear));

        Assert.Equal("Kostentrend · Monat / Vorjahr", ansicht.Name);
    }

    /// <summary>Ein Bericht ohne Zeitraum bekommt auch keinen in den Namen.</summary>
    [Fact]
    public async Task Der_Depotbericht_kennt_keinen_Zeitraum_und_nennt_auch_keinen()
    {
        var ansicht = await For(1).SaveViewAsync(new SaveReportViewRequest(
            ReportKind.PortfolioGainLoss, PeriodScope.Year, ComparisonBasis.PreviousPeriod));

        Assert.Equal("Depot G/V", ansicht.Name);
    }

    /// <summary>
    /// Jeder Bericht nennt sich mit seinem eigenen Namen.
    /// </summary>
    /// <remarks>
    /// Steuerjahr und Objekt &amp; Beteiligung hießen in der gespeicherten Ansicht
    /// „Kostentrend · Monat / Vorjahr“ — der Name des Berichts, in dem man gerade nicht war, samt
    /// einer Einstellung, die es dort nicht gibt.
    /// </remarks>
    [Theory]
    [InlineData(ReportKind.TaxYear, "Steuerjahr")]
    [InlineData(ReportKind.PropertyParticipation, "Objekt & Beteiligung")]
    [InlineData(ReportKind.DataQuality, "Datenqualität")]
    [InlineData(ReportKind.HealthBalance, "PKV-Bilanz")]
    public async Task Jeder_Bericht_nennt_sich_selbst(ReportKind bericht, string name)
    {
        var ansicht = await For(1).SaveViewAsync(new SaveReportViewRequest(
            bericht, PeriodScope.Year, ComparisonBasis.PreviousPeriod));

        Assert.Equal(name, ansicht.Name);
    }

    [Fact]
    public async Task Ein_eigener_Name_bleibt_stehen()
    {
        var ansicht = await For(1).SaveViewAsync(new SaveReportViewRequest(Name: "  Mein Blick  "));

        Assert.Equal("Mein Blick", ansicht.Name);
    }

    [Fact]
    public async Task Derselbe_Name_zweimal_wird_abgewiesen()
    {
        await For(1).SaveViewAsync(new SaveReportViewRequest());

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => For(1).SaveViewAsync(new SaveReportViewRequest()));

        Assert.Contains("gibt es schon", fehler.Message);
        Assert.Single(await For(1).GetViewsAsync());
    }

    // ── Sie gehoert einem Benutzer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_anderer_Benutzer_sieht_sie_nicht()
    {
        await For(1).SaveViewAsync(new SaveReportViewRequest());

        Assert.Single(await For(1).GetViewsAsync());
        Assert.Empty(await For(2).GetViewsAsync());
    }

    /// <summary>
    /// Derselbe Name darf zweimal vorkommen — bei zwei Benutzern.
    /// </summary>
    /// <remarks>
    /// „Kostentrend · Monat / Vorjahr“ ist der Name, den sich jede Ansicht selbst gibt. Ihn
    /// haushaltsweit nur einmal zuzulassen hieße, dass der Erste ihn allen wegnimmt.
    /// </remarks>
    [Fact]
    public async Task Zwei_Benutzer_duerfen_denselben_Namen_haben()
    {
        await For(1).SaveViewAsync(new SaveReportViewRequest());
        await For(2).SaveViewAsync(new SaveReportViewRequest());

        Assert.Single(await For(1).GetViewsAsync());
        Assert.Single(await For(2).GetViewsAsync());
    }

    [Fact]
    public async Task Eine_fremde_Ansicht_laesst_sich_nicht_loeschen()
    {
        var ansicht = await For(1).SaveViewAsync(new SaveReportViewRequest());

        Assert.False(await For(2).DeleteViewAsync(ansicht.Id));
        Assert.Single(await For(1).GetViewsAsync());

        Assert.True(await For(1).DeleteViewAsync(ansicht.Id));
        Assert.Empty(await For(1).GetViewsAsync());
    }

    [Fact]
    public async Task Eine_unbekannte_Ansicht_zu_loeschen_ist_kein_Fehler()
        => Assert.False(await For(1).DeleteViewAsync(9999));

    public void Dispose() => database.Dispose();
}
