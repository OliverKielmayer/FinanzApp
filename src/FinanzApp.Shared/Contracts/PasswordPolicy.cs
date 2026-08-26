namespace FinanzApp.Shared.Contracts;

/// <summary>Stufen der Stärkeanzeige unter dem Passwortfeld.</summary>
public enum PasswordStrength
{
    None = 0,
    TooWeak = 1,
    Weak = 2,
    Good = 3,
    Strong = 4,
}

/// <summary>
/// Bewertung der Passwortstärke. Client und Server benutzen dieselbe Bewertung — die Anzeige
/// unter dem Feld darf nicht „Gut“ sagen, wenn der Server das Passwort danach ablehnt.
/// </summary>
/// <remarks>
/// Die Bewertung stützt sich auf Länge, Zeichenvielfalt und ein paar offensichtliche
/// Schwachstellen. Für eine belastbarere Schätzung ist eine Bibliothek wie zxcvbn gedacht;
/// sie würde hier eintreten, ohne dass sich der Vertrag ändert.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Mindestlänge laut Platzhalter im Registrierungsformular.</summary>
    public const int MinimumLength = 12;

    /// <summary>Ab dieser Stufe nimmt der Server ein Passwort an.</summary>
    public const PasswordStrength Required = PasswordStrength.Good;

    /// <summary>
    /// Bekannte Passwörter. Sie werden als Teilzeichenkette gesucht, aber nicht als Verbot
    /// behandelt: macht das Wort den größten Teil aus, ist es das Passwort und wird abgelehnt;
    /// steckt es nur darin, kostet es eine Stufe.
    /// </summary>
    private static readonly string[] CommonPasswords =
    [
        "passwort", "password", "123456", "12345678", "123456789", "qwertz", "qwerty",
        "hallo", "willkommen", "letmein", "iloveyou", "admin", "finanzapp", "geheim",
    ];

    public static PasswordStrength Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PasswordStrength.None;
        }

        var normalized = password.ToLowerInvariant();
        var common = CommonPasswords
            .Where(word => normalized.Contains(word, StringComparison.Ordinal))
            .OrderByDescending(word => word.Length)
            .FirstOrDefault();

        // Ein bekanntes Wort ist das Passwort selbst, wenn es den größten Teil ausmacht —
        // dann hilft auch eine angehängte Ziffer nicht.
        if (common is not null && common.Length * 2 >= password.Length)
        {
            return PasswordStrength.TooWeak;
        }

        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= MinimumLength) score++;
        if (password.Length >= 16) score++;

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        if (classes >= 2) score++;
        if (classes >= 3) score++;

        if (HasLongRun(password)) score--;
        if (password.Distinct().Count() <= 4) score--;

        // Steckt ein bekanntes Wort nur darin, kostet das eine Stufe — es verbietet das Passwort
        // aber nicht. „Neues-Passwort-2026!“ ist kein schwaches Passwort, bloß weil das Wort
        // darin vorkommt.
        if (common is not null) score--;

        return (PasswordStrength)Math.Clamp(score, (int)PasswordStrength.TooWeak, (int)PasswordStrength.Strong);
    }

    public static bool IsAcceptable(string? password)
        => password is { Length: >= MinimumLength } && Evaluate(password) >= Required;

    /// <summary>
    /// Warum ein Passwort abgelehnt wurde — <c>null</c>, wenn es angenommen wird.
    /// </summary>
    /// <remarks>
    /// Die beiden Gründe werden getrennt genannt. „Zu schwach oder kürzer als 12 Zeichen“ lässt
    /// den Benutzer raten, welches von beidem gemeint ist — und wenn keines zutrifft, sucht er an
    /// der falschen Stelle.
    /// </remarks>
    public static string? Reject(string? password) => password switch
    {
        null or "" => "Bitte ein Passwort eingeben.",
        { Length: var length } when length < MinimumLength
            => $"Das Passwort ist {length} Zeichen lang — nötig sind mindestens {MinimumLength}.",
        _ when Evaluate(password) < Required
            => "Das Passwort ist zu schwach. Mehr Länge oder mehr verschiedene Zeichenarten helfen.",
        _ => null,
    };

    /// <summary>Text unter der Balkenreihe.</summary>
    public static string Describe(PasswordStrength strength) => strength switch
    {
        PasswordStrength.None => "Noch kein Passwort",
        PasswordStrength.TooWeak => "Zu schwach",
        PasswordStrength.Weak => "Schwach",
        PasswordStrength.Good => "Gut",
        PasswordStrength.Strong => "Stark",
        _ => string.Empty,
    };

    /// <summary>Vier oder mehr gleiche oder fortlaufende Zeichen — „aaaa“, „1234“, „wxyz“.</summary>
    private static bool HasLongRun(string password)
    {
        var same = 1;
        var ascending = 1;

        for (var i = 1; i < password.Length; i++)
        {
            same = password[i] == password[i - 1] ? same + 1 : 1;
            ascending = password[i] == password[i - 1] + 1 ? ascending + 1 : 1;

            if (same >= 4 || ascending >= 4)
            {
                return true;
            }
        }

        return false;
    }
}
