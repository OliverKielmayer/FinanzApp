using System.Globalization;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class ImportService(FinanzAppDbContext db, IClock clock)
{
    private enum RecordState
    {
        New,
        Existing,
        Duplicate,
        Error,
    }

    /// <summary>
    /// Prüft die Sätze der Importdatei gegen den Bestand, ohne etwas zu schreiben.
    /// </summary>
    /// <remarks>
    /// Ein Satz gilt als <em>bereits vorhanden</em>, wenn seine Importreferenz schon gebucht ist —
    /// das ist der verlässliche Weg, denn die Referenz vergibt die Bank. Er gilt als
    /// <em>mögliches Duplikat</em>, wenn Tag, Empfänger und Betrag auf eine vorhandene Buchung
    /// passen, die Referenz aber neu ist; solche Sätze übernimmt die Bestätigung nicht mit.
    /// </remarks>
    public async Task<ImportPreviewDto> GetPreviewAsync(CancellationToken ct = default)
    {
        var states = await ClassifyAsync(ct);

        return new ImportPreviewDto
        {
            Id = DemoImportBatch.PreviewId,
            FileName = DemoImportBatch.FileName,
            BankName = DemoImportBatch.BankName,
            Format = DemoImportBatch.Format,
            ProfileName = DemoImportBatch.ProfileName,
            RecordCount = states.Count,
            NewCount = states.Count(s => s.Value == RecordState.New),
            ExistingCount = states.Count(s => s.Value == RecordState.Existing),
            DuplicateCount = states.Count(s => s.Value == RecordState.Duplicate),
            ErrorCount = states.Count(s => s.Value == RecordState.Error),
        };
    }

    /// <summary>
    /// Übernimmt die neuen Sätze in einer Transaktion — entweder liegen danach alle im Bestand
    /// oder keiner. Duplikatverdächtige Sätze bleiben bewusst liegen: sie brauchen eine
    /// Einzelfallentscheidung, deren Oberfläche noch nicht entworfen ist.
    /// </summary>
    public async Task<ImportCommitResultDto> CommitAsync(Guid previewId, CancellationToken ct = default)
    {
        if (previewId != DemoImportBatch.PreviewId)
        {
            throw new ArgumentException("Unbekannte Importvorschau.", nameof(previewId));
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == DemoImportBatch.AccountName, ct)
                      ?? throw new InvalidOperationException("Zielkonto des Imports fehlt.");

        var rules = await db.CategorizationRules.AsNoTracking().ToListAsync(ct);
        var states = await ClassifyAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var imported = 0;
        foreach (var (record, state) in states)
        {
            if (state != RecordState.New)
            {
                continue;
            }

            db.Transactions.Add(new Transaction
            {
                BookingDate = record.BookingDate!.Value,
                Payee = record.Payee,
                Kind = record.Amount!.Value >= 0 ? TransactionKind.Income : TransactionKind.Expense,
                Amount = record.Amount.Value,
                AccountId = account.Id,
                CategoryId = MatchCategory(record.Payee, rules),
                ImportReference = record.Reference,
                CreatedAt = clock.Now,
            });
            imported++;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ImportCommitResultDto { ImportedCount = imported };
    }

    public Task<int> GetProfileCountAsync(CancellationToken ct = default)
        => db.ImportProfiles.CountAsync(ct);

    public async Task<string> GetProfileFormatsAsync(CancellationToken ct = default)
    {
        var formats = await db.ImportProfiles.AsNoTracking()
            .Select(p => p.Format)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(ct);

        // „CAMT.053“ und „CSV“ werden auf der Sammelseite als „CAMT & CSV“ genannt.
        return string.Join(" & ", formats.Select(f => f.Split('.')[0]));
    }

    /// <summary>Ordnet einem Empfänger über die Regeln eine Kategorie zu, sofern eine greift.</summary>
    private static int? MatchCategory(string payee, List<CategorizationRule> rules)
        => rules.FirstOrDefault(r => payee.StartsWith(r.PayeePattern, StringComparison.OrdinalIgnoreCase))?.CategoryId;

    private async Task<List<KeyValuePair<ImportRecord, RecordState>>> ClassifyAsync(CancellationToken ct)
    {
        var knownReferences = (await db.Transactions.AsNoTracking()
                .Where(t => t.ImportReference != null)
                .Select(t => t.ImportReference!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var knownBookings = (await db.Transactions.AsNoTracking()
                .Select(t => new { t.BookingDate, t.Payee, t.Amount })
                .ToListAsync(ct))
            .Select(t => BookingKey(t.BookingDate, t.Payee, t.Amount))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. DemoImportBatch.Records.Select(record =>
        {
            var state = record switch
            {
                { BookingDate: null } or { Amount: null } => RecordState.Error,
                _ when knownReferences.Contains(record.Reference) => RecordState.Existing,
                _ when knownBookings.Contains(
                    BookingKey(record.BookingDate.Value, record.Payee, record.Amount.Value)) => RecordState.Duplicate,
                _ => RecordState.New,
            };

            return new KeyValuePair<ImportRecord, RecordState>(record, state);
        })];
    }

    /// <summary>
    /// Schlüssel für die Duplikatprüfung.
    /// </summary>
    /// <remarks>
    /// Der Betrag wird fest auf zwei Nachkommastellen formatiert. <c>decimal</c> merkt sich seine
    /// Skala: <c>-92.30m</c> aus einem Literal und derselbe Wert aus der Datenbank
    /// (<c>-9230 / 100m</c>) sind zwar gleich, ergeben als Text aber „-92.30“ und „-92.3“.
    /// </remarks>
    private static string BookingKey(DateOnly date, string payee, decimal amount)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}|{payee.Trim()}|{amount:0.00}");
}
