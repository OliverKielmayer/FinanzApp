using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Füllt die Kurszeitreihe beim Start aus dem eigenen Bestand.
/// </summary>
/// <remarks>
/// <para>Beim Start und nicht beim Abruf: der Nachtrag betrifft Ausführungen und
/// Bestandsnachweise, die längst da sind, und er soll auch dann greifen, wenn nie eine Quelle
/// eingerichtet wird. Ein Haushalt, der sein Depot von Hand führt, hat danach trotzdem einen
/// Verlauf.</para>
/// <para><b>Kein Abruf.</b> Hier geht keine Anfrage nach außen — es werden nur Kurse
/// eingesammelt, die die Anwendung ohnehin gespeichert hat.</para>
/// </remarks>
public static class QuoteStartup
{
    public static async Task BackfillAsync(
        IServiceProvider services, FinanzAppDbContext db, ILogger logger)
    {
        try
        {
            var haushalte = await db.Households.IgnoreQueryFilters()
                .Select(h => h.Id)
                .ToListAsync();

            var options = services.GetRequiredService<QuoteOptions>();
            var clock = services.GetRequiredService<IClock>();

            foreach (var haushalt in haushalte)
            {
                // Ein eigener Kontext je Haushalt: der Mandantenfilter hängt am Kontext, und
                // ohne gesetzten Haushalt sähe er nichts.
                await using var kontext = new FinanzAppDbContext(
                    services.GetRequiredService<DbContextOptions<FinanzAppDbContext>>())
                {
                    CurrentHouseholdId = haushalt,
                };

                var dienst = new QuoteService(kontext, new NoQuoteSource(), options, clock);
                var neu = await dienst.BackfillAsync();

                if (neu > 0)
                {
                    logger.LogInformation(
                        "Kursverlauf um {Anzahl} Punkte aus Ausführungen und Bestandsnachweisen "
                        + "ergänzt (Haushalt {Haushalt}).", neu, haushalt);
                }
            }
        }
        catch (Exception ex)
        {
            // Ein misslungener Nachtrag darf den Start nicht verhindern: die Anwendung läuft
            // auch ohne Kursverlauf, sie zeigt dann eben keinen.
            logger.LogWarning(ex, "Der Kursverlauf ließ sich nicht aus dem Bestand ergänzen.");
        }
    }
}
