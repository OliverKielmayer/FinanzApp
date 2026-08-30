using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Vertrag führt seine Rohfelder — v5-Handoff, §19.4 bis §19.6.
/// </summary>
/// <remarks>
/// <para>Der Befund dahinter: eine Maske nimmt beim Anlegen Angaben an, die sie beim Bearbeiten
/// nicht mehr zeigt. Wer sie danach nachtragen will, kommt an sie nicht heran — und wer bloß
/// öffnet und speichert, verliert sie.</para>
/// <para>Deshalb prüfen diese Tests den ganzen Weg: Anlegen → Bearbeitenmaske → Speichern, und
/// dazu, dass beide Wege dieselbe Rechnung benutzen.</para>
/// </remarks>
public sealed class PolicyEditTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 30);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private CreateFormService Service()
    {
        var context = database.Context();
        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new CreateFormService(context, clock, documents, new NoPolicyDocumentAnalyzer());
    }

    private static Dictionary<string, string?> Vertrag(string? basis, string? bonus, string? wert) => new()
    {
        ["kind"] = nameof(PolicyKind.CapitalLife),
        ["provider"] = "Alte Leipziger",
        ["number"] = "01511104-01",
        ["baseValue"] = basis,
        ["accruedBonus"] = bonus,
        ["value"] = wert,
        ["asOf"] = "2025-07-31",
    };

    // ── §19.4: alle Rohfelder in der Maske ─────────────────────────────────────────────────

    /// <summary>
    /// Öffnen und Speichern verliert die Bestandteile des Werts nicht.
    /// </summary>
    /// <remarks>
    /// Sie standen im Formular, aber nicht in der Vorbelegung: die Maske öffnete leer, und wer
    /// nur den Ablauf korrigieren wollte, machte aus zwei erfassten Zahlen wieder eine einzelne.
    /// </remarks>
    [Fact]
    public async Task Oeffnen_und_Speichern_behaelt_die_Wertbestandteile()
    {
        var angelegt = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag("18.373,87", "2.107,65", wert: null));

        Assert.True(angelegt.Ok);

        var maske = await Service().GetFormAsync(CreateObjectType.Pension, angelegt.Id!.Value);

        Assert.Equal("18373,87", maske!.Values["baseValue"]);
        Assert.Equal("2107,65", maske.Values["accruedBonus"]);

        var gespeichert = await Service().UpdateAsync(
            CreateObjectType.Pension, angelegt.Id!.Value, new Dictionary<string, string?>(maske.Values));

        Assert.True(gespeichert.Ok);

        using var context = database.Context();
        var vertrag = context.Policies.Single();

        Assert.Equal(18373.87m, vertrag.BaseValue);
        Assert.Equal(2107.65m, vertrag.AccruedBonus);
        Assert.Equal(20481.52m, vertrag.CurrentValue);
    }

    // ── §19.5: die Summe entsteht aus den Bestandteilen ────────────────────────────────────

    /// <summary>
    /// Die Summe entsteht beim Anlegen genauso wie beim Ändern.
    /// </summary>
    /// <remarks>
    /// Der Anlegeweg rechnete sie zuerst nicht: dieselbe Eingabe wurde in der einen Maske
    /// angenommen und in der anderen mit „Erreichter Wert ist kein Betrag“ abgewiesen.
    /// </remarks>
    [Fact]
    public async Task Die_Bestandteile_ergeben_den_Wert_schon_beim_Anlegen()
    {
        var ergebnis = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag("12.000,00", "320,08", wert: null));

        Assert.True(ergebnis.Ok);
        Assert.Equal(12320.08m, database.Context().Policies.Single().CurrentValue);
    }

    /// <summary>Ohne Bestandteile bleibt das Wertfeld die Quelle.</summary>
    [Fact]
    public async Task Ohne_Bestandteile_zaehlt_der_eingetragene_Wert()
    {
        var ergebnis = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag(basis: null, bonus: null, wert: "9.400,00"));

        Assert.True(ergebnis.Ok);

        var vertrag = database.Context().Policies.Single();

        Assert.Equal(9400m, vertrag.CurrentValue);
        Assert.Null(vertrag.BaseValue);
    }

    /// <summary>Ganz ohne Wert wird abgewiesen — und zwar am Wertfeld.</summary>
    [Fact]
    public async Task Ohne_jede_Zahl_bleibt_es_bei_der_Rueckmeldung()
    {
        var ergebnis = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag(basis: null, bonus: null, wert: null));

        Assert.False(ergebnis.Ok);
        Assert.Equal("value", ergebnis.FieldKey);
    }

    // ── §19.6: auch erfasste Stände sind Stände ────────────────────────────────────────────

    /// <summary>Ein von Hand gepflegter Stand kommt in die Berichtsreihe.</summary>
    [Fact]
    public async Task Der_erfasste_Stand_steht_in_der_Berichtsreihe()
    {
        var angelegt = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag("18.373,87", "2.107,65", wert: null));

        using var context = database.Context();
        var bericht = Assert.Single(context.PolicyReports);

        Assert.Equal(angelegt.Id!.Value, bericht.PolicyId);
        Assert.Equal(new DateOnly(2025, 7, 31), bericht.AsOf);
        Assert.Equal(20481.52m, bericht.Value);
        Assert.Equal("erfasst", bericht.Source);
    }

    /// <summary>
    /// Ein neuer Stichtag legt einen Punkt daneben, derselbe überschreibt den vorhandenen.
    /// </summary>
    /// <remarks>
    /// Sonst entstünde aus einer korrigierten Zahl ein zweiter Punkt am selben Tag — und der
    /// Verlauf zeigte einen Sprung, den es nie gab.
    /// </remarks>
    [Fact]
    public async Task Ein_neuer_Stichtag_waechst_die_Reihe_derselbe_ersetzt_den_Stand()
    {
        var angelegt = await Service().CreateAsync(
            CreateObjectType.Pension, Vertrag("18.373,87", "2.107,65", wert: null));

        var korrigiert = Vertrag("18.400,00", "2.107,65", wert: null);
        korrigiert["displayName"] = "Alte Leipziger";
        await Service().UpdateAsync(CreateObjectType.Pension, angelegt.Id!.Value, korrigiert);

        using (var nachKorrektur = database.Context())
        {
            var bericht = Assert.Single(nachKorrektur.PolicyReports);
            Assert.Equal(20507.65m, bericht.Value);
        }

        var naechstesJahr = Vertrag("19.900,00", "2.480,00", wert: null);
        naechstesJahr["displayName"] = "Alte Leipziger";
        naechstesJahr["asOf"] = "2026-07-31";
        await Service().UpdateAsync(CreateObjectType.Pension, angelegt.Id!.Value, naechstesJahr);

        using var context = database.Context();
        var reihe = context.PolicyReports.OrderBy(r => r.AsOf).ToList();

        Assert.Equal(2, reihe.Count);
        Assert.Equal(20507.65m, reihe[0].Value);
        Assert.Equal(22380m, reihe[1].Value);
    }

    public void Dispose() => database.Dispose();
}
