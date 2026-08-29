using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Holt einmal am Tag nach Börsenschluss die Kurse — v5-Handoff, Abschnitt 16.1.
/// </summary>
/// <remarks>
/// <para><b>Pull, nicht Push, und einmal am Tag.</b> Ein Abruf bei jedem Seitenaufruf wäre bei
/// einer inoffiziellen Quelle der schnellste Weg zur Sperre; für eine Vermögensübersicht bringt
/// er ohnehin nichts. Wer zwischendurch einen frischen Stand will, drückt den Knopf.</para>
/// <para>Der Dienst läuft je Haushalt: die Kurszeitreihe ist mandantengefiltert wie alles
/// andere, und ein Kurs, den ein Haushalt geholt hat, gehört nicht dem nächsten.</para>
/// </remarks>
public sealed class QuoteRefreshWorker(
    IServiceScopeFactory scopes,
    QuoteOptions options,
    IClock clock,
    ILogger<QuoteRefreshWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            log.LogInformation("Kursabruf ist abgeschaltet — es läuft kein Zeitplan.");
            return;
        }

        log.LogInformation("Kursabruf täglich um {Zeit} Uhr.", options.Time);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UntilNext(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunAsync(stoppingToken);
        }
    }

    /// <summary>Wie lange bis zum nächsten Termin.</summary>
    /// <remarks>
    /// Gegen die Uhr der Anwendung gerechnet, nicht gegen <c>DateTime.Now</c> — sonst liefe der
    /// Zeitplan in einer Demo-Installation mit fester Uhr gegen ein anderes Heute als der Rest.
    /// </remarks>
    private TimeSpan UntilNext()
    {
        var jetzt = clock.Now;
        var heute = jetzt.Date.Add(options.Time.ToTimeSpan());
        var naechster = heute > jetzt ? heute : heute.AddDays(1);

        return naechster - jetzt;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinanzAppDbContext>();

            // Ohne gesetzten Haushalt sähe der Mandantenfilter nichts, und der Durchgang liefe
            // ins Leere. Deshalb einer nach dem anderen, jeder mit eigenem Kontext.
            var haushalte = await db.Households.IgnoreQueryFilters()
                .Select(h => h.Id)
                .ToListAsync(ct);

            foreach (var haushalt in haushalte)
            {
                using var eigener = scopes.CreateScope();
                var kontext = eigener.ServiceProvider.GetRequiredService<FinanzAppDbContext>();
                kontext.CurrentHouseholdId = haushalt;

                var dienst = new QuoteService(
                    kontext,
                    eigener.ServiceProvider.GetRequiredService<IQuoteSource>(),
                    options,
                    clock);

                var ergebnis = await dienst.RefreshAsync(manual: false, ct);

                log.LogInformation(
                    "Kursabruf Haushalt {Haushalt}: {Meldung}", haushalt, ergebnis.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Beim Herunterfahren ist das kein Fehler.
        }
        catch (Exception ex)
        {
            // Ein gescheiterter Durchgang darf den Zeitplan nicht beenden: morgen ist die
            // Quelle vielleicht wieder da, und bis dahin rechnet die Bewertung weiter mit dem
            // gespeicherten Kurs.
            log.LogWarning(ex, "Der geplante Kursabruf ist gescheitert.");
        }
    }
}
