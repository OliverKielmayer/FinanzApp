using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Entfernung und Arbeitstage in beiden Masken — v5-Handoff, §18.3.
/// </summary>
/// <remarks>
/// <para>Der Befund im Wortlaut: sie waren „nur beim Anlegen setzbar, nicht in der
/// Bearbeitenmaske“. Wer den Arbeitsweg nachtragen wollte, kam an die Felder nicht heran — und
/// die Entfernungspauschale im Steuerjahr blieb ohne erkennbaren Grund leer.</para>
/// <para>Geprüft wird der ganze Weg: Formular → Anlegen → Bearbeitenmaske → Speichern. Ein Feld,
/// das diesen Weg nicht übersteht, ist ein verlorenes Feld.</para>
/// </remarks>
public sealed class CommuteFieldTests : IDisposable
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

    private static Dictionary<string, string?> Arbeit(string? km, string? tage) => new()
    {
        ["employer"] = "Nordlicht Systeme",
        ["kind"] = nameof(EmploymentKind.Permanent),
        ["start"] = "2019-04-01",
        ["gross"] = "4.900,00",
        ["commuteKm"] = km,
        ["workDays"] = tage,
    };

    /// <summary>Beide Angaben stehen im Anlegen- und im Bearbeitenformular.</summary>
    /// <remarks>
    /// Weil beide Masken dieselbe Beschreibung benutzen, prüft der Test beide — und mit ihnen
    /// die Vorbelegung, an der der Befund hing.
    /// </remarks>
    [Fact]
    public async Task Entfernung_und_Arbeitstage_stehen_im_Anlegen_und_im_Bearbeiten()
    {
        var anlegen = await Service().GetFormAsync(CreateObjectType.Employment);

        Assert.Contains(anlegen!.Fields, f => f.Key == "commuteKm");
        Assert.Contains(anlegen.Fields, f => f.Key == "workDays");

        var angelegt = await Service().CreateAsync(CreateObjectType.Employment, Arbeit("38", "214"));
        var bearbeiten = await Service().GetFormAsync(CreateObjectType.Employment, angelegt.Id!.Value);

        Assert.Contains(bearbeiten!.Fields, f => f.Key == "commuteKm");
        Assert.Equal("38", bearbeiten.Values["commuteKm"]);
        Assert.Equal("214", bearbeiten.Values["workDays"]);
    }

    /// <summary>Was in der Maske geändert wird, steht danach am Arbeitsverhältnis.</summary>
    [Fact]
    public async Task Der_Arbeitsweg_laesst_sich_nachtragen()
    {
        var angelegt = await Service().CreateAsync(
            CreateObjectType.Employment, Arbeit(km: null, tage: null));

        using (var vorher = database.Context())
        {
            Assert.Null(vorher.Employments.Single().CommuteKilometres);
        }

        var geaendert = await Service().UpdateAsync(
            CreateObjectType.Employment, angelegt.Id!.Value, Arbeit("21,5", "220"));

        Assert.True(geaendert.Ok);

        using var context = database.Context();
        var arbeit = context.Employments.Single();

        Assert.Equal(21.5m, arbeit.CommuteKilometres);
        Assert.Equal(220, arbeit.WorkDaysPerYear);
    }

    /// <summary>
    /// Eine der beiden Angaben allein wird abgewiesen.
    /// </summary>
    /// <remarks>
    /// Aus einer Entfernung ohne Arbeitstage entsteht keine Pauschale. Die Eingabe stillschweigend
    /// anzunehmen hieße: die Maske sagt „gespeichert“, und im Steuerjahr steht trotzdem nichts.
    /// </remarks>
    [Theory]
    [InlineData("38", null, "workDays")]
    [InlineData(null, "214", "commuteKm")]
    public async Task Entfernung_ohne_Arbeitstage_wird_abgewiesen(
        string? km, string? tage, string erwartetesFeld)
    {
        var ergebnis = await Service().CreateAsync(CreateObjectType.Employment, Arbeit(km, tage));

        Assert.False(ergebnis.Ok);
        Assert.Equal(erwartetesFeld, ergebnis.FieldKey);
        Assert.Empty(database.Context().Employments);
    }

    /// <summary>Ganz ohne Angaben bleibt es dabei — der Weg wird eben nicht geführt.</summary>
    [Fact]
    public async Task Ohne_beide_Angaben_bleibt_das_Anlegen_moeglich()
    {
        var ergebnis = await Service().CreateAsync(
            CreateObjectType.Employment, Arbeit(km: null, tage: null));

        Assert.True(ergebnis.Ok);
    }

    [Theory]
    [InlineData("0", "214", "commuteKm")]
    [InlineData("38", "400", "workDays")]
    public async Task Unmoegliche_Angaben_werden_abgewiesen(string km, string tage, string feld)
    {
        var ergebnis = await Service().CreateAsync(CreateObjectType.Employment, Arbeit(km, tage));

        Assert.False(ergebnis.Ok);
        Assert.Equal(feld, ergebnis.FieldKey);
    }

    public void Dispose() => database.Dispose();
}
