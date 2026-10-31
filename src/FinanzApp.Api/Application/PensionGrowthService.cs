using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Vorsorge: Zuwachs gegen Beitrag.
/// </summary>
/// <remarks>
/// <para>Die Frage, die der Wert eines Vertrags nicht beantwortet: Was hat er selbst
/// erwirtschaftet? Sein Guthaben steigt auch dann, wenn von jedem eingezahlten Euro Kosten
/// abgehen — nur langsamer als die Summe der Einzahlungen.</para>
/// <para>Der Zuwachs ist gemessen: zwei Statusberichte, zwei Stände, die Differenz. Der Beitrag
/// steht in zwei Spalten, und das ist der Kern dieses Dienstes. <b>Gemessen</b> sind die
/// Buchungen, die über die Vertragsnummer an diesem Vertrag hängen — aber nur, wenn sie den
/// ganzen Zeitraum abdecken. <b>Hochgerechnet</b> ist der heutige Monatsbeitrag mal der Zahl
/// der Monate.</para>
/// <para>Beides zu verschmelzen wäre bequem und falsch. Die Berichte reichen hier bis 2012
/// zurück, die Buchungen bis Anfang des laufenden Jahres; eine Zahl aus beiden Quellen sähe
/// genauso aus wie eine gemessene und wäre doch zur Hälfte geraten.</para>
/// </remarks>
public sealed class PensionGrowthService(FinanzAppDbContext db)
{
    public async Task<PensionGrowthDto> GetAsync(CancellationToken ct = default)
    {
        var policen = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var berichte = await db.PolicyReports.AsNoTracking()
            .OrderBy(r => r.AsOf)
            .ToListAsync(ct);

        var ersteBuchung = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense)
            .OrderBy(t => t.BookingDate)
            .Select(t => (DateOnly?)t.BookingDate)
            .FirstOrDefaultAsync(ct);

        // Einmal laden statt je Vertrag: Die Zuordnung läuft über die Vertragsnummer im Text,
        // und das lässt sich in SQL nicht ausdrücken.
        var ausgaben = ersteBuchung is null
            ? []
            : await db.Transactions.AsNoTracking()
                .Where(t => t.Kind == TransactionKind.Expense)
                .Select(t => new Zahlung(t.BookingDate, Math.Abs(t.Amount), t.Purpose, t.BookingText, t.Payee))
                .ToListAsync(ct);

        var zeilen = new List<PensionGrowthRowDto>(policen.Count);
        var summeZuwachs = 0m;
        var summeHochgerechnet = 0m;
        var gezaehlt = 0;
        var uebersprungen = 0;

        foreach (var police in policen)
        {
            var reihe = berichte.Where(r => r.PolicyId == police.Id).ToList();

            if (reihe.Count < 2)
            {
                uebersprungen++;
                zeilen.Add(Leer(police, reihe.Count == 1
                    ? "Nur ein Stand erfasst — ein Zuwachs braucht zwei."
                    : "Kein Stand erfasst."));
                continue;
            }

            var anfang = reihe[^2];
            var ende = reihe[^1];
            var monate = Months(anfang.AsOf, ende.AsOf);

            if (monate <= 0)
            {
                uebersprungen++;
                zeilen.Add(Leer(police, "Die beiden jüngsten Stände liegen im selben Monat."));
                continue;
            }

            var zuwachs = ende.Value - anfang.Value;
            var monatsbeitrag = PaymentIntervals.PerMonth(police.Premium, police.PremiumInterval);
            var hochgerechnet = police.Premium == 0m ? (decimal?)null : monatsbeitrag * monate;

            var (gemessen, anzahl, hinweis) = Measured(police, ausgaben, ersteBuchung, anfang.AsOf, ende.AsOf);

            summeZuwachs += zuwachs;
            summeHochgerechnet += hochgerechnet ?? 0m;
            gezaehlt++;

            zeilen.Add(new PensionGrowthRowDto
            {
                PolicyId = police.Id,
                Name = police.Name,
                Provider = police.Provider,
                From = anfang.AsOf,
                To = ende.AsOf,
                Months = monate,
                ValueFrom = anfang.Value,
                ValueTo = ende.Value,
                Growth = zuwachs,
                Projected = hochgerechnet,
                Measured = gemessen,
                MeasuredCount = anzahl,
                DifferenceProjected = hochgerechnet is { } h ? zuwachs - h : null,
                DifferenceMeasured = gemessen is { } g ? zuwachs - g : null,
                Note = hochgerechnet is null
                    ? "Kein Beitrag hinterlegt — ohne ihn gibt es nichts hochzurechnen."
                    : hinweis,
            });
        }

