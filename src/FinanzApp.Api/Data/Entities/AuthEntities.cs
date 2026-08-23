using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Data.Entities;

/// <summary>
/// Der Haushalt besitzt die Daten: Konten, Buchungen, Budgets, Depots, Darlehen. Benutzer melden
/// sich einzeln an und gehören genau einem Haushalt. Jede Abfrage filtert darauf.
/// </summary>
public class Household
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<User> Users { get; set; } = [];
    public List<Invitation> Invitations { get; set; } = [];
}

public class User
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }

    public required string Name { get; set; }

    /// <summary>Anmeldename. Klein geschrieben abgelegt, damit der Vergleich eindeutig bleibt.</summary>
    public required string Email { get; set; }

    /// <summary>Hash im Format von <c>PasswordHasher</c> (PBKDF2). Nie das Passwort selbst.</summary>
    public required string PasswordHash { get; set; }

    public HouseholdRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    /// <summary>Zweiter Faktor. Noch nicht umgesetzt; die Oberfläche weist darauf hin.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Zeitpunkt, ab dem wieder ein Anmeldeversuch erlaubt ist.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>Fehlversuche seit der letzten erfolgreichen Anmeldung.</summary>
    public int FailedAttempts { get; set; }

    public List<UserSession> Sessions { get; set; } = [];
}

/// <summary>
/// Eine angemeldete Sitzung. Das Anmelde-Cookie trägt nur die Id — damit lässt sich eine Sitzung
/// serverseitig widerrufen, was „Angemeldet bleiben“ erst vertretbar macht.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>Grobe Gerätekennung aus dem User-Agent, für die Sitzungsübersicht.</summary>
    public string? Device { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

/// <summary>Einladungscode, mit dem ein neuer Benutzer einem Haushalt beitritt.</summary>
public class Invitation
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public Household? Household { get; set; }

    /// <summary>Format <c>HH-XXXX-XXXX</c>, ohne leicht verwechselbare Zeichen.</summary>
    public required string Code { get; set; }

    /// <summary>Rolle, die der Beitretende bekommt.</summary>
    public HouseholdRole Role { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public int? RedeemedByUserId { get; set; }

    public bool IsOpen(DateTime now) => RedeemedAt is null && ExpiresAt > now;
}

/// <summary>
/// Anforderung zum Zurücksetzen des Passworts. Abgelegt wird nur der Hash des Tokens — wer die
/// Datenbank liest, kann damit kein Passwort zurücksetzen.
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public required string TokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
