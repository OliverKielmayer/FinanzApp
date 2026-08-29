using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Steuerjahr-Bericht — v5-Handoff, Abschnitt 15.
/// </summary>
/// <remarks>
/// <para>Er sammelt Kandidaten mit Belegbezug und rechnet keine Steuer. Höchstbeträge,
/// zumutbare Belastung und die Trennung Arbeitslohn/Material hängen an Größen, die diese
/// Anwendung nicht kennt; sie zu unterschlagen wäre schlimmer, als sie wegzulassen.</para>
/// <para><b>Jede Position ist abgeleitet, keine ist gepflegt.</b> Es gibt keine
/// Steuerpositions-Tabelle: die Beiträge stehen in den Verträgen, die Eigenanteile in den
/// PKV-Vorgängen, die Handwerkerleistungen in den Buchungen. Der Grund steht in Abschnitt 15.4 —
/// vorher nannten PKV-Bilanz und Steuerbericht zwei verschiedene Zahlen für dieselbe Aussage,
/// weil eine davon abgetippt war.</para>
/// </remarks>
public sealed class TaxYearService(FinanzAppDbContext db, HealthBalanceService health, IClock clock)
{
    /// <summary>
    /// Kilometersatz der Entfernungspauschale für die ersten zwanzig Kilometer.
    /// </summary>
    /// <remarks>
    /// Ab dem einundzwanzigsten Kilometer gilt <see cref="LongDistanceRate"/>. Beide Sätze sind
    /// Gesetz und keine Schätzung; geschätzt ist allein, ob der Weg an so vielen Tagen wirklich
    /// gefahren wurde — deshalb trägt die Position trotzdem das Kennzeichen.
    /// </remarks>
    private const decimal ShortDistanceRate = 0.30m;

    private const decimal LongDistanceRate = 0.38m;

    private const int ShortDistanceLimit = 20;

    public async Task<TaxYearDto> GetAsync(int? year = null, CancellationToken ct = default)
    {
        var jahre = await YearsAsync(ct);

        // Ohne Angabe das laufende Jahr. Es steht immer in der Liste, auch wenn es noch leer
        // ist — wer den Bericht im Januar öffnet, will nicht im Vorjahr landen.
        var jahr = year ?? clock.Today.Year;

        var positionen = new List<TaxPositionDto>();
        positionen.AddRange(await PensionsAsync(jahr, ct));
        positionen.AddRange(await HealthAsync(jahr, ct));
        positionen.AddRange(await BookingsAsync(jahr, TaxCategory.Handwerkerleistung, TaxSectionKind.Handwerker, ct));
        positionen.AddRange(await CommuteAsync(jahr, ct));
        positionen.AddRange(await BookingsAsync(jahr, TaxCategory.Werbungskosten, TaxSectionKind.Werbungskosten, ct));

        // Eine Position über 0 € ist keine Steuerposition: sie steht unten und zählt nirgends mit.
        var gezaehlt = positionen.Where(p => !p.Pending).ToList();

        return new TaxYearDto
        {
            Year = jahr,
            Years = jahre,

            // Nur Abschnitte, die etwas enthalten. Ein leerer Abschnitt mit Einschränkungstext
            // behauptete eine Prüfung, die niemand angestellt hat.
            Sections =
            [
                .. Captions
                    .Where(c => gezaehlt.Any(p => p.Section == c.Key))
                    .Select(c => new TaxSectionDto
                    {
                        Kind = c.Key,
                        Title = c.Value.Title,
                        Caveat = c.Value.Caveat,
                        Positions = [.. gezaehlt.Where(p => p.Section == c.Key).OrderByDescending(p => p.Amount)],
                    }),
            ],

            Pending = [.. positionen.Where(p => p.Pending)],
            Excluded = Exclusions,
        };
    }

    // ── Abschnitte ─────────────────────────────────────────────────────────────────────────

