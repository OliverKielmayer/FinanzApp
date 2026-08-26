using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>Kategorien und Kategorisierungsregeln.</summary>
public sealed class CatalogService(FinanzAppDbContext db)
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        CategoryDirection? direction = null, CancellationToken ct = default)
        => await db.Categories.AsNoTracking()
            .Where(c => direction == null || c.Direction == direction)
            .OrderBy(c => c.Id)
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, Direction = c.Direction })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategorizationRuleDto>> GetRulesAsync(CancellationToken ct = default)
        => await db.CategorizationRules.AsNoTracking()
            .Include(r => r.Category)
            .OrderBy(r => r.PayeePattern)
            .Select(r => new CategorizationRuleDto
            {
                Id = r.Id,
                PayeePattern = r.PayeePattern,
                CategoryId = r.CategoryId,
                CategoryName = r.Category!.Name,
                LearnedOn = r.LearnedAt == null
                    ? null
                    : DateOnly.FromDateTime(r.LearnedAt.Value),
            })
            .ToListAsync(ct);

    public Task<int> GetCategoryCountAsync(CancellationToken ct = default) => db.Categories.CountAsync(ct);

    public Task<int> GetRuleCountAsync(CancellationToken ct = default) => db.CategorizationRules.CountAsync(ct);

    /// <summary>
    /// Löscht eine gelernte Regel.
    /// </summary>
    /// <remarks>
    /// Bereits importierte Buchungen bleiben unverändert. Eine Regel ordnet beim Einlesen zu; sie
    /// ist keine fortlaufende Verknüpfung, die man wieder aufziehen könnte. Der Regelscreen sagt
    /// das auch, damit niemand das Löschen für eine Korrektur der Vergangenheit hält.
    /// </remarks>
    public async Task<bool> DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.CategorizationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return false;
        }

        db.CategorizationRules.Remove(rule);
        await db.SaveChangesAsync(ct);

        return true;
    }
}