        return new PensionGrowthDto
        {
            Rows = [.. zeilen.OrderByDescending(z => z.Growth ?? decimal.MinValue).ThenBy(z => z.Name)],
            TotalGrowth = decimal.Round(summeZuwachs, 2),
            TotalProjected = decimal.Round(summeHochgerechnet, 2),
            CountedPolicies = gezaehlt,
            SkippedPolicies = uebersprungen,
            BookingsFrom = ersteBuchung,
            Note = Note(ersteBuchung, zeilen),
        };
    }

    /// <summary>Eine Zahlung, so weit sie für die Zuordnung gebraucht wird.</summary>
    private sealed record Zahlung(DateOnly Day, decimal Amount, string? Purpose, string? BookingText, string Payee);

    /// <summary>
    /// Die gemessene Beitragssumme im Zeitraum — oder nichts, mit Begründung.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Bedingungen, und beide sind Ausschlussgründe und keine Feinheiten. Ohne
    /// <b>Vertragsnummer</b> gibt es keine Zuordnung: Nach dem Anbieter zu gehen hieße, bei
    /// sieben Verträgen desselben Hauses jede Buchung an jeden zu hängen.</para>
    /// <para>Und die Buchungen müssen den Zeitraum <b>abdecken</b>. Reichen sie nur in sein
    /// letztes Drittel, ergäbe die Summe eine Zahl, die wie eine ganze aussieht und ein Drittel
    /// ist — jeder Vertrag sähe damit besser aus, als er ist.</para>
    /// </remarks>
    private static (decimal? Summe, int Anzahl, string Hinweis) Measured(
        Policy police, IReadOnlyList<Zahlung> ausgaben, DateOnly? ersteBuchung,
        DateOnly von, DateOnly bis)
    {
        if (PolicyService.Normalise(police.PolicyNumber) is not { Length: >= 4 } nummer)
        {
            return (null, 0, "Ohne Vertragsnummer lassen sich die Beiträge nicht zuordnen.");
        }

        if (ersteBuchung is not { } erste)
        {
            return (null, 0, "Es liegen keine Buchungen vor.");
        }

        if (erste > von)
        {
            return (null, 0,
                $"Buchungen liegen erst ab {GermanFormat.Date(erste)} vor, der Zeitraum beginnt "
                + $"am {GermanFormat.Date(von)}. Gemessen wäre nur ein Teil.");
        }

        var treffer = ausgaben
            .Where(z => z.Day > von && z.Day <= bis)
            .Where(z => Matches(z, nummer))
            .ToList();

        if (treffer.Count == 0)
        {
            return (null, 0, "Keine Beitragsbuchung mit dieser Vertragsnummer im Zeitraum.");
        }

        return (decimal.Round(treffer.Sum(z => z.Amount), 2), treffer.Count, string.Empty);
    }

    private static bool Matches(Zahlung z, string nummer)
        => PolicyService.Normalise(z.Purpose)?.Contains(nummer, StringComparison.Ordinal) == true
           || PolicyService.Normalise(z.BookingText)?.Contains(nummer, StringComparison.Ordinal) == true
           || PolicyService.Normalise(z.Payee)?.Contains(nummer, StringComparison.Ordinal) == true;

    /// <summary>
    /// Volle Monate zwischen zwei Stichtagen.
    /// </summary>
    /// <remarks>
    /// Der Faktor der Hochrechnung, und er soll die Zahl der <em>Abbuchungen</em> treffen, nicht
    /// die Tage. Vom 31.12. zum 30.09. sind das neun — neun Monatsbeiträge, und das deckt sich
    /// mit dem, was auf dem Konto steht.
    /// </remarks>
    private static int Months(DateOnly von, DateOnly bis)
        => ((bis.Year - von.Year) * 12) + bis.Month - von.Month;

    private static PensionGrowthRowDto Leer(Policy police, string hinweis) => new()
    {
        PolicyId = police.Id,
        Name = police.Name,
        Provider = police.Provider,
        Note = hinweis,
    };

    /// <summary>
    /// Der Satz über dem Bericht. Er sagt, worauf die Zahlen beruhen — ohne Beträge.
    /// </summary>
    private static string Note(DateOnly? ersteBuchung, IReadOnlyList<PensionGrowthRowDto> zeilen)
    {
        var gemessen = zeilen.Count(z => z.Measured is not null);

        var kern = "Der Zuwachs ist gemessen: zwei Statusberichte, zwei Stände. Der Beitrag steht "
                   + "in zwei Spalten — hochgerechnet aus dem heutigen Monatsbeitrag, und gemessen "
                   + "aus den Buchungen, wo sie den Zeitraum abdecken.";

        if (gemessen > 0)
        {
            return kern + $" {gemessen} von {zeilen.Count} Verträgen tragen beide Zahlen.";
        }

        return ersteBuchung is { } erste
            ? kern + $" Kein Vertrag trägt beide: Die Buchungen beginnen am {GermanFormat.Date(erste)}, "
                   + "und die Berichtspaare liegen davor. Ältere Kontoauszüge einzulesen schließt die Lücke."
            : kern + " Gemessene Beiträge gibt es nicht, solange keine Buchungen vorliegen.";
    }
}
