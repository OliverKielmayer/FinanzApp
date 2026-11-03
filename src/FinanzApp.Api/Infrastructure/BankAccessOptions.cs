namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Der Zugang zu einer Bank über FinTS.
/// </summary>
/// <remarks>
/// <para>Was eine Bank zum Anmelden verlangt, steht in ihrem FinTS-Zugangsdatenblatt und ist
/// bei jeder anders beschriftet. Die Felder hier tragen die Namen des Standards, nicht die der
/// jeweiligen Bank — sonst müsste man für jedes Institut raten, welches Feld gemeint ist.</para>
/// <para><b>Die PIN gehört nicht in <c>appsettings.json</c>.</b> Sie steht wie jedes andere
/// Geheimnis des Projekts in <c>dotnet user-secrets</c> oder in einer Umgebungsvariablen —
/// <c>Bankzugang__Banken__0__Pin</c> für die erste Bank der Liste. Eine Datei im Repository ist
/// der falsche Ort für etwas, womit sich ein Konto leerräumen lässt.</para>
/// </remarks>
public sealed class BankLoginOptions
{
    /// <summary>Wie die Bank in Meldungen heißt, etwa „Sparkasse“. Nur zum Lesen.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Die Bankleitzahl. FinTS spricht Banken darüber an, nicht über die BIC.</summary>
    public string Blz { get; set; } = string.Empty;

    /// <summary>
    /// Der PIN/TAN-Endpunkt der Bank, etwa <c>https://banking.example.de/fints30</c>.
    /// </summary>
    /// <remarks>
    /// Steht im Zugangsdatenblatt. Die Deutsche Kreditwirtschaft führt zu jeder Bankleitzahl ein
    /// Verzeichnis; eine falsche Adresse scheitert beim Dialogaufbau und nicht erst beim Abruf.
    /// </remarks>
    public string Url { get; set; } = string.Empty;

    /// <summary>Die Benutzerkennung — der Anmeldename des Online-Bankings.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Die Kunden-ID. Leer heißt: dieselbe wie die Benutzerkennung.
    /// </summary>
    /// <remarks>
    /// Bei den meisten Banken sind beide gleich, bei einigen nicht. Leer zu lassen ist deshalb
    /// die richtige Vorgabe — und nicht, die Kennung zu verdoppeln: Wer sie später ändert,
    /// änderte sonst nur eine von beiden.
    /// </remarks>
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>
    /// Das TAN-Verfahren, als Nummer der Bank — etwa 921 für pushTAN.
    /// </summary>
    /// <remarks>
    /// Auch eine reine Saldenabfrage wählt beim Dialogaufbau eines aus; sie löst nur keine TAN
    /// aus. Welche Nummern eine Bank anbietet, meldet sie selbst — bis der Abruf gebaut ist,
    /// steht die Nummer im Zugangsdatenblatt.
    /// </remarks>
    public int TanMethod { get; set; }

    /// <summary>
    /// Die PIN. Gehört in user-secrets oder eine Umgebungsvariable, nie in die Datei.
    /// </summary>
    public string Pin { get; set; } = string.Empty;

    /// <summary>
    /// Welche Konten über diesen Zugang abgefragt werden, nach IBAN.
    /// </summary>
    /// <remarks>
    /// Die IBAN ist der Bezug zum Konto in der Anwendung. Nicht die Kontonummer: Die steht in
    /// der Anwendung nirgends, und zwei Banken können dieselbe vergeben.
    /// </remarks>
    public string[] Ibans { get; set; } = [];

    /// <summary>Die Kunden-ID oder, wenn keine eingetragen ist, die Benutzerkennung.</summary>
    public string EffectiveCustomerId
        => string.IsNullOrWhiteSpace(CustomerId) ? UserId : CustomerId;

    /// <summary>Ob zu diesem Eintrag überhaupt etwas eingetragen wurde.</summary>
    public bool IsEmpty
        => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Blz)
           && string.IsNullOrWhiteSpace(Url) && string.IsNullOrWhiteSpace(UserId)
           && Ibans.Length == 0;
}

