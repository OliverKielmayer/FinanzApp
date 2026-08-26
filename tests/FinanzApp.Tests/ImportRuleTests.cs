using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace FinanzApp.Tests;

/// <summary>
/// Lernende Kategorieregeln beim Import.
/// </summary>
/// <remarks>
/// Der Kern von §8c: gefragt wird je <b>Empfänger</b>, nicht je Buchung, und was der Nutzer
/// antwortet, kann als Regel hängenbleiben. Gelernt wird erst bei der Übernahme — wer den Import
/// verwirft, soll keine Regel hinterlassen haben.
/// </remarks>
public sealed class ImportRuleTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 26);
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 50_000 });
    private readonly CurrentUser anonymous = new(new HttpContextAccessor());

    private readonly int account;
    private readonly int freizeit;
    private readonly int lebensmittel;
    private readonly int gehalt;

    public ImportRuleTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, Iban = "DE44 6725 0020 0034 8891 02",
            BalanceAsOf = new DateOnly(2026, 8, 26),
        };
        var a = new Category { Name = "Freizeit", Direction = CategoryDirection.Expense };
        var b = new Category { Name = "Lebensmittel", Direction = CategoryDirection.Expense };
        var c = new Category { Name = "Gehalt", Direction = CategoryDirection.Income };

        context.Accounts.Add(giro);
        context.Categories.AddRange(a, b, c);
        context.SaveChanges();

        account = giro.Id;
        freizeit = a.Id;
        lebensmittel = b.Id;
        gehalt = c.Id;
    }

    private ImportService Service()
        => new(database.Context(), clock, new CamtStatementParser(), cache, anonymous);

    private CatalogService Catalog() => new(database.Context());

    private async Task<ImportPreviewDto> ReadAsync()
    {
        await using var content = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Beispiele", "camt052-sparkasse.xml"));

        return await Service().ReadAsync(content, "camt052-sparkasse.xml");
    }

    private static IReadOnlyList<int> Selected(ImportPreviewDto preview)
        => [.. preview.Rows.Where(r => r.PreSelected).Select(r => r.Index)];

    [Fact]
    public async Task Ohne_Regeln_hat_kein_Satz_einen_Vorschlag()
    {
        var preview = await ReadAsync();

        Assert.All(preview.Rows, r => Assert.Null(r.SuggestedCategoryId));
        Assert.All(preview.Rows, r => Assert.Null(r.RuleId));
    }

    [Fact]
    public async Task Eine_gemerkte_Zuordnung_wird_beim_naechsten_Import_zum_Vorschlag()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id,
            AccountId = account,
            Indexes = Selected(first),
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: true)],
        });

        var second = await ReadAsync();
        var rewe = second.Rows.Single(r => r.Payee == "REWE Markt Heidelberg");

        Assert.Equal(lebensmittel, rewe.SuggestedCategoryId);
        Assert.Equal("Lebensmittel", rewe.CategoryName);

        // Die Herkunft gehoert dazu: nur so laesst sich „automatisch zugeordnet“ von „von Hand
        // gewaehlt“ unterscheiden.
        Assert.NotNull(rewe.RuleId);
    }

    [Fact]
    public async Task Ohne_Haken_bleibt_keine_Regel_zurueck()
    {
        var preview = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = account,
            Indexes = Selected(preview),
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: false)],
        });

        Assert.Empty(await Catalog().GetRulesAsync());

        // Die Buchung bekommt die Kategorie trotzdem — die Wahl galt für diesen Import.
        using var context = database.Context();
        Assert.Equal(
            lebensmittel,
            context.Transactions.Single(t => t.Payee == "REWE Markt Heidelberg").CategoryId);
    }

    [Fact]
    public async Task Die_Wahl_im_Import_schlaegt_die_Regel()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id, AccountId = account, Indexes = Selected(first),
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: true)],
        });

        // Zweiter Auszug, derselbe Empfänger — diesmal von Hand anders zugeordnet.
        var second = await ReadAsync();
        var rewe = second.Rows.Single(r => r.Payee == "REWE Markt Heidelberg");

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = second.Id, AccountId = account, Indexes = [rewe.Index],
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", freizeit, RememberRule: false)],
        });

        using var context = database.Context();
        var gebucht = context.Transactions
            .Where(t => t.Payee == "REWE Markt Heidelberg")
            .OrderBy(t => t.Id)
            .ToList();

        Assert.Equal(lebensmittel, gebucht[0].CategoryId);
        Assert.Equal(freizeit, gebucht[1].CategoryId);
    }

    [Fact]
    public async Task Eine_Regel_aendert_nie_was_schon_gebucht_ist()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id, AccountId = account, Indexes = Selected(first),
        });

        using (var vorher = database.Context())
        {
            Assert.All(
                vorher.Transactions.Where(t => t.Payee == "REWE Markt Heidelberg"),
                t => Assert.Null(t.CategoryId));
        }

        // Jetzt eine Regel lernen — die bereits gebuchte Zeile bleibt, wie sie ist.
        var second = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = second.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: true)],
        });

        using var nachher = database.Context();
        Assert.All(
            nachher.Transactions.Where(t => t.Payee == "REWE Markt Heidelberg"),
            t => Assert.Null(t.CategoryId));
    }

    [Fact]
    public async Task Dieselbe_Regel_zweimal_gelernt_wird_ueberschrieben_nicht_verdoppelt()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: true)],
        });

        var second = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = second.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("REWE Filiale Bergheim", freizeit, RememberRule: true)],
        });

        // Beide ergeben das Muster „REWE“. Zwei Regeln darauf waeren ein Widerspruch, den
        // niemand aufloesen kann.
        var rule = Assert.Single(await Catalog().GetRulesAsync());
        Assert.Equal("REWE", rule.PayeePattern);
        Assert.Equal(freizeit, rule.CategoryId);
    }

    [Fact]
    public async Task Der_Vergleich_uebersteht_Schreibweisen()
    {
        var preview = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("Stadtwerke Heidelberg", lebensmittel, RememberRule: true)],
        });

        // Dieselbe Firma, wie eine Bank sie schreiben koennte.
        Assert.True(Categorization.Matches("STADTWERKE  HEIDELBERG", "Stadtwerke"));
        Assert.True(Categorization.Matches("stadtwerke-heidelberg", "Stadtwerke"));
        Assert.False(Categorization.Matches("Stadtbücherei", "Stadtwerke"));
    }

    /// <summary>
    /// Ein kurzes Muster darf nicht mitten im Wort greifen.
    /// </summary>
    /// <remarks>
    /// An echten Bankdaten aufgefallen: „R + V LEBENSVERSICHERUNG“ ergibt als erstes Wort das
    /// Muster „R“. Ohne Wortgrenze fing das Rundfunk, REWE, das Restaurant und die Raiffeisenbank
    /// gleich mit ein — eine einzige gemerkte Regel hätte den halben Auszug falsch zugeordnet.
    /// </remarks>
    [Theory]
    [InlineData("R + V LEBENSVERSICHERUNG AKTIENGESELLSCHAFT", true)]
    [InlineData("Rundfunk ARD, ZDF, DRadio", false)]
    [InlineData("REWE Martin Sitter", false)]
    [InlineData("RAIFFBK BÜHLERTAL", false)]
    [InlineData("RESTAURANT ALT HALL", false)]
    public void Ein_kurzes_Muster_greift_nur_am_ganzen_Wort(string payee, bool erwartet)
        => Assert.Equal(erwartet, Categorization.Matches(payee, "R"));

    [Fact]
    public void Ein_Muster_darf_ein_laengeres_Wort_nicht_anschneiden()
    {
        // „Netto“ soll nicht auf „Nettobezuege“ greifen.
        Assert.True(Categorization.Matches("Netto Marken-Discount", "Netto"));
        Assert.False(Categorization.Matches("Nettobezuege Januar", "Netto"));
    }

    [Fact]
    public async Task Die_genauere_Regel_gewinnt()
    {
        using (var context = database.Context())
        {
            context.CategorizationRules.AddRange(
                new CategorizationRule { PayeePattern = "REWE", CategoryId = lebensmittel },
                new CategorizationRule { PayeePattern = "REWE Markt", CategoryId = freizeit });

            context.SaveChanges();
        }

        var preview = await ReadAsync();
        var rewe = preview.Rows.Single(r => r.Payee == "REWE Markt Heidelberg");

        // Sonst entschiede die Reihenfolge in der Tabelle, und dieselbe Buchung landete je nach
        // Anlagezeitpunkt woanders.
        Assert.Equal(freizeit, rewe.SuggestedCategoryId);
    }

    [Fact]
    public async Task Das_Ergebnis_nennt_was_ohne_Kategorie_blieb()
    {
        var preview = await ReadAsync();
        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = account,
            Indexes = Selected(preview),
            Choices = [new ImportCategoryChoice("REWE Markt Heidelberg", lebensmittel, RememberRule: true)],
        });

        // Die Brücke zum Triage-Banner: was hier verschwiegen wird, taucht dort unerklärt auf.
        Assert.Equal(result.ImportedCount - 1, result.WithoutCategory);
        Assert.Single(result.LearnedRuleIds);
    }

    [Fact]
    public async Task Eine_gelernte_Regel_traegt_ihren_Zeitpunkt()
    {
        var preview = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("Netflix Abo", freizeit, RememberRule: true)],
        });

        var rule = Assert.Single(await Catalog().GetRulesAsync());

        // Daran unterscheidet der Regelscreen „beim Import gelernt“ von „seit dem ersten Import“.
        Assert.Equal(new DateOnly(2026, 8, 26), rule.LearnedOn);
    }

    [Fact]
    public async Task Eine_geloeschte_Regel_greift_beim_naechsten_Import_nicht_mehr()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id, AccountId = account, Indexes = [],
            Choices = [new ImportCategoryChoice("Netflix Abo", freizeit, RememberRule: true)],
        });

        var rule = Assert.Single(await Catalog().GetRulesAsync());
        Assert.True(await Catalog().DeleteRuleAsync(rule.Id));

        var second = await ReadAsync();

        Assert.Null(second.Rows.Single(r => r.Payee == "Netflix Abo").SuggestedCategoryId);
    }

    public void Dispose()
    {
        cache.Dispose();
        database.Dispose();
    }
}
