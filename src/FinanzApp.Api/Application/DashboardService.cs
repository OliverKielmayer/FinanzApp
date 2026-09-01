using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>Stellt den Startscreen aus den Zahlen der übrigen Dienste zusammen.</summary>
public sealed class DashboardService(
    FinanzAppDbContext db,
    AccountService accounts,
    PortfolioService portfolio,
    LoanService loans,
    BudgetService budgets,
    IClock clock,
    CurrentUser user)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var cash = await accounts.GetTotalsByKindAsync(ct);
        var checking = cash[AccountKind.Checking];
        var savings = cash[AccountKind.Savings];
        var depotValue = await portfolio.GetTotalValueAsync(ct);

        // Nur kapitalbildende Verträge zählen ins Vermögen. Ein Risikoleben-Vertrag zahlt im
        // Todesfall - er ist kein Guthaben und darf hier nie auftauchen.
        var pensionRows = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming)
            .Select(p => new { p.CurrentValue, p.ValuationDate })
            .ToListAsync(ct);
        var pension = pensionRows.Sum(r => r.CurrentValue ?? 0m);
        var pensionAsOf = pensionRows.Select(r => r.ValuationDate).Where(d => d is not null).Min();
        var debt = await loans.GetTotalDebtAsync(ct);

        var gross = checking + savings + depotValue + pension;

        // Sachwerte kommen aus den Immobilien. Sie gehören ins Vermögen, aber nicht in
        // dieselbe Summe wie das, was auf Konten liegt.
        var quote = await ShareAsync(ct);
        var tangibleCount = await db.Properties.AsNoTracking().CountAsync(ct);
        var history = await db.NetWorthSnapshots.AsNoTracking()
            .OrderBy(s => s.Month)
            .Select(s => new TimeSeriesPointDto { Month = s.Month, Value = s.Value })
            .ToListAsync(ct);

        return new DashboardDto
        {
            NetWorth = new NetWorthDto
            {
                FinancialAssets = gross,
                TangibleAssets = quote.TangibleShare,
                TangibleTotal = quote.TangibleTotal,
                TangibleCount = tangibleCount,
                Liabilities = quote.DebtShare,
                LiabilitiesTotal = debt,
                Receivables = quote.Receivables,
                DeltaPreviousMonth = DeltaPreviousMonth(history),
                DeltaYearPercent = DeltaYearPercent(history),
            },
            Assets = await BuildAssetsAsync(checking, savings, depotValue, pension, pensionAsOf, gross, ct),
            History = history,
            Month = await BuildMonthKpiAsync(ct),
            Liability = await BuildLiabilityAsync(quote.DebtShare, debt, ct),
            TopBudgets = await budgets.GetTopAsync(3, ct),
        };
    }

    /// <summary>
    /// Wie viel von Objekten und Objektschulden dem Betrachter zusteht — Handoff „Gemeinsame
    /// Immobilie“, 3.2.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Quote wirkt an einer Stelle und schlägt überall durch.</b> Dashboard-Kopf,
    /// Bestand-Kopf und Vermögensbericht lesen dieselbe Zahl; jede Fläche für sich zu rechnen war
    /// im Entwurf der Grund, warum dieselbe Größe siebenmal auseinanderlief.</para>
    /// <para>Objekte ohne gepflegte Anteile gehören dem Haushalt ganz — dort zählt der volle
    /// Wert. Eine Quote zu erfinden, wo keine gepflegt ist, wäre schlimmer als keine.</para>
    /// <para>Die Schuld eines Objekts wird nur dann geteilt, wenn das Objekt geteilt ist. Ein
    /// Darlehen ohne Objekt — Auto, Anschaffung — trägt der Haushalt allein.</para>
    /// </remarks>
    private async Task<(decimal TangibleTotal, decimal TangibleShare, decimal DebtShare, decimal Receivables)>
        ShareAsync(CancellationToken ct)
    {
        var objekte = await db.Properties.AsNoTracking()
            .Include(p => p.Loan)
            .Include(p => p.Shares)
            .ToListAsync(ct);

        var schuldenGesamt = await loans.GetTotalDebtAsync(ct);

        var wertGesamt = objekte.Sum(p => p.MarketValue);
        var wertAnteil = 0m;
        var objektSchuld = 0m;
        var objektSchuldAnteil = 0m;
        var forderung = 0m;

        foreach (var objekt in objekte)
        {
            var meiner = objekt.Shares.FirstOrDefault(s => s.UserId == user.UserId);
            var schuld = objekt.Loan?.RemainingDebt ?? 0m;

            if (objekt.Shares.Count == 0)
            {
                wertAnteil += objekt.MarketValue;
                continue;
            }

            objektSchuld += schuld;

            if (meiner is null)
            {
                continue;
            }

            var quote = meiner.Percent / 100m;
            wertAnteil += Runde(quote * objekt.MarketValue);
            objektSchuldAnteil += Runde(quote * schuld);

            var eingebracht = objekt.Shares.Sum(s => s.Equity);
            forderung += Runde(meiner.Equity - (quote * eingebracht));
        }

        // Nur die Schuld geteilter Objekte wird gequotet; alle übrigen Darlehen bleiben ganz.
        var uebrigeSchuld = schuldenGesamt - objektSchuld;

        return (wertGesamt, wertAnteil, uebrigeSchuld + objektSchuldAnteil, forderung);
    }

    private static decimal Runde(decimal wert)
        => decimal.Round(wert, 2, MidpointRounding.AwayFromZero);

    private async Task<IReadOnlyList<AssetSliceDto>> BuildAssetsAsync(
        decimal checking, decimal savings, decimal depotValue, decimal pension, DateOnly? pensionAsOf,
        decimal gross, CancellationToken ct)
    {
        var accountRows = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Kind, a.BankName, a.InterestRatePercent })
            .ToListAsync(ct);

        var depotName = await db.Depots.AsNoTracking().Select(d => d.Name).FirstOrDefaultAsync(ct) ?? "Depot";
        // Der Stichtag gehört an die Kachel: ein Jahresstand ist kein Tageskurs.
        var pensionCount = await db.Policies.CountAsync(p => p.IsCapitalForming, ct);
        var pensionSubtitle = pensionAsOf is { } asOf
            ? $"{pensionCount} Verträge · Stand {asOf:MM}/{asOf:yyyy}"
            : $"{pensionCount} Verträge";

        var checkingBanks = Banks(accountRows.Where(a => a.Kind == AccountKind.Checking).Select(a => a.BankName));
        var savingsRows = accountRows.Where(a => a.Kind == AccountKind.Savings).ToList();
        var savingsSubtitle = Banks(savingsRows.Select(a => a.BankName));
        if (savingsRows.FirstOrDefault()?.InterestRatePercent is { } rate)
        {
            savingsSubtitle += " · " + GermanFormat.Percent(rate, 2);
        }

        return
        [
            Slice("Girokonten", checkingBanks, checking, gross, "/konten"),
            Slice("Tagesgeld", savingsSubtitle, savings, gross, "/konten"),
            Slice("Depot", depotName, depotValue, gross, "/depot"),
            Slice("Vorsorge", pensionSubtitle, pension, gross, "/vorsorge"),
        ];
    }

    private static string Banks(IEnumerable<string> names) => string.Join(" · ", names.Distinct());

    private static AssetSliceDto Slice(string label, string subtitle, decimal value, decimal gross, string route)
        => new()
        {
            Label = label,
            Subtitle = subtitle,
            Value = value,
            ShareOfFinancialAssets = gross == 0 ? 0 : value / gross,
            Route = route,
        };

    /// <summary>
    /// Einnahmen, Ausgaben und Sparquote des laufenden Monats. Umbuchungen bleiben außen vor —
    /// eine Verschiebung zwischen eigenen Konten ist weder das eine noch das andere.
    /// </summary>
    private async Task<MonthKpiDto> BuildMonthKpiAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind != TransactionKind.Transfer && t.BookingDate >= from && t.BookingDate <= to)
            .Select(t => new { t.Kind, t.Amount })
            .ToListAsync(ct);

        var income = rows.Where(r => r.Kind == TransactionKind.Income).Sum(r => r.Amount);
        var expenses = Math.Abs(rows.Where(r => r.Kind == TransactionKind.Expense).Sum(r => r.Amount));

        return new MonthKpiDto
        {
            Year = today.Year,
            Month = today.Month,
            Income = income,
            Expenses = expenses,
            SavingsRatePercent = income == 0 ? 0 : (income - expenses) / income * 100m,
        };
    }

    /// <summary>
    /// Die Bilanzzeile „Verbindlichkeiten“.
    /// </summary>
    /// <remarks>
    /// <b>Sie trägt den Haftungsanteil, nicht die ganze Restschuld.</b> Sonst stünde in derselben
    /// Bilanz über der Zeile ein anderer Betrag als in ihr — die Kopfzeile nennt den Anteil. Die
    /// ganze Schuld geht als eigene Zahl mit hinaus; der Schirm setzt daraus den Satz, und der
    /// Tilgungsplan hinter dem Sprung zeigt sie unverändert.
    /// </remarks>
    private async Task<LiabilityDto> BuildLiabilityAsync(
        decimal share, decimal total, CancellationToken ct)
    {
        var loan = await db.Loans.AsNoTracking().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);

        return new LiabilityDto
        {
            LoanId = loan?.Id ?? 0,
            Label = "Verbindlichkeiten",
            Subtitle = loan is null ? "keine" : "Darlehen · " + loan.Lender,
            Amount = share,
            AmountTotal = total,
        };
    }

    private static decimal DeltaPreviousMonth(List<TimeSeriesPointDto> history)
        => history.Count < 2 ? 0 : history[^1].Value - history[^2].Value;

    private static decimal DeltaYearPercent(List<TimeSeriesPointDto> history)
    {
        if (history.Count < 2)
        {
            return 0;
        }

        var start = history[0].Value;
        return start == 0 ? 0 : (history[^1].Value / start - 1) * 100m;
    }
}
