namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Die Einstellungen des Postfachs — die zweite Quelle desselben Eingangs.
/// </summary>
/// <remarks>
/// <para><b>Warum im Ordnerdienst und nicht als eigener Dienst.</b> Was er tut, ist bei beiden
/// Quellen dasselbe: nachsehen, was Neues da ist, es der Anwendung geben, es wegräumen und
/// nichts zweimal hinauslassen. Anmeldung, Wiederholung und Protokoll stehen hier schon. Ein
/// zweiter Dienst hieße: ein zweiter Zugang, ein zweites Kennwort im Dienstkonto, eine zweite
/// Stelle, an der man nachsieht, warum nichts ankommt.</para>
/// <para><b>Nichts geschieht ohne <see cref="Enabled"/>.</b> Ohne diesen Schalter sieht der
/// Dienst kein Postfach an, und wer ihn nur für den Ordner benutzt, merkt von alldem nichts.</para>
/// <para>Das Kennwort gehört <b>nicht</b> in <c>appsettings.json</c>, so wenig wie das des
/// Ordnerdienstes: im Betrieb in die Umgebungsvariable <c>Postfach__Password</c> des
/// Dienstkontos, in der Entwicklung in <c>dotnet user-secrets</c>.</para>
/// </remarks>
public sealed class MailboxOptions
{
    public const string SectionName = "Postfach";

    /// <summary>Ob das Postfach überhaupt angesehen wird.</summary>
    public bool Enabled { get; set; }

    /// <summary>Der IMAP-Server des Postfachs, etwa <c>imap.example.net</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>993 ist der verschlüsselte Zugang und die Vorgabe.</summary>
    public int Port { get; set; } = 993;

    /// <summary>
    /// Verschlüsselt verbinden. Bleibt <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Steht als Schalter da, weil ein Postfach im eigenen Netz ohne Verschlüsselung laufen
    /// kann — nicht, weil es eine vertretbare Wahl für ein Postfach im Internet wäre. Über die
    /// Leitung geht die Anmeldung und danach jede Mail im Klartext.
    /// </remarks>
    public bool UseSsl { get; set; } = true;

    /// <summary>Der Benutzername am Postfach.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Das Kennwort des Postfachs. Gehört nicht in die versionierte Datei.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Der Ordner im Postfach, der angesehen wird. Vorgabe: <c>FinanzApp</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Mit Absicht nicht der Posteingang.</b> Ein Dienst, der den Posteingang liest,
    /// bekommt jede Werbung, jede Benachrichtigung und jede private Nachricht zu sehen und legt
    /// ihre Anhänge in der Ablage ab. Ein eigener Ordner dreht die Richtung um: Er sieht nur,
    /// was jemand hineinschiebt — dieselbe Geste wie das Legen eines Blattes in den
    /// Scaneingang.</para>
    /// <para>Wer den Posteingang will, trägt <c>INBOX</c> ein. Das ist dann eine Entscheidung
    /// und kein Versehen.</para>
    /// </remarks>
    public string Folder { get; set; } = "FinanzApp";

    /// <summary>
    /// Wohin eine erledigte Mail wandert. Leer heißt <c>Erledigt</c> unter <see cref="Folder"/>.
    /// </summary>
    /// <remarks>
    /// Verschieben statt löschen, aus demselben Grund wie beim Ordner: gelöscht wäre die Mail
    /// weg, obwohl der Dienst sie nur weitergereicht hat. Und verschieben statt nur als gelesen
    /// markieren — gelesen ist ein Zeichen, das jedes Mailprogramm auch von selbst setzt, und
    /// daran dürfte nicht hängen, ob ein Beleg zweimal in der Ablage landet.
    /// </remarks>
    public string DoneFolder { get; set; } = string.Empty;

    /// <summary>
    /// Ob aus der Mail selbst ein Vorgang wird.
    /// </summary>
    /// <remarks>
    /// Der Betreff als Titel, Absender und Tag als Beschreibung. Was eine Mail verlangt, steht
    /// selten im Anhang und fast immer im Text darüber — und ein Anhang ohne den Satz, der ihn
    /// erklärt, ist ein abgelegtes Blatt ohne Auftrag.
    /// </remarks>
    public bool CreateTask { get; set; } = true;

