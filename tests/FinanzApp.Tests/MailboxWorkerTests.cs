using FinanzApp.Ordnerdienst;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Durchgang über das Postfach: was mit einer Mail nach dem Versuch geschieht.
/// </summary>
/// <remarks>
/// <para>Dieselbe Sorte Regeln wie beim Ordner, und dieselbe Begründung: Ein Fehler darin fällt
/// erst Wochen später auf — an einem Postfachordner, der volläuft, oder an Belegen, die nie
/// angekommen sind, obwohl die Mail als erledigt dasteht.</para>
/// <para>Postfach und Gegenstelle sind eingesetzt. Ein IMAP-Server, der auf Kommando eine
/// bestimmte Mail liefert und danach einen Verbindungsabbruch, existiert nicht.</para>
/// <para>Die Namen und Beträge hier sind erfunden.</para>
/// </remarks>
public sealed class MailboxWorkerTests
{
    /// <summary>Ein Postfach, das liefert, was der Test braucht, und mitzählt.</summary>
    private sealed class Postfach(params MailItem[] mails) : IMailbox, IMailSession
    {
        /// <summary>Welche Mails weggeräumt wurden — in dieser Reihenfolge.</summary>
        public List<string> Erledigt { get; } = [];

        public IReadOnlyList<MailItem> Messages => mails;

        public Task<IMailSession> OpenAsync(CancellationToken ct)
            => Task.FromResult<IMailSession>(this);

        public Task DoneAsync(MailItem item, CancellationToken ct)
        {
            Erledigt.Add(item.Subject);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Eine Gegenstelle, die antwortet, was der Test vorgibt, und mitschreibt.</summary>
    private sealed class Gegenstelle(
        HandoverStatus antwort = HandoverStatus.Handed, bool vorgangGeht = true) : IIntakeClient
    {
        public List<(string Datei, string Quelle, int Bytes)> Angeboten { get; } = [];

        public List<CreateTaskRequest> Vorgaenge { get; } = [];

        public Task<HandoverResult> HandOverAsync(
            byte[] content, string fileName, string source, CancellationToken ct)
        {
            Angeboten.Add((fileName, source, content.Length));
            return Task.FromResult(new HandoverResult(antwort, "geprüft"));
        }

        public Task<bool> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct)
        {
            if (vorgangGeht)
            {
                Vorgaenge.Add(request);
            }

            return Task.FromResult(vorgangGeht);
        }

        public Task<HandoverResult> HandOverAsync(string path, CancellationToken ct)
            => throw new NotSupportedException("Das Postfach reicht keine Pfade weiter.");
    }

    private static MailboxOptions Options(
        bool createTask = true, string[]? extensions = null, int maxMegabytes = 25) => new()
    {
        Enabled = true,
        Host = "imap.example.net",
        User = "dienst@haushalt-mustermann.de",
        Password = "geheim",
        Folder = "FinanzApp",
        CreateTask = createTask,
        Extensions = extensions ?? [".pdf"],
        MaxMegabytes = maxMegabytes,
    };

    private static MailItem Mail(string betreff, params MailAttachment[] anhaenge)
        => new("1", betreff, "absender@example.net", new DateTimeOffset(2026, 11, 16, 9, 0, 0,
            TimeSpan.FromHours(1)), anhaenge);

    private static MailAttachment Anhang(string name, int bytes = 64)
        => new(name, new byte[bytes]);

    private static async Task<(Postfach Postfach, Gegenstelle Gegenstelle)> LaufAsync(
        MailboxOptions options, Postfach postfach, Gegenstelle gegenstelle)
    {
        var worker = new MailboxWorker(
            options, postfach, gegenstelle, NullLogger<MailboxWorker>.Instance);

        // Ein Durchgang, dann Schluss: Der Dienst wartet danach auf den Takt, und auf den zu
        // warten hiesse, fuenf Minuten lang nichts zu pruefen.
        using var stop = new CancellationTokenSource();
        var lauf = worker.StartAsync(stop.Token);
        await lauf;

        await Task.Delay(50, CancellationToken.None);
        await stop.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        return (postfach, gegenstelle);
    }

