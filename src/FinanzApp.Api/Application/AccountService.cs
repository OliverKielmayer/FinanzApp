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
        var accounts = await db.Accounts.AsNoTracking()
            .Include(a => a.Owner)
            .Include(a => a.Shares).ThenInclude(s => s.User)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
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

            OwnerUserId = a.OwnerUserId,
            OwnerName = a.Owner?.Name,
            IsMine = a.OwnerUserId == db.CurrentUserId,
            Sharing = a.Sharing,
            SharedWith = [.. a.Shares
                .Where(s => s.User != null)
                .Select(s => new SharedWithDto(s.UserId, s.User!.Name))
                .OrderBy(s => s.Name, StringComparer.CurrentCulture)],
        })];
    }

    /// <summary>
    /// Ändert die Freigabe eines Kontos.
    /// </summary>
    /// <remarks>
    /// <para>Nur der Eigentümer darf das. Ein fremdes Konto ist für andere nicht bearbeitbar —
    /// sonst könnte sich jedes Mitglied selbst Zugang verschaffen, und die Freigabe wäre eine
    /// Anzeige-Konvention statt einer Regel.</para>
    /// <para>Ein Konto ohne Eigentümer gehört niemandem und lässt sich nicht umstellen; es bleibt
    /// beim Haushalt. Wer es sich zueignen dürfte, wäre eine eigene Entscheidung.</para>
    /// </remarks>
    public async Task<AccountDto> SetSharingAsync(
        int id, AccountSharing sharing, IReadOnlyList<int> userIds, CancellationToken ct = default)
    {
        var account = await db.Accounts
                          .Include(a => a.Shares)
                          .FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new RuleViolationException("Dieses Konto gibt es nicht.");

        if (account.OwnerUserId != db.CurrentUserId)
        {
            throw new RuleViolationException(
                account.OwnerUserId is null
                    ? "Dieses Konto hat keinen Eigentümer und bleibt beim Haushalt."
                    : "Die Freigabe verwaltet der Eigentümer des Kontos.");
        }

        account.Sharing = sharing;
        db.AccountShares.RemoveRange(account.Shares);

        if (sharing == AccountSharing.Named)
        {
            // Sich selbst braucht der Eigentümer nicht zu benennen — er sieht sein Konto ohnehin.
            foreach (var userId in userIds.Distinct().Where(u => u != account.OwnerUserId))
            {
                db.AccountShares.Add(new AccountShare { AccountId = id, UserId = userId });
            }
        }

        await db.SaveChangesAsync(ct);

        return (await GetAccountsAsync(ct)).Single(a => a.Id == id);
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
