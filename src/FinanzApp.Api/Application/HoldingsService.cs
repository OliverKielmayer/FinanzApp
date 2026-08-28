using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Bestand: alle Objekte in einer Liste — v5-Handoff, Abschnitt 3.
/// </summary>
/// <remarks>
/// <para>Ein Aggregat, nicht sieben Abrufe, die der Client zusammenlegt. Die Kontofreigaben
/// wirken davor, weil sie als Abfragefilter im <c>DbContext</c> sitzen: ein privates Konto eines
/// anderen Mitglieds erscheint nirgends und zählt in keine Summe.</para>
/// <para>Die Metazeilen entstehen je Klasse in einer Funktion. Der erste Bauversuch des
/// Prototyps las ein Anzeigefeld, das die Objekte gar nicht haben — 22 von 25 Zeilen blieben
/// ohne Untertitel, und die Liste war ärmer als jeder Einzelbereich vorher.</para>
/// </remarks>
public sealed class HoldingsService(
    FinanzAppDbContext db, DashboardService dashboard, VehicleService vehicles, IClock clock)
{
    public async Task<HoldingsDto> GetAsync(
        HoldingClass? filter = null, CancellationToken ct = default)
    {
        var alle = await BuildAsync(ct);
        var gezeigt = filter is { } klasse
            ? alle.Where(z => z.Class == klasse).ToList()
            : alle;

        var vermoegen = (await dashboard.GetAsync(ct)).NetWorth;
        var darlehen = await db.Loans.AsNoTracking().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);

        return new HoldingsDto
        {
            Classes =
            [
                new(null, "Alle", alle.Count),
                .. Enum.GetValues<HoldingClass>()
                    .Select(k => new HoldingClassCountDto(k, Label(k), alle.Count(z => z.Class == k))),
            ],
            Head = Head(filter, gezeigt, vermoegen, darlehen),
            Rows = gezeigt,
            AddIn = filter,
        };
    }

    // ── Der Kopf ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Kopfkennzahl je Filter.
    /// </summary>
    /// <remarks>
    /// Sie rechnet über <em>dieselben</em> Zeilen, die darunter stehen. Wer nachrechnet, kommt
    /// auf dieselbe Zahl — das ist der ganze Zweck, und ohne ihn wäre die Zusammenlegung der
    /// sieben Bereiche ein Verlust.
    /// </remarks>
    private static HoldingsHeadDto Head(
        HoldingClass? filter, List<HoldingRowDto> zeilen, NetWorthDto vermoegen, Loan? darlehen)
    {
        var werte = zeilen.Sum(z => z.Value ?? 0m);
        var kosten = zeilen.Sum(z => z.YearlyCost ?? 0m);

        // Beendetes trägt kein YearlyIncome — die Summe filtert sich damit von selbst.
        var einkommen = zeilen.Sum(z => z.YearlyIncome ?? 0m);

        return new HoldingsHeadDto
        {
            Class = filter,
            Value = filter switch
            {
                null => vermoegen.FinancialAssets,
                HoldingClass.Protection or HoldingClass.Vehicles => kosten,
                HoldingClass.Work => einkommen,
                _ => werte,
            },
            TangibleAssets = vermoegen.TangibleAssets,
            Liabilities = vermoegen.Liabilities,
            Net = vermoegen.Net,
            Count = filter switch
            {
                HoldingClass.Housing => zeilen.Count(z => z.Value is not null),

                // Laufende, nicht alle: die Unterzeile heißt „1 laufend“, und ein Zähler
                // muss zählen, was sein Wort sagt.
                HoldingClass.Work => zeilen.Count(z => z.YearlyIncome is not null),
                _ => zeilen.Count,
            },
            SecondaryCount = filter switch
            {
                HoldingClass.Housing => zeilen.Count(z => z.Value is null),
                HoldingClass.Work => zeilen.Count(z => z.YearlyIncome is null),
                _ => 0,
            },
            UrgentCount = zeilen.Count(z => z.Urgent),
            Installment = filter == HoldingClass.Loans ? darlehen?.Installment : null,
            NextPayment = filter == HoldingClass.Loans ? darlehen?.NextPaymentDate : null,
        };
    }

    // ── Die Zeilen ─────────────────────────────────────────────────────────────────────────

    private async Task<List<HoldingRowDto>> BuildAsync(CancellationToken ct)
    {
        var zeilen = new List<HoldingRowDto>();

        foreach (var konto in await db.Accounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct))
        {
            var gebucht = await db.Transactions.AsNoTracking()
                .Where(t => t.AccountId == konto.Id)
                .SumAsync(t => t.Amount, ct);

            zeilen.Add(Row(
                HoldingClass.Accounts, konto.Name, HoldingMeta.ForAccount(konto),
                value: konto.OpeningBalance + gebucht,
                note: $"Stand {GermanFormat.Date(konto.BalanceAsOf)}",
                route: "/konten"));
        }

        foreach (var depot in await db.Depots.AsNoTracking().OrderBy(d => d.Id).ToListAsync(ct))
        {
            var positionen = await db.PortfolioPositions.AsNoTracking()
                .Where(p => p.DepotId == depot.Id)
                .Select(p => new { p.Quantity, p.Price, p.PriceAsOf })
                .ToListAsync(ct);

            zeilen.Add(Row(
                HoldingClass.Depot, depot.Name,
                HoldingMeta.ForDepot(depot, positionen.Count),
                value: positionen.Sum(p => p.Quantity * p.Price),
                note: positionen.Count == 0
                    ? null
                    : $"Kurse {GermanFormat.Date(DateOnly.FromDateTime(positionen.Min(p => p.PriceAsOf)))}",
                route: "/depot"));
        }

        foreach (var police in await db.Policies.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct))
        {
            if (police.IsCapitalForming)
            {
                zeilen.Add(Row(
                    HoldingClass.Pension, police.Name, HoldingMeta.ForPension(police),
                    value: police.CurrentValue,
                    note: police.ValuationDate is { } tag ? $"Stand {GermanFormat.Date(tag)}" : null,
                    route: "/vorsorge"));

                continue;
            }

            zeilen.Add(Row(
                HoldingClass.Protection, police.Name, HoldingMeta.ForProtection(police),
                yearlyCost: Yearly(police.Premium, police.PremiumInterval),
                note: null,
                urgent: police.NoticeReminderOn is not null,
                route: "/absicherung"));
        }

        foreach (var objekt in await db.Properties.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Housing, objekt.Name, HoldingMeta.ForProperty(objekt),
                value: objekt.MarketValue,
                isTangible: true,
                note: null,
                route: "/wohnen"));
        }

        foreach (var vertrag in await db.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Housing, vertrag.Name, HoldingMeta.ForContract(vertrag),
                yearlyCost: vertrag.MonthlyAmount * 12m,
                note: null,
                route: "/wohnen"));
        }

        // Der Fahrzeugdienst rechnet die Zwoelfmonatskosten aus echten Buchungen samt
        // Versicherungsbeitrag. Sie hier noch einmal zu rechnen waere dieselbe Groesse zweimal.
        foreach (var fahrzeug in await vehicles.GetListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Vehicles, HoldingMeta.Join(fahrzeug.Name, fahrzeug.Plate),
                fahrzeug.Meta,
                yearlyCost: fahrzeug.CostsLastTwelveMonths,
                note: null,
                urgent: fahrzeug.HasDeadline,
                route: "/fahrzeuge"));
        }

        foreach (var stelle in await db.Employments.AsNoTracking()
                     .OrderBy(e => e.Id).ToListAsync(ct))
        {
            var laeuft = stelle.IsRunning(clock.Today);

            zeilen.Add(Row(
                HoldingClass.Work, stelle.Employer, HoldingMeta.ForEmployment(stelle),

                // Nur Laufendes trägt eine Jahreszahl. Der Prototyp summierte beide
                // Verhältnisse zu 127.200 €, während der Bereich selbst 77.760 € nannte.
                yearlyIncome: laeuft ? stelle.GrossMonthly * 12m : null,
                note: laeuft ? "Brutto pro Jahr" : "beendet",
                route: "/arbeit"));
        }

        foreach (var darlehen in await db.Loans.AsNoTracking().OrderBy(l => l.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Loans, darlehen.Name, HoldingMeta.ForLoan(darlehen),
                value: -darlehen.RemainingDebt,
                note: $"nächste Zahlung {GermanFormat.Date(darlehen.NextPaymentDate)}",
                route: "/darlehen"));
        }

        return zeilen;
    }

    private static HoldingRowDto Row(
        HoldingClass klasse, string name, string meta,
        decimal? value = null, decimal? yearlyCost = null, decimal? yearlyIncome = null,
        bool isTangible = false,
        string? note = null, bool urgent = false, string route = "/") => new()
    {
        Class = klasse,
        ClassLabel = Label(klasse),
        Name = name,
        Meta = meta,
        Value = value,
        YearlyCost = yearlyCost,
        YearlyIncome = yearlyIncome,
        IsTangible = isTangible,
        Note = note,
        Urgent = urgent,
        Route = route,
    };

    /// <summary>Ein Jahresbeitrag, egal in welchem Takt gezahlt wird.</summary>
    private static decimal Yearly(decimal amount, PremiumInterval interval) => interval switch
    {
        PremiumInterval.Monthly => amount * 12m,
        PremiumInterval.Quarterly => amount * 4m,
        PremiumInterval.HalfYearly => amount * 2m,
        _ => amount,
    };

    // ── Beschriftungen ─────────────────────────────────────────────────────────────────────

    private static string Label(HoldingClass klasse) => klasse switch
    {
        HoldingClass.Accounts => "Konten",
        HoldingClass.Depot => "Depot",
        HoldingClass.Pension => "Vorsorge",
        HoldingClass.Protection => "Absicherung",
        HoldingClass.Housing => "Wohnen",
        HoldingClass.Vehicles => "Fahrzeuge",
        HoldingClass.Work => "Arbeit",
        _ => "Darlehen",
    };
}
