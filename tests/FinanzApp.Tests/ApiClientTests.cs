using System.Net;
using FinanzApp.Client.Services;

namespace FinanzApp.Tests;

/// <summary>
/// Der HTTP-Zugriff des Clients gegen einen Attrappen-Server.
/// </summary>
/// <remarks>
/// Anlass war ein echter Fehlgriff: ein Endpunkt, der <c>204 No Content</c> antwortet, wurde über
/// den Helfer gerufen, der die Antwort als JSON liest. Ein leerer Rumpf ohne <c>Content-Type</c>
/// ist kein JSON — der Aufruf flog dem Aufrufer um die Ohren, statt still zu gelingen. Für solche
/// Endpunkte gibt es <c>SendWithoutResultAsync</c>; diese Tests halten die Zuordnung fest.
/// </remarks>
public sealed class ApiClientTests
{
    /// <summary>Antwortet immer gleich und merkt sich, was ankam.</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Received { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received = request;

            // Genau wie ASP.NET Core bei Results.NoContent(): leerer Rumpf, kein Content-Type.
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent([]),
            });
        }
    }

    private static (FinanzAppApi Api, StubHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.NoContent)
    {
        var handler = new StubHandler(status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (new FinanzAppApi(http), handler);
    }

    [Fact]
    public async Task Eine_Buchung_loeschen_vertraegt_die_leere_Antwort()
    {
        var (api, handler) = Build();

        await api.DeleteTransactionAsync(42);

        Assert.Equal(HttpMethod.Delete, handler.Received!.Method);
        Assert.Equal("/api/transactions/42", handler.Received.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Passwort_aendern_vertraegt_die_leere_Antwort()
    {
        var (api, handler) = Build();

        await api.ChangePasswordAsync("Demo-Haushalt-2026!", "Neues-Passwort-2026!");

        Assert.Equal(HttpMethod.Post, handler.Received!.Method);
        Assert.Equal("/api/auth/password", handler.Received.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Ein_Fehler_kommt_als_ApiException_und_nicht_roh()
    {
        var (api, _) = Build(HttpStatusCode.BadRequest);

        // Die Oberfläche fängt ApiException und stellt die Meldung ans Feld. Fliegt etwas anderes,
        // fängt sie es nicht — und die Seite bleibt mit einem Fehlerbalken stehen.
        await Assert.ThrowsAsync<ApiException>(
            () => api.ChangePasswordAsync("alt-alt-alt-1!", "neu-neu-neu-2!"));
    }

    // ── Was der Server sagt, und was der Client daraus macht ───────────────────────────────

    /// <summary>
    /// Kein Depot im Haushalt ist kein Fehler.
    /// </summary>
    /// <remarks>
    /// Der Endpunkt antwortet mit 404. Gelesen wurde das über <c>GetFromJsonAsync</c>, und das
    /// wirft bei jedem Status außerhalb von 2xx — die Seite meldete daraufhin „Keine Verbindung
    /// zum Server“, obwohl der Server sauber geantwortet hatte.
    /// </remarks>
    [Fact]
    public async Task Ohne_Depot_kommt_null_und_keine_Ausnahme()
    {
        var (api, handler) = Build(HttpStatusCode.NotFound);

        Assert.Null(await api.GetPortfolioAsync());
        Assert.Equal("/api/portfolio", handler.Received!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Ohne_Darlehen_kommt_null_und_keine_Ausnahme()
    {
        var (api, _) = Build(HttpStatusCode.NotFound);

        Assert.Null(await api.GetPrimaryLoanAsync());
    }

    /// <summary>
    /// Ein Serverfehler bleibt ein Fehler und nennt sich nicht „kein Depot“.
    /// </summary>
    /// <remarks>
    /// Nur die 404 heißt „gibt es nicht“. Fing der Aufruf jede Ausnahme ab, sähe ein Ausfall
    /// aus wie ein leerer Bestand — und der Bereich schwiege über einen Ausfall.
    /// </remarks>
    [Fact]
    public async Task Ein_Serverfehler_wird_nicht_zu_einem_leeren_Depot()
    {
        var (api, _) = Build(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<ApiException>(() => api.GetPortfolioAsync());
        await Assert.ThrowsAsync<ApiException>(() => api.GetPortfolioGainAsync());
    }

    /// <summary>
    /// Eine abgelehnte Anfrage ist keine fehlende Verbindung.
    /// </summary>
    /// <remarks>
    /// „Keine Verbindung zum Server“ behauptet eine Ursache. Hat der Server geantwortet, ist
    /// sie widerlegt — und der Benutzer sucht den Fehler an der falschen Stelle.
    /// </remarks>
    [Fact]
    public async Task Ein_abgelehnter_Zugriff_meldet_nicht_die_Verbindung()
    {
        var (api, _) = Build(HttpStatusCode.Forbidden);

        var fehler = await Assert.ThrowsAsync<ApiException>(() => api.GetPortfolioAsync());

        Assert.DoesNotContain("Verbindung", fehler.Message);
        Assert.Contains("Rechte", fehler.Message);
    }
}
