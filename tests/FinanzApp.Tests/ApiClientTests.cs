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
}
