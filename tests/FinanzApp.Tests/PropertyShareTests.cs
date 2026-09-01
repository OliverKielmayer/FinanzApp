using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Beteiligung an einer gemeinsamen Immobilie — Handoff „Gemeinsame Immobilie“, 3.1 und 9.
/// </summary>
/// <remarks>
/// <para>Drei Größen, die auseinanderfallen: der <b>Eigentumsanteil</b> steht im Grundbuch, das
/// <b>eingebrachte Eigenkapital</b> ist ungleich, die <b>laufenden Einlagen</b> verschieben den
/// Stand weiter. Wer den Anteil mit dem Eingebrachten verwechselt, beantwortet „wer hat mehr
/// getragen“ mit dem Grundbuch — und das ist falsch.</para>
/// <para>Der Ausgleich ist deshalb <b>abgeleitet</b>: eingebracht minus Eigentumsanteil an der
/// Summe des Eingebrachten. Er steht nirgends in der Datenbank.</para>
/// </remarks>
public sealed class PropertyShareTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Drei Personen im Haushalt: zwei, die besitzen können, und ein Lesezugriff.
    /// </summary>
    /// <remarks>
    /// Der Lesezugriff gehört dazu, weil er den interessanten Fall trägt: er sieht die Anteile,
    /// hat aber keinen eigenen.
    /// </remarks>
    public PropertyShareTests()
    {
        // Benutzer sind nicht mandantengefiltert — der Haushalt muss von Hand dran, sonst greift
        // der Fremdschlüssel ins Leere.
        var haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        context.Users.AddRange(
            Benutzer(haushalt, "Oliver W.", "oliver@test.de", HouseholdRole.Owner),
            Benutzer(haushalt, "Sabine K.", "sabine@test.de", HouseholdRole.Member),
            Benutzer(haushalt, "Steuerbüro", "kanzlei@test.de", HouseholdRole.ReadOnly));

        context.SaveChanges();

        haushaltId = haushalt;
    }

    private readonly int haushaltId;

    private User Benutzer(int haushalt, string name, string email, HouseholdRole rolle) => new()
    {
        HouseholdId = haushalt, Name = name, Email = email, PasswordHash = "-",
        Role = rolle, CreatedAt = clock.Now,
    };

    private PropertyService Service(int? alsBenutzer)
    {
        var context = database.Context(haushaltId);

        return new PropertyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(root),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(alsBenutzer)));
    }

    private (int PropertyId, int Oliver, int Sabine) Objekt(
        decimal marktwert = 420000m,
        decimal restschuld = 275100m,
        decimal anteilOliver = 50m,
        decimal anteilSabine = 50m,
        decimal eigenkapitalOliver = 90000m,
        decimal eigenkapitalSabine = 50000m)
    {
        using var context = database.Context(haushaltId);

        var oliver = context.Users.OrderBy(u => u.Id).First().Id;
        var sabine = context.Users.OrderBy(u => u.Id).Skip(1).First().Id;

        var darlehen = new Loan
        {
            Name = "Immobiliendarlehen",
            Lender = "Sparkasse",
            RemainingDebt = restschuld,
            InterestRatePercent = 1.84m,
            Installment = 1500m,
            NextPaymentDate = new DateOnly(2026, 10, 1),
        };

        context.Loans.Add(darlehen);
        context.SaveChanges();

        var objekt = new Property
        {
            Name = "Haus zu zweit",
            MarketValue = marktwert,
            PurchaseDate = new DateOnly(2026, 4, 1),
            LoanId = darlehen.Id,
            Shares =
            {
                new PropertyShare { UserId = oliver, Percent = anteilOliver, Equity = eigenkapitalOliver },
                new PropertyShare { UserId = sabine, Percent = anteilSabine, Equity = eigenkapitalSabine },
            },
        };

        context.Properties.Add(objekt);
        context.SaveChanges();

        return (objekt.Id, oliver, sabine);
    }

    // ── Der Ausgleich ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Ausgleich ist die halbe Differenz des Eigenkapitals.
    /// </summary>
    /// <remarks>
    /// 90.000 gegen 50.000 bei halbe-halbe: der Ausgleich ist ±20.000, nicht ±40.000. Weil das
    /// Eigentum halb ist, trägt der andere die Hälfte der Differenz.
    /// </remarks>
    [Fact]
    public async Task Der_Ausgleich_ist_die_halbe_Differenz()
    {
        var (id, oliver, sabine) = Objekt();

        var meiner = (await Service(oliver).GetAsync(id))!.Participation!;
        Assert.Equal(20000m, meiner.Settlement);

        var ihrer = (await Service(sabine).GetAsync(id))!.Participation!;
        Assert.Equal(-20000m, ihrer.Settlement);
    }

    /// <summary>Die Ausgleiche aller Beteiligten heben sich auf.</summary>
    /// <remarks>
    /// Eine Forderung ohne Gegenstück wäre Geld, das aus dem Nichts entsteht. Gilt auch bei
    /// ungleichen Anteilen — das ist der Punkt der Formel.
    /// </remarks>
    [Theory]
    [InlineData(50, 50)]
    [InlineData(70, 30)]
    [InlineData(33.34, 66.66)]
    public async Task Die_Ausgleiche_heben_sich_auf(double anteilA, double anteilB)
    {
        var (id, oliver, _) = Objekt(
            anteilOliver: (decimal)anteilA, anteilSabine: (decimal)anteilB);

        var beteiligung = (await Service(oliver).GetAsync(id))!.Participation!;

        Assert.Equal(0m, beteiligung.Participants.Sum(p => p.Settlement));
    }

    /// <summary>
    /// Gleiches Eigenkapital heißt kein Ausgleich.
    /// </summary>
    /// <remarks>
    /// Der ruhige Fall muss ruhig aussehen: eine Forderung von 0,01 € wäre schlimmer als keine.
    /// </remarks>
    [Fact]
    public async Task Bei_gleichem_Eigenkapital_gibt_es_keinen_Ausgleich()
    {
        var (id, oliver, _) = Objekt(eigenkapitalOliver: 70000m, eigenkapitalSabine: 70000m);

        Assert.Equal(0m, (await Service(oliver).GetAsync(id))!.Participation!.Settlement);
    }

    /// <summary>
    /// Ungleiche Anteile werden am Anteil gemessen, nicht am Kopf.
    /// </summary>
    /// <remarks>
    /// Bei 70/30 und 90.000/50.000 sind zusammen 140.000 eingebracht. Olivers Anteil verlangt
    /// 98.000 — er hat 90.000 gebracht und damit 8.000 zu wenig.
    /// </remarks>
    [Fact]
    public async Task Ungleiche_Anteile_verschieben_den_Ausgleich()
    {
        var (id, oliver, sabine) = Objekt(anteilOliver: 70m, anteilSabine: 30m);

        Assert.Equal(-8000m, (await Service(oliver).GetAsync(id))!.Participation!.Settlement);
        Assert.Equal(8000m, (await Service(sabine).GetAsync(id))!.Participation!.Settlement);
    }

    // ── Die eigene Sicht ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wert und Schuld werden nach dem Eigentumsanteil geteilt — und beide Größen bleiben da.
    /// </summary>
    /// <remarks>
    /// Die Restschuld bleibt die Restschuld; der Haftungsanteil ist eine zusätzliche Größe. Eine
    /// Konstante umzudefinieren und ihren Namen zu lassen war der schwerste Fehler des Entwurfs.
    /// </remarks>
    [Fact]
    public async Task Der_eigene_Anteil_teilt_Wert_und_Schuld()
    {
        var (id, oliver, _) = Objekt();

        var beteiligung = (await Service(oliver).GetAsync(id))!.Participation!;

        Assert.Equal(420000m, beteiligung.MarketValue);
        Assert.Equal(275100m, beteiligung.DebtTotal);
        Assert.Equal(210000m, beteiligung.ValueShare);
        Assert.Equal(137550m, beteiligung.DebtShare);
        Assert.Equal(72450m, beteiligung.NetShare);
    }

    /// <summary>
    /// Wer nicht beteiligt ist, bekommt keine eigene Sicht.
    /// </summary>
    /// <remarks>
    /// Das Steuerbüro sieht die Anteile, aber keinen eigenen — eine Null als Anteil auszugeben
    /// wäre eine Aussage über Eigentum, das es nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Ohne_eigenen_Anteil_gibt_es_keine_eigene_Sicht()
    {
        var (id, _, _) = Objekt();

        using var context = database.Context(haushaltId);
        var fremder = context.Users.OrderBy(u => u.Id).Last().Id;

        var beteiligung = (await Service(fremder).GetAsync(id))!.Participation!;

        Assert.Null(beteiligung.ValueShare);
        Assert.Null(beteiligung.DebtShare);
        Assert.Null(beteiligung.NetShare);
        Assert.Null(beteiligung.Settlement);
        Assert.Equal(2, beteiligung.Participants.Count);
        Assert.DoesNotContain(beteiligung.Participants, p => p.IsSelf);
    }

    /// <summary>Ohne gepflegte Anteile gibt es keine Beteiligung.</summary>
    /// <remarks>
    /// Dann gehört das Objekt dem Haushalt, und der ganze Wert zählt. Eine Quote zu erfinden
    /// wäre schlimmer als keine.
    /// </remarks>
    [Fact]
    public async Task Ohne_Anteile_gibt_es_keine_Beteiligung()
    {
        using (var context = database.Context(haushaltId))
        {
            context.Properties.Add(new Property { Name = "Haus allein", MarketValue = 300000m });
            context.SaveChanges();
        }

        using var lesen = database.Context(haushaltId);
        var id = lesen.Properties.Single().Id;

        Assert.Null((await Service(lesen.Users.First().Id).GetAsync(id))!.Participation);
    }

    /// <summary>Die Anteile kommen absteigend zurück, der eigene ist gekennzeichnet.</summary>
    [Fact]
    public async Task Die_Anteile_kommen_geordnet_und_gekennzeichnet()
    {
        var (id, oliver, _) = Objekt(anteilOliver: 30m, anteilSabine: 70m);

        var beteiligung = (await Service(oliver).GetAsync(id))!.Participation!;

        Assert.Equal(70m, beteiligung.Participants[0].Percent);
        Assert.True(beteiligung.Participants[1].IsSelf);
        Assert.True(beteiligung.PercentComplete);
    }

    /// <summary>Anteile, die nicht aufgehen, werden gemeldet statt weggerechnet.</summary>
    [Fact]
    public async Task Anteile_unter_hundert_Prozent_werden_gemeldet()
    {
        var (id, oliver, _) = Objekt(anteilOliver: 40m, anteilSabine: 50m);

        Assert.False((await Service(oliver).GetAsync(id))!.Participation!.PercentComplete);
    }

    public void Dispose() => database.Dispose();
}
