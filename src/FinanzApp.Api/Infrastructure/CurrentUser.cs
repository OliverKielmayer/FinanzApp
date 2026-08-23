using System.Security.Claims;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Namen der Ansprüche im Anmelde-Cookie.</summary>
public static class AppClaims
{
    /// <summary>Id der Sitzung. Erlaubt das serverseitige Widerrufen.</summary>
    public const string SessionId = "finanzapp:sid";

    /// <summary>Haushalt des Benutzers. Speist den Mandantenfilter.</summary>
    public const string HouseholdId = "finanzapp:hid";
}

/// <summary>Der angemeldete Benutzer der laufenden Anfrage.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId => ParseInt(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public int? HouseholdId => ParseInt(Principal?.FindFirstValue(AppClaims.HouseholdId));

    public Guid? SessionId => Guid.TryParse(Principal?.FindFirstValue(AppClaims.SessionId), out var id) ? id : null;

    public HouseholdRole? Role
        => Enum.TryParse<HouseholdRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public bool CanWrite => Role is HouseholdRole.Owner or HouseholdRole.Member;

    public bool CanManageUsers => Role is HouseholdRole.Owner;

    private static int? ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;
}
