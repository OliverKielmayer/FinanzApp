using System.Security.Cryptography;
using System.Text;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>Warum eine Anmeldung nicht zustande kam.</summary>
public enum SignInFailure
{
    None = 0,

    /// <summary>Adresse unbekannt oder Passwort falsch. Nach außen dieselbe Meldung.</summary>
    InvalidCredentials = 1,

    /// <summary>Zugangsdaten stimmen, aber das Konto ist nach Fehlversuchen gesperrt.</summary>
    LockedOut = 2,
}

public sealed record SignInResult(User? User, SignInFailure Failure, DateTime? LockedUntil = null);

public sealed record RegistrationResult(User? User, string? Error);

/// <summary>
/// Anmeldung, Registrierung, Sitzungen und Passwort-Reset.
/// </summary>
/// <remarks>
/// Hier läuft bewusst nicht <see cref="IClock"/>, sondern <see cref="TimeProvider"/>: der
/// Demo-Stichtag friert die fachliche Zeit ein, aber Sitzungen, Sperren und Reset-Token werden
/// gegen die echte Uhr geprüft — auch vom Browser, der das Anmelde-Cookie hält. Ein
/// eingefrorenes „jetzt“ würde jede Sitzung sofort ablaufen lassen.
/// </remarks>
public sealed class AuthService(
    FinanzAppDbContext db,
    IPasswordHasher<User> hasher,
    TimeProvider time,
    IMailSender mail,
    ILogger<AuthService> log)
{
    /// <summary>Fehlversuche, ab denen gesperrt wird.</summary>
    private const int MaxFailedAttempts = 5;

    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan PersistentSessionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// Prüft Zugangsdaten. Nach außen darf nur eine unspezifische Meldung entstehen — ob eine
    /// Adresse existiert, verrät die Antwort nicht.
    /// </summary>
    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        var user = await db.Users
            .Include(u => u.Household)
            .FirstOrDefaultAsync(u => u.Email == normalized, ct);

        if (user is null)
        {
            // Trotzdem einmal hashen, damit eine unbekannte Adresse nicht schneller antwortet
            // als eine bekannte und sich so verrät.
            hasher.HashPassword(new User { Name = "-", Email = "-", PasswordHash = "-" }, password);
            return new SignInResult(null, SignInFailure.InvalidCredentials);
        }

        var now = time.GetLocalNow().DateTime;
        var locked = user.LockedUntil is { } until && until > now;
        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = now.Add(LockoutDuration);
                log.LogWarning("Konto {UserId} nach {Versuche} Fehlversuchen gesperrt.", user.Id, user.FailedAttempts);
            }

            await db.SaveChangesAsync(ct);
            return new SignInResult(null, SignInFailure.InvalidCredentials);
        }

        if (locked)
        {
            // Das Passwort stimmt — hier darf die Sperre benannt werden. Wer das Passwort kennt,
            // erfährt nichts Neues über die Existenz des Kontos.
            return new SignInResult(null, SignInFailure.LockedOut, user.LockedUntil);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.LastSeenAt = now;
        await db.SaveChangesAsync(ct);

        return new SignInResult(user, SignInFailure.None);
    }

    /// <summary>Legt eine Sitzung an. „Angemeldet bleiben“ verlängert nur ihre Laufzeit —
    /// widerrufbar bleibt sie in beiden Fällen.</summary>
    public async Task<UserSession> StartSessionAsync(
        User user, bool staySignedIn, string? device, CancellationToken ct = default)
    {
        var now = time.GetLocalNow().DateTime;
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.Add(staySignedIn ? PersistentSessionLifetime : SessionLifetime),
            Device = device,
        };

        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is { RevokedAt: null })
        {
            session.RevokedAt = time.GetLocalNow().DateTime;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Lädt eine gültige Sitzung samt Benutzer und schreibt die letzte Aktivität fort.</summary>
    public async Task<UserSession?> TouchSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        var now = time.GetLocalNow().DateTime;
        if (session is null || !session.IsActive(now) || session.User is null)
        {
            return null;
        }

        // Nicht bei jedem Aufruf schreiben — einmal pro Minute reicht für „zuletzt aktiv“.
        if (now - session.LastSeenAt > TimeSpan.FromMinutes(1))
        {
            session.LastSeenAt = now;
            session.User.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
        }

        return session;
    }

    /// <summary>
    /// Legt einen Benutzer an — entweder in einem bestehenden Haushalt über einen Einladungscode
    /// oder in einem neu angelegten, dessen Inhaber er wird.
    /// </summary>
    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            return new RegistrationResult(null, "Name und E-Mail nötig.");
        }

        if (PasswordPolicy.Reject(request.Password) is { } problem)
        {
            return new RegistrationResult(null, problem);
        }

        var email = Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return new RegistrationResult(null, "Zu dieser E-Mail gibt es bereits ein Konto.");
        }

        var now = time.GetLocalNow().DateTime;
        Household household;
        HouseholdRole role;
        Invitation? invitation = null;

        if (request.HouseholdMode == HouseholdMode.Join)
        {
            var code = (request.InviteCode ?? string.Empty).Trim().ToUpperInvariant();
            invitation = await db.Invitations
                .Include(i => i.Household)
                .FirstOrDefaultAsync(i => i.Code == code, ct);

            if (invitation is null || !invitation.IsOpen(now) || invitation.Household is null)
            {
                return new RegistrationResult(null, "Der Einladungscode ist ungültig oder abgelaufen.");
            }

            household = invitation.Household;
            role = invitation.Role;
        }
        else
        {
            var name = (request.HouseholdName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return new RegistrationResult(null, "Der Haushalt braucht einen Namen.");
            }

            household = new Household { Name = name, CreatedAt = now };
            db.Households.Add(household);
            role = HouseholdRole.Owner;
        }

        var user = new User
        {
            Household = household,
            Name = request.Name.Trim(),
            Email = email,
            PasswordHash = "-",
            Role = role,
            CreatedAt = now,
            LastSeenAt = now,
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);

        if (invitation is not null)
        {
            invitation.RedeemedAt = now;
        }

        await db.SaveChangesAsync(ct);

        if (invitation is not null)
        {
            // Die Id des Benutzers entsteht erst beim Speichern; deshalb ein zweiter Durchgang.
            invitation.RedeemedByUserId = user.Id;
            await db.SaveChangesAsync(ct);
        }

        return new RegistrationResult(user, null);
    }

    /// <summary>
    /// Fordert einen Reset an. Die Antwort ist immer dieselbe — auch für unbekannte Adressen,
    /// sonst wird das Formular zum Verzeichnis vorhandener Konten.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email, string linkTemplate, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            log.LogInformation("Reset für unbekannte Adresse angefordert. Keine Mail versendet.");
            return;
        }

        var token = CreateToken();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            CreatedAt = time.GetLocalNow().DateTime,
            ExpiresAt = time.GetLocalNow().DateTime.Add(ResetTokenLifetime),
        });
        await db.SaveChangesAsync(ct);

        var link = linkTemplate.Replace("{token}", Uri.EscapeDataString(token), StringComparison.Ordinal);
        var body =
            $"Hallo {user.Name},\n\n" +
            "für dein FinanzApp-Konto wurde ein neues Passwort angefordert.\n" +
            $"Über diesen Link kannst du eines setzen:\n\n{link}\n\n" +
            $"Der Link gilt {ResetTokenLifetime.TotalMinutes:0} Minuten und lässt sich nur einmal verwenden.\n" +
            "Wenn du das nicht warst, ignoriere diese Nachricht — dein Passwort bleibt unverändert.\n";

        await mail.SendAsync(user.Email, user.Name, "FinanzApp: Passwort zurücksetzen", body, ct);
    }

    /// <summary>Löst ein Reset-Token ein. Alle Sitzungen des Benutzers werden dabei widerrufen.</summary>
    public async Task<string?> RedeemPasswordResetAsync(
        string token, string newPassword, CancellationToken ct = default)
    {
        if (PasswordPolicy.Reject(newPassword) is { } problem)
        {
            return problem;
        }

        var hash = HashToken(token);
        var entry = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        var now = time.GetLocalNow().DateTime;
        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt <= now || entry.User is null)
        {
            return "Der Link ist ungültig oder abgelaufen.";
        }

        entry.User.PasswordHash = hasher.HashPassword(entry.User, newPassword);
        entry.User.FailedAttempts = 0;
        entry.User.LockedUntil = null;
        entry.UsedAt = now;

        // Ein zurückgesetztes Passwort beendet alle offenen Sitzungen — wer das Konto
        // übernommen hatte, fliegt damit heraus.
        var sessions = await db.UserSessions
            .Where(s => s.UserId == entry.UserId && s.RevokedAt == null)
            .ToListAsync(ct);
        sessions.ForEach(s => s.RevokedAt = now);

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Erzeugt einen neuen Einladungscode und entwertet die vorherigen offenen.</summary>
    public async Task<Invitation> CreateInvitationAsync(
        int householdId, HouseholdRole role, CancellationToken ct = default)
    {
        var now = time.GetLocalNow().DateTime;
        var open = await db.Invitations
            .Where(i => i.HouseholdId == householdId && i.RedeemedAt == null && i.ExpiresAt > now)
            .ToListAsync(ct);
        open.ForEach(i => i.ExpiresAt = now);

        var invitation = new Invitation
        {
            HouseholdId = householdId,
            Code = CreateInvitationCode(),
            Role = role,
            CreatedAt = now,
            ExpiresAt = now.Add(InvitationLifetime),
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return invitation;
    }

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string CreateToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Format <c>HH-XXXX-XXXX</c>. Das Alphabet lässt Zeichen weg, die sich beim
    /// Abtippen verwechseln lassen.</summary>
    private static string CreateInvitationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = RandomNumberGenerator.GetItems<char>(alphabet, 8);
        return $"HH-{new string(chars[..4])}-{new string(chars[4..])}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
