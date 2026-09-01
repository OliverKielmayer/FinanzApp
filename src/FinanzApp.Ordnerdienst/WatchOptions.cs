namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Die Einstellungen des Ordnerdienstes.
/// </summary>
/// <remarks>
/// <para>Ein Ordner, eine Adresse, ein Zugang — mehr braucht der Dienst nicht. Alles andere hat
/// eine Vorgabe, die auf einem frisch aufgesetzten Rechner funktioniert.</para>
/// <para>Das Passwort gehört <b>nicht</b> in <c>appsettings.json</c>. Der Dienst liest die
/// üblichen Konfigurationsquellen; im Betrieb ist die Umgebungsvariable
/// <c>Ordnerdienst__Password</c> des Dienstkontos der Weg, in der Entwicklung
/// <c>dotnet user-secrets</c>.</para>
/// </remarks>
public sealed class WatchOptions
{
    public const string SectionName = "Ordnerdienst";

    /// <summary>Der überwachte Ordner. Ohne ihn startet der Dienst nicht.</summary>
    public string WatchFolder { get; set; } = string.Empty;

    /// <summary>
    /// Wohin eine übergebene Datei wandert. Leer heißt <c>_erledigt</c> im überwachten Ordner.
    /// </summary>
    /// <remarks>
    /// Verschieben statt löschen, und verschieben statt liegenlassen: gelöscht wäre das Original
    /// weg, obwohl der Dienst es nur weitergereicht hat, und liegengelassen bekäme es der Server
    /// bei jedem Durchgang erneut. Der Unterordner darf im überwachten Ordner liegen — überwacht
    /// wird nur dessen oberste Ebene.
    /// </remarks>
    public string DoneFolder { get; set; } = string.Empty;

    /// <summary>
    /// Wohin eine Datei wandert, die der Server dauerhaft ablehnt. Leer heißt
    /// <c>_fehlgeschlagen</c> im überwachten Ordner.
    /// </summary>
    public string FailedFolder { get; set; } = string.Empty;

    /// <summary>Adresse der FinanzApp, etwa <c>https://finanzapp.example.net/</c>.</summary>
    public string BaseAddress { get; set; } = "http://localhost:5111/";

    /// <summary>Zugang des Dienstes. Ein eigener Benutzer mit der Rolle Mitglied.</summary>
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Welche Dateiarten überhaupt angeboten werden. Leer heißt: alle, der Server entscheidet.
    /// </summary>
    /// <remarks>
    /// <para>Sollte zu <c>Documents:AllowedExtensions</c> der API passen. Was hier steht und dort
    /// nicht, wird abgelehnt und landet in <see cref="FailedFolder"/>; was hier fehlt, bleibt im
    /// überwachten Ordner liegen und fällt dort auf. Der Filter ist trotzdem sinnvoll: ein
    /// Scanner legt seine halbfertigen Dateien gern als <c>.tmp</c> ab.</para>
    /// <para>Die Vorgabe ist bewusst <em>leer</em> und die Liste steht in
    /// <c>appsettings.json</c>: die Konfigurationsbindung <b>ergänzt</b> Feldwerte, sie ersetzt
    /// sie nicht. Stünde hier eine Vorgabe, käme eine eingestellte Liste zu ihr hinzu — und wer
    /// den Dienst auf <c>.pdf</c> einschränken wollte, bekäme trotzdem alles.</para>
    /// </remarks>
    public string[] Extensions { get; set; } = [];

    /// <summary>
    /// Höchstgröße einer angebotenen Datei in Megabyte. 0 schaltet die Prüfung ab.
    /// </summary>
    /// <remarks>
    /// <para>Sollte zu <c>Documents:MaxFileSizeMegabytes</c> der API passen. Der Dienst prüft
    /// selbst, weil er sonst die ganze Datei hochlädt, um am Ende zu erfahren, dass sie zu groß
    /// ist — bei einem 300-MB-Scan aus dem Einzugsscanner fünfmal hintereinander.</para>
    /// <para>Und weil der Abbruch <em>während</em> des Sendens nicht wie eine Ablehnung aussieht,
    /// sondern wie ein Verbindungsfehler: die Anfrage stirbt mitten im Rumpf. Ohne diese Prüfung
    /// hielte eine einzige zu große Datei den ganzen Eingang auf.</para>
    /// </remarks>
    public int MaxMegabytes { get; set; } = 25;