    /// <summary>
    /// Welche Anhänge weitergereicht werden. Leer heißt: alle, der Server entscheidet.
    /// </summary>
    /// <remarks>
    /// Dieselbe Falle wie bei <see cref="WatchOptions.Extensions"/>: Die Vorgabe ist bewusst
    /// leer, weil die Konfigurationsbindung Feldwerte <b>ergänzt</b> statt sie zu ersetzen.
    /// Stünde hier eine Liste, käme die eingestellte zu ihr hinzu.
    /// </remarks>
    public string[] Extensions { get; set; } = [];

    /// <summary>Höchstgröße eines Anhangs in Megabyte. 0 schaltet die Prüfung ab.</summary>
    public int MaxMegabytes { get; set; } = 25;

    /// <summary>
    /// Abstand zweier Durchgänge in Sekunden.
    /// </summary>
    /// <remarks>
    /// Länger als beim Ordner, und das ist kein Versehen: Ein Scanner legt ein Blatt ab, während
    /// jemand davorsteht und wartet. Eine Mail hat niemand eilig, und jeder Durchgang ist eine
    /// Anmeldung am Postfach.
    /// </remarks>
    public int SweepSeconds { get; set; } = 300;

    /// <summary>
    /// Wie viele Mails ein Durchgang höchstens mitnimmt.
    /// </summary>
    /// <remarks>
    /// Ein frisch eingerichteter Ordner kann Hunderte enthalten. Sie in einem Zug durch die
    /// Anwendung zu schieben hieße, sie beim ersten Durchgang für Minuten zu belegen — und wer
    /// zusieht, sähe einen Dienst, der hängt.
    /// </remarks>
    public int MaxPerSweep { get; set; } = 20;

    /// <summary>Der Ordner für erledigte Mails, mit eingesetzter Vorgabe.</summary>
    public string ResolvedDoneFolder
        => string.IsNullOrWhiteSpace(DoneFolder) ? "Erledigt" : DoneFolder.Trim();

    /// <summary>
    /// Was an den Einstellungen fehlt — leer, wenn das Postfach angesehen werden kann.
    /// </summary>
    /// <remarks>
    /// Ist das Postfach abgeschaltet, fehlt nichts: Der Dienst soll ohne Postfach laufen, ohne
    /// sich über leere Felder zu beklagen, die niemand ausfüllen wollte.
    /// </remarks>
    public IReadOnlyList<string> Problems()
    {
        if (!Enabled)
        {
            return [];
        }

        List<string> problems = [];

        if (string.IsNullOrWhiteSpace(Host))
        {
            problems.Add($"{SectionName}:Host fehlt — der IMAP-Server des Postfachs.");
        }

        if (Port is < 1 or > 65535)
        {
            problems.Add($"{SectionName}:Port ist {Port}. Erlaubt ist 1 bis 65535; 993 ist der "
                + "verschlüsselte Zugang.");
        }

        if (string.IsNullOrWhiteSpace(User))
        {
            problems.Add($"{SectionName}:User fehlt — der Benutzername am Postfach.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            problems.Add($"{SectionName}:Password fehlt. Es gehört nicht in appsettings.json, "
                + $"sondern in die Umgebungsvariable {SectionName}__Password des Dienstkontos "
                + "oder in dotnet user-secrets.");
        }

        if (string.IsNullOrWhiteSpace(Folder))
        {
            problems.Add($"{SectionName}:Folder fehlt — der Ordner im Postfach, der angesehen "
                + "wird. Ein eigener Ordner ist die sichere Wahl; INBOX wäre der ganze "
                + "Posteingang.");
        }
        else if (string.Equals(Folder.Trim(), ResolvedDoneFolder, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{SectionName}:DoneFolder ist derselbe Ordner wie {SectionName}:Folder. "
                + "Eine erledigte Mail käme dann im nächsten Durchgang wieder.");
        }

        return problems;
    }
}
