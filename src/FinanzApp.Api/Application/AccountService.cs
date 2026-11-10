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
            HasBankAccess = a.HasBankAccess,
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

    /// <summary>
    /// Saldo je Konto-Id.
    /// </summary>
    /// <remarks>
    /// <para><b>Die eine Stelle, an der ein Kontostand entsteht.</b> Wer ihn anderswo noch
    /// einmal zusammenrechnet, bekommt irgendwann eine zweite Wahrheit — und bemerkt es an zwei
    /// Seiten, die verschiedene Zahlen zeigen.</para>
    /// <para>Ohne Kontrollpunkt gilt die alte Rechnung: Anfangsbestand plus alle Buchungen. Mit
    /// Kontrollpunkt setzt sie dort neu auf — der festgestellte Stand plus alles, was
    /// <em>danach</em> gebucht wurde. Buchungen vom Stichtag selbst stecken schon in der Zahl
    /// der Bank; sie noch einmal zu addieren, zählte sie doppelt.</para>
    /// <para>Eine Lücke vor dem Stichtag verschwindet damit aus dem heutigen Saldo, aber nicht
    /// aus der Welt: Sie steht als Differenz am Kontrollpunkt. Die Historie bleibt schief, und
    /// das ist richtig so — schief war sie vorher auch, nur unsichtbar.</para>
    /// </remarks>
    public async Task<Dictionary<int, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        var opening = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.OpeningBalance })
            .ToListAsync(ct);

        // Der jüngste Punkt je Konto. Bei zwei Punkten auf denselben Stichtag gilt der zuletzt
        // angelegte: Wer denselben Tag noch einmal einträgt, korrigiert sich.
        var anker = (await db.BalanceCheckpoints.AsNoTracking()
                .Select(k => new { k.Id, k.AccountId, k.AsOf, k.Balance })
                .ToListAsync(ct))
            .GroupBy(k => k.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(k => k.AsOf).ThenByDescending(k => k.Id).First());

        var booked = await db.Transactions.AsNoTracking()
            .Select(t => new { t.AccountId, t.Amount, t.BookingDate })
            .ToListAsync(ct);

        var sums = booked
            .GroupBy(t => t.AccountId)
            .ToDictionary(
                g => g.Key,
                g => anker.TryGetValue(g.Key, out var punkt)
                    ? g.Where(t => t.BookingDate > punkt.AsOf).Sum(t => t.Amount)
                    : g.Sum(t => t.Amount));

        return opening.ToDictionary(
            a => a.Id,
            a => (anker.TryGetValue(a.Id, out var punkt) ? punkt.Balance : a.OpeningBalance)
                 + (sums.TryGetValue(a.Id, out var s) ? s : 0m));
    }

    /// <summary>
    /// Was die Anwendung für einen bestimmten Stichtag rechnet.
    /// </summary>
    /// <remarks>
    /// <para>Für den Abgleich: Ein Kontrollpunkt vergleicht den festgestellten Stand mit dem
    /// gerechneten <em>desselben Tages</em>. Den heutigen danebenzustellen wäre ein Vergleich
    /// zweier verschiedener Zeitpunkte — und die Differenz daraus beschriebe nichts.</para>
    /// <para>Dieselbe Regel wie in <see cref="GetBalancesAsync"/>, nur auf einen Tag begrenzt:
    /// Grundlage ist der letzte Kontrollpunkt, der nicht nach dem Stichtag liegt, sonst der
    /// Anfangsbestand.</para>
    /// </remarks>
    public async Task<decimal> GetBalanceAtAsync(
        int accountId, DateOnly asOf, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null)
        {
            return 0m;
        }

        var punkt = await db.BalanceCheckpoints.AsNoTracking()
            .Where(k => k.AccountId == accountId && k.AsOf <= asOf)
            .OrderByDescending(k => k.AsOf)
            .ThenByDescending(k => k.Id)
            .FirstOrDefaultAsync(ct);

        var gebucht = db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.BookingDate <= asOf);

        if (punkt is not null)
        {
            gebucht = gebucht.Where(t => t.BookingDate > punkt.AsOf);
        }

        // SumAsync auf einer leeren Menge wirft bei decimal nicht, es kommt 0 zurück — anders
        // als bei MinAsync. Deshalb genügt hier der eine Aufruf.
        var summe = await gebucht.SumAsync(t => t.Amount, ct);

        return (punkt?.Balance ?? account.OpeningBalance) + summe;
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
