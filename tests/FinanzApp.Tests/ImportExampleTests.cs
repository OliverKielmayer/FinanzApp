using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace FinanzApp.Tests;

/// <summary>
/// Der eingebaute Beispielauszug und die Grenze, an der er aufhört.
/// </summary>
/// <remarks>
/// <para>Er ist zum Ausprobieren da: Wer die Anwendung zum ersten Mal öffnet, soll eine
/// Importvorschau sehen können, ohne erst eine Datei zu suchen. Seine Sätze sind erfunden.</para>
/// <para>Er endet aber im <b>selben</b> Übernahmeweg wie eine echte Datei. Neben echten
/// Buchungen ist er damit kein Angebot mehr, sondern eine Falle — ein Fehlklick, und
/// erfundene Sätze stehen im echten Konto.</para>
/// <para>Die Auskunft steht im Dienst, der Riegel am Endpunkt. <see cref="ImportService"/>
/// selbst sperrt nichts, damit das übrige Testgerüst den Import weiterhin zweimal
/// hintereinander durchspielen kann.</para>
/// </remarks>
public sealed class ImportExampleTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 11);

    private readonly IMemoryCache cache =
        new MemoryCache(new MemoryCacheOptions { SizeLimit = 50_000 });

    private readonly CurrentUser anonymous = new(new HttpContextAccessor());

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int konto;

    public ImportExampleTests()
    {
        using var context = database.Context();

        var account = new Account
        {
            Name = "Musterbank Giro",
            ShortName = "Musterbank",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 11),
        };

        context.Accounts.Add(account);
        context.SaveChanges();
        konto = account.Id;
    }

    private ImportService Service()
        => new(database.Context(), clock, new CamtStatementParser(), cache,
            TestDatabase.PathService(root), anonymous);

    /// <summary>Eine Buchung, irgendeine — es zählt nur, dass überhaupt eine da ist.</summary>
    private void Buchung()
    {
        using var context = database.Context();

        context.Transactions.Add(new Transaction
        {
            BookingDate = new DateOnly(2026, 9, 27),
            Payee = "Musterwerke",
            Kind = TransactionKind.Expense,
            Amount = -62.40m,
            AccountId = konto,
            CreatedAt = new DateTime(2026, 9, 27, 7, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task Im_leeren_Bestand_steht_die_Vorlage_bereit()
    {
        var vorschau = await Service().GetPreviewAsync();

        Assert.True(vorschau.LedgerIsEmpty);
        Assert.NotEmpty(vorschau.Rows);
    }

    /// <summary>
    /// Die eine Buchung genügt. Nicht „viele“, nicht „aus einem Import“ — sobald überhaupt
    /// gebucht wird, ist der Bestand echt.
    /// </summary>
    [Fact]
    public async Task Eine_einzige_Buchung_nimmt_der_Vorlage_ihren_Platz()
    {
        Buchung();

        var vorschau = await Service().GetPreviewAsync();

        Assert.False(vorschau.LedgerIsEmpty);
    }

    [Fact]
    public async Task Neben_echten_Buchungen_ist_die_Vorlage_zu()
    {
        Buchung();

        Assert.True(await Service().ExampleIntoRealBooksAsync(DemoImportBatch.PreviewId));
    }

    [Fact]
    public async Task Im_leeren_Bestand_bleibt_die_Vorlage_offen()
        => Assert.False(await Service().ExampleIntoRealBooksAsync(DemoImportBatch.PreviewId));

    /// <summary>
    /// Die Gegenprobe: Der Riegel hängt an <em>dieser</em> Vorschau und nicht daran, dass
    /// überhaupt gebucht wird. Ohne sie ginge auch ein echter Auszug nicht mehr durch, sobald
    /// einmal etwas im Bestand steht — und das fiele erst im Betrieb auf.
    /// </summary>
    [Fact]
    public async Task Ein_echter_Auszug_geht_auch_neben_Buchungen_durch()
    {
        Buchung();

        Assert.False(await Service().ExampleIntoRealBooksAsync(Guid.NewGuid()));
    }

    public void Dispose()
    {
        database.Dispose();
        cache.Dispose();
    }
}
