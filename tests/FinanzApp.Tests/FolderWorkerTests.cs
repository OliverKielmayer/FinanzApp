using FinanzApp.Ordnerdienst;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Durchgang des Ordnerdienstes: was mit einer Datei nach dem Versuch geschieht.
/// </summary>
/// <remarks>
/// <para>Die Bausteine sind anderswo geprüft — hier geht es um die Regeln, die sie verbinden, und
/// das sind die teuersten des Dienstes. Ein Fehler darin fällt erst Wochen später auf: an einem
/// Eingang, der stillsteht, oder an Dateien, die unter „fehlgeschlagen“ liegen, obwohl ihnen
/// nichts fehlt.</para>
/// <para>Mit echten Dateien in einem echten Ordner und dem laufenden Dienst — der Ordner
/// <em>ist</em> die Warteschlange, und eine gestellte Ablage prüfte davon nichts. Nur die
/// Gegenstelle ist eingesetzt: einen Server, der auf Kommando 401 sagt und danach 200, gibt es
/// nicht.</para>
/// </remarks>
public sealed class FolderWorkerTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    public FolderWorkerTests() => Directory.CreateDirectory(root);

    /// <summary>Eine Gegenstelle, die antwortet, was der Test braucht, und mitzählt.</summary>
    private sealed class Gegenstelle(params HandoverResult[] antworten) : IIntakeClient
    {
        private int index;

        /// <summary>Die Dateien in der Reihenfolge, in der sie angeboten wurden.</summary>
        public List<string> Angeboten { get; } = [];

        /// <summary>Wird nach jedem Angebot gesetzt — darauf wartet der Test.</summary>
        public SemaphoreSlim Angekommen { get; } = new(0);

        public Task<HandoverResult> HandOverAsync(string path, CancellationToken ct)
        {
            Angeboten.Add(path);

            // Die letzte Antwort gilt weiter, wenn mehr Versuche kommen als Antworten hinterlegt
            // sind. Sonst müsste jeder Test die Zahl der Durchgänge erraten.
            var antwort = antworten[Math.Min(index++, antworten.Length - 1)];

            Angekommen.Release();
            return Task.FromResult(antwort);
        }
    }

    private WatchOptions Options(int maxMegabytes = 25, int maxAttempts = 5) => new()
    {
        WatchFolder = root,
        Email = "dienst@haushalt-kielmayer.de",
        Password = "geheim",
        Extensions = [".pdf"],

        // Ohne Wartezeit und mit kurzem Takt: die Fertig-Erkennung ist anderswo geprüft, hier
        // geht es um das, was danach kommt.
        SettleSeconds = 0,
        SweepSeconds = 1,
        MaxAttempts = maxAttempts,
        MaxMegabytes = maxMegabytes,
    };

    private string Write(string name, int bytes = 32)
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    /// <summary>
    /// Lässt den Dienst laufen, bis so viele Dateien angeboten wurden — oder die Zeit abläuft.
    /// </summary>
    /// <remarks>
    /// Zwei Durchgänge trennt eine Sekunde; fünf Sekunden Geduld sind daher reichlich und halten
    /// den Test auch auf einem belasteten Rechner ruhig.
    /// </remarks>
    private async Task RunAsync(Gegenstelle gegenstelle, WatchOptions options, int angebote)
    {
        var worker = new FolderWorker(
            options,
            new FolderInbox(options, NullLogger<FolderInbox>.Instance),
            gegenstelle,
            NullLogger<FolderWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        try
        {
            for (var i = 0; i < angebote; i++)
            {
                Assert.True(
                    await gegenstelle.Angekommen.WaitAsync(TimeSpan.FromSeconds(5)),
                    $"Angebot {i + 1} von {angebote} kam nicht.");
            }

            // Ein Augenblick, damit der Durchgang die Datei auch noch wegräumt.
            await Task.Delay(300);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }
    }

    private string[] Files(string folder)
        => Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(Path.GetFileName)!]
            : [];

    private string[] Done() => Files(Path.Combine(root, "_erledigt"));

    private string[] Failed() => Files(Path.Combine(root, "_fehlgeschlagen"));

    private string[] Waiting() => [.. Directory.GetFiles(root).Select(Path.GetFileName)!];

    // ── Der gewöhnliche Weg ────────────────────────────────────────────────────────────────

    /// <summary>Eine übergebene Datei liegt danach unter „erledigt“ und nicht mehr im Eingang.</summary>
    /// <remarks>
    /// Bliebe sie liegen, ginge sie im nächsten Durchgang erneut hinaus — und der Scaneingang
    /// füllte sich mit Kopien desselben Belegs.
    /// </remarks>
    [Fact]
    public async Task Eine_uebergebene_Datei_wandert_aus_dem_Eingang()
    {
        Write("beleg.pdf");
        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Handed, "abgelegt"));

        await RunAsync(gegenstelle, Options(), angebote: 1);

        Assert.Equal(["beleg.pdf"], Done());
        Assert.Empty(Waiting());
    }

    /// <summary>Eine abgelehnte Datei wandert sofort beiseite — ein zweiter Versuch hilft nie.</summary>
    [Fact]
    public async Task Eine_abgelehnte_Datei_wandert_sofort_beiseite()
    {
        Write("falsch.pdf");
        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Rejected, "Dateityp"));

        await RunAsync(gegenstelle, Options(), angebote: 1);

        Assert.Equal(["falsch.pdf"], Failed());
        Assert.Single(gegenstelle.Angeboten);
    }

    // ── Der Server ist weg ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein blockierter Versuch beendet den Durchgang und kostet keiner Datei einen Versuch.
    /// </summary>
    /// <remarks>
    /// Die teuerste Regel des Dienstes. Ohne sie kostete ein Wartungsfenster jede Datei im
    /// Eingang einen Versuch, und nach fünf Durchgängen läge der ganze Stapel unter
    /// „fehlgeschlagen“, obwohl an keiner Datei etwas fehlte. Geprüft an drei Dateien: nur die
    /// erste wird angeboten, danach ist Schluss.
    /// </remarks>
    [Fact]
    public async Task Ein_blockierter_Versuch_beendet_den_Durchgang()
    {
        Write("eins.pdf");
        Write("zwei.pdf");
        Write("drei.pdf");

        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Blocked, "keine Verbindung"));

        await RunAsync(gegenstelle, Options(maxAttempts: 1), angebote: 1);

        // Alle drei liegen noch da, und keine ist beiseitegelegt — obwohl ein einziger Versuch
        // bei MaxAttempts = 1 sonst gereicht hätte.
        Assert.Equal(3, Waiting().Length);
        Assert.Empty(Failed());
        Assert.Empty(Done());
    }

    /// <summary>Ist der Server zurück, geht alles hinaus, was liegen geblieben ist.</summary>
    [Fact]
    public async Task Nach_der_Sperre_gehen_alle_Dateien_hinaus()
    {
        Write("eins.pdf");
        Write("zwei.pdf");

        // Erst blockiert, danach nimmt der Server wieder an.
        var gegenstelle = new Gegenstelle(
            new HandoverResult(HandoverStatus.Blocked, "keine Verbindung"),
            new HandoverResult(HandoverStatus.Handed, "abgelegt"));

        await RunAsync(gegenstelle, Options(), angebote: 3);

        Assert.Empty(Waiting());
        Assert.Equal(2, Done().Length);
    }

    // ── Vorübergehende Fehler ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein vorübergehender Fehler lässt die Datei liegen — bis der Geduldsfaden reißt.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zur Sperre: hier liegt es an dieser Datei, also zahlt sie auch. Nach
    /// <see cref="WatchOptions.MaxAttempts"/> wandert sie beiseite, damit sie den Eingang nicht
    /// für immer blockiert.
    /// </remarks>
    [Fact]
    public async Task Nach_zu_vielen_Fehlversuchen_wandert_die_Datei_beiseite()
    {
        Write("zaeh.pdf");
        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Deferred, "belegt"));

        await RunAsync(gegenstelle, Options(maxAttempts: 2), angebote: 2);

        Assert.Equal(["zaeh.pdf"], Failed());
        Assert.Empty(Waiting());
        Assert.Equal(2, gegenstelle.Angeboten.Count);
    }

    /// <summary>Vor dem letzten Versuch bleibt sie liegen, wo sie ist.</summary>
    [Fact]
    public async Task Ein_einzelner_Fehlversuch_laesst_die_Datei_liegen()
    {
        Write("zaeh.pdf");
        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Deferred, "belegt"));

        await RunAsync(gegenstelle, Options(maxAttempts: 5), angebote: 1);

        Assert.Equal(["zaeh.pdf"], Waiting());
        Assert.Empty(Failed());
    }

    // ── Die eigene Größenprüfung ───────────────────────────────────────────────────────────

    /// <summary>
    /// Eine zu große Datei wird gar nicht erst angeboten.
    /// </summary>
    /// <remarks>
    /// Der Server bräche sie <em>während</em> des Sendens ab, und das sähe aus wie ein
    /// Verbindungsfehler — eine einzige zu große Datei hielte damit den ganzen Eingang auf.
    /// Geprüft wird deshalb, dass die Gegenstelle sie nie zu sehen bekommt.
    /// </remarks>
    [Fact]
    public async Task Eine_zu_grosse_Datei_erreicht_den_Server_nicht()
    {
        Write("riesig.pdf", 2 * 1024 * 1024);
        Write("klein.pdf");

        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Handed, "abgelegt"));

        await RunAsync(gegenstelle, Options(maxMegabytes: 1), angebote: 1);

        Assert.Equal(["riesig.pdf"], Failed());
        Assert.Equal(["klein.pdf"], Done());
        Assert.Equal(["klein.pdf"], gegenstelle.Angeboten.Select(Path.GetFileName));
    }

    /// <summary>Was der Dienst nicht anbietet, rührt er auch nicht an.</summary>
    /// <remarks>
    /// Die halbfertige <c>.tmp</c>-Datei eines Scanners bleibt liegen und fällt dort auf. Sie
    /// beiseitezulegen wäre eine Aussage über eine Datei, die der Dienst nie gelesen hat.
    /// </remarks>
    [Fact]
    public async Task Eine_fremde_Dateiart_bleibt_unberuehrt_liegen()
    {
        Write("halbfertig.tmp");
        Write("beleg.pdf");

        var gegenstelle = new Gegenstelle(new HandoverResult(HandoverStatus.Handed, "abgelegt"));

        await RunAsync(gegenstelle, Options(), angebote: 1);

        Assert.Equal(["halbfertig.tmp"], Waiting());
        Assert.Equal(["beleg.pdf"], Done());
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
