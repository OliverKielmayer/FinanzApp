using FinanzApp.Api.Data.Entities;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Was das Programm der Bank über sich selbst sagt.
/// </summary>
/// <remarks>
/// <para>Nur zwei Felder, und beide bezeichnen die <em>Anwendung</em>: Jedes Programm, das FinTS
/// spricht, braucht eine eigene Registrierungsnummer der Deutschen Kreditwirtschaft. Sie ist für
/// alle Konten dieselbe und kein Geheimnis — sie sagt, welche Software anklopft, nicht wer.</para>
/// <para><b>Der Zugang zu einer Bank steht nicht hier, sondern am Konto.</b> Bank und IBAN stehen
/// dort ohnehin, und ein Zugang beschreibt genau dieses Konto bei genau dieser Bank. Eine zweite
/// Liste in der Konfigurationsdatei pflegte dieselbe Beziehung ein zweites Mal — und zwei Orte,
/// die dasselbe sagen sollen, laufen auseinander.</para>
/// <para><b>Die PIN steht nirgends.</b> Sie wird beim Abruf eingegeben und lebt nur für den
/// Dialog.</para>
/// </remarks>
public sealed class BankAccessOptions
{
    public const string SectionName = "Bankzugang";

    /// <summary>Die Produkt-Registrierungsnummer der Deutschen Kreditwirtschaft.</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Die Version, die das Programm der Bank nennt. Selbst vergeben.</summary>
    public string ProductVersion { get; set; } = "1.0";

    /// <summary>Ob die Anwendung sich gegenüber einer Bank ausweisen kann.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProductId);
}

/// <summary>
/// Ob ein Konto abgefragt werden kann — und was sonst noch fehlt.
/// </summary>
/// <remarks>
/// Geprüft wird erst, wenn jemand abruft, und nicht beim Start. Ein Konto ohne Bankzugang ist
/// kein Mangel: Die Anwendung liest Kontoauszüge als Datei und tat das immer. Gemeldet wird
/// deshalb nur dem, der gerade etwas davon will.
/// </remarks>
public static class BankAccessReadiness
{
    /// <summary>
    /// Was dem Abruf dieses Kontos noch fehlt — jedes Feld einzeln benannt.
    /// </summary>
    /// <remarks>
    /// Einzeln und bei seinem Namen, weil die Meldung beim Einrichten gelesen wird. „Der
    /// Bankzugang ist unvollständig“ sagt niemandem, wo er suchen soll.
    /// </remarks>
    public static IReadOnlyList<string> Problems(Account account, BankAccessOptions options)
    {
        List<string> probleme = [];

        if (!options.IsConfigured)
        {
            probleme.Add(
                $"{BankAccessOptions.SectionName}:ProductId fehlt — die "
                + "Produkt-Registrierungsnummer der Deutschen Kreditwirtschaft. Sie steht in "
                + "appsettings.json und gilt für alle Konten. Ohne sie weist die Bank den "
                + "Dialog ab.");
        }

        if (!account.HasBankAccess)
        {
            probleme.Add(
                $"Am Konto „{account.Name}“ ist kein Bankzugang hinterlegt. Bankleitzahl, "
                + "FinTS-Adresse, Benutzerkennung und TAN-Verfahren stehen im "
                + "Zugangsdatenblatt der Bank und werden am Konto eingetragen.");

            return probleme;
        }

        if (string.IsNullOrWhiteSpace(account.Blz))
        {
            probleme.Add($"Dem Konto „{account.Name}“ fehlt die Bankleitzahl.");
        }

        if (string.IsNullOrWhiteSpace(account.FinTsUrl))
        {
            probleme.Add($"Dem Konto „{account.Name}“ fehlt die FinTS-Adresse der Bank.");
        }

        if (string.IsNullOrWhiteSpace(account.FinTsUserId))
        {
            probleme.Add($"Dem Konto „{account.Name}“ fehlt die Benutzerkennung.");
        }

        if (account.FinTsTanMethod is not > 0)
        {
            probleme.Add(
                $"Dem Konto „{account.Name}“ fehlt das TAN-Verfahren — die Nummer der Bank, "
                + "etwa 921 für pushTAN.");
        }

        // Die IBAN war bisher freiwillig. Für den Abruf ist sie es nicht: An ihr erkennt die
        // Bank, welches ihrer Konten gemeint ist.
        if (string.IsNullOrWhiteSpace(account.Iban))
        {
            probleme.Add(
                $"Dem Konto „{account.Name}“ fehlt die IBAN. Ohne sie lässt sich nicht sagen, "
                + "welches Konto bei der Bank gemeint ist.");
        }

        return probleme;
    }

    /// <summary>Ob dieses Konto abgefragt werden kann.</summary>
    public static bool CanQuery(Account account, BankAccessOptions options)
        => Problems(account, options).Count == 0;
}
