using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Kurszeitreihe und ihr Nachschub — v5-Handoff, Abschnitt 16.
/// </summary>
/// <remarks>
/// <para><b>Der Verlauf ist die Datenhaltung, nicht die API.</b> Die Anwendung führt ihre eigene
/// Reihe; die Quelle liefert nur Nachschub. Fällt sie aus oder wird gewechselt, bleibt alles
/// stehen, und bewertet wird weiter mit dem jüngsten gespeicherten Kurs — dessen Datum
/// sichtbar dabeisteht.</para>
/// <para><b>Pull, nicht Push.</b> Abgerufen wird nach Zeitplan und auf Knopfdruck, nie beim
/// Seitenaufruf. Bei einer inoffiziellen Quelle ist das der schnellste Weg zur Sperre — und für
/// eine Vermögensübersicht bringt ein Kurs von vor zwei Minuten nichts.</para>
/// </remarks>
public sealed class QuoteService(
    FinanzAppDbContext db, IQuoteSource source, QuoteOptions options, IClock clock)
{
    /// <summary>Ab wann ein Abruf als veraltet gilt.</summary>
    /// <remarks>
    /// Drei Tage: über ein Wochenende hinweg gibt es keine neuen Schlusskurse, und ein Band,
    /// das jeden Montag „veraltet“ meldet, meldet bald gar nichts mehr.
    /// </remarks>
    private const int StaleAfterDays = 3;

    // ── Abrufen ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Holt für jedes gehaltene Papier einen Kurs und schreibt die Reihe fort.
    /// </summary>
    /// <remarks>
    /// Der Durchgang bricht bei einem Fehlschlag nicht ab: ein Papier, das die Quelle nicht
    /// kennt, darf die anderen nicht mitnehmen. Zwischen zwei Anfragen liegt eine kurze Pause —
    /// ein Depot mit zwanzig Positionen soll nicht als Lastspitze bei der Gegenseite ankommen.
    /// </remarks>
    public async Task<QuoteRefreshDto> RefreshAsync(bool manual, CancellationToken ct = default)
    {
        var begonnen = clock.Now;

        // Erst nachziehen, was im Haus ist: seit dem letzten Durchgang können Ausführungen
        // oder ein Bestandsnachweis dazugekommen sein, und deren Kurse gehören in dieselbe
        // Reihe. Der Nachtrag geht nicht nach außen.
        await BackfillAsync(ct);

        var papiere = await SecuritiesAsync(ct);

        var gespeichert = 0;
        var gescheitert = 0;
        string? grund = null;

        for (var i = 0; i < papiere.Count; i++)
        {
            if (i > 0 && options.DelayMilliseconds > 0)
            {
                await Task.Delay(options.DelayMilliseconds, ct);
            }

            var versuch = await source.FetchAsync(papiere[i], ct);

            if (versuch.Quote is not { } kurs)
            {
                gescheitert++;
                grund ??= versuch.Problem;
                continue;
            }

            await StoreAsync(kurs, ct);
            gespeichert++;
        }

        db.QuoteRuns.Add(new QuoteRun
        {
            StartedAt = begonnen,
            FinishedAt = clock.Now,
            Source = source.Name,
            Requested = papiere.Count,
            Stored = gespeichert,
            Failed = gescheitert,
            Problem = grund,
            Manual = manual,
        });

        await db.SaveChangesAsync(ct);

        return new QuoteRefreshDto
        {
            Requested = papiere.Count,
            Stored = gespeichert,
            Failed = gescheitert,
            Band = await GetBandAsync(ct),
            Message = Message(papiere.Count, gespeichert, gescheitert, grund),
        };
    }

    private static string Message(int gefragt, int gespeichert, int gescheitert, string? grund)
    {
        if (gefragt == 0)
        {
            return "Kein Wertpapier im Bestand — es gibt nichts abzurufen.";
        }

        if (gespeichert == 0)
        {
            return grund ?? "Kein Kurs abgerufen.";
        }

        var satz = $"{gespeichert} {(gespeichert == 1 ? "Kurs" : "Kurse")} abgerufen · Verlauf ergänzt";

        return gescheitert == 0
            ? satz
            : $"{satz} · {gescheitert} ohne Ergebnis";
    }

    /// <summary>
    /// Legt einen Kurs ab oder bringt den des Tages auf den neuen Stand.
    /// </summary>
    /// <remarks>
    /// Ein zweiter Abruf desselben Tages aktualisiert, statt zu verdoppeln — Abschnitt 16.5. Der
    /// eindeutige Index trägt dieselbe Regel auch dann, wenn zwei Durchgänge sich überholen.
    /// </remarks>
    private async Task StoreAsync(QuoteReading kurs, CancellationToken ct)
    {
        var vorhanden = await db.Quotes
            .FirstOrDefaultAsync(q => q.Isin == kurs.Isin && q.Date == kurs.Date, ct);

        if (vorhanden is null)
        {
            db.Quotes.Add(new Quote
            {
                Isin = kurs.Isin,
                Date = kurs.Date,
                Close = kurs.Close,
                Currency = kurs.Currency,
                Source = kurs.Source,
                FetchedAt = clock.Now,
            });

            return;
        }

        vorhanden.Close = kurs.Close;
        vorhanden.Currency = kurs.Currency;
        vorhanden.Source = kurs.Source;
        vorhanden.FetchedAt = clock.Now;
    }

    // ── Aus dem eigenen Bestand ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ergänzt die Reihe um Kurse, die längst im Haus sind.
    /// </summary>
    /// <remarks>
    /// <para>Jede Ausführung und jede Position eines Bestandsnachweises trägt einen Kurs mit
    /// Datum — beobachtete Kurse, keine geschätzten. Die frei zugängliche Quelle gibt keinen
    /// Verlauf heraus; ohne diesen Rückgriff bliebe der Chart monatelang leer, obwohl die
    /// Anwendung die Punkte hat.</para>
    /// <para>Abgerufene Kurse werden dabei <b>nicht</b> überschrieben: ein Börsenschlusskurs ist
    /// belastbarer als der Preis einer einzelnen Ausführung.</para>
    /// </remarks>
    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        // Hier ist die ganze Reihe nötig — es geht ja gerade darum, was noch fehlt. Sie kommt
        // als Schlüsselpaar und nicht als Entität; der Nachtrag läuft beim Start und beim
        // Abruf, nicht bei jeder Ansicht.
        var vorhanden = await db.Quotes.AsNoTracking()
            .Select(q => new { q.Isin, q.Date })
            .ToListAsync(ct);

        var bekannt = vorhanden
            .Select(q => (q.Isin, q.Date))
            .ToHashSet();

        var neu = 0;

        foreach (var punkt in await LedgerPointsAsync(ct))
        {
            if (!bekannt.Add((punkt.Isin, punkt.Date)))
            {
                continue;
            }

            db.Quotes.Add(new Quote
            {
                Isin = punkt.Isin,
                Date = punkt.Date,
                Close = punkt.Close,
                Currency = punkt.Currency,
                Source = punkt.Source,
                FetchedAt = clock.Now,
            });

            neu++;
        }

        if (neu > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return neu;
    }

    /// <summary>Die drei Herkünfte, aus denen die Reihe nachgetragen wird.</summary>
    /// <remarks>
    /// Ihre Reihenfolge entscheidet, wer am selben Tag gewinnt: ein Bestandsnachweis nennt einen
    /// Bewertungskurs, eine Ausführung einen einzelnen Abschluss, eine gepflegte Position eine
    /// Angabe von Hand.
    /// </remarks>
    private static readonly string[] LedgerOrder = ["Bestandsnachweis", "Ausführung", "erfasste Position"];

    /// <summary>Kurse aus dem eigenen Bestand, je Tag der belastbarste.</summary>
    private async Task<List<QuoteReading>> LedgerPointsAsync(CancellationToken ct)
    {
        var ausfuehrungen = await db.DepotTrades.AsNoTracking()
            .Where(t => t.Price > 0m)
            .Select(t => new { t.Isin, t.ExecutedAt, t.Price })
            .ToListAsync(ct);

        var nachweise = await db.DepotStatementPositions.AsNoTracking()
            .Where(p => p.Price > 0m && p.Statement != null)
            .Select(p => new { p.Isin, p.Statement!.AsOf, p.Price })
            .ToListAsync(ct);

        // Auch eine von Hand gepflegte Position ist ein Kurs mit Datum. Wer sein Depot ohne
        // Orderdatei führt, hätte sonst nie einen Verlauf.
        var erfasst = await db.PortfolioPositions.AsNoTracking()
            .Where(p => p.Price > 0m && p.Isin != null && p.Isin != "")
            .Select(p => new { Isin = p.Isin!, p.PriceAsOf, p.Price })
            .ToListAsync(ct);

        var punkte = ausfuehrungen
            .Select(t => new QuoteReading(
                t.Isin, DateOnly.FromDateTime(t.ExecutedAt), t.Price, "EUR", "Ausführung"))
            .Concat(nachweise.Select(p => new QuoteReading(
                p.Isin, p.AsOf, p.Price, "EUR", "Bestandsnachweis")))
            .Concat(erfasst.Select(p => new QuoteReading(
                p.Isin, DateOnly.FromDateTime(p.PriceAsOf), p.Price, "EUR", "erfasste Position")));

        return
        [
            .. punkte
                .GroupBy(p => (p.Isin, p.Date))
                .Select(g => g.OrderBy(p => Array.IndexOf(LedgerOrder, p.Source)).First()),
        ];
    }

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der jüngste gespeicherte Kurs je Papier — die Grundlage jeder Bewertung.
    /// </summary>
    /// <remarks>
    /// Erst den jüngsten Tag je Papier holen, dann diese Zeilen: die Reihe wächst täglich, und
    /// sie für jede Depotansicht vollständig in den Arbeitsspeicher zu ziehen, wäre nach einem
    /// Jahr eine spürbare Bremse an einer Stelle, die bei jedem Seitenaufruf durchläuft.
    /// </remarks>
    public async Task<Dictionary<string, Quote>> LatestAsync(CancellationToken ct = default)
    {
        var juengste = await db.Quotes.AsNoTracking()
            .GroupBy(q => q.Isin)
            .Select(g => new { Isin = g.Key, Date = g.Max(q => q.Date) })
            .ToListAsync(ct);

        if (juengste.Count == 0)
        {
            return new Dictionary<string, Quote>(StringComparer.OrdinalIgnoreCase);
        }

        var tage = juengste.Select(x => x.Date).Distinct().ToList();

        var kandidaten = await db.Quotes.AsNoTracking()
            .Where(q => tage.Contains(q.Date))
            .ToListAsync(ct);

        var gesucht = juengste.Select(x => (x.Isin, x.Date)).ToHashSet();

        return kandidaten
            .Where(q => gesucht.Contains((q.Isin, q.Date)))
            .ToDictionary(q => q.Isin, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Der Zustand des Kursbands.</summary>
    public async Task<QuoteBandDto> GetBandAsync(CancellationToken ct = default)
    {
        var lauf = await db.QuoteRuns.AsNoTracking()
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefaultAsync(ct);

        // Drei Aggregate statt der ganzen Tabelle: das Band braucht nur Anzahl, ältesten und
        // jüngsten Tag, und es steht über jeder Depotansicht.
        var umfang = await db.Quotes.AsNoTracking()
            .GroupBy(q => 1)
            .Select(g => new
            {
                Anzahl = g.Count(),
                Juengster = g.Max(q => q.Date),
                Aeltester = g.Min(q => q.Date),
            })
            .FirstOrDefaultAsync(ct);

        var papiere = await SecuritiesAsync(ct);

        return new QuoteBandDto
        {
            State = State(lauf),
            Source = lauf?.Source ?? source.Name,
            FetchedAt = lauf?.FinishedAt,
            LatestDate = umfang?.Juengster,
            FirstDate = umfang?.Aeltester,
            StoredCount = umfang?.Anzahl ?? 0,
            SecurityCount = papiere.Count,
            Problem = lauf?.Problem,
            CanFetch = options.Enabled && papiere.Count > 0,
        };
    }

    /// <summary>
    /// Wie es um die Kurse steht.
    /// </summary>
    /// <remarks>
    /// Ein Durchgang, in dem <em>kein</em> Kurs ankam, gilt als gescheitert, auch wenn er
    /// technisch durchlief. „Erfolgreich, aber nichts geholt“ wäre für den Leser dasselbe wie
    /// ein Fehler und darf nicht anders aussehen.
    /// </remarks>
    private QuoteState State(QuoteRun? lauf) => lauf switch
    {
        null => QuoteState.Never,
        { Requested: > 0, Stored: 0 } => QuoteState.Failed,
        _ when lauf.FinishedAt.Date.AddDays(StaleAfterDays) < clock.Now.Date => QuoteState.Stale,
        _ => QuoteState.Fresh,
    };

    /// <summary>
    /// Der Kursverlauf eines Papiers.
    /// </summary>
    /// <remarks>
    /// Der Zeitraum schneidet die Reihe, ändert sie aber nicht: Tief, Hoch und die Frage, ob der
    /// Einstand hineinpasst, beziehen sich immer auf das Gezeigte. Alles andere ergäbe eine
    /// Legende, die etwas über einen Ausschnitt sagt, den niemand sieht.
    /// </remarks>
    public async Task<QuoteSeriesDto> GetSeriesAsync(
        string isin, QuoteRange range, decimal? averageCost, CancellationToken ct = default)
    {
        var alle = await db.Quotes.AsNoTracking()
            .Where(q => q.Isin == isin)
            .OrderBy(q => q.Date)
            .Select(q => new QuotePointDto { Date = q.Date, Close = q.Close, Source = q.Source })
            .ToListAsync(ct);

        var von = From(range, alle);

        return new QuoteSeriesDto
        {
            Isin = isin,
            Range = range,
            Points = [.. alle.Where(p => von is null || p.Date >= von)],
            AverageCost = averageCost,
            StoredCount = alle.Count,
            FirstStored = alle.Count == 0 ? null : alle[0].Date,
            Sources = [.. alle.Select(p => p.Source).Distinct().Order()],
        };
    }

    /// <summary>Der früheste Tag des Zeitraums — gemessen am jüngsten Kurs, nicht an heute.</summary>
    /// <remarks>
    /// Sonst zeigte „1 Monat“ nach einer Woche ohne Abruf einen leeren Chart, obwohl Kurse da
    /// sind.
    /// </remarks>
    private static DateOnly? From(QuoteRange range, List<QuotePointDto> alle)
    {
        if (range == QuoteRange.All || alle.Count == 0)
        {
            return null;
        }

        var bis = alle[^1].Date;

        return range switch
        {
            QuoteRange.Month => bis.AddMonths(-1),
            QuoteRange.HalfYear => bis.AddMonths(-6),
            _ => bis.AddYears(-1),
        };
    }

    /// <summary>Die Papiere, für die ein Kurs gebraucht wird.</summary>
    /// <remarks>
    /// Aus Ausführungen und gepflegten Positionen. Ein Papier, das niemand hält, wird auch nicht
    /// abgefragt — jede überflüssige Anfrage an eine inoffizielle Quelle ist eine zu viel.
    /// </remarks>
    private async Task<List<string>> SecuritiesAsync(CancellationToken ct)
    {
        var ausAusfuehrungen = await db.DepotTrades.AsNoTracking()
            .Select(t => t.Isin)
            .Distinct()
            .ToListAsync(ct);

        var ausPositionen = await db.PortfolioPositions.AsNoTracking()
            .Where(p => p.Isin != null && p.Isin != "")
            .Select(p => p.Isin!)
            .Distinct()
            .ToListAsync(ct);

        return
        [
            .. ausAusfuehrungen
                .Concat(ausPositionen)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
