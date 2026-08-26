using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace FinanzApp.Tests;

/// <summary>
/// Der Weg einer echten Datei: einlesen, prüfen, übernehmen.
/// </summary>
/// <remarks>
/// Anders als <see cref="ImportTests"/>, die auf der eingebauten Vorlage arbeiten, geht dieser
/// Test über den Zwischenspeicher — genau den Weg, den eine hochgeladene Datei nimmt.
/// </remarks>
public sealed class ImportFileTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 26);
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 50_000 });
    private readonly CurrentUser anonymous = new(new HttpContextAccessor());

    private readonly int sparkasse;
    private readonly int raiffeisen;

    public ImportFileTests()
    {
        using var context = database.Context();

        // Die IBAN steht in den Stammdaten mit Leerzeichen, in der Datei ohne — das ist der Fall,
        // an dem eine Zuordnung ohne Normalisierung stillschweigend danebengreift.
        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, Iban = "DE44 6725 0020 0034 8891 02",
            BalanceAsOf = new DateOnly(2026, 8, 26),
        };
        var andere = new Account
        {
            Name = "Raiffeisenbank Giro", ShortName = "Raiffeisen", BankName = "Raiffeisenbank",
            Kind = AccountKind.Checking, Iban = "DE12 6706 2366 0009 1140 07",
            BalanceAsOf = new DateOnly(2026, 8, 26),
        };

        context.Accounts.AddRange(giro, andere);
        context.SaveChanges();

        sparkasse = giro.Id;
        raiffeisen = andere.Id;
    }

    private ImportService Service()
        => new(database.Context(), clock, new CamtStatementParser(), cache, anonymous);

    private static Stream Example()
        => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Beispiele", "camt052-sparkasse.xml"));

    private async Task<ImportPreviewDto> ReadAsync()
    {
        await using var content = Example();
        return await Service().ReadAsync(content, "camt052-sparkasse.xml");
    }

    [Fact]
    public async Task Die_Vorschau_kommt_aus_der_Datei_und_nicht_aus_der_Vorlage()
    {
        var preview = await ReadAsync();

        Assert.Equal("camt052-sparkasse.xml", preview.FileName);
        Assert.Equal("CAMT.052.001.08", preview.Format);
        Assert.Equal("Sparkasse Heidelberg", preview.BankName);
        Assert.Null(preview.Separator);

        // Der Saldo stammt aus der Datei, nicht aus einer Summe über die Sätze.
        Assert.Equal(6091.28m, preview.StatementBalance);
        Assert.Equal(new DateOnly(2026, 8, 24), preview.From);
        Assert.Equal(new DateOnly(2026, 8, 26), preview.To);
    }

    [Fact]
    public async Task Das_Zielkonto_wird_ueber_die_IBAN_erkannt()
    {
        var preview = await ReadAsync();

        Assert.Equal(sparkasse, preview.SuggestedAccountId);
        Assert.NotEqual(raiffeisen, preview.SuggestedAccountId);
    }

    [Fact]
    public async Task Was_keine_Buchung_ist_steht_da_und_laesst_sich_nicht_waehlen()
    {
        var preview = await ReadAsync();

        var vorgemerkt = preview.Rows.Single(r => r.Payee == "Baumarkt Hornbach");
        var unlesbar = preview.Rows.Single(r => r.Payee == "Apotheke am Markt");

        Assert.Equal(ImportRowState.Error, vorgemerkt.State);
        Assert.Equal(ImportRowState.Error, unlesbar.State);
        Assert.False(vorgemerkt.PreSelected);
        Assert.False(unlesbar.PreSelected);

        Assert.Contains("vorgemerkt", vorgemerkt.Problem);
        Assert.Equal("Betrag nicht lesbar", unlesbar.Problem);
    }

    [Fact]
    public async Task Uebernommen_wird_die_Auswahl_und_zwar_mit_Vorzeichen()
    {
        var preview = await ReadAsync();
        var chosen = preview.Rows.Where(r => r.PreSelected).Select(r => r.Index).ToList();

        var result = await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = sparkasse,
            Indexes = chosen,
        });

        Assert.Equal(chosen.Count, result.ImportedCount);
        Assert.Equal(0, result.ForcedDuplicates);

        using var context = database.Context();
        var lohn = context.Transactions.Single(t => t.Payee == "Kielmayer Systemtechnik GmbH");
        var rewe = context.Transactions.Single(t => t.Payee == "REWE Markt Heidelberg");

        Assert.Equal(TransactionKind.Income, lohn.Kind);
        Assert.Equal(2480.00m, lohn.Amount);
        Assert.Equal(TransactionKind.Expense, rewe.Kind);
        Assert.Equal(-68.42m, rewe.Amount);
    }

    [Fact]
    public async Task Dieselbe_Datei_zweimal_ergibt_beim_zweiten_Mal_nichts_Neues()
    {
        var first = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = first.Id,
            AccountId = sparkasse,
            Indexes = [.. first.Rows.Where(r => r.PreSelected).Select(r => r.Index)],
        });

        var second = await ReadAsync();

        // Das trägt die Referenz aus der Datei — und für die Sätze ohne Referenz der
        // Fingerabdruck. Wäre der nicht stabil, stünde hier wieder alles als neu.
        Assert.Equal(0, second.NewCount);
        Assert.DoesNotContain(second.Rows, r => r.PreSelected);
    }

    [Fact]
    public async Task Nach_der_Uebernahme_ist_die_Vorschau_verbraucht()
    {
        var preview = await ReadAsync();
        var chosen = preview.Rows.Where(r => r.PreSelected).Select(r => r.Index).ToList();

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id, AccountId = sparkasse, Indexes = chosen,
        });

        // Ein zweiter Klick auf denselben Schalter darf die Sätze nicht noch einmal buchen.
        var problem = await Assert.ThrowsAsync<ArgumentException>(
            () => Service().CommitAsync(new ImportCommitRequest
            {
                PreviewId = preview.Id, AccountId = sparkasse, Indexes = chosen,
            }));

        Assert.Contains("noch einmal einlesen", problem.Message);
    }

    [Fact]
    public async Task Eine_fremde_Datei_wird_abgewiesen_bevor_irgendetwas_entsteht()
    {
        await using var content = new MemoryStream("Datum;Betrag\n01.08.2026;-12,00"u8.ToArray());

        await Assert.ThrowsAsync<StatementFormatException>(
            () => Service().ReadAsync(content, "umsaetze.csv"));

        using var context = database.Context();
        Assert.Empty(context.Transactions);
    }

    public void Dispose()
    {
        cache.Dispose();
        database.Dispose();
    }
}