    // ── Der Regelfall ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aus der Mail wird ein Vorgang, aus ihrem Anhang ein Beleg — und dann ist sie erledigt.
    /// </summary>
    [Fact]
    public async Task Aus_der_Mail_wird_ein_Vorgang_und_aus_dem_Anhang_ein_Beleg()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(),
            new Postfach(Mail("Rechnung November", Anhang("rechnung.pdf"))),
            new Gegenstelle());

        var vorgang = Assert.Single(gegenstelle.Vorgaenge);
        Assert.Equal("Rechnung November", vorgang.Title);
        Assert.Contains("absender@example.net", vorgang.Detail);
        Assert.Null(vorgang.DueOn);

        var beleg = Assert.Single(gegenstelle.Angeboten);
        Assert.Equal("rechnung.pdf", beleg.Datei);

        Assert.Equal(["Rechnung November"], postfach.Erledigt);
    }

    /// <summary>
    /// Die Herkunft geht mit und nennt den Absender.
    /// </summary>
    /// <remarks>
    /// Sie steht danach als Beschreibung am Dokument. Ohne sie liesse sich ein Beleg nicht mehr
    /// bis zu seiner Quelle zurückverfolgen — und bei einer Mail ist das die Frage, die zuerst
    /// kommt: von wem kam das.
    /// </remarks>
    [Fact]
    public async Task Der_Beleg_traegt_die_Herkunft_der_Mail()
    {
        var (_, gegenstelle) = await LaufAsync(
            Options(),
            new Postfach(Mail("Rechnung November", Anhang("rechnung.pdf"))),
            new Gegenstelle());

        var quelle = Assert.Single(gegenstelle.Angeboten).Quelle;

        Assert.Contains("Postfach", quelle);
        Assert.Contains("Rechnung November", quelle);
        Assert.Contains("absender@example.net", quelle);
    }

    // ── Was liegen bleibt und was nicht ────────────────────────────────────────────────────

    /// <summary>
    /// Scheitert die Übergabe, bleibt die ganze Mail liegen.
    /// </summary>
    /// <remarks>
    /// Sie beim nächsten Durchgang erneut zu schicken ist harmlos: Der Eingang hält den Abdruck
    /// jeder Datei. Sie wegzuräumen wäre es nicht — der Beleg wäre weg, und niemand wüsste,
    /// welcher.
    /// </remarks>
    [Fact]
    public async Task Eine_gescheiterte_Uebergabe_laesst_die_Mail_liegen()
    {
        var (postfach, _) = await LaufAsync(
            Options(),
            new Postfach(Mail("Rechnung November", Anhang("rechnung.pdf"))),
            new Gegenstelle(HandoverStatus.Deferred));

        Assert.Empty(postfach.Erledigt);
    }

    /// <summary>
    /// Eine abgelehnte Datei hält die Mail nicht auf.
    /// </summary>
    /// <remarks>
    /// Abgelehnt heisst: Der Server will diese Datei nicht, und daran ändert der nächste
    /// Durchgang nichts. Die Mail deswegen liegen zu lassen hiesse, den Ordner mit etwas zu
    /// füllen, das nie durchgeht.
    /// </remarks>
    [Fact]
    public async Task Eine_abgelehnte_Datei_haelt_die_Mail_nicht_auf()
    {
        var (postfach, _) = await LaufAsync(
            Options(),
            new Postfach(Mail("Werbung", Anhang("prospekt.pdf"))),
            new Gegenstelle(HandoverStatus.Rejected));

        Assert.Equal(["Werbung"], postfach.Erledigt);
    }

    /// <summary>
    /// Ohne Vorgang keine Mail: Steht der Merkzettel nicht, bleibt sie liegen.
    /// </summary>
    /// <remarks>
    /// Andersherum käme der Beleg an und der Satz, der ihn erklärt, wäre für immer weg — und
    /// nachzureichen ist er dann nicht mehr, weil niemand weiss, dass er fehlt.
    /// </remarks>
    [Fact]
    public async Task Ohne_angelegten_Vorgang_bleibt_die_Mail_liegen()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(),
            new Postfach(Mail("Rechnung November", Anhang("rechnung.pdf"))),
            new Gegenstelle(vorgangGeht: false));

        Assert.Empty(postfach.Erledigt);
        Assert.Empty(gegenstelle.Angeboten);
    }

    /// <summary>Eine Mail ohne brauchbaren Anhang ist trotzdem erledigt.</summary>
    /// <remarks>
    /// Sonst läge sie für immer im Ordner und käme bei jedem Durchgang wieder.
    /// </remarks>
    [Fact]
    public async Task Eine_Mail_ohne_Anhang_wird_trotzdem_weggeraeumt()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(),
            new Postfach(Mail("Nur ein Hinweis")),
            new Gegenstelle());

        Assert.Single(gegenstelle.Vorgaenge);
        Assert.Empty(gegenstelle.Angeboten);
        Assert.Equal(["Nur ein Hinweis"], postfach.Erledigt);
    }

    // ── Welcher Anhang überhaupt angeboten wird ────────────────────────────────────────────

    /// <summary>
    /// Was nicht in der Liste steht, geht nicht hinaus.
    /// </summary>
    /// <remarks>
    /// Eine Signatur bringt Bilder mit, ein Kalendereintrag eine <c>.ics</c>. Beides als Beleg
    /// abzulegen hiesse, die Ablage mit Nebensachen zu füllen.
    /// </remarks>
    [Fact]
    public async Task Nur_die_erlaubten_Dateiarten_gehen_hinaus()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(extensions: [".pdf"]),
            new Postfach(Mail("Termin", Anhang("termin.ics"), Anhang("rechnung.pdf"))),
            new Gegenstelle());

        Assert.Equal(["rechnung.pdf"], gegenstelle.Angeboten.Select(a => a.Datei));
        Assert.Equal(["Termin"], postfach.Erledigt);
    }

    /// <summary>Eine leere Liste heisst: alle anbieten, der Server entscheidet.</summary>
    [Fact]
    public async Task Ohne_Liste_geht_jeder_Anhang_hinaus()
    {
        var (_, gegenstelle) = await LaufAsync(
            Options(extensions: []),
            new Postfach(Mail("Termin", Anhang("termin.ics"), Anhang("rechnung.pdf"))),
            new Gegenstelle());

        Assert.Equal(2, gegenstelle.Angeboten.Count);
    }

    /// <summary>
    /// Ein zu grosser Anhang wird gar nicht erst angeboten.
    /// </summary>
    /// <remarks>
    /// Er liegt schon im Arbeitsspeicher; ihn auch noch hochzuladen, um am Ende abgewiesen zu
    /// werden, wäre zweimal umsonst. Und der Abbruch mitten im Senden sieht nicht wie eine
    /// Ablehnung aus, sondern wie ein Verbindungsfehler.
    /// </remarks>
    [Fact]
    public async Task Ein_zu_grosser_Anhang_wird_nicht_angeboten()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(maxMegabytes: 1),
            new Postfach(Mail("Grosser Scan", Anhang("scan.pdf", bytes: 2 * 1024 * 1024))),
            new Gegenstelle());

        Assert.Empty(gegenstelle.Angeboten);
        Assert.Equal(["Grosser Scan"], postfach.Erledigt);
    }

    /// <summary>Ohne eingeschalteten Vorgang kommen nur die Anhänge an.</summary>
    [Fact]
    public async Task Abgeschaltet_entsteht_kein_Vorgang()
    {
        var (postfach, gegenstelle) = await LaufAsync(
            Options(createTask: false),
            new Postfach(Mail("Rechnung November", Anhang("rechnung.pdf"))),
            new Gegenstelle());

        Assert.Empty(gegenstelle.Vorgaenge);
        Assert.Single(gegenstelle.Angeboten);
        Assert.Equal(["Rechnung November"], postfach.Erledigt);
    }
}
