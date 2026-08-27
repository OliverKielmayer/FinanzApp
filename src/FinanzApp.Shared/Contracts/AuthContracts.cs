namespace FinanzApp.Shared.Contracts;

/// <summary>Rolle eines Benutzers innerhalb seines Haushalts.</summary>
public enum HouseholdRole
{
    /// <summary>Verwaltet Benutzer und Einladungen, voller Schreibzugriff.</summary>
    Owner = 0,

    /// <summary>Voller Schreibzugriff auf die Daten des Haushalts, keine Benutzerverwaltung.</summary>
    Member = 1,

    /// <summary>Sieht alles, ändert nichts — gedacht für das Steuerbüro.</summary>
    ReadOnly = 2,
}

/// <summary>Ob ein neuer Benutzer einem Haushalt beitritt oder einen eigenen anlegt.</summary>
public enum HouseholdMode
{
    Join = 0,
    Create = 1,
}

/// <summary>Der angemeldete Benutzer, so wie ihn der Client kennt.</summary>
public sealed record CurrentUserDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required HouseholdRole Role { get; init; }
    public required int HouseholdId { get; init; }
    public required string HouseholdName { get; init; }

    /// <summary>Beginn der laufenden Sitzung.</summary>
    public required DateTime SignedInAt { get; init; }

    /// <summary>Zweiter Faktor. Noch nicht umgesetzt, die Oberfläche weist darauf hin.</summary>
    public bool TwoFactorEnabled { get; init; }

    /// <summary>Darf schreibend arbeiten — Buchungen erfassen, kategorisieren, importieren.</summary>
    public bool CanWrite => Role is HouseholdRole.Owner or HouseholdRole.Member;

    /// <summary>Darf Benutzer und Einladungen verwalten.</summary>
    public bool CanManageUsers => Role is HouseholdRole.Owner;
}

public sealed record LoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }

    /// <summary>Hält die Sitzung über das Schließen des Browsers hinaus.</summary>
    public bool StaySignedIn { get; init; }
}

public sealed record RegisterRequest
{
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required HouseholdMode HouseholdMode { get; init; }

    /// <summary>Bei <see cref="HouseholdMode.Join"/> nötig.</summary>
    public string? InviteCode { get; init; }

    /// <summary>Bei <see cref="HouseholdMode.Create"/> nötig.</summary>
    public string? HouseholdName { get; init; }
}

public sealed record PasswordResetStartRequest
{
    public required string Email { get; init; }
}

/// <summary>
/// Passwort ändern, wenn man angemeldet ist. Das bisherige ist Pflicht — sonst könnte, wer einen
/// unbeaufsichtigten Bildschirm findet, das Konto übernehmen, ohne es je gekannt zu haben.
/// </summary>
public sealed record ChangePasswordRequest
{
    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }
}

public sealed record PasswordResetRedeemRequest
{
    public required string Token { get; init; }
    public required string NewPassword { get; init; }
}

/// <summary>Inhalt des Screens „Benutzer &amp; Anmeldung“.</summary>
public sealed record HouseholdOverviewDto
{
    public required string HouseholdName { get; init; }
    public required IReadOnlyList<HouseholdMemberDto> Members { get; init; }

    /// <summary>Offene Einladung. <c>null</c>, wenn keine gültige vorliegt oder der
    /// angemeldete Benutzer sie nicht sehen darf.</summary>
    public InvitationDto? Invitation { get; init; }

    public required SessionInfoDto Session { get; init; }
}

public sealed record HouseholdMemberDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required HouseholdRole Role { get; init; }
    public DateTime? LastSeenAt { get; init; }

    /// <summary>Wie viele Konten des Haushalts dieses Mitglied sehen darf.</summary>
    public int VisibleAccountCount { get; init; }

    /// <summary>Wie viele es insgesamt gibt — der Bezug, ohne den die erste Zahl nichts sagt.</summary>
    public int TotalAccountCount { get; init; }
}

public sealed record InvitationDto
{
    public required string Code { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

public sealed record SessionInfoDto
{
    public required string UserName { get; init; }
    public required DateTime SignedInAt { get; init; }
}
