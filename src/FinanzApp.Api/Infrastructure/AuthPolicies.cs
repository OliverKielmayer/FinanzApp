using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Rollenprüfung am Endpunkt. Sie gehört auf den Server: der Client blendet schreibende
/// Aktionen für Lesezugriff zwar aus, aber das ist Bequemlichkeit, keine Absicherung.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Darf Daten des Haushalts ändern — Inhaber und Mitglied.</summary>
    public const string Write = "write";

    /// <summary>Darf Benutzer und Einladungen verwalten — nur der Inhaber.</summary>
    public const string ManageUsers = "manage-users";

    public static void AddAppAuthorization(this IServiceCollection services)
        => services.AddAuthorizationBuilder()
            .AddPolicy(Write, policy => policy.RequireRole(
                nameof(HouseholdRole.Owner), nameof(HouseholdRole.Member)))
            .AddPolicy(ManageUsers, policy => policy.RequireRole(
                nameof(HouseholdRole.Owner)));
}
