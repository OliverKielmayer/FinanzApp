using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Speist den Screen „Benutzer &amp; Anmeldung“.
/// </summary>
/// <remarks>
/// Benutzer, Sitzungen und Einladungen tragen keinen globalen Abfragefilter — sie werden vor der
/// Anmeldung gebraucht. Jede Abfrage hier führt die Haushaltsbedingung deshalb ausdrücklich mit.
/// </remarks>
public sealed class HouseholdService(FinanzAppDbContext db, CurrentUser current, TimeProvider time)
{
    public async Task<HouseholdOverviewDto?> GetOverviewAsync(CancellationToken ct = default)
    {
        if (current.HouseholdId is not { } householdId || current.UserId is not { } userId)
        {
            return null;
        }

        var household = await db.Households.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);
        if (household is null)
        {
            return null;
        }

        // Wie viele Konten jedes Mitglied sieht. Gerechnet wird über alle Konten des Haushalts,
        // nicht über die sichtbaren — sonst stünde in der Zeile, was der Betrachter sieht, und
        // nicht, was das Mitglied sieht.
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(a => a.HouseholdId == householdId)
            .Select(a => new
            {
                a.OwnerUserId,
                a.Sharing,
                Shared = a.Shares.Select(x => x.UserId).ToList(),
            })
            .ToListAsync(ct);

        var members = await db.Users.AsNoTracking()
            .Where(u => u.HouseholdId == householdId)
            .OrderBy(u => u.Role)
            .ThenBy(u => u.Name)
            .Select(u => new HouseholdMemberDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                LastSeenAt = u.LastSeenAt,
            })
            .ToListAsync(ct);

        members =
        [
            .. members.Select(m => m with
            {
                TotalAccountCount = accounts.Count,
                VisibleAccountCount = accounts.Count(a =>
                    a.OwnerUserId == m.Id
                    || a.Sharing == AccountSharing.Household
                    || (a.Sharing == AccountSharing.Named && a.Shared.Contains(m.Id))),
            }),
        ];

        return new HouseholdOverviewDto
        {
            HouseholdName = household.Name,
            Members = members,
            Invitation = await GetOpenInvitationAsync(householdId, ct),
            Session = await GetSessionInfoAsync(userId, ct),
        };
    }

    /// <summary>
    /// Die offene Einladung. Nur der Inhaber bekommt sie zu sehen — ein Lesezugriff darf keine
    /// Möglichkeit haben, weitere Benutzer in den Haushalt zu holen.
    /// </summary>
    private async Task<InvitationDto?> GetOpenInvitationAsync(int householdId, CancellationToken ct)
    {
        if (!current.CanManageUsers)
        {
            return null;
        }

        var now = time.GetLocalNow().DateTime;
        var invitation = await db.Invitations.AsNoTracking()
            .Where(i => i.HouseholdId == householdId && i.RedeemedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return invitation is null
            ? null
            : new InvitationDto { Code = invitation.Code, ExpiresAt = invitation.ExpiresAt };
    }

    private async Task<SessionInfoDto> GetSessionInfoAsync(int userId, CancellationToken ct)
    {
        var name = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct) ?? "—";

        var signedInAt = current.SessionId is { } sessionId
            ? await db.UserSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => (DateTime?)s.CreatedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        return new SessionInfoDto
        {
            UserName = name,
            SignedInAt = signedInAt ?? time.GetLocalNow().DateTime,
        };
    }
}