    /// <summary>
    /// Abstand zweier Durchgänge in Sekunden.
    /// </summary>
    /// <remarks>
    /// Der Dienst wartet nicht darauf: die Ordnerüberwachung weckt ihn sofort, wenn etwas
    /// hereinkommt. Der Takt ist die Nachlese — Ereignisse gehen verloren, wenn der Puffer
    /// überläuft, und auf Netzlaufwerken kommen sie gar nicht erst an.
    /// </remarks>
    public int SweepSeconds { get; set; } = 60;

    /// <summary>
    /// Wie lange eine Datei unverändert sein muss, bevor sie als fertig gilt.
    /// </summary>
    /// <remarks>
    /// Ein Scanner meldet die Datei, sobald er sie anlegt, und schreibt danach weiter. Ohne
    /// diese Wartezeit ginge die erste Seite als vollständiges Dokument hinaus.
    /// </remarks>
    public int SettleSeconds { get; set; } = 5;

    /// <summary>
    /// Wie oft ein Übergabeversuch scheitern darf, bevor die Datei beiseitegelegt wird.
    /// </summary>
    /// <remarks>
    /// Gezählt werden nur vorübergehende Fehler. Eine abgelehnte Datei wandert sofort beiseite,
    /// und ein Server, der nicht antwortet, kostet keinen Versuch — sonst wäre nach einem
    /// Wochenende Wartungsarbeit der ganze Eingang „fehlgeschlagen“.
    /// </remarks>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Der Ordner für erledigte Dateien, mit eingesetzter Vorgabe.</summary>
    public string ResolvedDoneFolder => Fallback(DoneFolder, "_erledigt");

    /// <summary>Der Ordner für abgelehnte Dateien, mit eingesetzter Vorgabe.</summary>
    public string ResolvedFailedFolder => Fallback(FailedFolder, "_fehlgeschlagen");

    /// <summary>
    /// Was an den Einstellungen fehlt — leer, wenn der Dienst starten kann.
    /// </summary>
    /// <remarks>
    /// Beim Start geprüft und nicht beim ersten Beleg: ein Dienst, der stillschweigend läuft und
    /// nichts tut, ist schlimmer als einer, der sich weigert und sagt, warum.
    /// </remarks>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(WatchFolder))
        {
            problems.Add($"{SectionName}:WatchFolder ist leer — welcher Ordner überwacht werden soll.");
        }

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            problems.Add(
                $"{SectionName}:Email und {SectionName}:Password fehlen — der Dienst braucht einen "
                + "eigenen Zugang mit Schreibrecht.");
        }

        if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out _))
        {
            problems.Add($"{SectionName}:BaseAddress ist keine gültige Adresse: „{BaseAddress}“.");
        }

        if (SettleSeconds < 0)
        {
            problems.Add($"{SectionName}:SettleSeconds darf nicht negativ sein.");
        }

        if (SweepSeconds < 1)
        {
            problems.Add($"{SectionName}:SweepSeconds muss mindestens 1 sein.");
        }

        if (MaxAttempts < 1)
        {
            problems.Add($"{SectionName}:MaxAttempts muss mindestens 1 sein.");
        }

        if (MaxMegabytes < 0)
        {
            problems.Add($"{SectionName}:MaxMegabytes darf nicht negativ sein.");
        }

        return problems;
    }

    /// <summary>Ob die Erweiterung angeboten wird. Leere Liste heißt: alle.</summary>
    public bool Accepts(string fileName)
    {
        if (Extensions.Length == 0)
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return extension.Length > 0
               && Extensions.Any(e => string.Equals(
                   e.StartsWith('.') ? e : "." + e, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Ob die Datei klein genug ist, um überhaupt angeboten zu werden.</summary>
    public bool IsSmallEnough(long bytes) => MaxMegabytes <= 0 || bytes <= MaxMegabytes * 1024L * 1024L;

    private string Fallback(string configured, string name)
        => string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(WatchFolder, name)
            : configured;
}