    /// <summary>Titel und Einschränkung je Abschnitt.</summary>
    /// <remarks>
    /// Die Einschränkung steht im Bericht neben der Summe und nicht als Fußnote. „Nur
    /// Arbeitslohn, nicht Material“ entscheidet darüber, ob die Zahl darüber überhaupt etwas
    /// wert ist — wer sie überliest, reicht eine falsche Summe ein.
    /// </remarks>
    private static readonly Dictionary<TaxSectionKind, (string Title, string Caveat)> Captions = new()
    {
        [TaxSectionKind.Vorsorge] = (
            "Vorsorgeaufwendungen",
            "Kranken-, BU- und Rentenbeiträge. Für Riester braucht das Finanzamt die "
            + "Bescheinigung des Anbieters, nicht die Buchung."),

        [TaxSectionKind.Krankheit] = (
            "Krankheitskosten",
            "Nur Eigenanteile — erstattete Beträge zählen nicht. Wirksam erst über der "
            + "zumutbaren Belastung; die App kennt dein zu versteuerndes Einkommen nicht und "
            + "rechnet sie deshalb nicht aus."),

        [TaxSectionKind.Handwerker] = (
            "Handwerkerleistungen",
            "Nur Arbeitslohn, nicht Material — die App trennt das nicht selbst. Barzahlung wird "
            + "nicht anerkannt; alle Positionen hier stammen aus Kontobuchungen."),

        [TaxSectionKind.Werbungskosten] = (
            "Werbungskosten",
            "Kandidaten, keine Feststellung. Die Entfernungspauschale ist aus Entfernung und "
            + "Arbeitstagen gerechnet und muss geprüft werden."),
    };

    /// <summary>
    /// Was bewusst fehlt, mit Grund.
    /// </summary>
    /// <remarks>
    /// Der Block ist so wichtig wie die Summen: ohne ihn hält der Leser die Liste für
    /// vollständig. Die vier Fälle sind die, die in einem Haushalt mit Immobilie, Fahrzeug und
    /// privater Krankenversicherung regelmäßig für die Rückfrage sorgen.
    /// </remarks>
    private static readonly IReadOnlyList<TaxExclusionDto> Exclusions =
    [
        new("Darlehenszinsen der selbst genutzten Immobilie", "selbst genutzt — nicht absetzbar"),
        new("Kfz-Versicherung", "privat genutzt"),
        new("Beiträge zur Kapitallebensversicherung", "Altvertrag ohne Abzug"),
        new("Erstattete Arztrechnungen", "kein Eigenanteil"),
    ];

    // ── Vorsorgeaufwendungen ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Beiträge zu Kranken-, BU-, Risikoleben- und Altersvorsorgeverträgen.
    /// </summary>
    /// <remarks>
    /// Anteilig auf das Jahr: ein Vertrag, der im Juli beginnt, kostet in diesem Jahr sechs
    /// Monate. Den vollen Jahresbeitrag anzusetzen wäre bequem und falsch.
    /// </remarks>
    private async Task<List<TaxPositionDto>> PensionsAsync(int jahr, CancellationToken ct)
    {
        var vertraege = await db.Policies.AsNoTracking()
            .Where(p => Deductible.Contains(p.Kind))
            .ToListAsync(ct);

        var belegt = await LinkedAsync(LinkTargetType.Policy, ct);
        var positionen = new List<TaxPositionDto>();

        foreach (var vertrag in vertraege)
        {
            var monate = MonthsIn(jahr, vertrag.StartsOn, vertrag.EndsOn);
            if (monate == 0)
            {
                continue;
            }

            var betrag = decimal.Round(Yearly(vertrag) * monate / 12m, 2);
            if (betrag <= 0m)
            {
                continue;
            }

            // Riester ist der Sonderfall aus dem Handoff: die Buchung genügt dem Finanzamt
            // nicht, es will die Bescheinigung des Anbieters. Der Beleg heißt deshalb anders.
            var braucht = vertrag.Kind == PolicyKind.Riester;
            var vorhanden = !braucht && belegt.Contains(vertrag.Id);

            positionen.Add(new TaxPositionDto
            {
                Section = TaxSectionKind.Vorsorge,
                Label = vertrag.Name + " · Beiträge",
                Amount = betrag,
                Evidence = braucht
                    ? "Anbieterbescheinigung"
                    : vorhanden ? "Beitragsnachweis am Vertrag" : $"Beitragsrechnung {jahr}",
                DocumentMissing = !vorhanden,
                Href = $"/police/{vertrag.Id}",
            });
        }

        return positionen;
    }

    /// <summary>Vertragsarten, deren Beiträge überhaupt in Frage kommen.</summary>
    private static readonly PolicyKind[] Deductible =
    [
        PolicyKind.Health,
        PolicyKind.DisabilityInsurance,
        PolicyKind.TermLife,
        PolicyKind.Riester,
        PolicyKind.Pension,
        PolicyKind.OccupationalPension,
    ];

    private static decimal Yearly(Policy p) => p.PremiumInterval switch
    {
        PremiumInterval.Monthly => p.Premium * 12m,
        PremiumInterval.Quarterly => p.Premium * 4m,
        PremiumInterval.HalfYearly => p.Premium * 2m,
        _ => p.Premium,
    };

