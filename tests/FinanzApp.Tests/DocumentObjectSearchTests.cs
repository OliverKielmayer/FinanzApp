using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Objektsuche beim Zuordnen eines Belegs.
/// </summary>
/// <remarks>
/// <para>Das Suchfeld im Prüfplatz beantwortet die Frage „wozu gehört dieser Zettel“. Was es
/// nicht findet, gibt es für den Benutzer nicht — ein Beleg ohne Treffer bleibt im Eingang
/// liegen, obwohl sein Objekt längst in der Anwendung steht.</para>
/// <para>Genau so war es bei Konten: Verknüpfen und Beschriften konnten sie längst, gefunden
/// wurden sie nicht. Ein Bankschreiben war damit nicht zuzuordnen.</para>
/// </remarks>
public sealed class DocumentObjectSearchTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private void Konto(string name, string bank, string? iban = null)
    {
        using var context = database.Context();

        context.Accounts.Add(new Account
        {
            Name = name,
            ShortName = name[..Math.Min(4, name.Length)],
            BankName = bank,
            Iban = iban,
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 1),
        });

        context.SaveChanges();
    }

    [Fact]
    public async Task Ein_Konto_steht_als_Objekt_zur_Auswahl()
    {
        Konto("Gemeinschaftskonto", "Musterbank");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("Gemeinschaft");

        var konto = Assert.Single(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
        Assert.Equal("Gemeinschaftskonto", konto.Label);
        Assert.Equal("Konto", konto.TargetLabel);
    }

    /// <summary>
    /// Gefunden wird auch über die Bank und die IBAN.
    /// </summary>
    /// <remarks>
    /// Wer ein Bankschreiben in der Hand hält, liest darauf den Namen der Bank und eine
    /// Kontonummer — nicht den Namen, den das Konto in dieser Anwendung trägt.
    /// </remarks>
    [Theory]
    [InlineData("Musterbank")]
    [InlineData("DE02 1234")]
    public async Task Auch_Bank_und_IBAN_fuehren_zum_Konto(string suche)
    {
        Konto("Girokonto", "Musterbank", "DE02 1234 5678 9012 3456 78");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync(suche);

        Assert.Contains(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
    }

    /// <summary>Was nicht passt, steht auch nicht da.</summary>
    [Fact]
    public async Task Ohne_Treffer_bleibt_die_Liste_leer()
    {
        Konto("Girokonto", "Musterbank");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync("Zahnarzt");

        Assert.DoesNotContain(treffer.Objects, o => o.TargetType == LinkTargetType.Account);
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
