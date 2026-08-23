using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>Konten und ihre Salden.</summary>
/// <remarks>
/// Salden werden gerechnet, nie gespeichert: Anfangsbestand plus alle Buchungen des Kontos.
/// Damit kann ein Saldo nicht von den Buchungen abweichen, aus denen er entsteht.
/// Die Summen laufen im Speicher — Geldbeträge liegen als Cent in der Datenbank, und die
/// Datenmengen einer persönlichen Finanzverwaltung sind dafür klein genug.
/// </remarks>
public sealed class AccountService(FinanzAppDbContext db)
{
    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default)
    {
        var accounts = await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
        var balances = await GetBalancesAsync(ct);

        return [.. accounts.Select(a => new AccountDto
        {
            Id = a.Id,
            Name = a.Name,
            ShortName = a.ShortName,
            Iban = a.Iban,
            InterestRatePercent = a.InterestRatePercent,
            InterestYearToDate = a.InterestYearToDate,
            Balance = balances[a.Id],
            BalanceAsOf = a.BalanceAsOf,
        })];
    }

    /// <summary>Saldo je Konto-Id.</summary>
    public async Task<Dictionary<int, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        var opening = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.OpeningBalance })
            .ToListAsync(ct);

        var booked = await db.Transactions.AsNoTracking()
            .Select(t => new { t.AccountId, t.Amount })
            .ToListAsync(ct);

        var sums = booked
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        return opening.ToDictionary(
            a => a.Id,
            a => a.OpeningBalance + (sums.TryGetValue(a.Id, out var s) ? s : 0m));
    }

    /// <summary>Bestand je Kontoart in einem Durchgang — das Dashboard braucht beide Summen.</summary>
    public async Task<Dictionary<AccountKind, decimal>> GetTotalsByKindAsync(CancellationToken ct = default)
    {
        var kinds = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.Kind })
            .ToListAsync(ct);

        var balances = await GetBalancesAsync(ct);

        return Enum.GetValues<AccountKind>().ToDictionary(
            kind => kind,
            kind => kinds.Where(a => a.Kind == kind).Sum(a => balances[a.Id]));
    }
}
