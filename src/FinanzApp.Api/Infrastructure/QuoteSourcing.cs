using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Ein abgerufener Kurs.</summary>
/// <param name="Isin">Das Papier.</param>
/// <param name="Date">Der Handelstag, auf den der Kurs gehört.</param>
/// <param name="Close">Der Kurs.</param>
/// <param name="Currency">Die Währung.</param>
/// <param name="Source">Woher er kommt — steht später an jedem gespeicherten Punkt.</param>
public sealed record QuoteReading(
    string Isin, DateOnly Date, decimal Close, string Currency, string Source);

/// <summary>
/// Was ein Abrufversuch ergeben hat.
/// </summary>
/// <remarks>
/// Ein Fehlschlag ist keine Ausnahme, sondern ein Ergebnis: die Anwendung rechnet danach mit
/// dem gespeicherten Kurs weiter und sagt, wie alt er ist. Eine geworfene Ausnahme müsste jeder
/// Aufrufer erst wieder in diese Form bringen.
/// </remarks>
public sealed record QuoteAttempt
{
    public QuoteReading? Quote { get; init; }

    /// <summary>Warum es nicht geklappt hat. <c>null</c>, wenn es geklappt hat.</summary>
    public string? Problem { get; init; }

    public bool Ok => Quote is not null;

    public static QuoteAttempt Found(QuoteReading quote) => new() { Quote = quote };

    public static QuoteAttempt Failed(string problem) => new() { Problem = problem };
}

/// <summary>
/// Woher Kurse kommen.
/// </summary>
/// <remarks>
/// <para>Eine Schnittstelle mit der ISIN als Schlüssel — v5-Handoff, Abschnitt 16.1. Zweitquelle,
/// Handpflege und „gar keine Quelle“ sind Implementierungen davon und kein Sonderfall.</para>
/// <para>Sie liefert <b>einen</b> Kurs je Aufruf, nicht eine Reihe. Das ist keine Einschränkung
/// der Schnittstelle, sondern das, was frei zugängliche Quellen hergeben: den letzten
/// festgestellten Kurs. Die Reihe entsteht dadurch, dass die Anwendung sie führt.</para>
/// </remarks>
public interface IQuoteSource
{
    /// <summary>Wie die Quelle heißt — der Text steht an jedem gespeicherten Kurs.</summary>
    string Name { get; }

    Task<QuoteAttempt> FetchAsync(string isin, CancellationToken ct = default);
}

/// <summary>
/// Keine Quelle. Der eingebaute Stand, wenn keine angebunden oder sie abgeschaltet ist.
/// </summary>
/// <remarks>
/// Sie erfindet nichts. Ohne Quelle bleibt die gespeicherte Reihe stehen, und die Bewertung
/// rechnet mit ihrem jüngsten Punkt — genau wie bei einem Ausfall.
/// </remarks>
public sealed class NoQuoteSource : IQuoteSource
{
    public string Name => "keine Kursquelle";

    public Task<QuoteAttempt> FetchAsync(string isin, CancellationToken ct = default)
        => Task.FromResult(QuoteAttempt.Failed("Es ist keine Kursquelle eingerichtet."));
}

public sealed class QuoteOptions
{
    public const string SectionName = "Quotes";

    /// <summary>Ob überhaupt abgerufen wird.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Uhrzeit des täglichen Abrufs, lokale Zeit.
    /// </summary>
    /// <remarks>
    /// Nach Börsenschluss. Ein Abruf bei jedem Seitenaufruf wäre bei einer inoffiziellen Quelle
    /// der schnellste Weg zur Sperre — und für eine Vermögensübersicht ohne Nutzen.
    /// </remarks>
    public string DailyAt { get; set; } = "18:00";

    /// <summary>Handelsplatz. <c>XETR</c> ist Xetra, <c>XFRA</c> der Frankfurter Parketthandel.</summary>
    public string Venue { get; set; } = "XETR";

