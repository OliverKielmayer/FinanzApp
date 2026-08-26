using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class TransactionService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>
    /// Buchungsliste mit Suche und Paging, dazu die Zähler für Kopfzeile und Triage-Banner.
    /// Die Suche greift auf Empfänger und Kategoriename, ohne Groß-/Kleinschreibung zu unterscheiden.
    /// </summary>
    /// <remarks>
    /// Gefiltert und geblättert wird im Speicher. Bei den Datenmengen einer persönlichen
    /// Finanzverwaltung ist das unkritisch und hält die Suchsemantik über alle Datenbanken gleich —
    /// SQLite kennt für <c>LIKE</c> nur ASCII-Groß-/Kleinschreibung. Sobald Jahre an Buchungen
    /// zusammenkommen, gehört die Filterung in SQL, mit einem Volltextindex auf dem Empfänger.
    /// </remarks>
    public async Task<TransactionPageDto> GetPageAsync(
        string? search,
        int? accountId = null,
        int? categoryId = null,
        TransactionKind? kind = null,
        bool uncategorizedOnly = false,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var all = await LoadAsync(ct);

        IReadOnlyList<TransactionDto> filtered = [.. all.Where(t =>
            Matches(t, search)
            && (accountId is not { } account || t.AccountId == account)
            && (categoryId is not { } category || t.CategoryId == category)
            && (kind is not { } wanted || t.Kind == wanted)
            && (!uncategorizedOnly || t.IsUncategorized))];

        return new TransactionPageDto
        {
            Items = [.. filtered.Skip(skip).Take(take)],
            FilteredCount = filtered.Count,
            TotalCount = all.Count,
            UncategorizedCount = all.Count(t => t.IsUncategorized),
            FilteredUncategorizedCount = filtered.Count(t => t.IsUncategorized),
            Totals = Totals(filtered),
        };
    }

    private static bool Matches(TransactionDto transaction, string? search)
        => string.IsNullOrWhiteSpace(search)
           || transaction.Payee.Contains(search, StringComparison.OrdinalIgnoreCase)
           || (transaction.CategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Summen über den sichtbaren Ausschnitt. Umbuchungen bleiben draußen — sie sind weder
    /// Einnahme noch Ausgabe, sondern dasselbe Geld an einem anderen Ort.
    /// </summary>
    private static TransactionTotalsDto Totals(IReadOnlyList<TransactionDto> rows)
    {
        var real = rows.Where(t => t.Kind != TransactionKind.Transfer).ToList();
        var income = real.Where(t => t.Amount > 0).Sum(t => t.Amount);
        var expense = real.Where(t => t.Amount < 0).Sum(t => -t.Amount);

        return new TransactionTotalsDto
        {
            Income = income,
            Expense = expense,
            Balance = income - expense,
            TransferCount = rows.Count - real.Count,
        };
    }

    /// <summary>
    /// Stapelvergabe. <b>Umbuchungen bleiben unverändert</b>, sofern nicht ausdrücklich
    /// „Umbuchung“ gewählt wurde.
    /// </summary>
    /// <remarks>
    /// Das ist keine Bequemlichkeit, sondern eine fachliche Regel: wer fünfzehn Zeilen markiert
    /// und „Wohnen“ wählt, meint nicht die Umbuchung aufs Tagesgeld, die zufällig dazwischen
    /// liegt. Sie stillschweigend mitzunehmen würde jede Auswertung verfälschen — deshalb bleibt
    /// sie stehen, und die Meldung sagt es.
    /// </remarks>
    public async Task<BatchAssignResultDto> AssignCategoryBatchAsync(
        BatchAssignRequest request, CancellationToken ct = default)
    {
        var ids = request.TransactionIds.Distinct().ToList();
        var rows = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

        if (request.MarkAsTransfer)
        {
            foreach (var row in rows)
            {
                row.Kind = TransactionKind.Transfer;
                row.CategoryId = null;
            }

            await db.SaveChangesAsync(ct);
            return await ResultAsync(rows.Count, 0, $"{Plural(rows.Count)} als Umbuchung markiert", ids, ct);
        }

        if (request.CategoryId is not { } categoryId)
        {
            throw new ArgumentException("Ohne Kategorie lässt sich nichts zuweisen.", nameof(request));
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct)
                       ?? throw new ArgumentException("Unbekannte Kategorie.", nameof(request));

        var (targets, transfers) = (
            rows.Where(t => t.Kind != TransactionKind.Transfer).ToList(),
            rows.Count(t => t.Kind == TransactionKind.Transfer));

        foreach (var row in targets)
        {
            row.CategoryId = categoryId;
        }

        await db.SaveChangesAsync(ct);

        var message = $"{targets.Count} × {category.Name}";
        if (transfers > 0)
        {
            message += $" · {transfers} {(transfers == 1 ? "Umbuchung" : "Umbuchungen")} geschützt";
        }

        return await ResultAsync(targets.Count, transfers, message, ids, ct);
    }

    /// <summary>
    /// Löscht Buchungen. Sie sind Tatsachen — gelöscht wird nur, was ausdrücklich gewählt wurde,
    /// und nichts hängt daran, was still mitverschwände.
    /// </summary>
    public async Task<int> DeleteAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        var rows = await db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        db.Transactions.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private static string Plural(int count)
        => count == 1 ? "1 Buchung" : $"{count} Buchungen";

    private async Task<BatchAssignResultDto> ResultAsync(
        int assigned, int protectedTransfers, string message, List<int> ids, CancellationToken ct)
        => new()
        {
            Assigned = assigned,
            ProtectedTransfers = protectedTransfers,
            Message = message,
            Items = [.. (await LoadAsync(ct)).Where(t => ids.Contains(t.Id))],
        };

    public async Task<TransactionDto?> GetAsync(int id, CancellationToken ct = default)
        => (await LoadAsync(ct)).FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Legt eine Buchung an. Wiederholte Aufrufe mit demselben <see cref="CreateTransactionRequest.RequestKey"/>
    /// liefern die bereits angelegte Buchung zurück, statt eine zweite anzulegen — ein
    /// abgebrochener Sendeversuch auf dem Handy darf keine Doppelbuchung erzeugen.
    /// </summary>
    public async Task<TransactionDto> CreateAsync(CreateTransactionRequest request, CancellationToken ct = default)
    {
        var existing = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.RequestKey == request.RequestKey, ct);
        if (existing is not null)
        {
            return (await GetAsync(existing.Id, ct))!;
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Der Betrag muss größer als null sein.", nameof(request));
        }

        if (!await db.Accounts.AnyAsync(a => a.Id == request.AccountId, ct))
        {
            throw new ArgumentException("Unbekanntes Konto.", nameof(request));
        }

        // Umbuchungen tragen keine Kategorie — sie sind weder Einnahme noch Ausgabe.
        var categoryId = request.Kind == TransactionKind.Transfer ? null : request.CategoryId;

        var entity = new Transaction
        {
            BookingDate = request.BookingDate ?? clock.Today,
            Payee = string.IsNullOrWhiteSpace(request.Note) ? "Manuelle Buchung" : request.Note.Trim(),
            Kind = request.Kind,
            Amount = request.Kind == TransactionKind.Income ? request.Amount : -request.Amount,
            AccountId = request.AccountId,
            CategoryId = categoryId,
            Note = request.Note?.Trim(),
            RequestKey = request.RequestKey,
            CreatedAt = clock.Now,
        };

        db.Transactions.Add(entity);
        await db.SaveChangesAsync(ct);

        return (await GetAsync(entity.Id, ct))!;
    }

    /// <summary>
    /// Setzt die Kategorie einer Buchung und legt auf Wunsch eine Regel auf dem Empfänger-Präfix an.
    /// </summary>
    public async Task<TransactionDto?> AssignCategoryAsync(
        int id, AssignCategoryRequest request, CancellationToken ct = default)
    {
        var entity = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        if (request.MarkAsTransfer)
        {
            // Eine Umbuchung ist weder Einnahme noch Ausgabe und trägt deshalb keine Kategorie.
            entity.Kind = TransactionKind.Transfer;
            entity.CategoryId = null;
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct);
        }

        if (request.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new ArgumentException("Unbekannte Kategorie.", nameof(request));
        }

        entity.CategoryId = request.CategoryId;
        if (request.CategoryId is not null && entity.Kind == TransactionKind.Transfer)
        {
            // Eine zugeordnete Kategorie hebt die Umbuchung auf: die Buchung wird wieder
            // Einnahme oder Ausgabe, je nach Vorzeichen.
            entity.Kind = entity.Amount >= 0 ? TransactionKind.Income : TransactionKind.Expense;
            entity.CounterAccountId = null;
        }

        if (request.CreateRule && request.CategoryId is { } ruleCategoryId)
        {
            await UpsertRuleAsync(Categorization.RulePatternFor(entity.Payee), ruleCategoryId, ct);
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task UpsertRuleAsync(string pattern, int categoryId, CancellationToken ct)
    {
        var rule = await db.CategorizationRules.FirstOrDefaultAsync(r => r.PayeePattern == pattern, ct);
        if (rule is null)
        {
            db.CategorizationRules.Add(new CategorizationRule { PayeePattern = pattern, CategoryId = categoryId });
        }
        else
        {
            rule.CategoryId = categoryId;
        }
    }

    private async Task<List<TransactionDto>> LoadAsync(CancellationToken ct)
        => await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.Id)
            .Select(t => new TransactionDto
            {
                Id = t.Id,
                BookingDate = t.BookingDate,
                Payee = t.Payee,
                Kind = t.Kind,
                Amount = t.Amount,
                CategoryId = t.CategoryId,
                CategoryName = t.Category!.Name,
                AccountId = t.AccountId,
                AccountName = t.Account!.Name,
                AccountShortName = t.Account.ShortName,
                Note = t.Note,
            })
            .ToListAsync(ct);
}
