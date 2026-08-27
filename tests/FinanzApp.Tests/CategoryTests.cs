using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Kategorien verwalten.
/// </summary>
/// <remarks>
/// Kategorien sind Daten, keine Konstante im Code. Sie speisen die Chips bei Erfassung,
/// Kategorie-Fenster, Import und Budgetanlage — was hier geschieht, wirkt überall.
/// </remarks>
public sealed class CategoryTests : IDisposable
{
    private readonly TestDatabase database = new();

    private readonly int wohnen;
    private readonly int gehalt;

    public CategoryTests()
    {
        using var context = database.Context();

        var a = new Category { Name = "Wohnen", Direction = CategoryDirection.Expense };
        var b = new Category { Name = "Gehalt", Direction = CategoryDirection.Income };
        var konto = new Account
        {
            Name = "Giro", ShortName = "Giro", BankName = "Bank",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 26),
        };

        context.Categories.AddRange(a, b);
        context.Accounts.Add(konto);
        context.SaveChanges();

        wohnen = a.Id;
        gehalt = b.Id;

        context.Transactions.Add(new Transaction
        {
            BookingDate = new DateOnly(2026, 8, 1), Payee = "Stadtwerke",
            Kind = TransactionKind.Expense, Amount = -96m,
            AccountId = konto.Id, CategoryId = wohnen, CreatedAt = new DateTime(2026, 8, 1),
        });
        context.CategorizationRules.Add(new CategorizationRule
        {
            PayeePattern = "Stadtwerke", CategoryId = wohnen,
        });
        context.SaveChanges();
    }

    private CatalogService Service() => new(database.Context());

    [Fact]
    public async Task Der_Verwendungsnachweis_zaehlt_was_wirklich_dranhaengt()
    {
        var ausgaben = await Service().GetUsageAsync(CategoryDirection.Expense);
        var eintrag = ausgaben.Single(c => c.Id == wohnen);

        Assert.Equal(1, eintrag.TransactionCount);
        Assert.Equal(1, eintrag.RuleCount);
        Assert.False(eintrag.HasBudget);
        Assert.True(eintrag.IsUsed);
    }

    [Fact]
    public async Task Ausgaben_und_Einnahmen_sind_getrennte_Listen()
    {
        // Eine Ausgabenkategorie darf bei einer Gutschrift nicht erscheinen — sonst wird die
        // falsche Zuordnung erst möglich gemacht.
        var ausgaben = await Service().GetUsageAsync(CategoryDirection.Expense);
        var einnahmen = await Service().GetUsageAsync(CategoryDirection.Income);

        Assert.DoesNotContain(ausgaben, c => c.Id == gehalt);
        Assert.DoesNotContain(einnahmen, c => c.Id == wohnen);
    }

    [Fact]
    public async Task Ein_doppelter_Name_wird_abgewiesen()
    {
        var problem = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().CreateAsync("wohnen", CategoryDirection.Expense));

        Assert.Contains("gibt es schon", problem.Message);

        // In der anderen Richtung ist derselbe Name frei: „Sonstiges“ gibt es beidseits.
        var angelegt = await Service().CreateAsync("Wohnen", CategoryDirection.Income);
        Assert.Equal("Wohnen", angelegt.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ein_leerer_Name_wird_abgewiesen(string name)
        => await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().CreateAsync(name, CategoryDirection.Expense));

    [Fact]
    public async Task Umbenennen_wirkt_ueberall_zugleich()
    {
        var wirkung = await Service().RenameAsync(wohnen, "Wohnen & Energie");

        Assert.Equal(1, wirkung.TransactionCount);
        Assert.Equal(1, wirkung.RuleCount);

        using var context = database.Context();

        // Buchung und Regel zeigen per Id auf die Kategorie, nicht per Text — deshalb muss hier
        // nichts mitgezogen werden. Genau das hält die Historie zusammen.
        Assert.Equal("Wohnen & Energie", context.Categories.Single(c => c.Id == wohnen).Name);
        Assert.Equal(wohnen, context.Transactions.Single().CategoryId);
        Assert.Equal(wohnen, context.CategorizationRules.Single().CategoryId);
    }

    [Fact]
    public async Task Loeschen_laesst_die_Buchung_stehen_und_nimmt_ihr_die_Kategorie()
    {
        var wirkung = await Service().DeleteAsync(wohnen);

        Assert.Equal(1, wirkung.TransactionCount);
        Assert.Equal(1, wirkung.RuleCount);

        using var context = database.Context();

        // Die Buchung bleibt — sie ist eine Tatsache. Sie faellt auf „nicht zugeordnet“ und
        // erscheint damit im Triage-Banner, statt stillschweigend umgehaengt zu werden.
        var gebucht = context.Transactions.Single();
        Assert.Null(gebucht.CategoryId);
        Assert.Equal(-96m, gebucht.Amount);

        // Eine Regel ohne Ziel wuerde beim naechsten Import ins Leere greifen.
        Assert.Empty(context.CategorizationRules);
        Assert.DoesNotContain(context.Categories, c => c.Id == wohnen);
    }

    [Fact]
    public async Task Auf_eine_Kategorie_mit_Budget_wird_nicht_geloescht()
    {
        using (var context = database.Context())
        {
            context.Budgets.Add(new Budget
            {
                Name = "Wohnen", CategoryId = wohnen, Period = BudgetPeriod.Month,
                PlannedPerMonth = 900m, ValidFrom = new DateOnly(2026, 1, 1),
            });

            context.SaveChanges();
        }

        var problem = await Assert.ThrowsAsync<RuleViolationException>(() => Service().DeleteAsync(wohnen));

        Assert.Contains("Budget", problem.Message);

        using var nachher = database.Context();
        Assert.Contains(nachher.Categories, c => c.Id == wohnen);
    }

    /// <summary>
    /// Anlegen oder finden — für den Import, der den Fluss nicht verlassen darf.
    /// </summary>
    /// <remarks>
    /// Ein Screenwechsel und Rücksprung köstete alle bisherigen Zuordnungen. Trifft der Name
    /// eine vorhandene Kategorie, ist das kein Fehler, sondern der Normalfall bei einem gut
    /// geratenen Namen.
    /// </remarks>
    [Fact]
    public async Task Ein_neuer_Name_wird_angelegt()
    {
        var ergebnis = await Service().EnsureAsync("Abos", CategoryDirection.Expense);

        Assert.True(ergebnis.Created);
        Assert.Equal("Abos", ergebnis.Category.Name);
    }

    [Fact]
    public async Task Ein_vorhandener_Name_wird_gefunden_statt_abgewiesen()
    {
        var ergebnis = await Service().EnsureAsync("wohnen", CategoryDirection.Expense);

        Assert.False(ergebnis.Created);
        Assert.Equal(wohnen, ergebnis.Category.Id);

        // Und nichts wurde verdoppelt.
        using var context = database.Context();
        Assert.Single(context.Categories.Where(c => c.Name == "Wohnen"));
    }

    [Fact]
    public async Task Die_Richtung_trennt_auch_hier()
    {
        // „Wohnen“ gibt es als Ausgabe — als Einnahme ist der Name frei.
        var ergebnis = await Service().EnsureAsync("Wohnen", CategoryDirection.Income);

        Assert.True(ergebnis.Created);
        Assert.NotEqual(wohnen, ergebnis.Category.Id);
    }

    public void Dispose() => database.Dispose();
}
