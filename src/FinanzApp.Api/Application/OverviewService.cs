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
    DocumentService documents,
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
            Areas = await BuildAreaCountsAsync(ct),
            Security = new SecuritySummaryDto
            {
                TwoFactorEnabled = security?.TwoFactorEnabled ?? false,
                LastBackup = security?.LastBackup ?? DateTime.MinValue,
            },
        };
    }

    private async Task<AreaCountsDto> BuildAreaCountsAsync(CancellationToken ct)
    {
        var page = await documents.GetPageAsync(ct: ct);

        return new AreaCountsDto
        {
            DocumentCount = page.TotalCount,
            MissingFileCount = page.MissingFileCount,
            InsuranceCount = await db.Insurances.CountAsync(ct),
            OpenMedicalBillCount = await db.MedicalBills.CountAsync(
                b => b.Status != MedicalBillStatus.Completed && b.Status != MedicalBillStatus.Rejected, ct),
            PropertyCount = await db.Properties.CountAsync(ct),
            ContractCount = await db.Contracts.CountAsync(ct),
            OpenTaskCount = await db.TaskItems.CountAsync(t => t.State != TaskState.Done, ct),
        };
    }
}
