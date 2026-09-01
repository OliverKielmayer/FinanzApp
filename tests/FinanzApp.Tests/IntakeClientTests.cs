using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanzApp.Ordnerdienst;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Zugang zur FinanzApp: was aus einer Antwort des Servers wird.
/// </summary>
/// <remarks>
/// <para>Aus jeder Antwort wird eine Entscheidung über eine Datei — liegen lassen, beiseitelegen
/// oder den ganzen Durchgang beenden. Ein Fehler darin ist im Betrieb nicht zu sehen: die Datei
/// liegt dann eben woanders, und niemand weiß, warum.</para>
/// <para>Gegen einen eingesetzten Übertragungsweg und nicht gegen einen Server. Einen Server, der
/// auf Kommando 401 sagt und beim zweiten Versuch 200, gibt es nicht — und genau dieser Ablauf
/// ist der, den man prüfen will.</para>
/// </remarks>
public sealed class IntakeClientTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    public IntakeClientTests() => Directory.CreateDirectory(root);

    /// <summary>Ein Übertragungsweg, der antwortet, was der Test hinterlegt hat.</summary>
    private sealed class Gegenstelle : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> antworten = new();

        public List<HttpRequestMessage> Anfragen { get; } = [];

        public List<string> Wege => [.. Anfragen.Select(a => a.RequestUri!.PathAndQuery)];

        public Gegenstelle Antwortet(HttpStatusCode status, string? körper = null)
        {
            antworten.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(körper ?? string.Empty, Encoding.UTF8, "application/json"),
            });

            return this;
        }

        public Gegenstelle Wirft(HttpRequestError fehler)
        {
            antworten.Enqueue(_ => throw new HttpRequestException(fehler, "aus dem Test"));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Anfragen.Add(request);

            // Die letzte hinterlegte Antwort gilt weiter — sonst müsste jeder Test die Zahl der
            // Anfragen erraten, die der Client von sich aus stellt.
            var antwort = antworten.Count > 1 ? antworten.Dequeue() : antworten.Peek();
            return Task.FromResult(antwort(request));
        }
    }

    private static string Ergebnis(string summary = "abgelegt")
        => JsonSerializer.Serialize(
            new ScanIntakeResultDto
            {
                DocumentId = 7,
                InboxId = 3,
                FileName = "beleg.pdf",
                RelativePath = "Versicherungen/beleg.pdf",
                Area = DocumentArea.Insurance,
                Outcome = ScanIntakeOutcome.Assigned,
                PageCount = 2,
                Summary = summary,
            },
            Json);

    private static string Problem(string detail)
        => JsonSerializer.Serialize(new { title = "Fehler", detail }, Json);

    private (IntakeClient Client, Gegenstelle Handler) Build(Gegenstelle handler)
    {
        var options = new WatchOptions
        {
            WatchFolder = root,
            BaseAddress = "http://localhost:5222/",
            Email = "dienst@haushalt-kielmayer.de",
            Password = "geheim",
        };

        return (new IntakeClient(options, NullLogger<IntakeClient>.Instance, handler), handler);
    }

    private string Write(string name = "beleg.pdf")
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }

    // ── Der gewöhnliche Weg ────────────────────────────────────────────────────────────────

    /// <summary>Anmelden, senden, angekommen — und das Ergebnis kommt mit.</summary>
    [Fact]
    public async Task Eine_angenommene_Datei_gilt_als_uebergeben()
    {
        var (client, handler) = Build(
            new Gegenstelle()
                .Antwortet(HttpStatusCode.OK)
                .Antwortet(HttpStatusCode.OK, Ergebnis("Statusreport · abgelegt")));

        using var _ = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Handed, ergebnis.Status);
        Assert.Equal("Statusreport · abgelegt", ergebnis.Message);
        Assert.Equal(7, ergebnis.Intake!.DocumentId);
        Assert.Equal(ScanIntakeOutcome.Assigned, ergebnis.Intake.Outcome);

        Assert.Equal("/api/auth/login", handler.Wege[0]);
        Assert.StartsWith("/api/scan/intake", handler.Wege[1]);
    }

    /// <summary>
    /// Die Herkunft der Datei geht mit hinaus.
    /// </summary>
    /// <remarks>
    /// Sie steht danach als Beschreibung am Dokument. Wer im Eingang einen Beleg findet, den er
    /// nicht erwartet hat, sieht damit, aus welchem Ordner er kam.
    /// </remarks>
    [Fact]
    public async Task Die_Herkunft_steht_in_der_Adresse()
    {
        var (client, handler) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.OK).Antwortet(HttpStatusCode.OK, Ergebnis()));

        using var _ = client;
        var pfad = Write("statusreport 2014.pdf");
        await client.HandOverAsync(pfad, CancellationToken.None);

        Assert.Contains(Uri.EscapeDataString(pfad), handler.Wege[1]);
    }

    /// <summary>Nur einmal anmelden: die Sitzung gilt für alle weiteren Dateien.</summary>
    [Fact]
    public async Task Angemeldet_wird_einmal()
    {
        var (client, handler) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.OK).Antwortet(HttpStatusCode.OK, Ergebnis()));

        using var _ = client;
        await client.HandOverAsync(Write("eins.pdf"), CancellationToken.None);
        await client.HandOverAsync(Write("zwei.pdf"), CancellationToken.None);

        Assert.Single(handler.Wege, w => w == "/api/auth/login");
    }

    // ── Die abgelaufene Sitzung ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein 401 auf die Einlieferung führt zu genau einer neuen Anmeldung und einem zweiten Versuch.
    /// </summary>
    /// <remarks>
    /// Die Sitzung hält zwölf Stunden, der Dienst läuft länger. Ohne diesen zweiten Anlauf bliebe
    /// jede Datei nach Ablauf der Sitzung liegen, bis jemand den Dienst neu startet.
    /// </remarks>
    [Fact]
    public async Task Eine_abgelaufene_Sitzung_wird_einmal_erneuert()
    {
        var (client, handler) = Build(
            new Gegenstelle()
                .Antwortet(HttpStatusCode.OK)                               // Anmeldung
                .Antwortet(HttpStatusCode.Unauthorized)                     // Einlieferung: abgelaufen
                .Antwortet(HttpStatusCode.OK)                               // Anmeldung erneut
                .Antwortet(HttpStatusCode.OK, Ergebnis("beim zweiten Mal")));

        using var _ = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Handed, ergebnis.Status);
        Assert.Equal("beim zweiten Mal", ergebnis.Message);

        Assert.Equal(
            ["/api/auth/login", "/api/scan/intake", "/api/auth/login", "/api/scan/intake"],
            [.. handler.Wege.Select(w => w.Split('?')[0])]);
    }

    // ── Was der Server sonst sagt ──────────────────────────────────────────────────────────

    /// <summary>Eine abgelehnte Anmeldung beendet den Durchgang und wird nicht wiederholt.</summary>
    /// <remarks>
    /// Dasselbe Passwort wird beim zweiten Mal nicht richtig, und die Anmeldebremse der API zählt
    /// jeden Versuch mit.
    /// </remarks>
    [Fact]
    public async Task Eine_abgelehnte_Anmeldung_blockiert()
    {
        var (client, handler) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.Unauthorized, Problem("Zugang unbekannt.")));

        using var _ = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Blocked, ergebnis.Status);
        Assert.Contains("dienst@haushalt-kielmayer.de", ergebnis.Message);
        Assert.Contains("Zugang unbekannt.", ergebnis.Message);

        // Kein zweiter Anlauf und keine Einlieferung.
        Assert.Single(handler.Wege);
    }

    /// <summary>Die Meldung des Servers wird durchgereicht — sie steht später im Protokoll.</summary>
    [Fact]
    public async Task Eine_abgelehnte_Datei_traegt_den_Grund_des_Servers()
    {
        var (client, _) = Build(
            new Gegenstelle()
                .Antwortet(HttpStatusCode.OK)
                .Antwortet(HttpStatusCode.BadRequest, Problem("Dateityp nicht zugelassen.")));

        using var _c = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Rejected, ergebnis.Status);
        Assert.Equal("Dateityp nicht zugelassen.", ergebnis.Message);
    }

    /// <summary>Ohne Problem-Details im Rumpf bleibt der Statuscode als Auskunft.</summary>
    [Fact]
    public async Task Ohne_Meldung_nennt_der_Grund_den_Statuscode()
    {
        var (client, _) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.OK).Antwortet(HttpStatusCode.InternalServerError));

        using var _c = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Deferred, ergebnis.Status);
        Assert.Contains("500", ergebnis.Message);
    }

    /// <summary>Eine leere Antwort ist kein Erfolg, den man weiterreichen könnte.</summary>
    /// <remarks>
    /// Sonst wanderte die Datei nach „erledigt“, ohne dass jemand weiß, ob sie angekommen ist.
    /// Liegen bleiben und es noch einmal versuchen ist die ehrlichere Antwort.
    /// </remarks>
    [Fact]
    public async Task Eine_leere_Antwort_gilt_nicht_als_uebergeben()
    {
        var (client, _) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.OK).Antwortet(HttpStatusCode.OK, "null"));

        using var _c = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Deferred, ergebnis.Status);
        Assert.Contains("leer", ergebnis.Message);
    }

    /// <summary>Kein Server heißt: der ganze Durchgang endet, keine Datei zahlt dafür.</summary>
    [Fact]
    public async Task Ohne_Verbindung_endet_der_Durchgang()
    {
        var (client, _) = Build(new Gegenstelle().Wirft(HttpRequestError.ConnectionError));

        using var _c = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Blocked, ergebnis.Status);
        Assert.Contains("nicht erreichbar", ergebnis.Message);
    }

    /// <summary>
    /// Ein Abbruch mitten im Senden gehört dieser Datei.
    /// </summary>
    /// <remarks>
    /// Der Fall aus dem Betrieb: der Server schließt die Verbindung, weil der Rumpf zu groß ist.
    /// Als „keine Verbindung“ gezählt, stünde der ganze Eingang still.
    /// </remarks>
    [Fact]
    public async Task Ein_Abbruch_beim_Senden_zaehlt_der_Datei()
    {
        var (client, _) = Build(
            new Gegenstelle().Antwortet(HttpStatusCode.OK).Wirft(HttpRequestError.ResponseEnded));

        using var _c = client;
        var ergebnis = await client.HandOverAsync(Write(), CancellationToken.None);

        Assert.Equal(HandoverStatus.Deferred, ergebnis.Status);
        Assert.Contains("abgebrochen", ergebnis.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
