using System.Net;
using FinanzApp.Ordnerdienst;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Ordnerdienst, soweit er ohne Server prüfbar ist: wann eine Datei als fertig geschrieben
/// gilt und wohin sie danach wandert.
/// </summary>
/// <remarks>
/// <para>Die beiden Stellen, an denen ein Fehler teuer wäre. Eine zu früh übergebene Datei
/// bringt die erste Seite eines zehnseitigen Scans in die Ablage — und niemand merkt es, weil
/// eine Datei ja angekommen ist. Eine nicht weggeräumte Datei geht bei jedem Durchgang erneut
/// hinaus.</para>
/// <para>Der Ordner selbst ist die Warteschlange; deshalb geht es hier um echte Dateien in einem
/// echten Ordner und nicht um eine gestellte Ablage.</para>
/// </remarks>
public sealed class FolderServiceTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly WatchOptions options;

    public FolderServiceTests()
    {
        Directory.CreateDirectory(root);

        options = new WatchOptions
        {
            WatchFolder = root,
            Email = "dienst@haushalt-kielmayer.de",
            Password = "geheim",
            Extensions = [".pdf", ".jpg"],
            SettleSeconds = 5,
        };
    }

    private FolderInbox Inbox() => new(options, NullLogger<FolderInbox>.Instance);

    private string Write(string name, string content = "Inhalt")
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Fertig geschrieben oder nicht ──────────────────────────────────────────────────────

    /// <summary>
    /// Beim ersten Blick gilt keine Datei als fertig.
    /// </summary>
    /// <remarks>
    /// Ein Scanner meldet die Datei, sobald er sie anlegt. Wer beim ersten Blick zugreift,
    /// übergibt die erste Seite als vollständiges Dokument.
    /// </remarks>
    [Fact]
    public void Beim_ersten_Blick_gilt_nichts_als_fertig()
    {
        var inbox = Inbox();
        var path = Write("scan.pdf");

        Assert.False(inbox.IsSettled(path, DateTime.UtcNow));
    }

    /// <summary>Unverändert und lange genug still: fertig.</summary>
    [Fact]
    public void Eine_ruhende_Datei_gilt_als_fertig()
    {
        var inbox = Inbox();
        var path = Write("scan.pdf");
        var jetzt = DateTime.UtcNow;

        Assert.False(inbox.IsSettled(path, jetzt));
        Assert.True(inbox.IsSettled(path, jetzt.AddSeconds(10)));
    }

    /// <summary>
    /// Eine wachsende Datei fängt die Wartezeit von vorn an.
    /// </summary>
    /// <remarks>
    /// Der eigentliche Zweck der Probe: zwischen zwei Blicken ist die Datei größer geworden,
    /// also schreibt noch jemand. Die vergangene Zeit zählt dann nicht mehr.
    /// </remarks>
    [Fact]
    public void Eine_wachsende_Datei_gilt_nicht_als_fertig()
    {
        var inbox = Inbox();
        var path = Write("scan.pdf", "Seite 1");
        var jetzt = DateTime.UtcNow;

        Assert.False(inbox.IsSettled(path, jetzt));

        File.AppendAllText(path, "Seite 2");
        Assert.False(inbox.IsSettled(path, jetzt.AddSeconds(10)));

        // Erst der Blick nach der Ruhe, gemessen ab der letzten Änderung.
        Assert.True(inbox.IsSettled(path, jetzt.AddSeconds(20)));
    }

    /// <summary>Eine leere Datei ist noch keine Datei, sondern eine Ankündigung.</summary>
    [Fact]
    public void Eine_leere_Datei_gilt_nie_als_fertig()
    {
        var inbox = Inbox();
        var path = Write("scan.pdf", string.Empty);
        var jetzt = DateTime.UtcNow;

        Assert.False(inbox.IsSettled(path, jetzt));
        Assert.False(inbox.IsSettled(path, jetzt.AddSeconds(60)));
    }

    /// <summary>
    /// Eine Datei, die noch jemand offen hält, gilt nicht als fertig.
    /// </summary>
    /// <remarks>
    /// Die zweite Probe, und die verlässlichere unter Windows: Größe und Zeitstempel stehen
    /// still, während der Schreiber noch einen Block im Puffer hat. Ein offener Griff darauf
    /// steht nicht still.
    /// </remarks>
    [Fact]
    public void Eine_belegte_Datei_gilt_nicht_als_fertig()
    {
        var inbox = Inbox();
        var path = Write("scan.pdf");
        var jetzt = DateTime.UtcNow;

        Assert.False(inbox.IsSettled(path, jetzt));

        using var offen = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);

        // Ruhe lange genug, Größe unverändert — und trotzdem nicht fertig.
        Assert.False(inbox.IsSettled(path, jetzt.AddSeconds(60)));
    }

    // ── Was im Ordner liegt ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gesehen wird die oberste Ebene, und dort nur die angebotenen Dateiarten.
    /// </summary>
    /// <remarks>
    /// Die Unterordner bleiben außen vor, weil <c>_erledigt</c> und <c>_fehlgeschlagen</c> darin
    /// liegen dürfen. Sie mitzulesen wäre eine Endlosschleife: der Dienst sammelte seine eigenen
    /// Ergebnisse wieder ein.
    /// </remarks>
    [Fact]
    public void Gesehen_wird_die_oberste_Ebene()
    {
        Write("beleg.pdf");
        Write("foto.JPG");
        Write("halbfertig.tmp");

        Directory.CreateDirectory(Path.Combine(root, "_erledigt"));
        File.WriteAllText(Path.Combine(root, "_erledigt", "alt.pdf"), "Inhalt");

        var wartend = Inbox().Waiting().Select(Path.GetFileName).ToList();

        Assert.Equal(2, wartend.Count);
        Assert.Contains("beleg.pdf", wartend);
        Assert.Contains("foto.JPG", wartend);
    }

    /// <summary>Ein fehlender Ordner ist keine Ausnahme — ein Netzlaufwerk kommt manchmal später.</summary>
    [Fact]
    public void Ein_fehlender_Ordner_liefert_nichts()
    {
        options.WatchFolder = Path.Combine(root, "gibt-es-nicht");
        Assert.Empty(Inbox().Waiting());
    }

    // ── Wegräumen ──────────────────────────────────────────────────────────────────────────

    /// <summary>Eine übergebene Datei wandert in den Monatsordner unter <c>_erledigt</c>.</summary>
    [Fact]
    public void Eine_uebergebene_Datei_wandert_in_den_Monatsordner()
    {
        var path = Write("beleg.pdf");

        var ziel = Inbox().MoveToDone(path, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Local));

        Assert.NotNull(ziel);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(ziel));
        Assert.Equal(Path.Combine(root, "_erledigt", "2026-08", "beleg.pdf"), ziel);
    }

    /// <summary>
    /// Ein belegter Name wird nicht überschrieben.
    /// </summary>
    /// <remarks>
    /// Zwei Scans desselben Tages heißen beim selben Gerät gern gleich, und der ältere ist kein
    /// Ausschuss.
    /// </remarks>
    [Fact]
    public void Ein_belegter_Name_wird_nicht_ueberschrieben()
    {
        var inbox = Inbox();
        var stichtag = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Local);

        inbox.MoveToDone(Write("beleg.pdf", "erster"), stichtag);
        var zweites = inbox.MoveToDone(Write("beleg.pdf", "zweiter"), stichtag);

        Assert.Equal(Path.Combine(root, "_erledigt", "2026-08", "beleg_1.pdf"), zweites);
        Assert.Equal("erster", File.ReadAllText(Path.Combine(root, "_erledigt", "2026-08", "beleg.pdf")));
        Assert.Equal("zweiter", File.ReadAllText(zweites!));
    }

    /// <summary>Eine abgelehnte Datei liegt beiseite und steht im Eingang nicht mehr im Weg.</summary>
    [Fact]
    public void Eine_abgelehnte_Datei_liegt_beiseite()
    {
        var path = Write("kaputt.pdf");

        var ziel = Inbox().MoveToFailed(path);

        Assert.Equal(Path.Combine(root, "_fehlgeschlagen", "kaputt.pdf"), ziel);
        Assert.Empty(Inbox().Waiting());
    }

    // ── Einstellungen ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Was fehlt, steht beim Start fest.
    /// </summary>
    /// <remarks>
    /// Ein Dienst, der läuft und stillschweigend nichts tut, ist schlimmer als einer, der sich
    /// weigert und sagt, warum.
    /// </remarks>
    [Fact]
    public void Fehlende_Einstellungen_werden_benannt()
    {
        Assert.Empty(options.Problems());

        var leer = new WatchOptions { BaseAddress = "kein-uri" };
        var probleme = leer.Problems();

        Assert.Equal(3, probleme.Count);
        Assert.Contains(probleme, p => p.Contains("WatchFolder"));
        Assert.Contains(probleme, p => p.Contains("Password"));
        Assert.Contains(probleme, p => p.Contains("BaseAddress"));
    }

    /// <summary>Leere Liste heißt: alles anbieten und den Server entscheiden lassen.</summary>
    [Fact]
    public void Eine_leere_Dateiartenliste_bietet_alles_an()
    {
        options.Extensions = [];

        Assert.True(options.Accepts("beleg.tif"));
        Assert.True(options.Accepts("beleg.pdf"));
    }

    /// <summary>Die Erweiterung entscheidet, und die Schreibweise ist ihr gleich.</summary>
    [Fact]
    public void Die_Dateiart_wird_ohne_Ruecksicht_auf_Gross_und_Klein_geprueft()
    {
        Assert.True(options.Accepts("beleg.PDF"));
        Assert.True(options.Accepts(@"C:\Scans\beleg.Jpg"));
        Assert.False(options.Accepts("beleg.tif"));
        Assert.False(options.Accepts("beleg"));
    }

    /// <summary>
    /// Zu große Dateien werden gar nicht erst angeboten.
    /// </summary>
    /// <remarks>
    /// Die Grenze gehört auf beide Seiten. Der Server bricht eine zu große Anfrage mitten im
    /// Rumpf ab, und das sieht nicht aus wie eine Ablehnung, sondern wie ein Verbindungsfehler —
    /// mit dem der Dienst den ganzen Durchgang beendet. Eine einzige Datei hielte damit den
    /// Eingang auf. Im Betrieb genau so passiert.
    /// </remarks>
    [Fact]
    public void Zu_grosse_Dateien_werden_nicht_angeboten()
    {
        options.MaxMegabytes = 25;

        Assert.True(options.IsSmallEnough(25L * 1024 * 1024));
        Assert.False(options.IsSmallEnough(25L * 1024 * 1024 + 1));

        // 0 heißt: keine Prüfung. Wer den Server entscheiden lassen will, darf das.
        options.MaxMegabytes = 0;
        Assert.True(options.IsSmallEnough(long.MaxValue));
    }

    // ── Fehlschlag einordnen ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Nicht erreichbar heißt: alle Dateien, nicht diese eine.
    /// </summary>
    /// <remarks>
    /// Sonst kostete ein Wartungsfenster jede Datei im Eingang einen Versuch, und nach fünf
    /// Durchgängen läge der ganze Stapel unter „fehlgeschlagen“, obwohl an keiner Datei etwas
    /// fehlt.
    /// </remarks>
    [Fact]
    public void Ein_nicht_erreichbarer_Server_beendet_den_Durchgang()
    {
        foreach (var fehler in new[]
                 {
                     HttpRequestError.ConnectionError,
                     HttpRequestError.NameResolutionError,
                     HttpRequestError.SecureConnectionError,
                     HttpRequestError.ProxyTunnelError,
                 })
        {
            Assert.Equal(
                HandoverStatus.Blocked,
                Handover.Failure(new HttpRequestException(fehler, "aus")).Status);
        }
    }

    /// <summary>
    /// Eine abgebrochene Übertragung gehört dieser Datei.
    /// </summary>
    /// <remarks>
    /// Der Fall, der den Unterschied überhaupt nötig machte: der Server schließt die Verbindung,
    /// weil der Rumpf zu groß ist. Als „keine Verbindung“ gezählt, stünde der Eingang für immer.
    /// </remarks>
    [Fact]
    public void Eine_abgebrochene_Uebertragung_zaehlt_der_Datei()
    {
        var ergebnis = Handover.Failure(
            new HttpRequestException(HttpRequestError.ResponseEnded, "Rumpf zu groß"));

        Assert.Equal(HandoverStatus.Deferred, ergebnis.Status);
        Assert.Contains("abgebrochen", ergebnis.Message);
    }

    /// <summary>
    /// Der Statuscode sagt, wer etwas ändern muss.
    /// </summary>
    /// <remarks>
    /// 400 ändert sich nie von selbst, 500 und 429 schon, und 401/403/503 sind keine Aussage
    /// über die Datei.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HandoverStatus.Rejected)]
    [InlineData(HttpStatusCode.NotFound, HandoverStatus.Rejected)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HandoverStatus.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, HandoverStatus.Deferred)]
    [InlineData(HttpStatusCode.TooManyRequests, HandoverStatus.Deferred)]
    [InlineData(HttpStatusCode.Unauthorized, HandoverStatus.Blocked)]
    [InlineData(HttpStatusCode.Forbidden, HandoverStatus.Blocked)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HandoverStatus.Blocked)]
    public void Der_Statuscode_bestimmt_den_Weg(HttpStatusCode code, HandoverStatus erwartet)
        => Assert.Equal(erwartet, Handover.StatusFor(code));

    /// <summary>
    /// Der überwachte Ordner darf nicht sein eigenes Ziel sein.
    /// </summary>
    /// <remarks>
    /// Sonst verschiebt der Dienst jede Datei dorthin zurück, wo er sie genommen hat, weicht dem
    /// belegten Namen aus — und liefert sie im nächsten Durchgang erneut ein. Das hört nie auf
    /// und fällt erst auf, wenn der Scaneingang hundert Kopien desselben Belegs führt.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Ein_Zielordner_darf_nicht_der_ueberwachte_sein(bool erledigt, bool fehlgeschlagen)
    {
        var eigen = new WatchOptions
        {
            WatchFolder = root,
            Email = options.Email,
            Password = options.Password,
            DoneFolder = erledigt ? root : string.Empty,
            FailedFolder = fehlgeschlagen ? root : string.Empty,
        };

        Assert.Contains(eigen.Problems(), p => p.Contains("überwachten Ordner selbst"));
    }

    /// <summary>Dieselbe Stelle anders geschrieben ist dieselbe Stelle.</summary>
    /// <remarks>
    /// Ein abschließender Trennstrich oder ein <c>.</c> im Pfad macht aus dem Ordner keinen
    /// anderen — die Schleife entstünde trotzdem.
    /// </remarks>
    [Fact]
    public void Auch_anders_geschrieben_ist_es_derselbe_Ordner()
    {
        var eigen = new WatchOptions
        {
            WatchFolder = root,
            Email = options.Email,
            Password = options.Password,
            DoneFolder = Path.Combine(root, ".") + Path.DirectorySeparatorChar,
        };

        Assert.Contains(eigen.Problems(), p => p.Contains("überwachten Ordner selbst"));
    }

    /// <summary>Die Vorgabe darf nicht als Falle gelten — sie liegt im Ordner, ist aber nicht er.</summary>
    [Fact]
    public void Die_vorgegebenen_Unterordner_sind_keine_Falle()
    {
        Assert.DoesNotContain(options.Problems(), p => p.Contains("überwachten Ordner selbst"));
    }

    /// <summary>Ohne eigene Angabe entstehen die beiden Unterordner im überwachten Ordner.</summary>
    [Fact]
    public void Die_Ergebnisordner_liegen_im_ueberwachten_Ordner()
    {
        Assert.Equal(Path.Combine(root, "_erledigt"), options.ResolvedDoneFolder);
        Assert.Equal(Path.Combine(root, "_fehlgeschlagen"), options.ResolvedFailedFolder);

        options.DoneFolder = @"D:\Archiv";
        Assert.Equal(@"D:\Archiv", options.ResolvedDoneFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
