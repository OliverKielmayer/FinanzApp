using System.Text.Json;
using FinanzApp.Shared.Contracts;
using Microsoft.JSInterop;

namespace FinanzApp.Client.Services;

/// <summary>Ein Benutzer, der sich auf diesem Gerät schon einmal angemeldet hat.</summary>
public sealed record DeviceProfile(string Name, string Email, HouseholdRole Role);

/// <summary>
/// Die Liste „Profile auf diesem Gerät“ des Anmeldescreens.
/// </summary>
/// <remarks>
/// Gespeichert werden nur Name, Adresse und Rolle — nie ein Passwort und nie ein Token. Ein Tipp
/// auf ein Profil füllt bloß das E-Mail-Feld vor; angemeldet wird dadurch niemand. Wer das Gerät
/// in die Hand bekommt, erfährt daraus, wer es benutzt, kommt damit aber nicht in den Haushalt.
/// </remarks>
public sealed class DeviceProfileStore(IJSRuntime js)
{
    private const string StorageKey = "finanzapp.profiles";
    private const int MaxProfiles = 5;

    public async Task<IReadOnlyList<DeviceProfile>> GetAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<DeviceProfile>>(raw) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or JSException)
        {
            // Ein beschädigter Eintrag darf die Anmeldung nicht blockieren.
            await ClearAsync();
            return [];
        }
    }

    /// <summary>Merkt den Benutzer für die nächste Anmeldung auf diesem Gerät.</summary>
    public async Task RememberAsync(CurrentUserDto user)
    {
        var profiles = (await GetAsync())
            .Where(p => !string.Equals(p.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            .Prepend(new DeviceProfile(user.Name, user.Email, user.Role))
            .Take(MaxProfiles)
            .ToList();

        await SaveAsync(profiles);
    }

    public async Task ForgetAsync(string email)
    {
        var profiles = (await GetAsync())
            .Where(p => !string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await SaveAsync(profiles);
    }

    private async Task SaveAsync(List<DeviceProfile> profiles)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(profiles));
        }
        catch (JSException)
        {
            // Privater Modus oder gesperrter Speicher — die Liste bleibt dann eben leer.
        }
    }

    private async Task ClearAsync()
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
            // Nichts zu tun.
        }
    }
}
