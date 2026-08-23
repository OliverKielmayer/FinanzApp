using FinanzApp.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanzApp.Api.Data;

public class FinanzAppDbContext(DbContextOptions<FinanzAppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<PortfolioPosition> PortfolioPositions => Set<PortfolioPosition>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<NetWorthSnapshot> NetWorthSnapshots => Set<NetWorthSnapshot>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
    public DbSet<SecurityState> SecurityStates => Set<SecurityState>();

    /// <summary>
    /// Geldbeträge liegen in der Datenbank als ganzzahlige Cent.
    /// </summary>
    /// <remarks>
    /// SQLite kennt keinen dezimalen Typ und legt <c>decimal</c> sonst als TEXT ab — dann sind
    /// Summen und Sortierungen in SQL falsch. Als <c>long</c> bleibt beides korrekt, und in der
    /// Anwendung wird durchgehend mit <c>decimal</c> gerechnet.
    /// </remarks>
    private static readonly ValueConverter<decimal, long> MoneyConverter =
        new(value => (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero), cents => cents / 100m);

    private static readonly ValueConverter<decimal?, long?> NullableMoneyConverter =
        new(value => value == null ? null : (long)Math.Round(value.Value * 100m, MidpointRounding.AwayFromZero),
            cents => cents == null ? null : cents.Value / 100m);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ShortName).HasMaxLength(60).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.OpeningBalance).HasConversion(MoneyConverter);
            e.Property(x => x.InterestYearToDate).HasConversion(NullableMoneyConverter);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.Name, x.Direction }).IsUnique();
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Payee).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ImportReference).HasMaxLength(120);
            e.Property(x => x.Amount).HasConversion(MoneyConverter);
            e.HasIndex(x => x.BookingDate);
            e.HasIndex(x => x.ImportReference);

            // Trägt die Idempotenz von POST /api/transactions: derselbe Schlüssel kann nur
            // einmal eine Buchung anlegen, auch wenn der Client die Anfrage wiederholt.
            e.HasIndex(x => x.RequestKey).IsUnique().HasFilter("\"RequestKey\" IS NOT NULL");

            e.HasOne(x => x.Account).WithMany(a => a.Transactions)
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany(c => c.Transactions)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CategorizationRule>(e =>
        {
            e.Property(x => x.PayeePattern).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.PayeePattern).IsUnique();
            e.HasOne(x => x.Category).WithMany(c => c.Rules)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Budget>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.PlannedPerMonth).HasConversion(MoneyConverter);
            e.HasOne(x => x.Category).WithMany(c => c.Budgets)
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Depot>(e => e.Property(x => x.Name).HasMaxLength(120).IsRequired());

        b.Entity<PortfolioPosition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.HasOne(x => x.Depot).WithMany(d => d.Positions)
                .HasForeignKey(x => x.DepotId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Isin).HasMaxLength(12).IsRequired();
            e.Property(x => x.Price).HasConversion(MoneyConverter);
            e.Property(x => x.CostBasis).HasConversion(MoneyConverter);
        });

        b.Entity<Loan>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Lender).HasMaxLength(120).IsRequired();
            e.Property(x => x.RemainingDebt).HasConversion(MoneyConverter);
            e.Property(x => x.Installment).HasConversion(MoneyConverter);
        });

        b.Entity<InsurancePolicy>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(120).IsRequired();
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.SurrenderValue).HasConversion(MoneyConverter);
        });

        b.Entity<ImportProfile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Format).HasMaxLength(40).IsRequired();
        });

        b.Entity<NetWorthSnapshot>(e =>
        {
            e.Property(x => x.Value).HasConversion(MoneyConverter);
            e.HasIndex(x => x.Month).IsUnique();
        });

        b.Entity<PortfolioSnapshot>(e =>
        {
            e.Property(x => x.Value).HasConversion(MoneyConverter);
            e.HasIndex(x => x.Month).IsUnique();
        });
    }
}
