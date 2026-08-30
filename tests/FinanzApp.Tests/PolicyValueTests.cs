using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Wie der erreichte Wert entsteht und wie er sich entwickelt — v5-Handoff, §19.5 bis §19.7.
/// </summary>
/// <remarks>
/// <para>Zwei Regeln tragen diese Tests. <b>Nie eine Kurve aus einem Punkt</b>: der erste Bau
/// zeichnete einen Verlauf aus <c>Wert × [0,72 · 0,81 · 0,91 · 1,0]</c> und beschriftete ihn
/// als gemessene Historie — alle Verträge hatten dieselbe Linie, weil nur der aktuelle Wert
/// gespeichert war.</para>
/// <para>Und: <b>die Bezeichnung ist eine Funktion der Vertragsart.</b> Ein Bausparvertrag hat
/// keinen Rückkaufswert; ihn so zu nennen macht aus einer richtigen Zahl eine falsche Aussage.</para>
/// </remarks>
public sealed class PolicyValueTests : IDisposable
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
                TestDatabase.PathService(Path.Combine(Path.GetTempPath(), "finanzapp-tests", "value")),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance),
            clock);
    }

    private int Policy(
        PolicyKind art, decimal? basis, decimal? ueberschuss, decimal wert,
        DateOnly? stichtag = null)
    {
        using var context = database.Context();
        var vertrag = new Policy
        {
            Name = "Muster",
            Provider = "Musteranbieter",
            Kind = art,
            IsCapitalForming = true,
            BaseValue = basis,
            AccruedBonus = ueberschuss,
            CurrentValue = wert,
            ValuationDate = stichtag ?? new DateOnly(2025, 7, 31),
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();
        return vertrag.Id;
    }

    private void Report(int policyId, string stichtag, decimal wert)
    {
        using var context = database.Context();
        context.PolicyReports.Add(new PolicyReport
        {
            PolicyId = policyId,
            AsOf = DateOnly.Parse(stichtag),
            Value = wert,
            Source = "Statusreport",
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    // ── So entsteht der Wert ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei Bestandteile ergeben zwei Zeilen und eine Summe.
    /// </summary>
    [Fact]
    public async Task Der_Wert_zeigt_seine_beiden_Bestandteile()
    {
        var id = Policy(PolicyKind.CapitalLife, 18373.87m, 2107.65m, 20481.52m);

        var teile = (await Service().GetAsync(id))!.ValueParts;

        Assert.Equal(2, teile.Count);
        Assert.Equal("Rückkaufswert", teile[0].Label);
        Assert.Equal(18373.87m, teile[0].Amount);
        Assert.Equal("Ansammlungsguthaben", teile[1].Label);
        Assert.Equal(20481.52m, teile.Sum(t => t.Amount));
        Assert.Equal("Statusreport 31.07.2025", teile[0].Origin);
    }

    /// <summary>
    /// Ein Vertrag mit nur einem Bestandteil bekommt keine Summe.
    /// </summary>
    /// <remarks>
    /// Eine Summe aus einem Summanden ist keine. Beim Bausparen gibt es kein
    /// Ansammlungsguthaben — die Zeile darf dort nicht erscheinen, auch nicht mit null Euro.
    /// </remarks>
    [Fact]
    public async Task Ein_Bausparvertrag_hat_nur_einen_Bestandteil()
    {
        var id = Policy(PolicyKind.BuildingSociety, 12320.08m, ueberschuss: null, 12320.08m);

        var teil = Assert.Single((await Service().GetAsync(id))!.ValueParts);

        Assert.Equal("Sparguthaben", teil.Label);
        Assert.Equal("Auszug 31.07.2025", teil.Origin);
    }

    /// <summary>
    /// Ein Ansammlungsguthaben an einer Art, die keines führt, wird nicht ausgewiesen.
    /// </summary>
    /// <remarks>
    /// Auch wenn im Feld etwas steht: die Vertragsart entscheidet, was es gibt. Sonst behauptete
    /// der Schirm eine Überschussbeteiligung, die es beim Bausparen nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Was_die_Art_nicht_kennt_erscheint_nicht()
    {
        var id = Policy(PolicyKind.BuildingSociety, 12000m, ueberschuss: 320.08m, 12320.08m);

        Assert.Single((await Service().GetAsync(id))!.ValueParts);
    }

    /// <summary>Ohne erfasste Bestandteile bleibt der Block leer statt etwas zu behaupten.</summary>
    [Fact]
    public async Task Ohne_Bestandteile_gibt_es_keinen_Block()
    {
        var id = Policy(PolicyKind.CapitalLife, basis: null, ueberschuss: null, 20481.52m);

        Assert.Empty((await Service().GetAsync(id))!.ValueParts);
    }

    // ── Die Bezeichnungen ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PolicyKind.CapitalLife, "Rückkaufswert")]
    [InlineData(PolicyKind.BuildingSociety, "Sparguthaben")]
    [InlineData(PolicyKind.Riester, "Deckungskapital")]
    [InlineData(PolicyKind.Pension, "Deckungskapital")]
    [InlineData(PolicyKind.OccupationalPension, "Deckungskapital")]
    public void Der_Wertbestandteil_heisst_je_Art_anders(PolicyKind art, string erwartet)
        => Assert.Equal(erwartet, PolicyValueNaming.BaseValueLabel(art));

    /// <summary>
    /// „Statusreport“ nur, wo es einen gibt.
    /// </summary>
    /// <remarks>
    /// Ein Bausparvertrag bekommt einen Jahresauszug. Ihn Statusreport zu nennen hieße, ein
    /// Dokument zu behaupten, das nie kam.
    /// </remarks>
    [Theory]
    [InlineData(PolicyKind.CapitalLife, "Statusreport", "Verlauf aus Statusreports")]
    [InlineData(PolicyKind.BuildingSociety, "Auszug", "Verlauf aus Jahresauszügen")]
    [InlineData(PolicyKind.Riester, "Auszug", "Verlauf aus Jahresauszügen")]
    public void Der_Bericht_heisst_je_Art_anders(PolicyKind art, string bericht, string verlauf)
    {
        Assert.Equal(bericht, PolicyValueNaming.ReportLabel(art));
        Assert.Equal(verlauf, PolicyValueNaming.HistoryLabel(art));
    }

    /// <summary>
    /// Der Plural kommt aus derselben Stelle wie der Singular.
    /// </summary>
    /// <remarks>
    /// Ein angehängtes „e“ machte aus dem Auszug einen „Auszuge“ — und zwar an beiden Stellen,
    /// an denen der Schirm mehrere davon nennt.
    /// </remarks>
    [Theory]
    [InlineData(PolicyKind.CapitalLife, "Statusreporte")]
    [InlineData(PolicyKind.BuildingSociety, "Auszüge")]
    [InlineData(PolicyKind.Riester, "Auszüge")]
    public void Mehrere_Berichte_heissen_richtig(PolicyKind art, string erwartet)
        => Assert.Equal(erwartet, PolicyValueNaming.ReportPlural(art));

    /// <summary>
    /// Der Hinweis auf Bewertungsreserven gilt nur bei der Kapitallebensversicherung.
    /// </summary>
    /// <remarks>
    /// Diese Posten gibt es bei Bausparen und Riester nicht — und ohne Statusreport gibt es
    /// auch keinen Bericht, auf den sich der Satz berufen könnte.
    /// </remarks>
    [Fact]
    public void Der_Ausschluss_hinweis_gilt_nur_wo_es_die_Posten_gibt()
    {
        Assert.True(PolicyValueNaming.MentionsUnguaranteed(PolicyKind.CapitalLife));
        Assert.False(PolicyValueNaming.MentionsUnguaranteed(PolicyKind.BuildingSociety));
        Assert.False(PolicyValueNaming.MentionsUnguaranteed(PolicyKind.Riester));
    }

    // ── Der Verlauf ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ohne gespeicherte Berichte gibt es keinen Verlauf.
    /// </summary>
    /// <remarks>
    /// Der Kern der Regel: der erste Bau zeichnete eine Kurve aus dem aktuellen Wert mal einer
    /// festen Folge — alle Verträge hatten dieselbe Linie, beschriftet als Historie.
    /// </remarks>
    [Fact]
    public async Task Ohne_Berichte_gibt_es_keinen_Verlauf()
    {
        var id = Policy(PolicyKind.CapitalLife, 18373.87m, 2107.65m, 20481.52m);

        Assert.Empty((await Service().GetAsync(id))!.Reports);
    }

    /// <summary>
    /// Ein Bericht ist kein Verlauf.
    /// </summary>
    /// <remarks>
    /// Er kommt zurück, damit der Schirm sagen kann, dass einer da ist — gezeichnet wird daraus
    /// nichts.
    /// </remarks>
    [Fact]
    public async Task Ein_einzelner_Bericht_bleibt_ein_Punkt()
    {
        var id = Policy(PolicyKind.Riester, 11930.40m, null, 11930.40m);
        Report(id, "2025-12-31", 11930.40m);

        var bericht = Assert.Single((await Service().GetAsync(id))!.Reports);

        Assert.Equal(new DateOnly(2025, 12, 31), bericht.AsOf);
        Assert.Equal(11930.40m, bericht.Value);
    }

    /// <summary>Mehrere Berichte kommen in Stichtagsreihenfolge zurück.</summary>
    [Fact]
    public async Task Mehrere_Berichte_stehen_nach_Stichtag()
    {
        var id = Policy(PolicyKind.CapitalLife, 18373.87m, 2107.65m, 20481.52m);

        Report(id, "2025-07-31", 20481.52m);
        Report(id, "2023-07-31", 17400.00m);
        Report(id, "2024-07-31", 19960.14m);

        var berichte = (await Service().GetAsync(id))!.Reports;

        Assert.Equal(3, berichte.Count);
        Assert.Equal(
            [new DateOnly(2023, 7, 31), new DateOnly(2024, 7, 31), new DateOnly(2025, 7, 31)],
            berichte.Select(r => r.AsOf));
    }

    public void Dispose() => database.Dispose();
}