    /// <summary>Wie viele Monate eines Jahres ein Zeitraum abdeckt.</summary>
    private static int MonthsIn(int jahr, DateOnly? von, DateOnly? bis)
    {
        var start = new DateOnly(jahr, 1, 1);
        var ende = new DateOnly(jahr, 12, 31);

        if (von is { } a && a > ende)
        {
            return 0;
        }

        if (bis is { } b && b < start)
        {
            return 0;
        }

        var ersterMonat = von is { } s && s > start ? s.Month : 1;
        var letzterMonat = bis is { } e && e < ende ? e.Month : 12;

        return Math.Max(0, letzterMonat - ersterMonat + 1);
    }

    // ── Krankheitskosten ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Eigenanteile — aus derselben Rechnung wie die PKV-Bilanz.
    /// </summary>
    /// <remarks>
    /// Nicht nachgebaut, sondern <see cref="HealthBalanceService"/> gefragt. Genau daran ist der
    /// Prototyp gescheitert: 9.620 € in der Bilanz gegen 12.798 € im Bericht für dieselbe
    /// Aussage, weil die Apotheke einmal doppelt zählte. Zwei Rechnungen für eine Größe laufen
    /// irgendwann auseinander, und der Tag, an dem es passiert, fällt niemandem auf.
    /// </remarks>
    private async Task<List<TaxPositionDto>> HealthAsync(int jahr, CancellationToken ct)
    {
        var bilanz = await health.GetAsync(jahr, allYears: false, ct);
        var eigen = bilanz.Split.OwnShare;

        if (eigen <= 0m)
        {
            return [];
        }

        var anzahl = bilanz.BillCount;

        return
        [
            new TaxPositionDto
            {
                Section = TaxSectionKind.Krankheit,
                Label = "Eigenanteile Arzt, Klinik, Apotheke",
                Amount = decimal.Round(eigen, 2),
                Evidence = $"{anzahl} {(anzahl == 1 ? "Rechnung" : "Rechnungen")} · aus der PKV-Bilanz",
                Href = "/gesundheit",
            },
        ];
    }

