using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Metazeile eines Objekts — v5-Handoff, Abschnitt 3(c) und 6.4.
/// </summary>
/// <remarks>
/// <para>Der Handoff verlangt <em>eine</em> Builder-Funktion je Klasse, von Klassenliste,
/// Bestand-Liste und Suchtreffern genutzt. Vorher baute jede Ansicht ihre eigene: die
/// Policenliste nannte nur Vertragsart und Notiz, die Suche nur den Anbieter, der Bestand einen
/// dritten Satz. Drei Antworten auf dieselbe Frage.</para>
/// <para>Die wichtigsten Tests hier vergleichen darum nicht nur den Inhalt, sondern die
/// Ansichten <em>untereinander</em> — genau die Zusage, die der Handoff gibt.</para>
/// </remarks>
public sealed class HoldingMetaTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents()
    {
        var context = database.Context();

        return new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);
    }

    private HoldingsService Holdings()
    {
        var context = database.Context();

        var dashboard = new DashboardService(
            context,
            new AccountService(context),
            TestDatabase.Portfolio(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock,
            TestDatabase.SignedIn(null));

        return new HoldingsService(
            context, dashboard, new VehicleService(context, Documents(), clock), TestDatabase.Portfolio(context), clock);
    }

    private PolicyService Policies() => new(database.Context(), Documents(), clock);

    // ── Der Bauplan ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Was ein Objekt hat, steht da — in dieser Reihenfolge.
    /// </summary>
    [Fact]
    public void Eine_volle_Police_nennt_alles_aus_ihren_Rohfeldern()
    {
        var police = new Policy
        {
            Name = "Hausrat", Provider = "HUK", PolicyNumber = "HR-4711",
            Kind = PolicyKind.HouseholdContents, Premium = 13m,
            PremiumInterval = PremiumInterval.Monthly,
            EndsOn = new DateOnly(2027, 12, 31), NoticePeriodMonths = 3,
            Notes = "Versicherungssumme 250.000 €",
        };

        Assert.Equal(
            "Hausrat · HUK · Nr. HR-4711 · bis 31.12.2027 · Kündigungsfrist 3 Monate · "
            + "Versicherungssumme 250.000 €",
            HoldingMeta.ForProtection(police));
    }

    /// <summary>
    /// Das freie Zusatzfeld überlebt jede Bearbeitung und steht am Ende.
    /// </summary>
    /// <remarks>
    /// Der Handoff nennt es ausdrücklich: „MLP bestpartner classic“, „kein Rückkaufswert“ —
    /// Angaben, die in kein Formularfeld passen und trotzdem die einzige Auskunft sein können,
    /// die den Vertrag unterscheidbar macht.
    /// </remarks>
    [Fact]
    public void Die_freie_Notiz_haengt_hinten_an()
    {
        var police = new Policy
        {
            Name = "Kapital-LV", Provider = "MLP", Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true, Notes = "MLP bestpartner classic",
        };

        Assert.EndsWith("· MLP bestpartner classic", HoldingMeta.ForPension(police));
    }

    /// <summary>
    /// Leere Felder fallen weg — samt ihrem Trennzeichen.
    /// </summary>
    /// <remarks>
    /// Der alte Fehler lautete „Vertrag · ohne Konto“: eine Aussage über ein Feld, das schlicht
    /// leer ist. Richtig ist, die Angabe wegzulassen, nicht ihre Abwesenheit zu formulieren.
    /// </remarks>
    [Fact]
    public void Leeres_faellt_weg_und_hinterlaesst_keinen_Mittelpunkt()
    {
        var police = new Policy
        {
            Name = "Haftpflicht", Provider = "Allianz", Kind = PolicyKind.Liability,
        };

        var meta = HoldingMeta.ForProtection(police);

        Assert.Equal("Haftpflicht · Allianz", meta);
        Assert.DoesNotContain("· ·", meta);
        Assert.False(meta.StartsWith('·') || meta.EndsWith('·'));
    }

    /// <summary>Null Monate sind keine Frist, sondern eine fehlende Angabe.</summary>
    [Fact]
    public void Eine_Frist_von_null_steht_nicht_da()
    {
        var vertrag = new Contract
        {
            Name = "Strom", Provider = "Stadtwerke", NoticePeriodWeeks = 0,
        };

        Assert.DoesNotContain("Kündigungsfrist", HoldingMeta.ForContract(vertrag));
    }

    [Fact]
    public void Das_Kennzeichen_steht_nicht_in_der_Metazeile()
    {
        var fahrzeug = new Vehicle
        {
            Name = "VW Passat", Plate = "HD-AB 123", Usage = "Erstwagen",
            FirstRegistration = new DateOnly(2019, 4, 1),
        };

        var meta = HoldingMeta.ForVehicle(fahrzeug);

        Assert.Equal("Erstwagen · EZ 01.04.2019", meta);
        Assert.DoesNotContain("HD-AB", meta);
    }

    /// <summary>
    /// Jede benannte Vertragsart hat eine Beschriftung.
    /// </summary>
    /// <remarks>
    /// Fällt eine durch, heißt sie „Vertrag“ — und in der Suche stand dann „Vertrag · Vertrag“,
    /// weil die Trefferzeile ihre Objektart davorsetzt. Aufgefallen an der Krankenversicherung,
    /// die in einer zweiten, unvollständigen Tabelle fehlte. Jetzt gibt es nur noch eine, und
    /// dieser Test hält sie vollständig.
    /// </remarks>
    [Fact]
    public void Nur_Other_faellt_auf_die_Ersatzbeschriftung()
    {
        var ohneEigene = Enum.GetValues<PolicyKind>()
            .Where(k => k != PolicyKind.Other && HoldingMeta.KindLabel(k) == "Vertrag")
            .ToList();

        Assert.Empty(ohneEigene);
    }

    // ── Dieselbe Zeile in jeder Ansicht ────────────────────────────────────────────────────

    private Policy AngelegtePolice()
    {
        using var context = database.Context();
        var police = new Policy
        {
            Name = "Hausrat", Provider = "HUK", PolicyNumber = "HR-4711",
            Kind = PolicyKind.HouseholdContents, Premium = 13m,
            PremiumInterval = PremiumInterval.Monthly,
            EndsOn = new DateOnly(2027, 12, 31), NoticePeriodMonths = 3,
            Notes = "Versicherungssumme 250.000 €",
        };

        context.Policies.Add(police);
        context.SaveChanges();

        return police;
    }

    /// <summary>
    /// Klassenliste, Bestand und Suche zeigen für dieselbe Police denselben Satz.
    /// </summary>
    /// <remarks>
    /// Das ist die Zusage des Handoffs, und sie lässt sich nur so prüfen: nicht gegen eine
    /// erwartete Zeichenkette, sondern die drei Ansichten gegeneinander. Eine vierte Ansicht,
    /// die sich wieder etwas Eigenes baut, fällt hier auf.
    /// </remarks>
    [Fact]
    public async Task Klassenliste_Bestand_und_Suche_zeigen_dieselbe_Zeile()
    {
        var police = AngelegtePolice();
        var erwartet = HoldingMeta.ForProtection(police);

        var klassenliste = (await Policies().GetOverviewAsync(capitalForming: false))
            .Items.Single();
        var bestand = (await Holdings().GetAsync(HoldingClass.Protection)).Rows.Single();
        var treffer = (await Documents().SearchAsync("Hausrat")).Objects
            .Single(o => o.TargetType == LinkTargetType.Policy);

        Assert.Equal(erwartet, klassenliste.Meta);
        Assert.Equal(erwartet, bestand.Meta);
        Assert.Equal(erwartet, treffer.Subtitle);
    }

    /// <summary>
    /// Die Suche findet jetzt auch, was nur im Untertitel steht.
    /// </summary>
    /// <remarks>
    /// Kein Selbstzweck, sondern die Folge: sie durchsucht Bezeichnung und Untertitel, und der
    /// Untertitel trägt seit dem gemeinsamen Builder die Vertragsnummer. Vorher stand dort nur
    /// der Anbieter, und wer die Nummer eintippte, fand nichts.
    /// </remarks>
    [Fact]
    public async Task Die_Suche_findet_eine_Vertragsnummer()
    {
        AngelegtePolice();

        var treffer = (await Documents().SearchAsync("HR-4711")).Objects;

        Assert.Single(treffer);
        Assert.Equal("Hausrat", treffer[0].Label);
    }

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
