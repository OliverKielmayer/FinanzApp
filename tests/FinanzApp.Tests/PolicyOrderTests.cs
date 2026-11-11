using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// In welcher Reihenfolge die beiden Vertragslisten stehen.
/// </summary>
/// <remarks>
/// <para>Die Vorsorge steht alphabetisch. Sie ist eine Liste, in der gesucht wird: Wer seinen
/// Bausparvertrag aufschlagen will, weiß seinen Namen und nicht seinen Rang. Nach Wert
/// sortiert wanderte derselbe Vertrag nach jeder Wertmitteilung an eine andere Stelle.</para>
/// <para>Die Absicherung bleibt nach Beitrag sortiert. Dort ist die Reihenfolge die Aussage:
/// oben steht, was am meisten kostet.</para>
/// </remarks>
public sealed class PolicyOrderTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 11);

    private PolicyService Service()
    {
        var context = database.Context();

        return new PolicyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(
                    Path.Combine(Path.GetTempPath(), "finanzapp-tests", "policy-order")),
                new ObjectLabelService(context),
                clock,
                new PdfPigTextReader(), NullLogger<DocumentService>.Instance),
            clock);
    }

    private void Vorsorge(string name, decimal wert)
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = name,
            Provider = "Musterkasse",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            CurrentValue = wert,
            ValuationDate = new DateOnly(2026, 5, 13),
            Premium = 37m,
            PremiumInterval = PremiumInterval.Monthly,
        });

        context.SaveChanges();
    }

    private void Absicherung(string name, decimal monatsbeitrag)
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = name,
            Provider = "Musterkasse",
            Kind = PolicyKind.Liability,
            IsCapitalForming = false,
            Premium = monatsbeitrag,
            PremiumInterval = PremiumInterval.Monthly,
        });

        context.SaveChanges();
    }

    /// <summary>
    /// Die drei Namen stehen bewusst gegen ihre Werte: Nach Wert sortiert käme genau die
    /// umgekehrte Reihenfolge heraus. Ein Test, der mit beiden Regeln durchginge, prüfte
    /// nichts.
    /// </summary>
    [Fact]
    public async Task Vorsorgevertraege_stehen_alphabetisch()
    {
        Vorsorge("Zwergenhort Bausparen", 27_413m);
        Vorsorge("Mondpfad Kapital", 18_206m);
        Vorsorge("Amselweg Riester", 9_155m);

        var sicht = await Service().GetOverviewAsync(capitalForming: true);

        Assert.Equal(
            ["Amselweg Riester", "Mondpfad Kapital", "Zwergenhort Bausparen"],
            sicht.Items.Select(i => i.Name));
    }

    /// <summary>
    /// Kleingeschrieben einsortiert wie großgeschrieben. Eine Sortierung über die Zeichenwerte
    /// hängte „amselweg“ hinten an, hinter das große Z — und niemand fände den Vertrag dort.
    /// </summary>
    [Fact]
    public async Task Die_Schreibweise_entscheidet_nicht_ueber_den_Platz()
    {
        Vorsorge("Zwergenhort Bausparen", 9_155m);
        Vorsorge("amselweg Riester", 9_155m);

        var sicht = await Service().GetOverviewAsync(capitalForming: true);

        Assert.Equal(
            ["amselweg Riester", "Zwergenhort Bausparen"],
            sicht.Items.Select(i => i.Name));
    }

    /// <summary>
    /// Die Gegenprobe: Die Umstellung darf nur die eine Liste betreffen. Auch hier stehen die
    /// Namen gegen die Beiträge, sonst bewiese der Test nichts.
    /// </summary>
    [Fact]
    public async Task Absicherungen_stehen_weiter_nach_Beitrag()
    {
        Absicherung("Amselweg Haftpflicht", 8m);
        Absicherung("Zwergenhort Hausrat", 24m);

        var sicht = await Service().GetOverviewAsync(capitalForming: false);

        Assert.Equal(
            ["Zwergenhort Hausrat", "Amselweg Haftpflicht"],
            sicht.Items.Select(i => i.Name));
    }

    public void Dispose() => database.Dispose();
}
