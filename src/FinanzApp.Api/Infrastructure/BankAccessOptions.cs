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

    /// <summary>
    /// Die längste Registrierungsnummer, die in eine Dialogeröffnung passt.
    /// </summary>
    /// <remarks>
    /// Das Feld „Produktbezeichnung“ in FinTS fasst 25 Zeichen. Was länger ist, weist die Bank
    /// ab — und zwar mit einer Meldung über ein ungültiges Segment, die niemand mit einer zu
    /// langen Nummer in Verbindung bringt.
    /// </remarks>
    public const int ProductIdMaxLength = 25;
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
        else if (options.ProductId.Trim().Length > BankAccessOptions.ProductIdMaxLength
                 || options.ProductId.Any(char.IsWhiteSpace))
        {
            // Zwei Fehler, eine Meldung: Beide enden bei der Bank als abgewiesenes Segment, und
            // beide sieht man der Nummer nicht an. Gemeldet wird die Länge, nicht die Nummer —
            // sie ist zwar kein Geheimnis, gehört aber niemandem vor die Augen, der sie nicht
            // gerade einträgt.
            probleme.Add(
                $"{BankAccessOptions.SectionName}:ProductId ist "
                + $"{options.ProductId.Trim().Length} Zeichen lang und enthält "
                + (options.ProductId.Any(char.IsWhiteSpace) ? "Leerzeichen" : "keine Leerzeichen")
                + $". Erlaubt sind höchstens {BankAccessOptions.ProductIdMaxLength} Zeichen ohne "
                + "Leerzeichen; die Registrierungsnummer der Deutschen Kreditwirtschaft hat "
                + "genau diese Länge.");
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
        else if (account.Blz.Trim() is not { Length: 8 } blz || !blz.All(char.IsAsciiDigit))
        {
            // Beim Abtippen entsteht das leicht: eine Ziffer zu viel, ein Leerzeichen, ein
            // Punkt. Geprüft wird es hier, weil die Bankleitzahl weiter unten als Zahl
            // gebraucht wird — eine elfstellige passt in keine, und der Abruf bräche mit einer
            // Meldung ab, die von der Bankleitzahl kein Wort sagte.
            probleme.Add(
                $"Die Bankleitzahl am Konto „{account.Name}“ hat "
                + $"{account.Blz.Trim().Length} Zeichen. Eine deutsche Bankleitzahl hat genau "
                + "acht Ziffern — sie steht in den ersten acht Stellen der IBAN nach der "
                + "Prüfziffer.");
        }

        // Die BIC steht hier bei der Bankleitzahl und nicht bei den Zugangsdaten: Sie ist
        // keine Kennung des Benutzers, sondern gehört zum Konto. Gebraucht wird sie erst beim
        // Abruf — die Saldenabfrage benennt das Konto als IBAN und BIC. Ohne sie weist die
        // Bank die Nachricht mit einem fehlenden Mussfeld ab und sagt nicht, welchem.
        if (string.IsNullOrWhiteSpace(account.Bic))
        {
            probleme.Add(
                $"Dem Konto „{account.Name}“ fehlt die BIC. Sie steht auf jedem Kontoauszug — "
                + "die Saldenabfrage benennt das Konto mit IBAN und BIC.");
        }
        else if (account.Bic.Trim() is not { Length: 8 or 11 } bic
                 || !bic.All(char.IsAsciiLetterOrDigit))
        {
            // Dieselbe Falle wie bei der Bankleitzahl, und sie ist zugeschnappt: Am echten
            // Konto stand eine BIC mit zwölf Zeichen — ein Buchstabe zu viel beim Abtippen.
            // Die Bank wies den Abruf daraufhin mit „Ungültige Auftragsnachricht: Unbekannter
            // Aufbau“ ab, einem Satz, der von der BIC kein Wort sagt. Das Feld in der
            // FinTS-Nachricht fasst elf Zeichen; ein zwölftes sprengt den Aufbau.
            //
            // Nach ISO 9362 hat eine BIC acht Zeichen oder elf — vier für das Institut, zwei
            // fürs Land, zwei für den Ort, und wahlweise drei für die Filiale. Neun, zehn oder
            // zwölf gibt es nicht.
            probleme.Add(
                $"Die BIC am Konto „{account.Name}“ hat {account.Bic.Trim().Length} Zeichen. "
                + "Eine BIC hat acht oder elf — die ersten acht bezeichnen Institut, Land und "
                + "Ort, die drei weiteren die Filiale. Sie steht auf jedem Kontoauszug.");
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
