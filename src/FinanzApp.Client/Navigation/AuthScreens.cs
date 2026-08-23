namespace FinanzApp.Client.Navigation;

/// <summary>
/// Die Screens vor der Anmeldung. Sie liegen bewusst außerhalb von <see cref="ScreenCatalog"/>:
/// ohne Sitzung gibt es weder Kopfzeile noch Tab-Bar, in die sie sich einreihen könnten.
/// </summary>
public static class AuthScreens
{
    public const string Login = "/anmelden";
    public const string Register = "/registrieren";
    public const string ForgotPassword = "/passwort-vergessen";
    public const string ResetPassword = "/passwort-zuruecksetzen";

    /// <summary>Erklärzeile unter der Wortmarke, je nach Modus.</summary>
    public static string IntroFor(string relativePath)
    {
        var path = "/" + relativePath.Split('?')[0].Split('#')[0].Trim('/');

        return path switch
        {
            Register => "Neuen Benutzer anlegen und einem Haushalt zuordnen.",
            ForgotPassword or ResetPassword => "Passwort zurücksetzen.",
            _ => "Anmelden — jeder Benutzer hat eigene Zugangsdaten.",
        };
    }
}
