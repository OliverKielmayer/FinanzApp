using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die PKV-Bilanz — v5-Handoff, Abschnitt 12.
/// </summary>
/// <remarks>
/// <para>Sie schließt den Weg Rechnung → Einreichung → Erstattung mit einer Jahresbilanz.
/// Zeitraum ist das <b>Kalenderjahr</b> und nicht der Berichtsrahmen der übrigen Auswertungen:
/// Eigenanteile und Beiträge zählen steuerlich jahresweise.</para>
/// <para>Zwei Regeln tragen den ganzen Bericht. <b>Eigenanteile sind die Gesundheitsausgabe,
/// erstattete Beträge nicht</b> — beides zu addieren machte die Ausgabenseite doppelt so hoch.
/// Und <b>Anspruch ist nicht Auszahlung</b>: „ausgezahlt“ meint Geld, das eingegangen ist,
/// „erwartet“ den offenen Anspruch. Die beiden stehen nie unter demselben Wort.</para>
/// </remarks>
public sealed class HealthBalanceService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>
    /// Die Bilanz eines Kalenderjahres.
    /// </summary>
    /// <param name="year">Das Jahr. Ohne Angabe das laufende.</param>
    /// <param name="allYears">
    /// Ausdrücklich über alle Jahre. Dann entfallen Beitrag und Steuerbrücke: beides sind
    /// Jahresgrößen, und einen Jahresbeitrag neben mehrjährige Eigenanteile zu stellen ergäbe
    /// eine Summe, die es nicht gibt.
    /// </param>
    public async Task<HealthBalanceDto> GetAsync(
        int? year = null, bool allYears = false, CancellationToken ct = default)
    {
        var alle = await db.MedicalBills.AsNoTracking().ToListAsync(ct);

        // Das Rechnungsdatum bestimmt das Jahr, nicht der Zahlungseingang: die Behandlung fand
        // damals statt, und steuerlich hängt der Eigenanteil an ihr.
        var jahr = allYears ? (int?)null : year ?? clock.Today.Year;
        var gezeigt = jahr is { } j ? alle.Where(b => b.BillDate.Year == j).ToList() : alle;

        var abgeschlossen = gezeigt.Where(Abgeschlossen).ToList();
        var schnitt = AverageDays(abgeschlossen);
        var aufteilung = Split(gezeigt);

        return new HealthBalanceDto
        {
            Year = jahr,
            Years =
            [
                new(null, alle.Count, alle.Sum(b => b.GrossAmount)),
                .. alle.Select(b => b.BillDate.Year).Distinct().OrderByDescending(y => y)
                    .Select(y =>
                    {
                        var davon = alle.Where(b => b.BillDate.Year == y).ToList();
                        return new HealthYearDto(y, davon.Count, davon.Sum(b => b.GrossAmount));
                    }),
            ],

            Split = aufteilung,
            BillCount = gezeigt.Count,
            CompletedCount = abgeschlossen.Count,

            OwnSharePercent = Share(aufteilung.OwnShare, aufteilung.Total),
            PaidSharePercent = Share(aufteilung.Paid, aufteilung.Paid + aufteilung.Expected),

            AverageDays = schnitt,

            // Nur im Jahresblick: ein Jahresbeitrag über mehreren Jahren wäre eine Zahl ohne Bezug.
            YearlyPremium = jahr is null ? null : await PremiumAsync(ct),

            Providers = Providers(gezeigt),
            OpenBills = OpenBills(gezeigt, schnitt),
        };
    }

    // ── Die drei Teile ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rechnungssumme in ausgezahlt, erwartet und Eigenanteil.
    /// </summary>
    /// <remarks>
    /// Erwartet ist der <em>offene</em> Anspruch, also die erwartete Erstattung abzüglich dessen,
    /// was schon kam. Ein abgelehnter oder abgeschlossener Vorgang hat keinen offenen Anspruch
    /// mehr — was dort nicht gezahlt wurde, ist kein Erwarten, sondern eine Absage.
    /// </remarks>
    private static HealthSplitDto Split(List<MedicalBill> bills)
    {
        var gezahlt = bills.Sum(b => b.ActualReimbursement ?? 0m);
        var offen = bills.Sum(b => b.OpenAmount);

        // Der Eigenanteil steht in der Rechnung; was weder erstattet noch offen noch Eigenanteil
        // ist, hat die Versicherung abgelehnt — es trägt der Haushalt und gehört dazu.
        var eigen = bills.Sum(b => Math.Max(0m, b.GrossAmount - (b.ActualReimbursement ?? 0m) - b.OpenAmount));

        return new HealthSplitDto { Paid = gezahlt, Expected = offen, OwnShare = eigen };
    }

    /// <summary>
    /// Abgeschlossen ist, woran nichts mehr offen ist.
    /// </summary>
    /// <remarks>
    /// Abgeleitet aus <see cref="MedicalBill.OpenAmount"/> und nicht aus einer zweiten Liste von
    /// Zuständen. Genau daran ist es beim ersten Bau gescheitert: derselbe Vorgang galt hier als
    /// abgeschlossen und stand unten trotzdem mit 124 € offen. Eine Menge, eine Definition.
    /// </remarks>
    private static bool Abgeschlossen(MedicalBill b) => b.OpenAmount == 0m;

    /// <summary>
    /// Die durchschnittliche Bearbeitungsdauer, gerechnet statt gesetzt.
    /// </summary>
    /// <remarks>
    /// Aus Einreich- und Zahldatum der abgeschlossenen Vorgänge. Derselbe Wert entscheidet
    /// darüber, welcher offene Vorgang „über dem Schnitt“ wartet — sonst hätte die Einordnung
    /// eine andere Grundlage als die Kennzahl daneben.
    /// </remarks>
    private static decimal? AverageDays(List<MedicalBill> bills)
    {
        var dauern = bills
            .Where(b => b.SubmittedAt is not null && b.PaidAt is not null)
            .Select(b => (b.PaidAt!.Value.Date - b.SubmittedAt!.Value.Date).TotalDays)
            .Where(t => t >= 0)
            .ToList();

        return dauern.Count == 0 ? null : decimal.Round((decimal)dauern.Average(), 1);
    }

    /// <summary>
    /// Der Jahresbeitrag der privaten Krankenversicherung.
    /// </summary>
    /// <remarks>
    /// Aus den Verträgen der Art „Krankenversicherung“, auf ein Jahr gerechnet. Er steht im
    /// Bericht <em>getrennt</em>: der Beitrag ist Absicherung, keine Behandlungskosten. In eine
    /// Summe mit den Eigenanteilen geworfen wäre er beides und keines.
    /// </remarks>
    private async Task<decimal> PremiumAsync(CancellationToken ct)
    {
        var vertraege = await db.Policies.AsNoTracking()
            .Where(p => p.Kind == PolicyKind.Health)
            .Select(p => new { p.Premium, p.PremiumInterval })
            .ToListAsync(ct);

        return vertraege.Sum(v => v.PremiumInterval switch
        {
            PremiumInterval.Monthly => v.Premium * 12m,
            PremiumInterval.Quarterly => v.Premium * 4m,
            PremiumInterval.HalfYearly => v.Premium * 2m,
            _ => v.Premium,
        });
    }

    private static List<HealthProviderDto> Providers(List<MedicalBill> bills)
        => [.. bills
            .GroupBy(b => b.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var aufteilung = Split([.. g]);

                return new HealthProviderDto
                {
                    Provider = g.Key,
                    BillCount = g.Count(),
                    Split = aufteilung,
                    PaidSharePercent = Share(aufteilung.Paid, aufteilung.Paid + aufteilung.Expected),
                };
            })
            .OrderByDescending(p => p.Split.Total)];

    /// <summary>
    /// Die Vorgänge, bei denen noch Geld aussteht.
    /// </summary>
    /// <remarks>
    /// Nicht eingereicht heißt: es wartet niemand auf die Versicherung, sondern auf einen selbst.
    /// Der Unterschied steht in <see cref="HealthOpenBillDto.SubmittedOn"/>, damit die Anzeige
    /// nicht „wartet seit 0 Tagen“ schreibt, wo noch gar nichts unterwegs ist.
    /// </remarks>
    private List<HealthOpenBillDto> OpenBills(List<MedicalBill> bills, decimal? schnitt)
    {
        var heute = clock.Today;

        return
        [
            .. bills
                .Where(b => b.OpenAmount > 0m)
                .OrderBy(b => b.SubmittedAt ?? DateTime.MaxValue)
                .Select(b =>
                {
                    var eingereicht = b.SubmittedAt is { } s ? DateOnly.FromDateTime(s) : (DateOnly?)null;
                    var tage = eingereicht is { } e ? heute.DayNumber - e.DayNumber : (int?)null;

                    return new HealthOpenBillDto
                    {
                        Id = b.Id,
                        Provider = b.Provider,
                        BillDate = b.BillDate,
                        Expected = b.OpenAmount,
                        SubmittedOn = eingereicht,
                        WaitingDays = tage,
                        AboveAverage = tage is { } t && schnitt is { } d && t > d,
                    };
                }),
        ];
    }

    /// <summary>Ein Anteil in Prozent — oder nichts, wenn es keine Grundlage gibt.</summary>
    /// <remarks>
    /// Ohne Grundlage ist es nicht null Prozent, sondern unbekannt. „0 % ausgezahlt“ läse sich
    /// wie eine Ablehnung, wo es gar keinen Anspruch gab.
    /// </remarks>
    private static decimal? Share(decimal teil, decimal ganzes)
        => ganzes == 0m ? null : decimal.Round(teil / ganzes * 100m, 1);
}
