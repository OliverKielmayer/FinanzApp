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

        var abrechnungen = await LadeAbrechnungenAsync(verhaeltnis, ct);

        return new ChargingOverviewDto
        {
            EmploymentId = verhaeltnis,
            Employer = verhaeltnis is { } id
                ? await db.Employments.AsNoTracking()
                    .Where(e => e.Id == id).Select(e => e.Employer).FirstOrDefaultAsync(ct)
                : null,

            Statements = abrechnungen,
            Next = Vorschau(staende, saetze, abrechnungen),
            OpenAmount = abrechnungen.Sum(a => a.OpenAmount),

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
    /// <para>Erlaubt, und ohne Nachfrage an die Nachbarn: Der Zeitraum, den sie geteilt hat,
    /// wächst wieder zusammen, und der Verbrauch darüber bleibt derselbe — er stand nie
    /// irgendwo geschrieben. Genau dafür wird er gerechnet.</para>
    /// <para><b>Solange sie nicht abgerechnet ist.</b> Eine Ablesung, die in einer Abrechnung
    /// steckt, steht auf einem Blatt beim Arbeitgeber. Sie zu entfernen hieße, dass der Zähler
    /// hier etwas anderes sagt als das Papier dort — und die nächste Abrechnung setzt auf einem
    /// Endstand auf, zu dem es keine Ablesung mehr gäbe.</para>
    /// </remarks>
    public async Task<bool> DeleteReadingAsync(int id, CancellationToken ct = default)
    {
        var ablesung = await db.ChargingReadings.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ablesung is null)
        {
            return false;
        }

        var abgerechnet = await db.ChargingStatements.AsNoTracking()
            .Where(a => a.EmploymentId == ablesung.EmploymentId && a.ToOn >= ablesung.ReadOn)
            .OrderBy(a => a.ToOn)
            .FirstOrDefaultAsync(ct);

        if (abgerechnet is not null)
        {
            throw new ArgumentException(
                $"Diese Ablesung steckt in der Abrechnung bis zum {Tag(abgerechnet.ToOn)}, die "
                + "beim Arbeitgeber liegt. Nimm erst die Abrechnung zurück.");
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

    // ── Abrechnungen ─────────────────────────────────────────────────────────

    /// <summary>Was eine Station heißt, wenn ein Mensch sie liest.</summary>
    private static string StatusLabel(ChargingStatementStatus status) => status switch
    {
        ChargingStatementStatus.Recorded => "Erfasst",
        ChargingStatementStatus.Submitted => "Eingereicht",
        ChargingStatementStatus.Paid => "Erstattet",
        _ => "Unbekannt",
    };

    private async Task<List<ChargingStatementDto>> LadeAbrechnungenAsync(
        int? verhaeltnis, CancellationToken ct)
    {
        var rows = await db.ChargingStatements.AsNoTracking()
            .Where(a => a.EmploymentId == verhaeltnis)
            .Include(a => a.Vehicle)
            .Include(a => a.ReimbursementTransaction)
            .OrderByDescending(a => a.ToOn)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

        return
        [
            .. rows.Select((a, platz) => new ChargingStatementDto
            {
                Id = a.Id,
                FromOn = a.FromOn,
                FromMeterKwh = a.FromMeterKwh,
                ToOn = a.ToOn,
                ToMeterKwh = a.ToMeterKwh,
                Kwh = a.Kwh,
                EuroPerKwh = a.EuroPerKwh,
                Amount = a.Amount,
                Status = a.Status,
                StatusLabel = StatusLabel(a.Status),
                OpenAmount = a.OpenAmount,
                SubmittedAt = a.SubmittedAt,
                PaidAt = a.PaidAt,
                TransactionId = a.ReimbursementTransactionId,
                TransactionLabel = a.ReimbursementTransaction is { } b
                    ? $"{b.Payee} · {Tag(b.BookingDate)}"
                    : null,
                VehicleName = a.Vehicle?.Name,
                VehiclePlate = a.Vehicle?.Plate,
                Note = a.Note,

                // Die Liste steht absteigend — die jüngste ist die erste, und nur sie darf weg.
                CanDelete = platz == 0,
            }),
        ];
    }

    /// <summary>
    /// Was eine nächste Abrechnung umfassen würde — oder warum keine möglich ist.
    /// </summary>
    /// <remarks>
    /// Dieselbe Rechnung wie <see cref="CreateStatementAsync"/>, nur ohne zu schreiben. Sie
    /// steht hier und nicht zweimal: Eine Vorschau, die anders rechnet als die Tat, ist
    /// schlimmer als keine.
    /// </remarks>
    private static ChargingNextStatementDto Vorschau(
        List<ChargingReading> staende,
        List<ChargingRate> saetze,
        List<ChargingStatementDto> abrechnungen)
    {
        if (Naechste(staende, saetze, abrechnungen.Count > 0 ? abrechnungen[0].ToOn : null)
            is not { } naechste)
        {
            return new ChargingNextStatementDto
            {
                Possible = false,
                Obstacle = Hindernis(staende, abrechnungen),
            };
        }

        if (naechste.FehlenderSatz is { } luecke)
        {
            return new ChargingNextStatementDto
            {
                Possible = false,
                Obstacle = $"Für den Zeitraum bis zum {Tag(luecke)} ist kein Satz vereinbart.",
                FromOn = naechste.Von.ReadOn,
                ToOn = naechste.Bis.ReadOn,
                Kwh = naechste.Kwh,
            };
        }

        return new ChargingNextStatementDto
        {
            Possible = true,
            FromOn = naechste.Von.ReadOn,
            ToOn = naechste.Bis.ReadOn,
            Kwh = naechste.Kwh,
            Amount = naechste.Betrag,
        };
    }

    /// <summary>Warum sich gerade nichts abrechnen lässt, in einem Satz.</summary>
    private static string Hindernis(
        List<ChargingReading> staende, List<ChargingStatementDto> abrechnungen)
    {
        if (staende.Count == 0)
        {
            return "Noch keine Ablesung erfasst.";
        }

        if (staende.Count == 1 && abrechnungen.Count == 0)
        {
            return "Erst die zweite Ablesung ergibt einen Zeitraum.";
        }

        return "Seit der letzten Abrechnung ist nichts dazugekommen.";
    }

    /// <summary>
    /// Der Zeitraum, der als nächstes abzurechnen wäre, samt seiner Rechnung.
    /// </summary>
    /// <remarks>
    /// <para>Er beginnt am Endstand der letzten Abrechnung — oder, wenn es keine gibt, an der
    /// ersten Ablesung überhaupt. So bleibt zwischen zwei Abrechnungen keine Kilowattstunde
    /// liegen und keine geht zweimal hinaus.</para>
    /// <para><b>Abschnittsweise gerechnet.</b> Liegen mehrere Ablesungen im Zeitraum, bekommt
    /// jeder Abschnitt den Satz seines Ablesetags, und der Betrag ist ihre Summe. Ein
    /// Mittelwert über den ganzen Zeitraum wäre eine Zahl, die in keiner Vereinbarung steht.</para>
    /// </remarks>
    private static Zeitraum? Naechste(
        List<ChargingReading> staende, List<ChargingRate> saetze, DateOnly? abgerechnetBis)
    {
        var offen = abgerechnetBis is { } bis
            ? staende.Where(r => r.ReadOn >= bis).ToList()
            : staende;

        if (offen.Count < 2)
        {
            return null;
        }

        decimal betrag = 0m;
        DateOnly? fehlt = null;
        var saetzeImZeitraum = new HashSet<decimal>();

        for (var i = 1; i < offen.Count; i++)
        {
            var menge = offen[i].MeterKwh - offen[i - 1].MeterKwh;
            var satz = SatzAm(saetze, offen[i].ReadOn);

            if (satz is not { } preis)
            {
                fehlt ??= offen[i].ReadOn;
                continue;
            }

            saetzeImZeitraum.Add(preis);
            betrag += decimal.Round(menge * preis, 2, MidpointRounding.AwayFromZero);
        }

        return new Zeitraum(
            offen[0],
            offen[^1],
            offen[^1].MeterKwh - offen[0].MeterKwh,
            betrag,
            saetzeImZeitraum.Count == 1 ? saetzeImZeitraum.Single() : null,
            fehlt);
    }

    /// <summary>Ein abzurechnender Zeitraum mit allem, was aus ihm folgt.</summary>
    private sealed record Zeitraum(
        ChargingReading Von,
        ChargingReading Bis,
        decimal Kwh,
        decimal Betrag,
        decimal? EinSatz,
        DateOnly? FehlenderSatz);

    /// <summary>
    /// Schreibt den offenen Zeitraum als Abrechnung fest.
    /// </summary>
    /// <remarks>
    /// <para>Ab hier sind Verbrauch, Satz und Betrag Zahlen und keine Rechnung mehr. Das ist
    /// der Zweck: Der Arbeitgeber bekommt ein Blatt mit einer Summe, und ein später
    /// vereinbarter Satz darf daraus keine andere machen.</para>
    /// <para><b>Ohne Satz wird nicht festgeschrieben.</b> Eine Abrechnung mit einer Lücke wäre
    /// ein Blatt, das zu wenig fordert — und der Zeitraum wäre verbraucht, weil die nächste
    /// dahinter ansetzt.</para>
    /// </remarks>
    public async Task<ChargingStatementDto> CreateStatementAsync(
        CreateChargingStatementRequest request, CancellationToken ct = default)
    {
        var verhaeltnis = await ResolveEmploymentAsync(request.EmploymentId, ct);

        var staende = await db.ChargingReadings.AsNoTracking()
            .Where(r => r.EmploymentId == verhaeltnis)
            .OrderBy(r => r.ReadOn).ThenBy(r => r.Id)
            .ToListAsync(ct);

        var saetze = await db.ChargingRates.AsNoTracking()
            .Where(x => x.EmploymentId == verhaeltnis)
            .OrderBy(x => x.ValidFrom)
            .ToListAsync(ct);

        var letzte = await db.ChargingStatements.AsNoTracking()
            .Where(a => a.EmploymentId == verhaeltnis)
            .OrderByDescending(a => a.ToOn)
            .FirstOrDefaultAsync(ct);

        if (Naechste(staende, saetze, letzte?.ToOn) is not { } zeitraum)
        {
            throw new ArgumentException(
                Hindernis(staende, letzte is null ? [] : [new ChargingStatementDto()]));
        }

        if (zeitraum.FehlenderSatz is { } luecke)
        {
            throw new ArgumentException(
                $"Für den Zeitraum bis zum {Tag(luecke)} ist kein Satz vereinbart. Ohne ihn "
                + "fürderte das Blatt zu wenig — und der Zeitraum wäre verbraucht, weil die "
                + "nächste Abrechnung dahinter ansetzt.");
        }

        var abrechnung = new ChargingStatement
        {
            EmploymentId = verhaeltnis,

            // Das Fahrzeug der letzten Ablesung des Zeitraums: Es steht auf dem Blatt, und wer
            // gewechselt hat, meint das neue.
            VehicleId = zeitraum.Bis.VehicleId,

            FromOn = zeitraum.Von.ReadOn,
            FromMeterKwh = zeitraum.Von.MeterKwh,
            ToOn = zeitraum.Bis.ReadOn,
            ToMeterKwh = zeitraum.Bis.MeterKwh,
            Kwh = zeitraum.Kwh,
            EuroPerKwh = zeitraum.EinSatz,
            Amount = zeitraum.Betrag,
            Status = ChargingStatementStatus.Recorded,
            Note = Clean(request.Note),
            CreatedAt = clock.Now,
        };

        db.ChargingStatements.Add(abrechnung);
        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(verhaeltnis, ct);
        return alle.Statements.First(a => a.Id == abrechnung.Id);
    }

    /// <summary>
    /// Setzt die Abrechnung auf ihre nächste Station.
    /// </summary>
    /// <remarks>
    /// Die Stempel werden gesetzt, nicht zurückgenommen: Wer von „eingereicht“ auf „erfasst“
    /// zurückgeht, hat sich vertan — dass sie einmal hinausging, bleibt trotzdem wahr, und das
    /// Datum dazu hilft beim Nachsehen mehr als eine leere Zeile.
    /// </remarks>
    public async Task<ChargingStatementDto?> AdvanceAsync(
        int id, ChargingStatementStatus ziel, CancellationToken ct = default)
    {
        var abrechnung = await db.ChargingStatements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (abrechnung is null)
        {
            return null;
        }

        abrechnung.Status = ziel;

        if (ziel >= ChargingStatementStatus.Submitted)
        {
            abrechnung.SubmittedAt ??= clock.Now;
        }

        if (ziel == ChargingStatementStatus.Paid)
        {
            abrechnung.PaidAt ??= clock.Now;
        }

        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(abrechnung.EmploymentId, ct);
        return alle.Statements.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>
    /// Verknüpft die Erstattung mit einer Buchung und schließt die Abrechnung ab.
    /// </summary>
    public async Task<ChargingStatementDto?> LinkPaymentAsync(
        int id, int transactionId, CancellationToken ct = default)
    {
        var abrechnung = await db.ChargingStatements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (abrechnung is null)
        {
            return null;
        }

        var buchung = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

        if (buchung is null)
        {
            throw new ArgumentException("Die Buchung gibt es nicht.");
        }

        abrechnung.ReimbursementTransactionId = transactionId;
        abrechnung.Status = ChargingStatementStatus.Paid;
        abrechnung.SubmittedAt ??= clock.Now;
        abrechnung.PaidAt = buchung.BookingDate.ToDateTime(TimeOnly.MinValue);

        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(abrechnung.EmploymentId, ct);
        return alle.Statements.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>Löst die Zuordnung wieder. Eingereicht bleibt sie.</summary>
    public async Task<ChargingStatementDto?> DetachPaymentAsync(
        int id, CancellationToken ct = default)
    {
        var abrechnung = await db.ChargingStatements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (abrechnung is null)
        {
            return null;
        }

        abrechnung.ReimbursementTransactionId = null;
        abrechnung.PaidAt = null;
        abrechnung.Status = ChargingStatementStatus.Submitted;

        await db.SaveChangesAsync(ct);

        var alle = await GetAsync(abrechnung.EmploymentId, ct);
        return alle.Statements.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>
    /// Nimmt eine Abrechnung zurück — nur die jüngste.
    /// </summary>
    /// <remarks>
    /// Jede setzt auf dem Endstand der vorigen auf. Eine aus der Mitte zu entfernen hinterließe
    /// einen Zeitraum, den keine Abrechnung mehr deckt, und die nächste würde ihn nicht
    /// nachholen — sie beginnt beim Endstand <em>ihrer</em> Vorgängerin. Die Lücke fällt erst
    /// auf, wenn die Jahressumme nicht mehr zum Zähler passt.
    /// </remarks>
    public async Task<bool> DeleteStatementAsync(int id, CancellationToken ct = default)
    {
        var abrechnung = await db.ChargingStatements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (abrechnung is null)
        {
            return false;
        }

        var juengere = await db.ChargingStatements.AsNoTracking()
            .AnyAsync(a => a.EmploymentId == abrechnung.EmploymentId && a.ToOn > abrechnung.ToOn, ct);

        if (juengere)
        {
            throw new ArgumentException(
                "Nur die jüngste Abrechnung lässt sich zurücknehmen. Jede setzt auf dem Endstand "
                + "der vorigen auf; diese aus der Mitte zu entfernen hinterließe einen Zeitraum, "
                + "den keine Abrechnung mehr deckt.");
        }

        db.ChargingStatements.Remove(abrechnung);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Nimmt einen Erstattungssatz zurück.
    /// </summary>
    /// <remarks>
    /// Was er einmal gerechnet hat, steht in den Abrechnungen und bleibt dort: Die haben ihre
    /// Zahlen festgeschrieben. Offene Zeiträume rechnen danach ohne ihn — und sagen es.
    /// </remarks>
    public async Task<bool> DeleteRateAsync(int id, CancellationToken ct = default)
    {
        var satz = await db.ChargingRates.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (satz is null)
        {
            return false;
        }

        db.ChargingRates.Remove(satz);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Buchungen, die zur erwarteten Erstattung passen könnten.
    /// </summary>
    /// <remarks>
    /// Dasselbe Verfahren wie beim PKV-Vorgang und bei der Lohnabrechnung: Die Bewertung
    /// <em>schlägt vor</em>, sie entscheidet nicht. Zugeordnet wird erst, wenn jemand bestätigt.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentCandidateDto>> GetPaymentCandidatesAsync(
        int id, CancellationToken ct = default)
    {
        var abrechnung = await db.ChargingStatements.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (abrechnung is null)
        {
            return [];
        }

        var von = abrechnung.SubmittedAt is { } eingereicht
            ? DateOnly.FromDateTime(eingereicht)
            : abrechnung.ToOn;

        var bis = von.AddDays(120);

        var buchungen = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Income
                        && t.BookingDate >= von && t.BookingDate <= bis)
            .OrderBy(t => t.BookingDate)
            .Take(50)
            .ToListAsync(ct);

        var bewertet = buchungen
            .Select(t => Bewerte(abrechnung, t, von))
            .Where(k => k.Score > 0)
            .OrderByDescending(k => k.Score)
            .ThenByDescending(k => k.BookingDate)
            .Take(6)
            .ToList();

        return bewertet.Count == 0
            ? []
            : [.. bewertet.Select((k, platz) => k with { IsBestMatch = platz == 0 })];
    }

    /// <summary>
    /// Wie gut eine Buchung zur Abrechnung passt.
    /// </summary>
    /// <remarks>
    /// Der Betrag wiegt am schwersten, weil er der einzige harte Anhaltspunkt ist: Eine
    /// Erstattung über 61,81 € kommt genau so herein. Die Nähe zur Einreichung und ein Wort
    /// aus dem Zweck schärfen nach — „Ladestrom“ oder „Strom“ steht oft darin, weil die
    /// Lohnbuchhaltung es hinschreibt.
    /// </remarks>
    private static PaymentCandidateDto Bewerte(
        ChargingStatement abrechnung, Transaction buchung, DateOnly von)
    {
        var punkte = 0;
        var gruende = new List<string>();

        var betrag = Math.Abs(buchung.Amount);
        var unterschied = Math.Abs(betrag - abrechnung.Amount);

        if (unterschied < 0.01m)
        {
            punkte += 60;
            gruende.Add("Betrag stimmt");
        }
        else if (abrechnung.Amount > 0m && unterschied <= abrechnung.Amount * 0.02m)
        {
            punkte += 35;
            gruende.Add("Betrag weicht um Cent ab");
        }
        else if (abrechnung.Amount > 0m && unterschied <= abrechnung.Amount * 0.10m)
        {
            punkte += 15;
            gruende.Add("Betrag liegt nahe");
        }

        var tage = buchung.BookingDate.DayNumber - von.DayNumber;
        if (tage is >= 0 and <= 31)
        {
            punkte += 30;
            gruende.Add("im Monat nach der Einreichung");
        }
        else if (tage is > 31 and <= 62)
        {
            punkte += 10;
            gruende.Add("im zweiten Monat danach");
        }

        var text = $"{buchung.Payee} {buchung.Purpose}";
        if (text.Contains("ladestrom", StringComparison.OrdinalIgnoreCase)
            || text.Contains("strom", StringComparison.OrdinalIgnoreCase))
        {
            punkte += 20;
            gruende.Add("Strom im Verwendungszweck");
        }

        return new PaymentCandidateDto
        {
            TransactionId = buchung.Id,
            BookingDate = buchung.BookingDate,
            Payee = buchung.Payee,
            Amount = buchung.Amount,
            AccountName = buchung.Account?.Name ?? string.Empty,
            Purpose = buchung.Purpose,
            Score = punkte,

            // Der Grund steht am Vorschlag, nicht nur die Punktzahl: Eine Zahl ohne Begründung
            // lässt sich nicht prüfen, und bestätigen soll sie ein Mensch.
            Reason = gruende.Count > 0 ? string.Join(" · ", gruende) : "passt nur ungefähr",

            // Wer der beste ist, entscheidet die Reihenfolge danach — hier ist noch keiner es.
            IsBestMatch = false,
        };
    }
}
