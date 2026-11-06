using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Fragt ein lokal laufendes Modell über HTTP.
/// </summary>
/// <remarks>
/// <para>Ollama spricht eine schlichte Schnittstelle: ein POST auf <c>/api/generate</c>, die
/// Antwort als ein Feld. Mehr braucht es nicht — es wird keine Unterhaltung geführt, sondern
/// einmal eine Frage gestellt.</para>
/// <para><b>Temperatur null.</b> Dieselbe Frage zum selben Beleg soll dieselbe Antwort geben.
/// Ein Nachlauf, dessen Ergebnis bei jedem Druck anders ausfällt, wäre nicht nachvollziehbar,
/// und niemand könnte sagen, ob eine Änderung am Programm etwas verbessert hat.</para>
/// <para><b>Fehler sind kein Fehler.</b> Läuft kein Modell, ist der Port zu, dauert es zu
/// lange — dann findet dieser Weg nichts, und das ist genau der Zustand ohne Modell. Ein
/// Beleg, der deswegen nicht eingeliefert werden könnte, machte die Ablage von etwas
/// abhängig, das standardmäßig gar nicht da ist.</para>
/// </remarks>
public sealed class OllamaActionFinder(
    HttpClient client,
    LanguageModelOptions options,
    ILogger<OllamaActionFinder> log) : IDocumentActionFinder
{
    public bool Available => true;

    public async Task<string?> FindActionLineAsync(string text, CancellationToken ct = default)
    {
        var kandidaten = ActionEvidence.Candidates(text, options.MaxCharacters);
        if (kandidaten.Count == 0)
        {
            return null;
        }

        var frage = ActionEvidence.PromptFor(kandidaten);

        try
        {
            var antwort = await client.PostAsJsonAsync(
                "/api/generate",
                new OllamaRequest
                {
                    Model = options.Model,
                    Prompt = frage,
                    Stream = false,
                    Options = new OllamaSettings { Temperature = 0, NumPredict = 6 },
                },
                ct);

            if (!antwort.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "Sprachmodell {Modell} antwortete mit {Code}. Der Beleg bleibt ohne Forderung.",
                    options.Model, (int)antwort.StatusCode);
                return null;
            }

            var inhalt = await antwort.Content.ReadFromJsonAsync<OllamaResponse>(ct);
            return ActionEvidence.LineFrom(inhalt?.Response, kandidaten);
        }
        catch (Exception fehler) when (fehler is HttpRequestException or TaskCanceledException)
        {
            // Kein laufendes Modell, kein offener Port, zu lange gewartet. Alles drei heißt
            // dasselbe: Es gibt heute keine zweite Schicht.
            log.LogInformation(
                "Sprachmodell unter {Adresse} nicht erreichbar ({Grund}). Es wird nur gelesen, "
                    + "was die Leseregeln finden.",
                options.Url, fehler.Message);
            return null;
        }
    }

    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("prompt")]
        public required string Prompt { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("options")]
        public OllamaSettings? Options { get; init; }
    }

    private sealed class OllamaSettings
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; init; }
    }
}
