using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Buchungsliste, Summen und Stapelvergabe.
/// </summary>
/// <remarks>
/// Die eine Regel, um die es hier vor allem geht: <b>Umbuchungen bleiben von der Stapelvergabe
/// ausgenommen</b>, sofern nicht ausdrücklich „Umbuchung“ gewählt wird. Wer fünfzehn Zeilen
/// markiert und „Wohnen“ wählt, meint nicht die Umbuchung aufs Tagesgeld, die zufällig
/// dazwischen liegt.
/// </remarks>
public sealed class TransactionListTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private int accountId;
    private int secondAccountId;
    private int wohnenId;
    private int freizeitId;

    public TransactionListTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
        };
        var tagesgeld = new Account
        {
            Name = "Tagesgeld", ShortName = "Tagesgeld", BankName = "Raiffeisen",
            Kind = AccountKind.Savings, BalanceAsOf = new DateOnly(2026, 8, 23),
        };
        context.Accounts.AddRange(giro, tagesgeld);

        var wohnen = new Category { Name = "Wohnen", Direction = CategoryDirection.Expense };
        var freizeit = new Category { Name = "Freizeit", Direction = CategoryDirection.Expense };
        context.Categories.AddRange(wohnen, freizeit);
        context.SaveChanges();

        accountId = giro.Id;
        secondAccountId = tagesgeld.Id;
        wohnenId = wohnen.Id;
        freizeitId = freizeit.Id;
    }

    private TransactionService Service() => new(database.Context(), clock);

    private int Add(string payee, decimal amount, TransactionKind kind, int? categoryId = null, int? account = null)
    {
        using var context = database.Context();
        var transaction = new Transaction
        {
            BookingDate = new DateOnly(2026, 8, 10),
            Payee = payee,
            Kind = kind,
            Amount = amount,
            CategoryId = categoryId,
            AccountId = account ?? accountId,
            CreatedAt = new DateTime(2026, 8, 10),
        };

        context.Transactions.Add(transaction);
        context.SaveChanges();
        return transaction.Id;
    }

    [Fact]
    public async Task Stapelvergabe_laesst_Umbuchungen_unveraendert()
    {
        var miete = Add("Vermieter", -900m, TransactionKind.Expense);
        var strom = Add("Stadtwerke", -142.50m, TransactionKind.Expense);
        var umbuchung = Add("Auf Tagesgeld", -500m, TransactionKind.Transfer);

        var result = await Service().AssignCategoryBatchAsync(new BatchAssignRequest
        {
            TransactionIds = [miete, strom, umbuchung],
            CategoryId = wohnenId,
        });

        Assert.Equal(2, result.Assigned);
        Assert.Equal(1, result.ProtectedTransfers);
        Assert.Equal("2 × Wohnen · 1 Umbuchung geschützt", result.Message);

        using var check = database.Context();
        Assert.Equal(wohnenId, check.Transactions.Single(t => t.Id == miete).CategoryId);
        Assert.Equal(wohnenId, check.Transactions.Single(t => t.Id == strom).CategoryId);

        var untouched = check.Transactions.Single(t => t.Id == umbuchung);
        Assert.Equal(TransactionKind.Transfer, untouched.Kind);
        Assert.Null(untouched.CategoryId);
    }

    [Fact]
    public async Task Ohne_Umbuchung_im_Stapel_bleibt_die_Meldung_schlicht()
    {
        var a = Add("Vermieter", -900m, TransactionKind.Expense);
        var b = Add("Stadtwerke", -142.50m, TransactionKind.Expense);

        var result = await Service().AssignCategoryBatchAsync(new BatchAssignRequest
        {
            TransactionIds = [a, b],
            CategoryId = wohnenId,
        });

        Assert.Equal("2 × Wohnen", result.Message);
        Assert.Equal(0, result.ProtectedTransfers);
    }

    [Fact]
    public async Task Ausdrueckliche_Umbuchung_fasst_alles_an()
    {
        var a = Add("Auf Tagesgeld", -500m, TransactionKind.Expense, freizeitId);
        var b = Add("Vom Giro", 500m, TransactionKind.Income);

        var result = await Service().AssignCategoryBatchAsync(new BatchAssignRequest
        {
            TransactionIds = [a, b],
            MarkAsTransfer = true,
        });

        Assert.Equal(2, result.Assigned);

        using var check = database.Context();
        Assert.All(check.Transactions, t => Assert.Equal(TransactionKind.Transfer, t.Kind));

        // Eine Umbuchung trägt keine Kategorie mehr — auch die vorher gesetzte fällt weg.
        Assert.All(check.Transactions, t => Assert.Null(t.CategoryId));
    }

    [Fact]
    public async Task Summen_lassen_Umbuchungen_aussen_vor()
    {
        Add("Gehalt", 3200m, TransactionKind.Income, null);
        Add("Vermieter", -900m, TransactionKind.Expense, wohnenId);
        Add("Auf Tagesgeld", -500m, TransactionKind.Transfer);

        var page = await Service().GetPageAsync(null);

        Assert.Equal(3200m, page.Totals.Income);
        Assert.Equal(900m, page.Totals.Expense);
        Assert.Equal(2300m, page.Totals.Balance);
        Assert.Equal(1, page.Totals.TransferCount);
    }

    [Fact]
    public async Task Summen_folgen_dem_Filter()
    {
        Add("Gehalt", 3200m, TransactionKind.Income);
        Add("Vermieter", -900m, TransactionKind.Expense, wohnenId, secondAccountId);

        var nurGiro = await Service().GetPageAsync(null, accountId: accountId);

        Assert.Equal(3200m, nurGiro.Totals.Income);
        Assert.Equal(0m, nurGiro.Totals.Expense);
        Assert.Equal(3200m, nurGiro.Totals.Balance);
    }

    [Fact]
    public async Task Triage_zaehlt_nur_was_der_Filter_zeigt()
    {
        Add("Ohne Kategorie A", -20m, TransactionKind.Expense, null, accountId);
        Add("Ohne Kategorie B", -30m, TransactionKind.Expense, null, secondAccountId);
        Add("Mit Kategorie", -40m, TransactionKind.Expense, wohnenId, accountId);

        var alles = await Service().GetPageAsync(null);
        Assert.Equal(2, alles.UncategorizedCount);
        Assert.Equal(2, alles.FilteredUncategorizedCount);

        var nurGiro = await Service().GetPageAsync(null, accountId: accountId);

        // Der Bestand hat zwei, der Ausschnitt zeigt eine — das Banner nennt die eine.
        Assert.Equal(2, nurGiro.UncategorizedCount);
        Assert.Equal(1, nurGiro.FilteredUncategorizedCount);
    }

    [Fact]
    public async Task Umbuchungen_gelten_nie_als_unkategorisiert()
    {
        Add("Auf Tagesgeld", -500m, TransactionKind.Transfer);

        var page = await Service().GetPageAsync(null);

        Assert.Equal(0, page.UncategorizedCount);
        Assert.Equal(0, page.FilteredUncategorizedCount);
    }

    [Fact]
    public async Task Filter_auf_Art_und_Kategorie_greifen_zusammen()
    {
        Add("Gehalt", 3200m, TransactionKind.Income);
        Add("Vermieter", -900m, TransactionKind.Expense, wohnenId);
        Add("Kino", -18m, TransactionKind.Expense, freizeitId);

        var ausgabenWohnen = await Service().GetPageAsync(
            null, categoryId: wohnenId, kind: TransactionKind.Expense);

        var einzige = Assert.Single(ausgabenWohnen.Items);
        Assert.Equal("Vermieter", einzige.Payee);
        Assert.Equal(1, ausgabenWohnen.FilteredCount);
        Assert.Equal(3, ausgabenWohnen.TotalCount);
    }

    [Fact]
    public async Task Nur_offene_zeigt_ausschliesslich_unkategorisierte()
    {
        Add("Ohne", -20m, TransactionKind.Expense);
        Add("Mit", -40m, TransactionKind.Expense, wohnenId);

        var offen = await Service().GetPageAsync(null, uncategorizedOnly: true);

        var einzige = Assert.Single(offen.Items);
        Assert.Equal("Ohne", einzige.Payee);
    }

    public void Dispose() => database.Dispose();
}
