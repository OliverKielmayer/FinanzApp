using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Eine SQLite-Datenbank im Arbeitsspeicher, frisch je Test.
/// </summary>
/// <remarks>
/// Bewusst SQLite und nicht der In-Memory-Anbieter von EF Core: die Anwendung legt Geldbeträge
/// über einen Wertkonverter als Cent ab und verlässt sich auf Indizes mit Filter. Beides prüft
/// nur ein echter relationaler Anbieter.
/// </remarks>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    public TestDatabase(int householdId = 1)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        Options = new DbContextOptionsBuilder<FinanzAppDbContext>()
            .UseSqlite(connection)
            .Options;

        using var context = new FinanzAppDbContext(Options);
        context.Database.EnsureCreated();

        HouseholdId = householdId;
    }

    public DbContextOptions<FinanzAppDbContext> Options { get; }

    public int HouseholdId { get; }

    /// <summary>Ein Kontext, der auf den angegebenen Haushalt sieht.</summary>
    public FinanzAppDbContext Context(int? householdId = null)
        => new(Options) { CurrentHouseholdId = householdId ?? HouseholdId };

    /// <summary>Legt einen Haushalt an und gibt seine Id zurück.</summary>
    public int AddHousehold(string name)
    {
        using var context = new FinanzAppDbContext(Options);
        var household = new Household { Name = name, CreatedAt = new DateTime(2026, 1, 1) };
        context.Households.Add(household);
        context.SaveChanges();
        return household.Id;
    }

    public static IClock ClockAt(int year, int month, int day, int hour = 8, int minute = 0)
        => new FixedClock(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

    public static DocumentPathService PathService(string root)
        => new(
            new DocumentStorageOptions { Root = root },
            new TestHostEnvironment(root),
            NullLogger<DocumentPathService>.Instance);

    /// <summary>
    /// Ein <see cref="ReportService"/> samt seiner Mitspieler.
    /// </summary>
    /// <remarks>
    /// Der Datenqualitätsbericht fragt, ob die hinterlegten Dateien wirklich liegen — das weiß
    /// nur der <see cref="DocumentService"/>, und der bringt eigene Abhängigkeiten mit. Sie hier
    /// einmal zu verdrahten ist besser, als sie in jedem Test noch einmal aufzuzählen.
    /// </remarks>
    public ReportService Reports(IClock clock, string? documentRoot = null)
    {
        var context = Context();
        var root = documentRoot
                   ?? Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

        return new ReportService(
            context,
            clock,
            new DocumentService(
                context,
                PathService(root),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance));
    }

    public void Dispose() => connection.Dispose();

    private sealed class TestHostEnvironment(string root) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string ApplicationName { get; set; } = "FinanzApp.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }
}
