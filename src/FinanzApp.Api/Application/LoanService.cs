using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class LoanService(FinanzAppDbContext db)
{
    /// <summary>Wie viele Monate der Tilgungsplan standardmäßig vorausrechnet.</summary>
    public const int DefaultScheduleMonths = 12;

    public async Task<LoanDto?> GetAsync(int id, int months = DefaultScheduleMonths, CancellationToken ct = default)
    {
        var loan = await db.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (loan is null)
        {
            return null;
        }

        return new LoanDto
        {
            Id = loan.Id,
            Name = loan.Name,
            Lender = loan.Lender,
            RemainingDebt = loan.RemainingDebt,
            InterestRatePercent = loan.InterestRatePercent,
            Installment = loan.Installment,
            NextPaymentDate = loan.NextPaymentDate,
            Schedule = BuildSchedule(
                loan.RemainingDebt, loan.InterestRatePercent, loan.Installment, loan.NextPaymentDate, months),
        };
    }

    public async Task<int?> GetPrimaryLoanIdAsync(CancellationToken ct = default)
        => await db.Loans.AsNoTracking().OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);

    /// <summary>Summe aller Restschulden, positiv geführt.</summary>
    public async Task<decimal> GetTotalDebtAsync(CancellationToken ct = default)
        => (await db.Loans.AsNoTracking().Select(l => l.RemainingDebt).ToListAsync(ct)).Sum();

    /// <summary>
    /// Annuitätischer Tilgungsplan. Je Monat: Zins auf die Restschuld, der Rest der Rate tilgt.
    /// Gerechnet wird auf Cent genau — die letzte Rate wird auf die verbleibende Schuld gekappt.
    /// </summary>
    public static IReadOnlyList<AmortizationEntryDto> BuildSchedule(
        decimal remainingDebt, decimal interestRatePercent, decimal installment, DateOnly firstPayment, int months)
    {
        var monthlyRate = interestRatePercent / 100m / 12m;
        var schedule = new List<AmortizationEntryDto>(months);
        var month = new DateOnly(firstPayment.Year, firstPayment.Month, 1);

        for (var i = 0; i < months && remainingDebt > 0m; i++)
        {
            var interest = Math.Round(remainingDebt * monthlyRate, 2, MidpointRounding.AwayFromZero);
            var principal = Math.Min(installment - interest, remainingDebt);
            remainingDebt -= principal;

            schedule.Add(new AmortizationEntryDto
            {
                Month = month,
                Interest = interest,
                Principal = principal,
                RemainingDebt = remainingDebt,
            });

            month = month.AddMonths(1);
        }

        return schedule;
    }
}
