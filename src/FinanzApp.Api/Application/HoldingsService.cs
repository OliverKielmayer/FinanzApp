using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
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
    FinanzAppDbContext db, DashboardService dashboard, VehicleService vehicles)
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

        return new HoldingsHeadDto
        {
            Class = filter,
            Value = filter switch
            {
                null => vermoegen.FinancialAssets,
                HoldingClass.Protection or HoldingClass.Vehicles => kosten,
                _ => werte,
            },
            TangibleAssets = vermoegen.TangibleAssets,
            Liabilities = vermoegen.Liabilities,
            Net = vermoegen.Net,
            Count = filter == HoldingClass.Housing
                ? zeilen.Count(z => z.Value is not null)
                : zeilen.Count,
            SecondaryCount = filter == HoldingClass.Housing
                ? zeilen.Count(z => z.Value is null)
                : 0,
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
                HoldingClass.Accounts, konto.Name, MetaAccount(konto),
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
                Join(Count(positionen.Count, "Position", "Positionen")),
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
                    HoldingClass.Pension, police.Name, MetaPension(police),
                    value: police.CurrentValue,
                    note: police.ValuationDate is { } tag ? $"Stand {GermanFormat.Date(tag)}" : null,
                    route: "/vorsorge"));

                continue;
            }

            zeilen.Add(Row(
                HoldingClass.Protection, police.Name, MetaProtection(police),
                yearlyCost: Yearly(police.Premium, police.PremiumInterval),
                note: null,
                urgent: police.NoticeReminderOn is not null,
                route: "/absicherung"));
        }

        foreach (var objekt in await db.Properties.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Housing, objekt.Name, MetaProperty(objekt),
                value: objekt.MarketValue,
                isTangible: true,
                note: null,
                route: "/wohnen"));
        }

        foreach (var vertrag in await db.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Housing, vertrag.Name, MetaContract(vertrag),
                yearlyCost: vertrag.MonthlyAmount * 12m,
                note: null,
                route: "/wohnen"));
        }

        // Der Fahrzeugdienst rechnet die Zwoelfmonatskosten aus echten Buchungen samt
        // Versicherungsbeitrag. Sie hier noch einmal zu rechnen waere dieselbe Groesse zweimal.
        foreach (var fahrzeug in await vehicles.GetListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Vehicles, fahrzeug.Name, Join(fahrzeug.Plate, fahrzeug.Meta),
                yearlyCost: fahrzeug.CostsLastTwelveMonths,
                note: null,
                urgent: fahrzeug.HasDeadline,
                route: "/fahrzeuge"));
        }

        foreach (var darlehen in await db.Loans.AsNoTracking().OrderBy(l => l.Id).ToListAsync(ct))
        {
            zeilen.Add(Row(
                HoldingClass.Loans, darlehen.Name, MetaLoan(darlehen),
                value: -darlehen.RemainingDebt,
                note: $"nächste Zahlung {GermanFormat.Date(darlehen.NextPaymentDate)}",
                route: "/darlehen"));
        }

        return zeilen;
    }

    private static HoldingRowDto Row(
        HoldingClass klasse, string name, string meta,
        decimal? value = null, decimal? yearlyCost = null, bool isTangible = false,
        string? note = null, bool urgent = false, string route = "/") => new()
    {
        Class = klasse,
        ClassLabel = Label(klasse),
        Name = name,
        Meta = meta,
        Value = value,
        YearlyCost = yearlyCost,
        IsTangible = isTangible,
        Note = note,
        Urgent = urgent,
        Route = route,
    };

    // ── Metazeilen aus Rohfeldern ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fügt zusammen, was vorhanden ist.
    /// </summary>
    /// <remarks>
    /// Leere Teile fallen weg, statt als Abwesenheit ausgeschrieben zu werden. Eine Zeile
    /// „Vertrag · ohne Konto“ behauptet etwas über ein Feld, das schlicht leer ist.
    /// </remarks>
    private static string Join(params string?[] teile)
        => string.Join(" · ", teile.Where(t => !string.IsNullOrWhiteSpace(t)));

    private static string? Count(int anzahl, string einzahl, string mehrzahl)
        => anzahl == 0 ? null : $"{anzahl} {(anzahl == 1 ? einzahl : mehrzahl)}";

    private static string MetaAccount(Account konto)
        => Join(konto.BankName, konto.Iban, KindLabel(konto.Kind));

    private static string MetaPension(Policy police)
        => Join(
            PolicyLabel(police.Kind),
            police.Provider,
            police.PolicyNumber is { Length: > 0 } nr ? $"Nr. {nr}" : null,
            police.EndsOn is { } ablauf ? $"Ablauf {GermanFormat.Date(ablauf)}" : null);

    private static string MetaProtection(Policy police)
        => Join(
            police.Provider,
            police.PolicyNumber is { Length: > 0 } nr ? $"Nr. {nr}" : null,
            police.EndsOn is { } ende ? $"bis {GermanFormat.Date(ende)}" : null,
            police.NoticePeriodMonths > 0
                ? $"Kündigungsfrist {police.NoticePeriodMonths} "
                  + (police.NoticePeriodMonths == 1 ? "Monat" : "Monate")
                : null);

    private static string MetaProperty(Property objekt)
        => Join(
            PropertyLabel(objekt.Kind),
            objekt.Address,
            objekt.PurchaseDate is { } kauf ? $"Kauf {GermanFormat.Date(kauf)}" : null,
            objekt.LoanId is null ? null : "mit Darlehen");

    private static string MetaContract(Contract vertrag)
        => Join(
            "Vertrag",
            vertrag.Provider,
            vertrag.ContractNumber is { Length: > 0 } nr ? $"Nr. {nr}" : null,
            vertrag.NoticePeriodWeeks > 0
                ? $"Frist {vertrag.NoticePeriodWeeks} "
                  + (vertrag.NoticePeriodWeeks == 1 ? "Woche" : "Wochen")
                : null);

    private static string MetaLoan(Loan darlehen)
        => Join(
            darlehen.Lender,
            $"{GermanFormat.Percent(darlehen.InterestRatePercent, 2)} Sollzins",
            $"Rate {GermanFormat.EuroRounded(darlehen.Installment)}");

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
        _ => "Darlehen",
    };

    private static string KindLabel(AccountKind kind)
        => kind == AccountKind.Savings ? "Tagesgeld" : "Girokonto";

    private static string PolicyLabel(PolicyKind kind) => kind switch
    {
        PolicyKind.CapitalLife => "Kapital-LV",
        PolicyKind.Pension => "Rentenversicherung",
        PolicyKind.Riester => "Riester",
        PolicyKind.BuildingSociety => "Bausparen",
        PolicyKind.OccupationalPension => "Betriebliche Altersvorsorge",
        _ => "Vertrag",
    };

    private static string PropertyLabel(PropertyKind kind) => kind switch
    {
        PropertyKind.Apartment => "Wohnung",
        PropertyKind.Land => "Grundstück",
        _ => "Haus",
    };
}
