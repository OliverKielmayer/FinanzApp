using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Der Zugang zur FinanzApp: anmelden und Dateien einliefern.
/// </summary>
/// <remarks>
/// <para>Angemeldet wird wie ein Browser, mit demselben Cookie — die Anwendung kennt keinen
/// zweiten Weg herein, und einen dafür zu erfinden hieße, die Sitzungsverwaltung zu umgehen, die
/// es schon gibt. Der Vorteil ist handfest: die Sitzung des Dienstes steht in der
/// Benutzerverwaltung und lässt sich dort widerrufen wie jede andere.</para>
/// <para>Die Sitzung hält zwölf Stunden und bleibt serverseitig widerrufbar. Der Dienst merkt
/// ihr Ende an einem 401 und meldet sich <em>einmal</em> neu an. Eine abgelehnte Anmeldung wird
/// nicht wiederholt: dasselbe Passwort wird beim zweiten Mal nicht richtig, und die
/// Anmeldebremse der API zählt jeden Versuch mit.</para>
/// </remarks>
public sealed class IntakeClient : IIntakeClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WatchOptions options;
    private readonly ILogger<IntakeClient> log;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);

    private bool signedIn;

    public IntakeClient(WatchOptions options, ILogger<IntakeClient> log)
        // Eigener Handler mit Cookie-Behälter: das Anmelde-Cookie muss über alle Anfragen hinweg
        // erhalten bleiben, und genau dafür ist er da.
        : this(options, log, new HttpClientHandler { CookieContainer = new CookieContainer() })
    {
    }

    /// <summary>
    /// Mit eigenem Übertragungsweg — für Prüfungen ohne Server.
    /// </summary>
    /// <remarks>
    /// Die Einordnung eines Fehlschlags entscheidet, ob eine Datei liegen bleibt, beiseitewandert
    /// oder den Durchgang beendet. Das lässt sich gegen einen echten Server nicht verlässlich
    /// herstellen — ein Server, der auf Kommando 401 sagt und danach 200, ist kein Server.
    /// </remarks>
    public IntakeClient(WatchOptions options, ILogger<IntakeClient> log, HttpMessageHandler handler)
    {
        this.options = options;
        this.log = log;

        http = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.BaseAddress),

            // Großzügig: eine Einlieferung liest ein PDF, analysiert es und legt es ab. Zwei
            // Minuten sind kein Wartezimmer, sondern der Unterschied zwischen „langsam“ und
            // „scheinbar kaputt“.
            Timeout = TimeSpan.FromMinutes(2),
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd("FinanzApp-Ordnerdienst/0.4 (Windows)");
    }

    /// <summary>Reicht eine Datei an die FinanzApp weiter.</summary>
    public async Task<HandoverResult> HandOverAsync(string path, CancellationToken ct)
    {
        var (result, sessionExpired) = await SendAsync(path, ct);

        // Nur der eine Fall wird wiederholt: die Sitzung war abgelaufen. Die Datei liegt noch da,
        // es hat sie nur niemand angenommen.
        return sessionExpired ? (await SendAsync(path, ct)).Result : result;
    }

    /// <summary>
    /// Ein Übergabeversuch.
    /// </summary>
    /// <returns>
    /// Das Ergebnis und die Angabe, ob es an einer abgelaufenen Sitzung lag — nur dann lohnt ein
    /// zweiter Anlauf. Ein Zustandsfeld statt eines Rückgabewerts wäre hier eine Wette darauf,
    /// dass nie zwei Übergaben gleichzeitig laufen.
    /// </returns>
    private async Task<(HandoverResult Result, bool SessionExpired)> SendAsync(
        string path, CancellationToken ct)
    {
        if (!signedIn && await SignInAsync(ct) is { } problem)
        {
            return (new HandoverResult(HandoverStatus.Blocked, problem), false);
        }

        try
        {
            // Zum Lesen geöffnet und für andere nicht gesperrt: der Scanner ist längst fertig,
            // aber ein Virenwächter oder eine Sicherung liest vielleicht gerade mit, und daran
            // soll keine Übergabe scheitern.
            await using var content = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            using var form = new MultipartFormDataContent();
            using var file = new StreamContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", Path.GetFileName(path));

            var address = "api/scan/intake?source=" + Uri.EscapeDataString(path);
            using var response = await http.PostAsync(address, form, ct);

            if (response.IsSuccessStatusCode)
            {
                var intake = await response.Content.ReadFromJsonAsync<ScanIntakeResultDto>(Json, ct);

                return intake is null
                    ? (new HandoverResult(HandoverStatus.Deferred, "Die Antwort des Servers war leer."), false)
                    : (new HandoverResult(HandoverStatus.Handed, intake.Summary) { Intake = intake }, false);
            }

            // Ein 401 macht die gemerkte Anmeldung ungültig, sonst probierte der nächste Versuch
            // mit derselben abgelaufenen Sitzung weiter.
            var abgelaufen = response.StatusCode is HttpStatusCode.Unauthorized;
            if (abgelaufen)
            {
                signedIn = false;
            }

            var beschreibung = await DescribeAsync(response, ct);
            return (new HandoverResult(Handover.StatusFor(response.StatusCode), beschreibung), abgelaufen);
        }
        catch (IOException ex)
        {
            // Die Datei ist doch noch belegt. Nicht die Verbindung, also ein Versuch dieser Datei.
            return (new HandoverResult(HandoverStatus.Deferred, "Die Datei ist belegt: " + ex.Message), false);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Das Dienstkonto darf die Datei nicht lesen. Das ändert sich nicht von selbst, aber
            // beiseitelegen lässt sie sich aus demselben Grund auch nicht — also melden und
            // liegen lassen.
            return (new HandoverResult(
                HandoverStatus.Deferred, "Kein Zugriff auf die Datei: " + ex.Message), false);
        }
        catch (HttpRequestException ex)
        {
            return (Handover.Failure(ex), false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (new HandoverResult(
                HandoverStatus.Deferred, "Der Server hat nicht rechtzeitig geantwortet."), false);
        }
    }

    /// <summary>
    /// Meldet den Dienst an. Gibt <c>null</c> zurück, wenn es geklappt hat, sonst den Grund.
    /// </summary>
    /// <remarks>
    /// Ein Tor um die Anmeldung: laufen zwei Übergaben gleichzeitig in einen 401, meldete sich
    /// der Dienst sonst zweimal an und ließe eine Sitzung ungenutzt zurück.
    /// </remarks>
    private async Task<string?> SignInAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);

        try
        {
            if (signedIn)
            {
                return null;
            }

            var request = new LoginRequest
            {
                Email = options.Email,
                Password = options.Password,

                // Eine dauerhafte Sitzung: der Dienst hat kein Fenster, das sich schließt.
                StaySignedIn = true,
            };

            using var response = await http.PostAsJsonAsync("api/auth/login", request, Json, ct);

            if (!response.IsSuccessStatusCode)
            {
                return $"Anmeldung als {options.Email} abgelehnt: " + await DescribeAsync(response, ct);
            }

            signedIn = true;
            log.LogInformation("Als {Zugang} an {Adresse} angemeldet.", options.Email, options.BaseAddress);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      && !ct.IsCancellationRequested)
        {
            return $"Die FinanzApp unter {options.BaseAddress} ist nicht erreichbar: {ex.Message}";
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Die Meldung des Servers, wenn er eine mitschickt.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, ct);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Keine Problem-Details im Rumpf — dann bleibt der Statuscode.
        }

        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
    }

    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }

    private sealed record ProblemPayload(string? Title, string? Detail);
}
