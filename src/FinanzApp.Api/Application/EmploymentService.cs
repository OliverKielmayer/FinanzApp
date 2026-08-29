using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Arbeit &amp; Beruf — v5-Handoff, Abschnitt 8.
/// </summary>
/// <remarks>
/// <para>Der Bereich liefert die Einnahmenseite, die den Auswertungen bisher fehlte. Er legt
/// dafür <b>kein</b> Geld an: die Zahlung ist und bleibt die Buchung auf dem Konto, und die
/// Abrechnung verweist nur darauf. Eine zweite Geldbuchung hieße, denselben Eingang doppelt zu
/// zählen — einmal als Bankumsatz, einmal als Lohn.</para>
/// <para>Jede Jahreslast rechnet nur über <em>laufende</em> Verhältnisse. Das ist die Regel,
/// gegen die der Prototyp verstoßen hat: er summierte beide Arbeitsverhältnisse zu 127.200 €
/// Bruttogehalt pro Jahr, während der Bereich selbst 77.760 € nannte.</para>
/// </remarks>
public sealed class EmploymentService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>
    /// Die Grenze, innerhalb derer eine Gutschrift überhaupt als Zahlung in Frage kommt.
    /// </summary>
    /// <remarks>
    /// Dieselbe Grenze gilt für den Vorschlag <em>und</em> für die Zuordnung von Hand. Ein
    /// Vorschlagsfenster, das enger ist als das Erlaubte, macht die Bestätigung zur Attrappe;
    /// eines, das weiter ist, schlägt vor, was danach abgewiesen wird.
    /// </remarks>
    private const decimal Tolerance = 0.15m;

    /// <summary>
    /// Grobfaktor für ein fehlendes Nettogehalt.
    /// </summary>
    /// <remarks>
    /// Steuerklasse, Kirche, Kinderfreibetrag und Beitragsbemessungsgrenze sind hier nicht
    /// bekannt — die Zahl kann nur eine Hausnummer sein. Sie greift deshalb <b>nur</b>, wo das
    /// Netto leer geblieben ist, und wird überall als geschätzt ausgewiesen: ein Faktor, der
    /// niemandes Steuerklasse kennt, darf nicht unsichtbar in Auswertungen wirken.
    /// </remarks>
    private const decimal NetFactor = 0.59m;

    public async Task<EmploymentOverviewDto> GetAsync(CancellationToken ct = default)
    {
        var heute = clock.Today;

        var verhaeltnisse = await db.Employments.AsNoTracking()
            .Include(e => e.Payslips)
            .ToListAsync(ct);

        // Laufende zuerst, darin das jüngste oben — der Kopf nennt den Arbeitgeber des ersten.
        var sortiert = verhaeltnisse
            .OrderByDescending(e => e.IsRunning(heute))
            .ThenByDescending(e => e.StartsOn)
            .ToList();

        var laufend = sortiert.Where(e => e.IsRunning(heute)).ToList();

        var abrechnungen = await db.Payslips.AsNoTracking()
            .Include(p => p.Employment)
            .Include(p => p.Document)
            .Include(p => p.Transaction).ThenInclude(t => t!.Account)
            .OrderByDescending(p => p.Month)
            .ToListAsync(ct);

        var vereinbarungen = await db.WorkAgreements.AsNoTracking()
            .Include(a => a.Document)
            .OrderByDescending(a => a.SignedOn)
            .ToListAsync(ct);

        return new EmploymentOverviewDto
        {
            Head = Head(laufend, sortiert.Count),
            Employments = [.. sortiert.Select(e => Row(e, heute))],
            Payslips = [.. abrechnungen.Select(PayslipRow)],
            Agreements = [.. vereinbarungen.Select(AgreementRow)],
            WithoutDocumentCount = abrechnungen.Count(p => p.DocumentId is null),
            WithoutPaymentCount = abrechnungen.Count(p => p.TransactionId is null),
        };
    }

    // ── Kopf und Zeilen ────────────────────────────────────────────────────────────────────

    private static EmploymentHeadDto Head(List<Employment> laufend, int gesamt)
    {
        var brutto = laufend.Sum(e => e.GrossMonthly);
        var netto = laufend.Sum(Net);

        return new EmploymentHeadDto
        {
            Employer = laufend.Count == 0 ? null : laufend[0].Employer,
            YearlyGross = brutto * 12m,
            MonthlyGross = brutto,
            MonthlyNet = netto,
            NetIsEstimated = laufend.Any(e => e.NetMonthly is null),

            // Ohne Brutto gibt es nichts zu teilen. Eine Quote von 0 % läse sich wie „keine
            // Abgaben“ statt wie „nicht bekannt“.
            DeductionRatePercent = brutto == 0m
                ? null
                : Math.Round((brutto - netto) / brutto * 100m, 1),

            ActiveCount = laufend.Count,
            TotalCount = gesamt,
        };
    }

    private static EmploymentRowDto Row(Employment e, DateOnly heute)
    {
        var laeuft = e.IsRunning(heute);

        return new EmploymentRowDto
        {
            Id = e.Id,
            Employer = e.Employer,
            Meta = HoldingMeta.ForEmployment(e),
            Kind = e.Kind,
            KindLabel = HoldingMeta.EmploymentLabel(e.Kind),
            StartsOn = e.StartsOn,
            EndsOn = e.EndsOn,
            GrossMonthly = e.GrossMonthly,
            NetMonthly = Net(e),
            NetIsEstimated = e.NetMonthly is null,
            IsActive = laeuft,

            // Beendetes trägt keine Jahreslast. Die Zeile zeigt dann „—“.
            YearlyGross = laeuft ? e.GrossMonthly * 12m : null,
            PayslipCount = e.Payslips.Count,
        };
    }

    private static PayslipRowDto PayslipRow(Payslip p) => new()
    {
        Id = p.Id,
        EmploymentId = p.EmploymentId,
        Employer = p.Employment?.Employer,
        Month = p.Month,
        Gross = p.Gross,

        // Wo nichts eingetragen ist, steht die Schätzung — und das Kennzeichen daneben.
        Net = p.Net ?? Estimate(p.Gross),
        NetIsEstimated = p.Net is null,
        Payout = p.Payout,
        DocumentId = p.DocumentId,
        DocumentTitle = p.Document?.Title,
        TransactionId = p.TransactionId,
        PaidOn = p.Transaction?.BookingDate,
        PaidFrom = p.Transaction?.Account?.Name,
        PaidAmount = p.Transaction is { } b ? Math.Abs(b.Amount) : null,
    };

    private static WorkAgreementRowDto AgreementRow(WorkAgreement a) => new()
    {
        Id = a.Id,
        EmploymentId = a.EmploymentId,
        Name = a.Name,
        SignedOn = a.SignedOn,
        Kind = a.Kind,
        KindLabel = HoldingMeta.AgreementLabel(a.Kind),
        DocumentId = a.DocumentId,
        DocumentTitle = a.Document?.Title,
    };

    /// <summary>Das erfasste Netto, oder die Schätzung daraus.</summary>
    public static decimal Net(Employment e) => e.NetMonthly ?? Estimate(e.GrossMonthly);

    /// <summary>Was vom Brutto vermutlich übrig bleibt.</summary>
    public static decimal Estimate(decimal brutto) => Math.Round(brutto * NetFactor, 2);

    // ── Abrechnungen ───────────────────────────────────────────────────────────────────────

    public async Task<PayslipRowDto> CreatePayslipAsync(
        CreatePayslipRequest request, CancellationToken ct = default)
    {
        var verhaeltnis = await db.Employments.FirstOrDefaultAsync(e => e.Id == request.EmploymentId, ct)
            ?? throw new RuleViolationException("Dieses Arbeitsverhältnis gibt es nicht.");

        var monat = new DateOnly(request.Month.Year, request.Month.Month, 1);

        if (await db.Payslips.AnyAsync(p => p.EmploymentId == verhaeltnis.Id && p.Month == monat, ct))
        {
            throw new RuleViolationException(
                $"Für {GermanFormat.MonthYear(monat)} ist bei {verhaeltnis.Employer} schon eine "
                + "Abrechnung erfasst.");
        }

        if (request.Gross <= 0m)
        {
            throw new RuleViolationException("Das Bruttogehalt fehlt.");
        }

        // Netto über Brutto wäre eine negative Abgabenquote — und die Quote steht im Kopf.
        if (request.Net > request.Gross)
        {
            throw new RuleViolationException("Das Nettogehalt kann nicht über dem Brutto liegen.");
        }

        var abrechnung = new Payslip
        {
            EmploymentId = verhaeltnis.Id,
            Month = monat,
            Gross = request.Gross,

            // Leer bleibt leer. Die Schätzung entsteht bei der Anzeige und wird dort
            // gekennzeichnet — hier gespeichert wäre sie später nicht mehr von einer erfassten
            // Zahl zu unterscheiden.
            Net = request.Net,

            // Meist gleich dem Netto. Abweichen darf es — Vorschüsse und Pfändungen gehen
            // dazwischen —, aber erfunden wird es nicht.
            Payout = request.Payout ?? request.Net ?? Estimate(request.Gross),
        };

        db.Payslips.Add(abrechnung);
        await db.SaveChangesAsync(ct);

        return PayslipRow(await LoadPayslipAsync(abrechnung.Id, ct));
    }

    public async Task<bool> DeletePayslipAsync(int id, CancellationToken ct = default)
    {
        var abrechnung = await db.Payslips.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (abrechnung is null)
        {
            return false;
        }

        db.Payslips.Remove(abrechnung);
        await db.SaveChangesAsync(ct);

        return true;
    }

    // ── Zahlungszuordnung ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gutschriften, die zur Auszahlung passen könnten — vorschlagen, nicht entscheiden.
    /// </summary>
    /// <remarks>
    /// Gesucht wird auf <em>sichtbaren</em> Konten: der Mandanten- und der Freigabefilter sitzen
    /// im <c>DbContext</c>, ein privates Konto eines anderen Mitglieds taucht hier gar nicht auf.
    /// Verglichen wird gegen den <b>Auszahlungsbetrag</b>, denn er ist der, der auf dem Konto
    /// ankommt — nicht das Brutto und nicht das Netto.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentCandidateDto>> GetPaymentCandidatesAsync(
        int payslipId, CancellationToken ct = default)
    {
        var abrechnung = await db.Payslips.AsNoTracking()
            .Include(p => p.Employment)
            .FirstOrDefaultAsync(p => p.Id == payslipId, ct);

        if (abrechnung is null || abrechnung.Payout <= 0m)
        {
            return [];
        }

        // Vom Monatsersten bis in den Folgemonat hinein: Gehalt kommt am Monatsende, in
        // manchen Häusern erst zur Monatsmitte danach.
        var von = abrechnung.Month;
        var bis = abrechnung.Month.AddMonths(1).AddDays(20);

        var buchungen = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Income
                        && t.BookingDate >= von && t.BookingDate <= bis)
            .OrderByDescending(t => t.BookingDate)
            .ToListAsync(ct);

        var treffer = buchungen
            .Select(t => Score(abrechnung, t))
            .Where(c => c is not null)
            .Select(c => c!)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.BookingDate)
            .Take(6)
            .ToList();

        return treffer.Count == 0
            ? []
            : [.. treffer.Select((c, i) => c with { IsBestMatch = i == 0 })];
    }

    /// <summary>
    /// Bewertet eine Gutschrift, oder verwirft sie.
    /// </summary>
    /// <remarks>
    /// <c>null</c> heißt: kein Kandidat. Der Handoff zieht die Grenze bei 15 % Abweichung vom
    /// Auszahlungsbetrag — was weiter weg liegt, ist keine knappe Sache, sondern eine andere
    /// Zahlung, und sie mit niedriger Bewertung anzubieten wäre eine Einladung zum Fehlgriff.
    /// </remarks>
    private static PaymentCandidateDto? Score(Payslip abrechnung, Transaction buchung)
    {
        var betrag = Math.Abs(buchung.Amount);
        var abweichung = Math.Abs(betrag - abrechnung.Payout) / abrechnung.Payout;

        if (abweichung > Tolerance)
        {
            return null;
        }

        var punkte = 0;
        var gruende = new List<string>();

        if (abweichung < 0.001m)
        {
            punkte += 60;
            gruende.Add("Betrag stimmt");
        }
        else if (abweichung <= 0.05m)
        {
            punkte += 40;
            gruende.Add("Betrag fast gleich");
        }
        else
        {
            punkte += 20;
            gruende.Add("Betrag weicht ab");
        }

        var folgemonat = abrechnung.Month.AddMonths(1);

        if (buchung.BookingDate < folgemonat)
        {
            punkte += 30;
            gruende.Add("im Abrechnungsmonat");
        }
        else
        {
            punkte += 20;
            gruende.Add("im Folgemonat");
        }

        var heuhaufen = (buchung.Payee + " " + (buchung.Note ?? string.Empty)).ToLowerInvariant();

        if (EmployerKeyword(abrechnung.Employment?.Employer) is { } wort
            && heuhaufen.Contains(wort, StringComparison.Ordinal))
        {
            punkte += 20;
            gruende.Insert(0, "Name des Arbeitgebers");
        }

        return new PaymentCandidateDto
        {
            TransactionId = buchung.Id,
            BookingDate = buchung.BookingDate,
            Payee = buchung.Payee,
            Amount = betrag,
            AccountName = buchung.Account?.Name ?? string.Empty,
            Score = Math.Min(100, punkte),
            Reason = string.Join(" · ", gruende),
            IsBestMatch = false,
        };
    }

    private static readonly string[] LegalForms = ["gmbh", "mbh", "kgaa", "ohg"];

    /// <summary>Das längste Wort des Arbeitgebers — Rechtsformkürzel helfen beim Suchen nicht.</summary>
    private static string? EmployerKeyword(string? employer)
        => employer?
            .Split([' ', ',', '.', '&', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(teil => teil.ToLowerInvariant())
            .Where(teil => teil.Length > 3 && !LegalForms.Contains(teil))
            .OrderByDescending(teil => teil.Length)
            .FirstOrDefault();

    /// <summary>
    /// Verweist die Abrechnung auf eine vorhandene Buchung. Es entsteht kein zweiter Geldvorgang.
    /// </summary>
    public async Task<PayslipRowDto> LinkPaymentAsync(
        int payslipId, int transactionId, CancellationToken ct = default)
    {
        var abrechnung = await db.Payslips.FirstOrDefaultAsync(p => p.Id == payslipId, ct)
            ?? throw new RuleViolationException("Diese Abrechnung gibt es nicht.");

        var buchung = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new RuleViolationException("Diese Buchung gibt es nicht.");

        if (buchung.Kind != TransactionKind.Income)
        {
            throw new RuleViolationException("Eine Lohnzahlung ist eine Gutschrift, keine Ausgabe.");
        }

        var betrag = Math.Abs(buchung.Amount);
        var abweichung = abrechnung.Payout == 0m
            ? 1m
            : Math.Abs(betrag - abrechnung.Payout) / abrechnung.Payout;

        // Dieselbe Grenze wie beim Vorschlag. Wäre sie hier weiter, hieße die Bestätigung
        // nichts mehr — man käme an jeder Prüfung vorbei, indem man von Hand zuordnet.
        if (abweichung > Tolerance)
        {
            throw new RuleViolationException(
                "Die Buchung weicht um mehr als 15 % vom Auszahlungsbetrag ab. Wenn sie trotzdem "
                + "stimmt, gehört der Auszahlungsbetrag korrigiert.");
        }

        abrechnung.TransactionId = buchung.Id;
        await db.SaveChangesAsync(ct);

        return PayslipRow(await LoadPayslipAsync(payslipId, ct));
    }

    /// <summary>Löst die Zuordnung. Die Buchung bleibt, wo sie ist — sie war nie unsere.</summary>
    public async Task<PayslipRowDto> DetachPaymentAsync(int payslipId, CancellationToken ct = default)
    {
        var abrechnung = await db.Payslips.FirstOrDefaultAsync(p => p.Id == payslipId, ct)
            ?? throw new RuleViolationException("Diese Abrechnung gibt es nicht.");

        abrechnung.TransactionId = null;
        await db.SaveChangesAsync(ct);

        return PayslipRow(await LoadPayslipAsync(payslipId, ct));
    }

    private async Task<Payslip> LoadPayslipAsync(int id, CancellationToken ct)
        => await db.Payslips.AsNoTracking()
               .Include(p => p.Employment)
               .Include(p => p.Document)
               .Include(p => p.Transaction).ThenInclude(t => t!.Account)
               .FirstAsync(p => p.Id == id, ct);
}
