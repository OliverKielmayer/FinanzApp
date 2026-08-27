using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Bearbeiten und Löschen.
/// </summary>
/// <remarks>
/// Zwei Regeln aus dem Handoff stehen hier im Mittelpunkt: ein gepflegter Name darf beim
/// Bearbeiten <b>nicht</b> neu aus Art und Anbieter zusammengesetzt werden, und die Folgen einer
/// Löschung werden <b>gezählt</b>, nicht behauptet.
/// </remarks>
public sealed class EditAndDeleteTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private CreateFormService Service()
    {
        var context = database.Context();
        var paths = TestDatabase.PathService(root);
        var documents = new DocumentService(
            context, paths, new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        return new CreateFormService(context, clock, documents, new NoPolicyDocumentAnalyzer());
    }

    private int AddPolicy(string name, PolicyKind kind, string provider, bool capital = false)
    {
        using var context = database.Context();
        var policy = new Policy
        {
            Name = name,
            Kind = kind,
            Provider = provider,
            IsCapitalForming = capital,
            Premium = 42m,
            PremiumInterval = PremiumInterval.Monthly,
        };

        context.Policies.Add(policy);
        context.SaveChanges();
        return policy.Id;
    }

    [Fact]
    public async Task Ein_gepflegter_Name_ueberlebt_Oeffnen_und_Speichern()
    {
        // Genau der Fall aus dem Handoff: aus „Risikoleben“ darf nicht „Risikoleben Hannoversche“
        // werden, bloß weil jemand das Formular geöffnet und gespeichert hat.
        var id = AddPolicy("Risikoleben", PolicyKind.TermLife, "Hannoversche");

        var form = await Service().GetFormAsync(CreateObjectType.Protection, id);
        Assert.NotNull(form);
        Assert.Equal("Bearbeiten", form.Kicker);
        Assert.Equal("Versicherung bearbeiten", form.Title);
        Assert.Equal("Änderungen speichern", form.SubmitLabel);
        Assert.Equal("Risikoleben", form.Values["displayName"]);

        // Unverändert speichern.
        var result = await Service().UpdateAsync(
            CreateObjectType.Protection, id, new Dictionary<string, string?>(form.Values));

        Assert.True(result.Ok);

        using var check = database.Context();
        Assert.Equal("Risikoleben", check.Policies.Single().Name);
    }

    [Fact]
    public async Task Werte_kommen_aus_den_Rohfeldern_nicht_aus_der_Anzeigezeile()
    {
        var id = AddPolicy("Risikoleben", PolicyKind.TermLife, "Hannoversche");

        var form = await Service().GetFormAsync(CreateObjectType.Protection, id);

        // Der Versicherer steht nirgends im Namen — wer ihn dort herausparsen wollte, ließe das
        // Pflichtfeld leer.
        Assert.Equal("Hannoversche", form!.Values["provider"]);
        Assert.Equal(nameof(PolicyKind.TermLife), form.Values["kind"]);
    }

    [Fact]
    public async Task Das_Anlegeformular_kennt_keine_Bezeichnung_das_Bearbeiten_schon()
    {
        var create = await Service().GetFormAsync(CreateObjectType.Protection);
        Assert.DoesNotContain(create!.Fields, f => f.Key == "displayName");
        Assert.Null(create.DeleteImpact);
        Assert.Null(create.EditingId);

        var id = AddPolicy("Hausrat HUK", PolicyKind.HouseholdContents, "HUK-Coburg");
        var edit = await Service().GetFormAsync(CreateObjectType.Protection, id);

        Assert.Contains(edit!.Fields, f => f.Key == "displayName");
        Assert.Equal(id, edit.EditingId);
        Assert.NotNull(edit.DeleteImpact);
    }

    [Fact]
    public async Task Die_Loeschfolge_zaehlt_echte_Buchungen()
    {
        int accountId;
        using (var context = database.Context())
        {
            var account = new Account
            {
                Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
            };
            context.Accounts.Add(account);
            context.SaveChanges();
            accountId = account.Id;

            for (var i = 0; i < 3; i++)
            {
                context.Transactions.Add(new Transaction
                {
                    BookingDate = new DateOnly(2026, 8, 10),
                    Payee = "Test " + i,
                    Kind = TransactionKind.Expense,
                    Amount = -10m,
                    AccountId = accountId,
                    CreatedAt = new DateTime(2026, 8, 10),
                });
            }

            context.SaveChanges();
        }

        var form = await Service().GetFormAsync(CreateObjectType.Account, accountId);

        Assert.Equal("Konto löschen", form!.DeleteImpact!.Title);
        Assert.Contains("3 Buchungen", form.DeleteImpact.Consequence);
        Assert.Contains("Ohne Konto", form.DeleteImpact.Consequence);
    }

    [Fact]
    public async Task Ohne_Bezug_sagt_die_Folge_auch_das()
    {
        int accountId;
        using (var context = database.Context())
        {
            var account = new Account
            {
                Name = "Leeres Konto", ShortName = "Leer", BankName = "Bank",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
            };
            context.Accounts.Add(account);
            context.SaveChanges();
            accountId = account.Id;
        }

        var form = await Service().GetFormAsync(CreateObjectType.Account, accountId);

        Assert.Equal("An diesem Konto hängt keine Buchung.", form!.DeleteImpact!.Consequence);
    }

    [Fact]
    public async Task Ein_geloeschtes_Konto_nimmt_seine_Buchungen_nicht_mit()
    {
        int accountId;
        using (var context = database.Context())
        {
            var account = new Account
            {
                Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
            };
            context.Accounts.Add(account);
            context.SaveChanges();
            accountId = account.Id;

            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 8, 10),
                Payee = "Vermieter",
                Kind = TransactionKind.Expense,
                Amount = -900m,
                AccountId = accountId,
                CreatedAt = new DateTime(2026, 8, 10),
            });
            context.SaveChanges();
        }

        var result = await Service().DeleteAsync(CreateObjectType.Account, accountId);

        Assert.True(result.Ok);
        Assert.Contains("Ohne Konto", result.Message);

        using var check = database.Context();

        // Die Buchung ist eine Tatsache — sie bleibt, das Konto war nur ihre Schublade.
        var transaction = Assert.Single(check.Transactions);
        var orphan = check.Accounts.Single();
        Assert.Equal("Ohne Konto", orphan.Name);
        Assert.Equal(orphan.Id, transaction.AccountId);
    }

    [Fact]
    public async Task Ein_geloeschter_Kfz_Vertrag_nimmt_das_Fahrzeug_nicht_mit()
    {
        var policyId = AddPolicy("Kfz WGV", PolicyKind.Vehicle, "WGV");

        using (var context = database.Context())
        {
            context.Vehicles.Add(new Vehicle { Name = "VW Passat", Plate = "L-2905", PolicyId = policyId });
            context.SaveChanges();
        }

        var form = await Service().GetFormAsync(CreateObjectType.Protection, policyId);
        Assert.Contains("1 Fahrzeug verliert", form!.DeleteImpact!.Consequence);

        Assert.True((await Service().DeleteAsync(CreateObjectType.Protection, policyId)).Ok);

        using var check = database.Context();
        var vehicle = Assert.Single(check.Vehicles);
        Assert.Null(vehicle.PolicyId);
    }

    [Fact]
    public async Task Ein_Budget_im_Quartal_kommt_als_Quartalsbetrag_zurueck()
    {
        int budgetId;
        using (var context = database.Context())
        {
            var category = new Category { Name = "Kleidung", Direction = CategoryDirection.Expense };
            context.Categories.Add(category);
            context.SaveChanges();

            var budget = new Budget
            {
                Name = category.Name,
                CategoryId = category.Id,
                PlannedPerMonth = 300m,
                Period = PeriodScope.Quarter,
            };
            context.Budgets.Add(budget);
            context.SaveChanges();
            budgetId = budget.Id;
        }

        var form = await Service().GetFormAsync(CreateObjectType.Budget, budgetId);

        // Intern je Monat gefuehrt, im Formular im gewaehlten Zeitraum gezeigt — sonst wuerde
        // ein blosses Oeffnen und Speichern den Betrag dritteln.
        Assert.Equal("900,00", form!.Values["amount"]);
        Assert.Equal(nameof(PeriodScope.Quarter), form.Values["period"]);

        Assert.True((await Service().UpdateAsync(
            CreateObjectType.Budget, budgetId, new Dictionary<string, string?>(form.Values))).Ok);

        using var check = database.Context();
        Assert.Equal(300m, check.Budgets.Single().PlannedPerMonth);
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