/// <summary>
/// Die Einstellungen für den Bankabruf über FinTS.
/// </summary>
/// <remarks>
/// <para><b>Nicht eingerichtet ist ein gültiger Zustand.</b> Die Anwendung lief jahrelang ohne
/// Bankabruf und tut es weiter: Kontoauszüge lassen sich als Datei einlesen. Ein leerer
/// Abschnitt ist deshalb kein Fehler und wird nicht gemeldet.</para>
/// <para><b>Halb eingerichtet ist einer.</b> Wer anfängt, etwas einzutragen, will den Abruf —
/// und ein fehlendes Feld, das niemand nennt, sucht man am falschen Ende. Deshalb nennt
/// <see cref="Problems"/> jedes einzeln, sobald überhaupt etwas dasteht.</para>
/// </remarks>
public sealed class BankAccessOptions
{
    public const string SectionName = "Bankzugang";

    /// <summary>
    /// Die Produkt-Registrierungsnummer der Deutschen Kreditwirtschaft.
    /// </summary>
    /// <remarks>
    /// Jede Anwendung, die FinTS spricht, braucht eine eigene. Sie ist kein Geheimnis — sie
    /// bezeichnet das Programm, nicht den Kunden — und darf deshalb in der Datei stehen.
    /// </remarks>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Die Version, die das Programm der Bank nennt. Selbst vergeben.</summary>
    public string ProductVersion { get; set; } = "1.0";

    /// <summary>Die Zugänge, einer je Bank.</summary>
    public BankLoginOptions[] Banken { get; set; } = [];

    /// <summary>Die Zugänge, bei denen überhaupt etwas eingetragen ist.</summary>
    public IReadOnlyList<BankLoginOptions> Konfigurierte
        => [.. Banken.Where(b => !b.IsEmpty)];

    /// <summary>Ob jemand angefangen hat, den Abruf einzurichten.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(ProductId) || Konfigurierte.Count > 0;

    /// <summary>
    /// Was zum Abruf noch fehlt — jedes Feld einzeln benannt.
    /// </summary>
    /// <remarks>
    /// Einzeln und mit dem vollen Schlüssel, weil die Meldung beim Einrichten gelesen wird und
    /// nicht beim Entwickeln. „Der Bankzugang ist unvollständig“ sagt niemandem, wo er suchen
    /// soll; <c>Bankzugang:Banken:0:Url</c> schon.
    /// </remarks>
    public IReadOnlyList<string> Problems()
    {
        List<string> probleme = [];

        if (!IsConfigured)
        {
            return probleme;
        }

        if (string.IsNullOrWhiteSpace(ProductId))
        {
            probleme.Add(
                $"{SectionName}:ProductId ist leer — die Produkt-Registrierungsnummer der "
                + "Deutschen Kreditwirtschaft. Ohne sie weist die Bank den Dialog ab.");
        }

        for (var i = 0; i < Banken.Length; i++)
        {
            var bank = Banken[i];
            if (bank.IsEmpty)
            {
                continue;
            }

            var pfad = $"{SectionName}:Banken:{i}";
            var wer = string.IsNullOrWhiteSpace(bank.Name) ? pfad : $"„{bank.Name}“";

            if (string.IsNullOrWhiteSpace(bank.Blz))
            {
                probleme.Add($"{pfad}:Blz ist leer — die Bankleitzahl von {wer}.");
            }

            if (string.IsNullOrWhiteSpace(bank.Url))
            {
                probleme.Add($"{pfad}:Url ist leer — der PIN/TAN-Endpunkt von {wer}.");
            }

            if (string.IsNullOrWhiteSpace(bank.UserId))
            {
                probleme.Add($"{pfad}:UserId ist leer — die Benutzerkennung für {wer}.");
            }

            // Eigens benannt: Sie steht nicht in der Datei, und wer sie dort sucht, sucht
            // vergeblich. Die Meldung sagt deshalb gleich, wo sie hingehört.
            if (string.IsNullOrWhiteSpace(bank.Pin))
            {
                probleme.Add(
                    $"{pfad}:Pin ist leer — sie gehört nicht in appsettings.json, sondern in "
                    + $"dotnet user-secrets oder die Umgebungsvariable "
                    + $"{SectionName}__Banken__{i}__Pin.");
            }

            if (bank.TanMethod <= 0)
            {
                probleme.Add(
                    $"{pfad}:TanMethod ist nicht gesetzt — die Nummer des TAN-Verfahrens, "
                    + $"etwa 921 für pushTAN. Steht im Zugangsdatenblatt von {wer}.");
            }

            if (bank.Ibans.Length == 0)
            {
                probleme.Add(
                    $"{pfad}:Ibans ist leer — welche Konten über {wer} abgefragt werden sollen.");
            }
        }

        return probleme;
    }
}
