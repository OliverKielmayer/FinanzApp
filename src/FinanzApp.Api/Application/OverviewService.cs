using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>Kennzahlen für die Sammelseite „Mehr“.</summary>
public sealed class OverviewService(
    FinanzAppDbContext db,
    CatalogService catalog,
    ImportService imports,
    LoanService loans,
    CurrentUser current)
{
    public async Task<MoreOverviewDto> GetAsync(CancellationToken ct = default)
    {
        var policies = await db.InsurancePolicies.AsNoTracking().ToListAsync(ct);
        var loan = await db.Loans.AsNoTracking().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
        var security = await db.SecurityStates.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

        return new MoreOverviewDto
        {
            Insurance = new InsuranceSummaryDto
            {
                Provider = policies.FirstOrDefault()?.Provider ?? "—",
                SurrenderValue = policies.Sum(p => p.SurrenderValue),
                ValuationDate = policies.Count == 0
                    ? DateOnly.MinValue
                    : policies.Max(p => p.ValuationDate),
            },
            Loan = new LoanSummaryDto
            {
                LoanId = loan?.Id ?? 0,
                Lender = loan?.Lender ?? "—",
                Installment = loan?.Installment ?? 0m,
                RemainingDebt = await loans.GetTotalDebtAsync(ct),
            },
            Import = new ImportSummaryDto
            {
                ProfileCount = await imports.GetProfileCountAsync(ct),
                Formats = await imports.GetProfileFormatsAsync(ct),
            },
            CategoryCount = await catalog.GetCategoryCountAsync(ct),
            RuleCount = await catalog.GetRuleCountAsync(ct),
            HouseholdMemberCount = await db.Users.CountAsync(u => u.HouseholdId == current.HouseholdId, ct),
            Security = new SecuritySummaryDto
            {
                TwoFactorEnabled = security?.TwoFactorEnabled ?? false,
                LastBackup = security?.LastBackup ?? DateTime.MinValue,
            },
        };
    }
}
