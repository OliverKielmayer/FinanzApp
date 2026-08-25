using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Anlege-Flows. Geprüft wird, was der Handoff ausdrücklich verlangt: die Validierung nennt
/// das fehlende Feld beim Namen, doppelte Anlage wird abgelehnt, und jeder Flow schreibt
/// wirklich — kein Formular, das nur so tut.
/// </summary>
public sealed class CreateFormTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private CreateFormService Service() => new(database.Context(), clock);

    [Fact]
    public async Task Fehlendes_Pflichtfeld_wird_beim_Namen_genannt()
    {
        var result = await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.HouseholdContents),
            // Versicherer fehlt
            ["premium"] = "156",
        });

        Assert.False(result.Ok);
        Assert.Equal("provider", result.FieldKey);
        Assert.Equal("Versicherer fehlt", result.Message);
    }

    [Fact]
    public async Task Die_Meldung_nennt_das_erste_fehlende_Feld_der_Reihenfolge_nach()
    {
        // Art steht vor Versicherer im Formular, also wird sie zuerst bemängelt.
        var result = await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>());

        Assert.False(result.Ok);
        Assert.Equal("kind", result.FieldKey);
        Assert.Equal("Art fehlt", result.Message);
    }

    [Fact]
    public async Task Konto_wird_wirklich_angelegt_und_ist_danach_waehlbar()
    {
        var created = await Service().CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
        {
            ["kind"] = "checking",
            ["bank"] = "Volksbank",
            ["opening"] = "1.234,56",
            ["asOf"] = "2026-08-01",
        });

        Assert.True(created.Ok);

        using (var context = database.Context())
        {
            var account = Assert.Single(context.Accounts);
            Assert.Equal("Volksbank Giro", account.Name);
            Assert.Equal(1234.56m, account.OpeningBalance);
            Assert.Equal(new DateOnly(2026, 8, 1), account.BalanceAsOf);
        }

        // Und es steht sofort in der Vertragsanlage zur Auswahl — der Handoff verlangt genau das.
        var contractForm = await Service().GetFormAsync(CreateObjectType.Contract);
        var accountField = contractForm!.Fields.Single(f => f.Key == "account");
        Assert.Contains(accountField.Options!, o => o.Label == "Volksbank Giro");
    }

    [Fact]
    public async Task Zweites_Budget_auf_dieselbe_Kategorie_wird_abgelehnt()
    {
        int categoryId;
        using (var context = database.Context())
        {
            var category = new Category { Name = "Lebensmittel", Direction = CategoryDirection.Expense };
            context.Categories.Add(category);
            context.SaveChanges();
            categoryId = category.Id;
        }

        var values = new Dictionary<string, string?>
        {
            ["category"] = categoryId.ToString(),
            ["amount"] = "600",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Budget, values)).Ok);

        var second = await Service().CreateAsync(CreateObjectType.Budget, values);

        Assert.False(second.Ok);
        Assert.Equal("category", second.FieldKey);
        Assert.Equal("Budget für Lebensmittel besteht bereits", second.Message);
    }

    [Fact]
    public async Task Quartalsbudget_wird_auf_den_Monat_heruntergerechnet()
    {
        int categoryId;
        using (var context = database.Context())
        {
            var category = new Category { Name = "Kleidung", Direction = CategoryDirection.Expense };
            context.Categories.Add(category);
            context.SaveChanges();
            categoryId = category.Id;
        }

        await Service().CreateAsync(CreateObjectType.Budget, new Dictionary<string, string?>
        {
            ["category"] = categoryId.ToString(),
            ["amount"] = "900",
            ["period"] = nameof(BudgetPeriod.Quarter),
        });

        using var check = database.Context();
        var budget = Assert.Single(check.Budgets);
        Assert.Equal(300m, budget.PlannedPerMonth);
        Assert.Equal(BudgetPeriod.Quarter, budget.Period);
    }

    [Fact]
    public async Task Depot_im_Kontoformular_legt_kein_Konto_an()
    {
        // Der Handoff: „Depot" im Konto-Formular verweist auf den Depot-Flow.
        var form = await Service().GetFormAsync(CreateObjectType.Account);
        var depotOption = form!.Fields.Single(f => f.Key == "kind").Options!.Single(o => o.Value == "depot");

        Assert.Equal("/neu/depot", depotOption.RedirectTo);

        var result = await Service().CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
        {
            ["kind"] = "depot",
            ["bank"] = "finanzen.net ZERO",
            ["opening"] = "0",
            ["asOf"] = "2026-08-01",
        });

        Assert.False(result.Ok);
        using var check = database.Context();
        Assert.Empty(check.Accounts);
    }

    [Fact]
    public async Task Vorsorgevertrag_traegt_seinen_Wert_und_Stichtag()
    {
        var result = await Service().CreateAsync(CreateObjectType.Pension, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.Riester),
            ["provider"] = "Debeka",
            ["value"] = "11.930,40",
            ["asOf"] = "2025-12-31",
        });

        Assert.True(result.Ok);

        using var check = database.Context();
        var policy = Assert.Single(check.Policies);
        Assert.True(policy.IsCapitalForming);
        Assert.Equal(11930.40m, policy.AssetValue);
        Assert.Equal(new DateOnly(2025, 12, 31), policy.ValuationDate);
    }

    [Fact]
    public async Task Eine_Absicherung_bekommt_keinen_Wert_auch_nicht_versehentlich()
    {
        await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.TermLife),
            ["provider"] = "Heidelberger Leben",
            ["premium"] = "42",
            ["interval"] = nameof(PremiumInterval.Monthly),

            // Diese beiden Felder gibt es im Absicherungs-Formular gar nicht.
            ["value"] = "250000",
            ["asOf"] = "2025-12-31",
        });

        using var check = database.Context();
        var policy = Assert.Single(check.Policies);
        Assert.False(policy.IsCapitalForming);
        Assert.Null(policy.AssetValue);
        Assert.Null(policy.CurrentValue);
    }

    [Fact]
    public async Task Betrag_wird_deutsch_und_englisch_gelesen()
    {
        foreach (var (input, expected) in new[] { ("1.234,56", 1234.56m), ("1234,56", 1234.56m), ("1234.56", 1234.56m) })
        {
            using var fresh = new TestDatabase();
            var service = new CreateFormService(fresh.Context(), clock);

            var result = await service.CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
            {
                ["kind"] = "checking",
                ["bank"] = "Testbank",
                ["opening"] = input,
                ["asOf"] = "2026-08-01",
            });

            Assert.True(result.Ok, input);
            using var check = fresh.Context();
            Assert.Equal(expected, check.Accounts.Single().OpeningBalance);
        }
    }

    [Fact]
    public async Task Jeder_Objekttyp_hat_ein_Formular_mit_Pflichtfeldern()
    {
        CreateObjectType[] types =
        [
            CreateObjectType.Account, CreateObjectType.Depot, CreateObjectType.Pension,
            CreateObjectType.Protection, CreateObjectType.Property, CreateObjectType.Contract,
            CreateObjectType.Budget,
        ];

        foreach (var type in types)
        {
            var form = await Service().GetFormAsync(type);

            Assert.NotNull(form);
            Assert.NotEmpty(form.Fields);
            Assert.Contains(form.Fields, f => f.Required);
            Assert.False(string.IsNullOrWhiteSpace(form.SubmitLabel), type.ToString());
        }
    }

    [Fact]
    public async Task Fahrzeug_gibt_es_noch_nicht()
    {
        // Kommt mit Schritt 8. Bis dahin darf der Typ nichts vortäuschen.
        Assert.Null(await Service().GetFormAsync(CreateObjectType.Vehicle));
    }

    public void Dispose() => database.Dispose();
}
