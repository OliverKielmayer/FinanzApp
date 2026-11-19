using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Ladestrom des Dienstwagens: abgelesene Stände, vereinbarte Sätze, was dazwischen liegt.
/// </summary>
/// <remarks>
/// <para><b>Gespeichert werden zwei Dinge: der Stand und der Satz.</b> Alles andere — der
/// Verbrauch, der Betrag, die Summe — entsteht hier beim Lesen. Ein gespeicherter Verbrauch
/// wäre eine zweite Wahrheit neben den Ständen, aus denen er sich ergibt, und eine berichtigte
/// Ablesung ließe ihn falsch stehen, ohne dass es auffällt.</para>
/// <para><b>Ein Zeitraum läuft von Ablesung zu Ablesung</b>, nicht vom Monatsersten zum
/// Monatsletzten. Niemand steht um Mitternacht am Zähler, und ein Zeitraum, der sich am
/// Kalender festmachte, verschenkte die Tage dazwischen oder erfände sie.</para>
/// </remarks>
public sealed class ChargingService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>
    /// Alles zu einem Arbeitsverhältnis: Ablesungen mit ihren Zeiträumen, Sätze, Summen.
    /// </summary>
    /// <remarks>
    /// Ohne <paramref name="employmentId"/> wird das laufende Verhältnis genommen — es gibt
    /// meist genau eines, und danach zu fragen wäre eine Frage, deren Antwort feststeht.
    /// </remarks>
    public async Task<ChargingOverviewDto> GetAsync(
        int? employmentId = null, CancellationToken ct = default)
    {
        var verhaeltnis = await ResolveEmploymentAsync(employmentId, ct);

        var staende = await db.ChargingReadings.AsNoTracking()
            .Where(r => r.EmploymentId == verhaeltnis)
            .Include(r => r.Vehicle)
            .Include(r => r.Document)
            .OrderBy(r => r.ReadOn)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        var saetze = await db.ChargingRates.AsNoTracking()
            .Where(s => s.EmploymentId == verhaeltnis)
            .OrderBy(s => s.ValidFrom)
            .ToListAsync(ct);

        var zeilen = new List<ChargingReadingDto>(staende.Count);
        decimal summeKwh = 0m, summeBetrag = 0m;
        var ohneSatz = 0;

        for (var i = 0; i < staende.Count; i++)
        {
            var stand = staende[i];
            var vorher = i > 0 ? staende[i - 1] : null;

            decimal? kwh = vorher is null ? null : stand.MeterKwh - vorher.MeterKwh;
            var satz = SatzAm(saetze, stand.ReadOn);
            decimal? betrag = kwh is { } menge && satz is { } preis
                ? decimal.Round(menge * preis, 2, MidpointRounding.AwayFromZero)
                : null;

            if (kwh is { } gezaehlt)
            {
                summeKwh += gezaehlt;

                if (betrag is { } gerechnet)
                {
                    summeBetrag += gerechnet;
                }
                else
                {
                    ohneSatz++;
                }
            }

            zeilen.Add(new ChargingReadingDto
            {
                Id = stand.Id,
                ReadOn = stand.ReadOn,
                MeterKwh = stand.MeterKwh,
                Kwh = kwh,
                Since = vorher?.ReadOn,
                EuroPerKwh = kwh is null ? null : satz,
                Amount = betrag,
                RateChangedInPeriod = vorher is not null
                                      && SatzWechseltZwischen(saetze, vorher.ReadOn, stand.ReadOn),
                VehicleId = stand.VehicleId,
                VehicleName = stand.Vehicle?.Name,
                DocumentId = stand.DocumentId,
                DocumentName = stand.Document?.FileName,
                Note = stand.Note,
            });
        }

        return new ChargingOverviewDto
        {
            EmploymentId = verhaeltnis,
            Employer = verhaeltnis is { } id
                ? await db.Employments.AsNoTracking()
                    .Where(e => e.Id == id).Select(e => e.Employer).FirstOrDefaultAsync(ct)
                : null,

            // Jüngste zuerst: Wer die Seite öffnet, will den letzten Stand sehen und nicht den
            // ersten. Gerechnet wird trotzdem vorwärts — anders ergäbe eine Differenz nichts.
            Readings = [.. Enumerable.Reverse(zeilen)],
            Rates = [.. Saetze(saetze)],
            TotalKwh = summeKwh,
            TotalAmount = summeBetrag,
            PeriodsWithoutRate = ohneSatz,
        };
    }

    /// <summary>
    /// Trägt eine Ablesung ein.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Reihe muss aufwärts bleiben.</b> Ein Zähler zählt nur in eine Richtung, und
    /// ein Stand, der unter seinem Vorgänger liegt, ist kein Rückgang, sondern ein Zahlendreher.
    /// Ihn anzunehmen hieße, einen negativen Verbrauch zu führen — und die Abrechnung an den
    /// Arbeitgeber wäre falsch, bevor jemand sie ansieht.</para>
    /// <para>Geprüft wird nach <em>beiden</em> Seiten, nicht nur gegen den letzten Stand: Wer
    /// eine vergessene Ablesung nachträgt, schiebt sie mitten in die Reihe, und dort muss sie
    /// zwischen ihre Nachbarn passen.</para>
    /// </remarks>
    public async Task<ChargingReadingDto> AddReadingAsync(
        CreateChargingReadingRequest request, CancellationToken ct = default)
    {
        var verhaeltnis = await ResolveEmploymentAsync(request.EmploymentId, ct);

        if (request.ReadOn > clock.Today)
        {
            throw new ArgumentException(
                "Ein Zähler lässt sich nicht im Voraus ablesen — das Datum liegt in der Zukunft.");
        }

        if (request.MeterKwh < 0m)
        {
            throw new ArgumentException("Ein Zählerstand ist nicht negativ.");
        }

        var amSelbenTag = await db.ChargingReadings.AsNoTracking()
            .AnyAsync(r => r.EmploymentId == verhaeltnis && r.ReadOn == request.ReadOn, ct);

        if (amSelbenTag)
        {
            throw new ArgumentException(
                $"Für den {Tag(request.ReadOn)} ist schon eine Ablesung erfasst. Zwei Stände für "
                + "denselben Tag ließen den Verbrauch dazwischen nicht mehr ordnen.");
        }

        var davor = await db.ChargingReadings.AsNoTracking()
            .Where(r => r.EmploymentId == verhaeltnis && r.ReadOn < request.ReadOn)
            .OrderByDescending(r => r.ReadOn)
            .FirstOrDefaultAsync(ct);

        if (davor is not null && request.MeterKwh < davor.MeterKwh)
        {
            throw new ArgumentException(
                $"Der Stand {Kwh(request.MeterKwh)} liegt unter dem vom {Tag(davor.ReadOn)} "
                + $"({Kwh(davor.MeterKwh)}). Ein Zähler zählt aufwärts.");
        }

        var danach = await db.ChargingReadings.AsNoTracking()
            .Where(r => r.EmploymentId == verhaeltnis && r.ReadOn > request.ReadOn)
            .OrderBy(r => r.ReadOn)
            .FirstOrDefaultAsync(ct);

        if (danach is not null && request.MeterKwh > danach.MeterKwh)
        {
            throw new ArgumentException(
                $"Der Stand {Kwh(request.MeterKwh)} liegt über dem vom {Tag(danach.ReadOn)} "
                + $"({Kwh(danach.MeterKwh)}), der später abgelesen wurde. Ein Zähler zählt aufwärts.");
        }

        var ablesung = new ChargingReading
        {
            EmploymentId = verhaeltnis,
            VehicleId = request.VehicleId,
            ReadOn = request.ReadOn,
            MeterKwh = request.MeterKwh,
            DocumentId = request.DocumentId,
            Note = Clean(request.Note),
            CreatedAt = clock.Now,
        };

        db.ChargingReadings.Add(ablesung);
        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(verhaeltnis, ct);
        return alle.Readings.First(r => r.Id == ablesung.Id);
    }

    /// <summary>
    /// Trägt einen Erstattungssatz ein, gültig ab einem Tag.
    /// </summary>
    /// <remarks>
    /// Gibt es für denselben Tag schon einen, wird er überschrieben statt ein zweiter angelegt:
    /// Zwei Sätze ab demselben Tag hieße, dass die Reihenfolge der Zeilen entscheidet, welcher
    /// gilt — und die ist keine Aussage.
    /// </remarks>
    public async Task<ChargingRateDto> AddRateAsync(
        CreateChargingRateRequest request, CancellationToken ct = default)
    {
        var verhaeltnis = await ResolveEmploymentAsync(request.EmploymentId, ct);

        if (request.EuroPerKwh <= 0m)
        {
            throw new ArgumentException("Ein Erstattungssatz über null ergibt keine Abrechnung.");
        }

        var vorhanden = await db.ChargingRates
            .FirstOrDefaultAsync(
                s => s.EmploymentId == verhaeltnis && s.ValidFrom == request.ValidFrom, ct);

        if (vorhanden is null)
        {
            vorhanden = new ChargingRate
            {
                EmploymentId = verhaeltnis,
                ValidFrom = request.ValidFrom,
                CreatedAt = clock.Now,
            };

            db.ChargingRates.Add(vorhanden);
        }

        vorhanden.EuroPerKwh = request.EuroPerKwh;
        vorhanden.Note = Clean(request.Note);

        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(verhaeltnis, ct);
        return alle.Rates.First(s => s.Id == vorhanden.Id);
    }

    /// <summary>
    /// Nimmt eine Ablesung zurück.
    /// </summary>
    /// <remarks>
    /// Erlaubt, und ohne Nachfrage an die Nachbarn: Der Zeitraum, den sie geteilt hat, wächst
    /// wieder zusammen, und der Verbrauch darüber bleibt derselbe — er stand nie irgendwo
    /// geschrieben. Genau dafür wird er gerechnet.
    /// </remarks>
    public async Task<bool> DeleteReadingAsync(int id, CancellationToken ct = default)
    {
        var ablesung = await db.ChargingReadings.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ablesung is null)
        {
            return false;
        }

        db.ChargingReadings.Remove(ablesung);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Welcher Satz an einem Tag gilt: der jüngste, der nicht nach ihm beginnt.
    /// </summary>
    /// <remarks>
    /// <b>Der Tag der Ablesung entscheidet</b>, nicht der Anfang des Zeitraums. Läuft ein
    /// Zeitraum über einen Satzwechsel, bekommt er den neuen — und das ist eine Entscheidung,
    /// keine Rechnung: Die Kilowattstunden auf die Tage zu verteilen hieße, eine Ladekurve zu
    /// erfinden, die niemand gemessen hat. Dass der Zeitraum darüber lief, steht an der Zeile
    /// (<see cref="ChargingReadingDto.RateChangedInPeriod"/>); wer es genau will, liest am Tag
    /// des Wechsels ab.
    /// </remarks>
    private static decimal? SatzAm(List<ChargingRate> saetze, DateOnly tag)
        => saetze.Where(s => s.ValidFrom <= tag)
            .OrderByDescending(s => s.ValidFrom)
            .Select(s => (decimal?)s.EuroPerKwh)
            .FirstOrDefault();

    /// <summary>Ob zwischen zwei Ablesungen ein neuer Satz zu gelten begann.</summary>
    private static bool SatzWechseltZwischen(List<ChargingRate> saetze, DateOnly von, DateOnly bis)
        => saetze.Any(s => s.ValidFrom > von && s.ValidFrom <= bis);

    /// <summary>Die Sätze mit ihrem abgeleiteten Bis-Datum.</summary>
    private static IEnumerable<ChargingRateDto> Saetze(List<ChargingRate> saetze)
    {
        for (var i = saetze.Count - 1; i >= 0; i--)
        {
            yield return new ChargingRateDto
            {
                Id = saetze[i].Id,
                ValidFrom = saetze[i].ValidFrom,
                EuroPerKwh = saetze[i].EuroPerKwh,
                Note = saetze[i].Note,
                ValidUntil = i + 1 < saetze.Count
                    ? saetze[i + 1].ValidFrom.AddDays(-1)
                    : null,
            };
        }
    }

    /// <summary>
    /// Das gemeinte Arbeitsverhältnis.
    /// </summary>
    /// <remarks>
    /// Ohne Angabe das laufende. Gibt es mehrere laufende, das zuletzt begonnene — und gibt es
    /// gar keines, bleibt es <c>null</c>: Eine Ablesung ohne Verhältnis ist besser als gar
    /// keine, und sie lässt sich später zuordnen.
    /// </remarks>
    private async Task<int?> ResolveEmploymentAsync(int? gewuenscht, CancellationToken ct)
    {
        if (gewuenscht is { } id)
        {
            return await db.Employments.AsNoTracking().AnyAsync(e => e.Id == id, ct) ? id : null;
        }

        var heute = clock.Today;

        return await db.Employments.AsNoTracking()
            .Where(e => e.IsActive && (e.EndsOn == null || e.EndsOn >= heute))
            .OrderByDescending(e => e.StartsOn)
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static string? Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Tag(DateOnly tag)
        => tag.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string Kwh(decimal wert)
        => wert.ToString("N3", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " kWh";
}