    // ── Buchungen ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ausgaben aus Kategorien, die steuerlich eingeordnet sind.
    /// </summary>
    /// <remarks>
    /// Je Zahlungsempfänger eine Zeile. Dass alles hier aus Kontobuchungen stammt, ist keine
    /// Umsetzungsbequemlichkeit, sondern die Bedingung: Barzahlung erkennt das Finanzamt bei
    /// Handwerkerleistungen nicht an.
    /// </remarks>
    private async Task<List<TaxPositionDto>> BookingsAsync(
        int jahr, TaxCategory art, TaxSectionKind abschnitt, CancellationToken ct)
    {
        var von = new DateOnly(jahr, 1, 1);
        var bis = new DateOnly(jahr, 12, 31);

        var buchungen = await db.Transactions.AsNoTracking()
            .Where(t => t.Category != null && t.Category.TaxCategory == art)
            .Where(t => t.BookingDate >= von && t.BookingDate <= bis && t.Amount < 0)
            .Select(t => new { t.Id, t.Payee, t.Amount, t.BookingDate })
            .ToListAsync(ct);

        if (buchungen.Count == 0)
        {
            return [];
        }

        var belegt = await LinkedAsync(LinkTargetType.Transaction, ct);

        return
        [
            .. buchungen
                .GroupBy(t => t.Payee, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var vorhanden = g.All(t => belegt.Contains(t.Id));
                    var anzahl = g.Count();

                    // Der Beleg heißt nach seinem Datum, nicht nach dem Empfänger: der steht
                    // schon in der Zeile darüber, und „fehlt: Rechnung Heizungswartung Grau
                    // 2025" unter „Heizungswartung Grau" sagt dasselbe zweimal.
                    var wann = anzahl == 1
                        ? GermanFormat.MonthYear(g.First().BookingDate)
                        : jahr.ToString();

                    return new TaxPositionDto
                    {
                        Section = abschnitt,
                        Label = g.Key,
                        Amount = decimal.Round(g.Sum(t => -t.Amount), 2),
                        Evidence = vorhanden
                            ? $"{anzahl} {(anzahl == 1 ? "Beleg" : "Belege")} am Konto"
                            : $"{(anzahl == 1 ? "Rechnung" : anzahl + " Rechnungen")} {wann}",
                        DocumentMissing = !vorhanden,
                        Href = "/konten",
                    };
                }),
        ];
    }

    // ── Entfernungspauschale ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Weg zur Arbeit.
    /// </summary>
    /// <remarks>
    /// <para>Das Musterbeispiel für die Trennung der beiden Kennzeichen: die Position ist
    /// <b>belegt</b> — Entfernung und Arbeitstage stehen im Arbeitsverhältnis — und trotzdem
    /// <b>geschätzt</b>, weil niemand nachgehalten hat, an welchen Tagen der Weg wirklich
    /// anfiel. Sie im Topf „ohne Beleg“ zu führen hätte den Nutzer Belege suchen lassen, die er
    /// längst hat.</para>
    /// <para>Ohne Entfernung oder ohne Arbeitstage entsteht die Position nicht. Eine Pauschale
    /// aus geratener Entfernung wäre keine Schätzung mehr.</para>
    /// </remarks>
    private async Task<List<TaxPositionDto>> CommuteAsync(int jahr, CancellationToken ct)
    {
        var verhaeltnisse = await db.Employments.AsNoTracking().ToListAsync(ct);
        var positionen = new List<TaxPositionDto>();

        foreach (var arbeit in verhaeltnisse)
        {
            if (arbeit.CommuteKilometres is not { } km || arbeit.WorkDaysPerYear is not { } tage)
            {
                continue;
            }

            if (km <= 0m || tage <= 0 || MonthsIn(jahr, arbeit.StartsOn, arbeit.EndsOn) == 0)
            {
                continue;
            }

            // Anteilig, wenn das Verhältnis nicht das ganze Jahr lief.
            var anteil = MonthsIn(jahr, arbeit.StartsOn, arbeit.EndsOn) / 12m;
            var gefahren = (int)Math.Round(tage * anteil, MidpointRounding.AwayFromZero);

            var betrag = decimal.Round(gefahren * DailyRate(km), 2);
            if (betrag <= 0m)
            {
                continue;
            }

            positionen.Add(new TaxPositionDto
            {
                Section = TaxSectionKind.Werbungskosten,
                Label = $"Fahrten zur Arbeit · {gefahren} Tage × {GermanFormat.Quantity(km)} km",
                Amount = betrag,
                Evidence = $"aus Entfernung und Arbeitstagen bei {arbeit.Employer} gerechnet",
                Estimated = true,
                Href = "/arbeit",
            });
        }

        return positionen;
    }

    /// <summary>
    /// Die Pauschale für einen Arbeitstag.
    /// </summary>
    /// <remarks>
    /// Gestaffelt, wie das Gesetz sie kennt: die ersten zwanzig Kilometer zu 0,30 €, jeder
    /// weitere zu 0,38 €. Flach zu rechnen wäre einfacher und läge bei langen Wegen deutlich
    /// daneben.
    /// </remarks>
    private static decimal DailyRate(decimal km)
    {
        var kurz = Math.Min(km, ShortDistanceLimit);
        var lang = Math.Max(0m, km - ShortDistanceLimit);

        return kurz * ShortDistanceRate + lang * LongDistanceRate;
    }

    // ── Rundherum ──────────────────────────────────────────────────────────────────────────

    /// <summary>Die Zielobjekte, an denen ein Dokument hängt.</summary>
    private async Task<HashSet<int>> LinkedAsync(LinkTargetType art, CancellationToken ct)
        => [.. await db.DocumentLinks.AsNoTracking()
            .Where(l => l.TargetType == art)
            .Select(l => l.TargetId)
            .Distinct()
            .ToListAsync(ct)];

    /// <summary>
    /// Die Jahre, für die es überhaupt etwas zu zeigen gibt.
    /// </summary>
    /// <remarks>
    /// Aus den Daten und nicht aus einer festen Liste: ein Jahreswechsler, der auf 2019 springen
    /// lässt und dort nichts findet, behauptet Daten, die es nicht gibt. Das laufende Jahr ist
    /// immer dabei — es füllt sich noch.
    /// </remarks>
    private async Task<List<int>> YearsAsync(CancellationToken ct)
    {
        var ausRechnungen = await db.MedicalBills.AsNoTracking()
            .Select(b => b.BillDate.Year)
            .Distinct()
            .ToListAsync(ct);

        var ausBuchungen = await db.Transactions.AsNoTracking()
            .Where(t => t.Category != null && t.Category.TaxCategory != TaxCategory.None)
            .Select(t => t.BookingDate.Year)
            .Distinct()
            .ToListAsync(ct);

        return
        [
            .. ausRechnungen
                .Concat(ausBuchungen)
                .Append(clock.Today.Year)
                .Distinct()
                .OrderByDescending(j => j),
        ];
    }
}
