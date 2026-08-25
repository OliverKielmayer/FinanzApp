using FinanzApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Bringt das Schema beim Start auf den Stand der Migrationen.</summary>
public static class SchemaStartup
{
    /// <summary>
    /// Wendet die ausstehenden Migrationen an — und erkennt vorher den einen Fall, in dem das
    /// nicht gutgehen kann.
    /// </summary>
    /// <remarks>
    /// <para>Frühere Fassungen haben das Schema mit <c>EnsureCreated</c> angelegt. Solche
    /// Datenbanken haben alle Tabellen, aber keine Migrationshistorie. <c>Migrate</c> hält sie
    /// deshalb für leer, versucht die erste Migration anzuwenden und scheitert beim ersten
    /// <c>CREATE TABLE</c> — mit einer SQLite-Meldung, aus der niemand ableiten kann, was zu tun
    /// ist.</para>
    /// <para>Automatisch übernehmen lässt sich so eine Datenbank nicht: ob ihr Schema zum ersten
    /// Migrationsstand passt, weiß niemand — es fehlen ihr je nach Alter ganze Tabellen. Und
    /// einfach löschen darf die Anwendung sie nicht, es könnten echte Buchungen darin stehen.
    /// Also: klar sagen, was los ist, und die Entscheidung dem Menschen lassen.</para>
    /// </remarks>
    public static async Task MigrateAsync(
        FinanzAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);

        if (!applied.Any() && await HasLegacySchemaAsync(db, ct))
        {
            throw Unusable(
                db,
                """
                 Die Datenbank stammt aus einer Fassung vor dem Umstieg auf EF-Core-Migrationen:
                 sie hat bereits Tabellen, aber keine Migrationshistorie.
                 """);
        }

        // Zweiter Fall, entstanden mit Handoff v4: die Migrationen wurden neu aufgesetzt, weil
        // Vorsorge und Absicherung in einem Modell zusammengeführt wurden. Eine Datenbank aus
        // der alten Linie trägt eine Historie, die diese Fassung nicht kennt. Würde man sie
        // durchlassen, scheiterte die erste Migration am ersten CREATE TABLE.
        if (applied.Any() && !applied.Any(known.Contains))
        {
            throw Unusable(
                db,
                $"""
                 Die Datenbank stammt aus einer älteren Migrationslinie: ihre Historie
                 ({string.Join(", ", applied)}) kommt in dieser Fassung nicht mehr vor.
                 """);
        }

        var pending = await db.Database.GetPendingMigrationsAsync(ct);
        if (pending.Any())
        {
            logger.LogInformation(
                "Wende {Anzahl} Migration(en) an: {Migrationen}",
                pending.Count(),
                string.Join(", ", pending));
        }

        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Ob eine Tabelle der Anwendung existiert, obwohl keine Migration verzeichnet ist.
    /// <c>Accounts</c> gibt es in jeder Fassung, die je gelaufen ist.
    /// </summary>
    private static async Task<bool> HasLegacySchemaAsync(FinanzAppDbContext db, CancellationToken ct)
    {
        if (!await db.Database.CanConnectAsync(ct))
        {
            return false;
        }

        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Accounts'";

        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Die Meldung für eine Datenbank, die sich nicht übernehmen lässt. Sie nennt das Problem,
    /// den Dateipfad und den Ausweg — und überlässt die Entscheidung dem Menschen: löschen darf
    /// die Anwendung sie nicht, es könnten echte Buchungen darin stehen.
    /// </summary>
    private static InvalidOperationException Unusable(FinanzAppDbContext db, string reason)
        => new($"""
                {reason}

                Sie enthält nur Beispieldaten, solange nichts Eigenes erfasst wurde. Der Weg:

                    {DescribeDataSource(db)} löschen und die Anwendung neu starten.

                Steht Eigenes darin, vorher eine Kopie der Datei sichern.
                """);

    /// <summary>Der Dateiname aus der Verbindungszeichenfolge, für die Meldung.</summary>
    private static string DescribeDataSource(FinanzAppDbContext db)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
            db.Database.GetConnectionString() ?? string.Empty);

        return string.IsNullOrWhiteSpace(builder.DataSource)
            ? "Die Datenbankdatei"
            : Path.GetFullPath(builder.DataSource);
    }
}
