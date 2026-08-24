using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Start gegen eine Datenbank aus der Zeit vor den Migrationen.
/// </summary>
/// <remarks>
/// Ohne Schutz endet dieser Fall in „SQLite Error 1: table Accounts already exists“ — einer
/// Meldung, aus der niemand ableiten kann, was zu tun ist. Der Test hält fest, dass stattdessen
/// eine Anweisung kommt.
/// </remarks>
public sealed class SchemaStartupTests : IDisposable
{
    private readonly string file = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N") + ".db");

    public SchemaStartupTests()
        => Directory.CreateDirectory(Path.GetDirectoryName(file)!);

    [Fact]
    public async Task Leere_Datenbank_wird_migriert()
    {
        await using var db = Create();

        await SchemaStartup.MigrateAsync(db, NullLogger.Instance);

        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Zweiter_Start_laeuft_ohne_Aenderung_durch()
    {
        await using (var first = Create())
        {
            await SchemaStartup.MigrateAsync(first, NullLogger.Instance);
        }

        await using var second = Create();
        await SchemaStartup.MigrateAsync(second, NullLogger.Instance);

        Assert.Empty(await second.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Datenbank_ohne_Migrationshistorie_meldet_was_zu_tun_ist()
    {
        // So sah es aus, als das Schema noch mit EnsureCreated entstand: alle Tabellen da,
        // keine Historie.
        await using (var legacy = Create())
        {
            await legacy.Database.EnsureCreatedAsync();
        }

        await using var db = Create();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SchemaStartup.MigrateAsync(db, NullLogger.Instance));

        // Die Meldung nennt das Problem, den Dateipfad und den Ausweg.
        Assert.Contains("Migrationshistorie", error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(file), error.Message, StringComparison.Ordinal);
        Assert.Contains("löschen", error.Message, StringComparison.Ordinal);
    }

    private FinanzAppDbContext Create()
        => new(new DbContextOptionsBuilder<FinanzAppDbContext>()
            .UseSqlite($"Data Source={file}")
            .Options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }
}