    /// <summary>Pause zwischen zwei Papieren, in Millisekunden.</summary>
    /// <remarks>
    /// Ein Depot mit zwanzig Positionen soll nicht als Lastspitze bei der Gegenseite ankommen.
    /// </remarks>
    public int DelayMilliseconds { get; set; } = 400;

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Die Uhrzeit als solche, oder 18:00, wenn die Angabe unbrauchbar ist.</summary>
    public TimeOnly Time
        => TimeOnly.TryParse(DailyAt, CultureInfo.InvariantCulture, out var zeit)
            ? zeit
            : new TimeOnly(18, 0);
}

/// <summary>
/// Kurse von der Börse Frankfurt.
/// </summary>
/// <remarks>
/// <para><b>Die Quelle ist inoffiziell.</b> Es gibt keine dokumentierte Schnittstelle, keine
/// Zusage über Bestand, Format oder Nutzungsrecht. Genau deshalb hängt hier nichts dran außer
/// dem Nachschub: gespeichert wird in der eigenen Reihe, bewertet wird aus ihr, und fällt die
/// Quelle aus, bleibt alles stehen — v5-Handoff, Abschnitt 16.1.</para>
/// <para>Abgerufen wird der zuletzt festgestellte Kurs. Einen Verlauf gibt die Quelle nicht
/// heraus: der zugehörige Endpunkt verlangt eine Signatur, die der Anbieter nur seiner eigenen
/// Oberfläche mitgibt. Sie nachzubauen hieße, eine Zugangssperre zu umgehen; die Reihe wächst
/// stattdessen Tag für Tag mit den eigenen Abrufen.</para>
/// </remarks>
public sealed class BoerseFrankfurtQuoteSource(
    HttpClient http, QuoteOptions options, ILogger<BoerseFrankfurtQuoteSource> log) : IQuoteSource
{
    public const string SourceName = "Börse Frankfurt";

    /// <summary>Xetra und Frankfurt notieren in Euro — das ist eine Eigenschaft des Platzes.</summary>
    private const string Currency = "EUR";

    public string Name => SourceName;

    public async Task<QuoteAttempt> FetchAsync(string isin, CancellationToken ct = default)
    {
        if (!options.Enabled)
        {
            return QuoteAttempt.Failed("Der Kursabruf ist abgeschaltet.");
        }

        var url = "v1/data/quote_box/single"
                  + $"?isin={Uri.EscapeDataString(isin)}&mic={Uri.EscapeDataString(options.Venue)}";

        try
        {
            var antwort = await http.GetAsync(url, ct);

            if (!antwort.IsSuccessStatusCode)
            {
                return QuoteAttempt.Failed($"Die Kursquelle antwortet mit {(int)antwort.StatusCode}.");
            }

            var kurs = await antwort.Content.ReadFromJsonAsync<QuoteBox>(ct);

            if (kurs?.LastPrice is not { } preis || preis <= 0m)
            {
                return QuoteAttempt.Failed($"Die Kursquelle kennt {isin} nicht.");
            }

            // Der Handelstag, nicht der Abrufzeitpunkt. Ein Kurs von gestern Abend, heute früh
            // geholt, gehört auf gestern — sonst behauptete die Reihe einen Kurs für heute.
            var tag = DateOnly.FromDateTime(
                (kurs.TimestampLastPrice ?? kurs.Timestamp ?? DateTime.UtcNow).ToLocalTime());

            return QuoteAttempt.Found(new QuoteReading(isin, tag, preis, Currency, SourceName));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return QuoteAttempt.Failed("Die Kursquelle antwortet nicht rechtzeitig.");
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Kursabruf für {Isin} fehlgeschlagen", isin);
            return QuoteAttempt.Failed("Die Kursquelle antwortet nicht.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Kursantwort für {Isin} unlesbar", isin);
            return QuoteAttempt.Failed("Die Antwort der Kursquelle war unlesbar.");
        }
    }

    /// <summary>Nur die drei Felder, auf die es ankommt.</summary>
    private sealed record QuoteBox
    {
        [JsonPropertyName("lastPrice")]
        public decimal? LastPrice { get; init; }

        [JsonPropertyName("timestampLastPrice")]
        public DateTime? TimestampLastPrice { get; init; }

        [JsonPropertyName("timestamp")]
        public DateTime? Timestamp { get; init; }
    }
}
