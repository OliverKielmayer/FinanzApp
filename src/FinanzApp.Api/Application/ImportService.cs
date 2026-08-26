using System.Globalization;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Kontoauszug einlesen — Vorschau und Übernahme.
/// </summary>
/// <remarks>
/// <para>Die Duplikatprüfung läuft <b>gegen den Bestand</b>, nicht nur innerhalb der Datei.
/// Ein Satz gilt als <em>bereits vorhanden</em>, wenn seine Importreferenz schon gebucht ist —
/// das ist der verlässliche Weg, denn die Referenz vergibt die Bank. Er gilt als <em>mögliches
/// Duplikat</em>, wenn Tag, Empfänger und Betrag auf eine vorhandene Buchung passen, die Referenz
/// aber neu ist. Derselbe Auszug zweimal eingelesen ergibt beim zweiten Mal null Vorschläge.</para>
/// <para>Was übernommen wird, entscheidet allein die <b>Auswahl des Nutzers</b>. Der Dienst
/// schlägt vor — neue Sätze angehakt, Treffer abgewählt — und führt dann aus, was dasteht. Ein
/// zugeschaltetes Duplikat wird gebucht; sonst widerspräche der Knopf dem Kopf.</para>
/// </remarks>
public sealed class ImportService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>Der Text, der das Kriterium benennt — er gehört sichtbar an die Prüfung.</summary>
    private const string Criterion =
        "Geprüft gegen den Bestand: gleiche Importreferenz gilt als vorhanden, "
        + "gleicher Tag mit gleichem Empfänger und Betrag als mögliches Duplikat.";

    /// <summary>Prüft die Sätze der Importdatei gegen den Bestand, ohne etwas zu schreiben.</summary>
    public async Task<ImportPreviewDto> GetPreviewAsync(CancellationToken ct = default)
    {
        var rows = await ClassifyAsync(ct);

        var accounts = await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ImportAccountDto { Id = a.Id, Name = a.Name, Iban = a.Iban })
            .ToListAsync(ct);

        // Vorgeschlagen wird das Konto, das zur Bank des Auszugs passt — änderbar bleibt es.
        var suggested = accounts.FirstOrDefault(a => a.Name == DemoImportBatch.AccountName)
                        ?? accounts.FirstOrDefault(a =>
                            a.Name.Contains(DemoImportBatch.BankName, StringComparison.OrdinalIgnoreCase));

        var dated = rows.Where(r => r.BookingDate is not null).Select(r => r.BookingDate!.Value).ToList();

        return new ImportPreviewDto
        {
            Id = DemoImportBatch.PreviewId,
            FileName = DemoImportBatch.FileName,
            BankName = DemoImportBatch.BankName,
            Format = DemoImportBatch.Format,
            ProfileName = DemoImportBatch.ProfileName,
            From = dated.Count == 0 ? null : dated.Min(),
            To = dated.Count == 0 ? null : dated.Max(),
            StatementBalance = rows.Where(r => r.Amount is not null).Sum(r => r.Amount!.Value),
            Separator = DemoImportBatch.Format.StartsWith("CSV", StringComparison.OrdinalIgnoreCase) ? ";" : null,
            Accounts = accounts,
            SuggestedAccountId = suggested?.Id,
            RecordCount = rows.Count,
            NewCount = rows.Count(r => r.State == ImportRowState.New),
            ExistingCount = rows.Count(r => r.State == ImportRowState.Existing),
            DuplicateCount = rows.Count(r => r.State == ImportRowState.Duplicate),
            ErrorCount = rows.Count(r => r.State == ImportRowState.Error),
            DuplicateCriterion = Criterion,
            Rows = rows,
            LastImport = await LastImportAsync(ct),
        };
    }

    /// <summary>
    /// Übernimmt die <b>gewählten</b> Sätze in einer Transaktion — entweder liegen danach alle im
    /// Bestand oder keiner.
    /// </summary>
    /// <remarks>
    /// Fehlerhafte Sätze lassen sich nicht zuschalten: aus einem unlesbaren Betrag wird keine
    /// Buchung, egal wie oft jemand darauf tippt. Alles andere folgt der Auswahl.
    /// </remarks>
    public async Task<ImportCommitResultDto> CommitAsync(
        ImportCommitRequest request, CancellationToken ct = default)
    {
        if (request.PreviewId != DemoImportBatch.PreviewId)
        {
            throw new ArgumentException("Unbekannte Importvorschau.", nameof(request));
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
                      ?? throw new ArgumentException("Unbekanntes Zielkonto.", nameof(request));

        var rules = await db.CategorizationRules.AsNoTracking().ToListAsync(ct);
        var rows = await ClassifyAsync(ct);
        var chosen = request.Indexes.ToHashSet();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var imported = 0;
        var forced = 0;

        foreach (var row in rows)
        {
            if (!chosen.Contains(row.Index) || row.State == ImportRowState.Error)
            {
                continue;
            }

            var record = DemoImportBatch.Records[row.Index];
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
            if (row.State is ImportRowState.Duplicate or ImportRowState.Existing)
            {
                forced++;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ImportCommitResultDto { ImportedCount = imported, ForcedDuplicates = forced };
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

    private static string? MatchCategoryName(string payee, List<CategorizationRule> rules)
        => rules.FirstOrDefault(r => payee.StartsWith(r.PayeePattern, StringComparison.OrdinalIgnoreCase))
            ?.Category?.Name;

    /// <summary>
    /// Der letzte tatsächlich erfolgte Import, abgeleitet aus den Buchungen mit Importreferenz.
    /// </summary>
    /// <remarks>
    /// Abgeleitet statt gespeichert: eine eigene Importhistorie wäre eine zweite Wahrheit über
    /// dasselbe Ereignis, und sie liefe der Wirklichkeit hinterher, sobald jemand eine der
    /// importierten Buchungen löscht.
    /// </remarks>
    private async Task<ImportHistoryDto?> LastImportAsync(CancellationToken ct)
    {
        var last = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.ImportReference != null && t.ImportReference.StartsWith("CAMT"))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (last is null)
        {
            return null;
        }

        var day = last.CreatedAt.Date;
        var count = await db.Transactions.CountAsync(
            t => t.ImportReference != null && t.CreatedAt.Date == day, ct);

        return new ImportHistoryDto
        {
            FileName = DemoImportBatch.FileName,
            ImportedOn = DateOnly.FromDateTime(last.CreatedAt),
            AccountName = last.Account?.Name ?? "—",
            RecordCount = count,
        };
    }

    private async Task<List<ImportRowDto>> ClassifyAsync(CancellationToken ct)
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

        var rules = await db.CategorizationRules.AsNoTracking()
            .Include(r => r.Category)
            .ToListAsync(ct);

        return
        [
            .. DemoImportBatch.Records.Select((record, index) =>
            {
                var state = record switch
                {
                    { BookingDate: null } or { Amount: null } => ImportRowState.Error,
                    _ when knownReferences.Contains(record.Reference) => ImportRowState.Existing,
                    _ when knownBookings.Contains(
                        BookingKey(record.BookingDate.Value, record.Payee, record.Amount.Value))
                        => ImportRowState.Duplicate,
                    _ => ImportRowState.New,
                };

                return new ImportRowDto
                {
                    Index = index,
                    BookingDate = record.BookingDate,
                    Payee = record.Payee,
                    Amount = record.Amount,
                    State = state,
                    Problem = state != ImportRowState.Error
                        ? null
                        : record.BookingDate is null ? "Datum nicht lesbar" : "Betrag nicht lesbar",
                    CategoryName = state == ImportRowState.Error
                        ? null
                        : MatchCategoryName(record.Payee, rules),

                    // Neue Sätze angehakt, Treffer abgewählt — ein Vorschlag, keine Entscheidung.
                    PreSelected = state == ImportRowState.New,
                };
            }),
        ];
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
