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
        var pensions = await db.Policies.AsNoTracking().Where(p => p.IsCapitalForming).ToListAsync(ct);
        var loan = await db.Loans.AsNoTracking().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
        var security = await db.SecurityStates.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct);

        return new MoreOverviewDto
        {
            Pension = new PensionSummaryDto
            {
                Provider = pensions.Count == 1
                    ? pensions[0].Provider
                    : $"{pensions.Count} Verträge",
                TotalValue = pensions.Sum(p => p.AssetValue ?? 0m),

                // Der älteste Stichtag, nicht der jüngste: die Summe ist nur so frisch wie
                // ihr ältester Bestandteil.
                ValuationDate = pensions
                    .Select(p => p.ValuationDate)
                    .Where(d => d is not null)
                    .Min() ?? DateOnly.MinValue,
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
            PensionCount = await db.Policies.CountAsync(p => p.IsCapitalForming, ct),
            ProtectionCount = await db.Policies.CountAsync(p => !p.IsCapitalForming, ct),
            OpenMedicalBillCount = await db.MedicalBills.CountAsync(
                b => b.Status != MedicalBillStatus.Completed && b.Status != MedicalBillStatus.Rejected, ct),
            PropertyCount = await db.Properties.CountAsync(ct),
            ContractCount = await db.Contracts.CountAsync(ct),
            OpenTaskCount = await db.TaskItems.CountAsync(t => t.State != TaskState.Done, ct),
        };
    }
}
