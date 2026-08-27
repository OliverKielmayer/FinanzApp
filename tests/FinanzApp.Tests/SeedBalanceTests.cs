using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Tests;

/// <summary>
/// Die Demo-Konten treffen nach dem Seeden ihren Stand.
/// </summary>
/// <remarks>
/// <para>Der Seed legt Buchungen an und rechnet danach die Anfangsbestände so, dass die Salden
/// den Demo-Ständen entsprechen. Das ging still schief, seit Konten freigebbar sind: die
/// Rückrechnung las die Buchungen <em>durch</em> den Sichtbarkeitsfilter, und beim Seeden gibt
/// es keinen angemeldeten Benutzer. Für ein privates Konto kam damit „null Buchungen“ heraus,
/// sein Anfangsbestand blieb unausgeglichen, und der Saldo verfehlte den Stand um genau die
/// Summe seiner Buchungen.</para>
/// <para>Aufgefallen ist es an einer Kachel des Dashboards: „Girokonten −455,14 €“. Der Fehler
/// ist so alt wie die Freigaben; er fiel erst auf, als mehr Buchungen dazukamen und die
/// Abweichung nicht mehr wie ein Kontostand aussah. Dieser Test prüft alle drei Konten, damit
/// nicht das nächste private Konto denselben Weg geht.</para>
/// </remarks>
public sealed class SeedBalanceTests : IDisposable
{
    private readonly TestDatabase database = new();

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Die Stände, auf die der Seed rechnet — hier noch einmal unabhängig genannt.</summary>
    private static readonly (string Name, decimal Balance)[] Expected =
    [
        ("Sparkasse Giro", 4812.60m),
        ("Raiffeisenbank Giro", 1947.35m),
        ("Tagesgeld Raiffeisen", 50000.00m),
    ];

    [Fact]
    public async Task Jedes_Demo_Konto_trifft_seinen_Stand()
    {
        await SeedAsync();

        foreach (var (name, erwartet) in Expected)
        {
            Assert.Equal(erwartet, await BalanceAsync(name));
        }
    }

    /// <summary>
    /// Das private Konto ausdrücklich noch einmal.
    /// </summary>
    /// <remarks>
    /// Es ist der Fall, an dem es scheiterte. Stünde es nur in der Schleife oben, ginge beim
    /// nächsten Umbau der Demo-Konten unbemerkt der Prüfling verloren.
    /// </remarks>
    [Fact]
    public async Task Auch_das_private_Konto_wird_ausgeglichen()
    {
        await SeedAsync();

        using var context = database.Context();
        var konto = await context.Accounts.IgnoreQueryFilters()
            .SingleAsync(a => a.Name == "Raiffeisenbank Giro");

        Assert.Equal(AccountSharing.Private, konto.Sharing);
        Assert.Equal(1947.35m, await BalanceAsync(konto.Name));
    }

    private async Task SeedAsync()
    {
        using var context = database.Context();
        await SeedData.EnsureSeededAsync(
            context, new PasswordHasher<User>(), TestDatabase.PathService(root));
    }

    /// <summary>Anfangsbestand plus alle Buchungen — ohne Filter, wie eine Kasse rechnet.</summary>
    private async Task<decimal> BalanceAsync(string name)
    {
        using var context = database.Context();

        var konto = await context.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Name == name);
        var gebucht = await context.Transactions.IgnoreQueryFilters()
            .Where(t => t.AccountId == konto.Id)
            .SumAsync(t => t.Amount);

        return konto.OpeningBalance + gebucht;
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
