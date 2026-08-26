using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Kontoauszug einlesen.
/// </summary>
/// <remarks>
/// Die beiden Regeln, die der Handoff verbindlich nennt: die Duplikatprüfung läuft <b>gegen den
/// Bestand</b> — derselbe Auszug zweimal eingelesen ergibt beim zweiten Mal null Vorschläge —
/// und Zähler wie Aktionsschalter lesen <b>dieselbe</b> Auswahl.
/// </remarks>
public sealed class ImportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private int accountId;

    public ImportTests()
    {
        using var context = database.Context();
        var account = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
        };

        context.Accounts.Add(account);
        context.SaveChanges();
        accountId = account.Id;
    }

    private ImportService Service() => new(database.Context(), clock);

    [Fact]
    public async Task Ein_frischer_Bestand_kennt_lauter_neue_Saetze()
    {
        var preview = await Service().GetPreviewAsync();

        Assert.True(preview.NewCount > 0);
        Assert.Equal(0, preview.ExistingCount);
        Assert.Equal(preview.RecordCount, preview.Rows.Count);

        // Neue Sätze sind vorgeschlagen, alles andere nicht.
        Assert.All(preview.Rows, r => Assert.Equal(r.State == ImportRowState.New, r.PreSelected));
    }

    [Fact]
    public async Task Derselbe_Auszug_zweimal_ergibt_beim_zweiten_Mal_nichts_Neues()
    {
        var first = await Service().GetPreviewAsync();
        var chosen = first.Rows.Where(r => r.PreSelected).Select(r => r.Index).ToList();

        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id,
            AccountId = accountId,
            Indexes = chosen,
        });

        Assert.Equal(chosen.Count, result.ImportedCount);

        var second = await Service().GetPreviewAsync();

        Assert.Equal(0, second.NewCount);
        Assert.DoesNotContain(second.Rows, r => r.PreSelected);
    }

    [Fact]
    public async Task Ein_zugeschaltetes_Duplikat_wird_gebucht_und_gezählt()
    {
        // Erst einlesen, dann denselben Auszug noch einmal - jetzt ist alles ein Treffer.
        var first = await Service().GetPreviewAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id,
            AccountId = accountId,
            Indexes = [.. first.Rows.Where(r => r.PreSelected).Select(r => r.Index)],
        });

        var second = await Service().GetPreviewAsync();
        var treffer = second.Rows.First(r => r.State is ImportRowState.Existing or ImportRowState.Duplicate);

        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = second.Id,
            AccountId = accountId,
            Indexes = [treffer.Index],
        });

        // Wer ausdrücklich zuschaltet, bekommt die Buchung — und die Meldung sagt es.
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.ForcedDuplicates);
    }

    [Fact]
    public async Task Fehlerhafte_Saetze_lassen_sich_nicht_zuschalten()
    {
        var preview = await Service().GetPreviewAsync();
        var broken = preview.Rows.Where(r => r.State == ImportRowState.Error).ToList();

        Assert.NotEmpty(broken);
        Assert.All(broken, r => Assert.False(r.PreSelected));
        Assert.All(broken, r => Assert.False(string.IsNullOrWhiteSpace(r.Problem)));

        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = accountId,
            Indexes = [.. broken.Select(r => r.Index)],
        });

        // Aus einem unlesbaren Betrag wird keine Buchung, egal wie oft jemand darauf tippt.
        Assert.Equal(0, result.ImportedCount);

        using var check = database.Context();
        Assert.Empty(check.Transactions);
    }

    [Fact]
    public async Task Nichts_gewaehlt_heisst_nichts_gebucht()
    {
        var preview = await Service().GetPreviewAsync();

        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = accountId,
            Indexes = [],
        });

        Assert.Equal(0, result.ImportedCount);
    }

    [Fact]
    public async Task Das_Zielkonto_entscheidet_wohin_gebucht_wird()
    {
        int second;
        using (var context = database.Context())
        {
            var other = new Account
            {
                Name = "Raiffeisenbank Giro", ShortName = "Raiffeisen", BankName = "Raiffeisenbank",
                Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 23),
            };
            context.Accounts.Add(other);
            context.SaveChanges();
            second = other.Id;
        }

        var preview = await Service().GetPreviewAsync();

        // Vorgeschlagen wird das Konto zur Bank des Auszugs — gebucht wird, was gewählt ist.
        Assert.Equal(accountId, preview.SuggestedAccountId);

        var row = preview.Rows.First(r => r.PreSelected);
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = second,
            Indexes = [row.Index],
        });

        using var check = database.Context();
        Assert.Equal(second, check.Transactions.Single().AccountId);
    }

    [Fact]
    public async Task Der_Hinweistext_nennt_das_Kriterium()
    {
        var preview = await Service().GetPreviewAsync();

        Assert.Contains("Importreferenz", preview.DuplicateCriterion);
        Assert.Contains("Bestand", preview.DuplicateCriterion);
    }

    public void Dispose() => database.Dispose();
}
