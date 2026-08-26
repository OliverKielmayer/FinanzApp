using System.Security.Claims;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Einzige Meldung für „Adresse unbekannt“ und „Passwort falsch“. Wer probiert, ob eine
    /// Adresse existiert, erfährt es hier nicht.
    /// </summary>
    private const string InvalidCredentials = "E-Mail oder Passwort stimmt nicht.";

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Anmeldung");

        auth.MapPost("/login", async (
            LoginRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
        {
            var result = await service.SignInAsync(request.Email, request.Password, ct);

            if (result.Failure == SignInFailure.LockedOut)
            {
                return Results.Problem(
                    $"Zu viele Fehlversuche. Bitte ab {result.LockedUntil:HH:mm} Uhr erneut versuchen.",
                    statusCode: StatusCodes.Status423Locked);
            }

            if (result.User is not { } user)
            {
                return Results.Problem(InvalidCredentials, statusCode: StatusCodes.Status401Unauthorized);
            }

            var session = await service.StartSessionAsync(user, request.StaySignedIn, DeviceOf(http), ct);
            await SignInAsync(http, user, session, request.StaySignedIn);

            return Results.Ok(ToDto(user, session));
        }).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/register", async (
            RegisterRequest request, AuthService service, FinanzAppDbContext db,
            HttpContext http, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(request, ct);
            if (result.User is not { } user)
            {
                return Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
            }

            // Ein frisch angelegter Haushalt startet mit den Grundkategorien; ohne sie ließe sich
            // keine Buchung zuordnen.
            if (request.HouseholdMode == HouseholdMode.Create)
            {
                await SeedData.SeedNewHouseholdAsync(db, user.HouseholdId, ct);
            }

            var session = await service.StartSessionAsync(user, staySignedIn: true, DeviceOf(http), ct);
            await SignInAsync(http, user, session, isPersistent: true);

            return Results.Ok(ToDto(user, session));
        }).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/logout", async (AuthService service, CurrentUser current, HttpContext http) =>
        {
            if (current.SessionId is { } sessionId)
            {
                await service.RevokeSessionAsync(sessionId);
            }

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (CurrentUser current, FinanzAppDbContext db, CancellationToken ct) =>
        {
            if (current.UserId is not { } userId)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.AsNoTracking()
                .Include(u => u.Household)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user is null)
            {
                return Results.Unauthorized();
            }

            var signedInAt = current.SessionId is { } sessionId
                ? await db.UserSessions.AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => (DateTime?)s.CreatedAt)
                    .FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(ToDto(user, signedInAt));
        }).RequireAuthorization();

        auth.MapPost("/password-reset", async (
            PasswordResetStartRequest request, AuthService service, HttpContext http, CancellationToken ct) =>
        {
            var link = $"{http.Request.Scheme}://{http.Request.Host}/passwort-zuruecksetzen?token={{token}}";
            await service.RequestPasswordResetAsync(request.Email, link, ct);

            // Immer dieselbe Antwort, ob die Adresse existiert oder nicht.
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting("auth");

        auth.MapPost("/password-reset/redeem", async (
            PasswordResetRedeemRequest request, AuthService service, CancellationToken ct) =>
        {
            var error = await service.RedeemPasswordResetAsync(request.Token, request.NewPassword, ct);
            return error is null
                ? Results.NoContent()
                : Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }).AllowAnonymous().RequireRateLimiting("auth");

        // Passwort ändern, angemeldet. Nicht anonym und nicht über den Reset-Weg: hier zählt,
        // dass jemand das bisherige Passwort kennt.
        auth.MapPost("/password", async (
            ChangePasswordRequest request, AuthService service, CurrentUser current, CancellationToken ct) =>
        {
            if (current.UserId is not { } userId || current.SessionId is not { } sessionId)
            {
                return Results.Unauthorized();
            }

            var error = await service.ChangePasswordAsync(
                userId, sessionId, request.CurrentPassword, request.NewPassword, ct);

            return error is null
                ? Results.NoContent()
                : Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }).RequireAuthorization().RequireRateLimiting("auth");

        var household = app.MapGroup("/api/household").WithTags("Haushalt").RequireAuthorization();

        household.MapGet("/", async (HouseholdService service, CancellationToken ct) =>
        {
            var overview = await service.GetOverviewAsync(ct);
            return overview is null ? Results.NotFound() : Results.Ok(overview);
        });

        household.MapPost("/invitations", async (
            AuthService service, CurrentUser current, CancellationToken ct) =>
        {
            if (current.HouseholdId is not { } householdId)
            {
                return Results.Unauthorized();
            }

            var invitation = await service.CreateInvitationAsync(householdId, HouseholdRole.Member, ct);
            return Results.Ok(new InvitationDto { Code = invitation.Code, ExpiresAt = invitation.ExpiresAt });
        }).RequireAuthorization(AuthPolicies.ManageUsers);
    }

    /// <summary>Setzt das Anmelde-Cookie. Es trägt nur Id, Rolle, Haushalt und Sitzung —
    /// keine Daten, die sich ändern können, ohne dass die Sitzung neu geprüft wird.</summary>
    private static Task SignInAsync(HttpContext http, User user, UserSession session, bool isPersistent)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Name));
        identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role.ToString()));
        identity.AddClaim(new Claim(AppClaims.HouseholdId, user.HouseholdId.ToString()));
        identity.AddClaim(new Claim(AppClaims.SessionId, session.Id.ToString()));

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = session.ExpiresAt.ToUniversalTime(),
            });
    }

    private static CurrentUserDto ToDto(User user, UserSession session)
        => ToDto(user, session.CreatedAt);

    private static CurrentUserDto ToDto(User user, DateTime? signedInAt) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        HouseholdId = user.HouseholdId,
        HouseholdName = user.Household?.Name ?? string.Empty,
        SignedInAt = signedInAt ?? user.CreatedAt,
        TwoFactorEnabled = user.TwoFactorEnabled,
    };

    /// <summary>Grobe Gerätekennung für die Sitzungsübersicht — der volle User-Agent wäre
    /// mehr, als dafür nötig ist.</summary>
    private static string? DeviceOf(HttpContext http)
    {
        var agent = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(agent))
        {
            return null;
        }

        return agent switch
        {
            var a when a.Contains("Android", StringComparison.OrdinalIgnoreCase) => "Android",
            var a when a.Contains("iPhone", StringComparison.OrdinalIgnoreCase) => "iPhone",
            var a when a.Contains("iPad", StringComparison.OrdinalIgnoreCase) => "iPad",
            var a when a.Contains("Macintosh", StringComparison.OrdinalIgnoreCase) => "Mac",
            var a when a.Contains("Windows", StringComparison.OrdinalIgnoreCase) => "Windows",
            _ => "Unbekanntes Gerät",
        };
    }
}
