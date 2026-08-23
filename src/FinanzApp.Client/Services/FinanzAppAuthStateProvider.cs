using System.Security.Claims;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace FinanzApp.Client.Services;

/// <summary>
/// Kennt den angemeldeten Benutzer. Die Wahrheit steht im Anmelde-Cookie auf dem Server —
/// dieser Anbieter hält nur ab, was <c>/api/auth/me</c> geantwortet hat.
/// </summary>
/// <remarks>
/// Was hier steht, entscheidet nur darüber, was die Oberfläche zeigt. Ob eine Anfrage
/// durchgeht, entscheidet der Server. Wer die Rolle im Browser manipuliert, sieht andere
/// Schaltflächen und bekommt beim Klick eine 403.
/// </remarks>
public sealed class FinanzAppAuthStateProvider(FinanzAppApi api) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    private CurrentUserDto? user;
    private bool loaded;

    /// <summary>Der angemeldete Benutzer, oder <c>null</c>.</summary>
    public CurrentUserDto? User => user;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!loaded)
        {
            user = await api.GetCurrentUserAsync();
            loaded = true;
        }

        return user is null ? Anonymous : new AuthenticationState(BuildPrincipal(user));
    }

    /// <summary>Nach Anmeldung, Registrierung oder Abmeldung aufzurufen.</summary>
    public void SetUser(CurrentUserDto? value)
    {
        user = value;
        loaded = true;

        NotifyAuthenticationStateChanged(Task.FromResult(
            value is null ? Anonymous : new AuthenticationState(BuildPrincipal(value))));
    }

    private static ClaimsPrincipal BuildPrincipal(CurrentUserDto user)
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            authenticationType: "finanzapp",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
}
