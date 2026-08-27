using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
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

    /// <summary>
    /// Die Kategorien einer Richtung samt dem, was an ihnen hängt.
    /// </summary>
    /// <remarks>
    /// Ausgaben und Einnahmen sind zwei getrennte Listen: eine Ausgabenkategorie darf bei einer
    /// Gutschrift nicht erscheinen, sonst wird eine falsche Zuordnung erst möglich gemacht.
    /// </remarks>
    public async Task<IReadOnlyList<CategoryUsageDto>> GetUsageAsync(
        CategoryDirection direction, CancellationToken ct = default)
        => await db.Categories.AsNoTracking()
            .Where(c => c.Direction == direction)
            .OrderBy(c => c.Id)
            .Select(c => new CategoryUsageDto
            {
                Id = c.Id,
                Name = c.Name,
                Direction = c.Direction,
                TransactionCount = c.Transactions.Count,
                RuleCount = c.Rules.Count,
                HasBudget = c.Budgets.Any(),
            })
            .ToListAsync(ct);

    /// <summary>Legt eine Kategorie an. Der Name muss neu und darf nicht leer sein.</summary>
    public async Task<CategoryDto> CreateAsync(
        string name, CategoryDirection direction, CancellationToken ct = default)
    {
        var sauber = (name ?? string.Empty).Trim();
        if (sauber.Length == 0)
        {
            throw new RuleViolationException("Bitte einen Namen eingeben.");
        }

        await EnsureFreeAsync(sauber, direction, keeping: null, ct);

        var category = new Category { Name = sauber, Direction = direction };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return new CategoryDto { Id = category.Id, Name = category.Name, Direction = category.Direction };
    }

    /// <summary>
    /// Benennt eine Kategorie um.
    /// </summary>
    /// <remarks>
    /// Buchungen, Regeln und Budgets zeigen per Id auf die Kategorie, nicht per Text — deshalb
    /// wirkt das Umbenennen überall zugleich, ohne dass hier etwas mitgezogen werden müsste. Was
    /// zurückkommt, ist die Zählung: der Nutzer soll sehen, wie weit die Änderung reicht.
    /// </remarks>
    public async Task<CategoryChangeResultDto> RenameAsync(
        int id, string name, CancellationToken ct = default)
    {
        var category = await db.Categories
                           .Include(c => c.Transactions)
                           .Include(c => c.Rules)
                           .Include(c => c.Budgets)
                           .FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new RuleViolationException("Diese Kategorie gibt es nicht.");

        var sauber = (name ?? string.Empty).Trim();
        if (sauber.Length == 0)
        {
            throw new RuleViolationException("Bitte einen Namen eingeben.");
        }

        await EnsureFreeAsync(sauber, category.Direction, keeping: id, ct);

        var ergebnis = Describe(category);
        category.Name = sauber;
        await db.SaveChangesAsync(ct);

        return ergebnis;
    }

    /// <summary>
    /// Löscht eine Kategorie und löst, was an ihr hing.
    /// </summary>
    /// <remarks>
    /// Buchungen fallen auf „nicht zugeordnet“ und tauchen damit im Triage-Banner auf — sie
    /// verschwinden nicht und werden auch nicht stillschweigend umgehängt. Regeln auf diese
    /// Kategorie werden entfernt: eine Regel ohne Ziel würde beim nächsten Import ins Leere greifen.
    /// </remarks>
    public async Task<CategoryChangeResultDto> DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await db.Categories
                           .Include(c => c.Transactions)
                           .Include(c => c.Rules)
                           .Include(c => c.Budgets)
                           .FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new RuleViolationException("Diese Kategorie gibt es nicht.");

        if (category.Budgets.Count > 0)
        {
            throw new RuleViolationException(
                "Auf diese Kategorie läuft ein Budget. Bitte erst das Budget ändern.");
        }

        var ergebnis = Describe(category);

        category.Transactions.ForEach(t => t.CategoryId = null);
        db.CategorizationRules.RemoveRange(category.Rules);
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);

        return ergebnis;
    }

    private static CategoryChangeResultDto Describe(Category category) => new()
    {
        TransactionCount = category.Transactions.Count,
        RuleCount = category.Rules.Count,
        HadBudget = category.Budgets.Count > 0,
    };

    /// <summary>Zwei Kategorien mit demselben Namen wären in jeder Liste nicht zu unterscheiden.</summary>
    private async Task EnsureFreeAsync(
        string name, CategoryDirection direction, int? keeping, CancellationToken ct)
    {
        var belegt = await db.Categories
            .Where(c => c.Direction == direction && c.Id != keeping)
            .AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct);

        if (belegt)
        {
            throw new RuleViolationException($"„{name}“ gibt es schon.");
        }
    }

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
