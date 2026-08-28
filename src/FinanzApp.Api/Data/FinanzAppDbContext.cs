using System.Linq.Expressions;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinanzApp.Api.Data;

public class FinanzAppDbContext(DbContextOptions<FinanzAppDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Haushalt, dessen Daten in diesem Kontext sichtbar sind. Wird je Anfrage aus dem
    /// angemeldeten Benutzer gesetzt.
    /// </summary>
    /// <remarks>
    /// Bleibt der Wert 0, findet der Abfragefilter nichts — der Standardfall ist also „nichts
    /// sichtbar“, nicht „alles sichtbar“. Ein vergessenes Setzen führt damit zu einer leeren
    /// Antwort, nicht zu fremden Daten.
    /// </remarks>
    public int CurrentHouseholdId { get; set; }

    /// <summary>
    /// Der angemeldete Benutzer. Entscheidet, welche Konten sichtbar sind.
    /// </summary>
    /// <remarks>
    /// Wie beim Haushalt gilt: bleibt der Wert 0, sieht der Filter nur, was auf „Haushalt“ steht.
    /// Ein vergessenes Setzen zeigt damit weniger, nie mehr.
    /// </remarks>
    public int CurrentUserId { get; set; }

    public DbSet<Household> Households => Set<Household>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountShare> AccountShares => Set<AccountShare>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<PortfolioPosition> PortfolioPositions => Set<PortfolioPosition>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<NetWorthSnapshot> NetWorthSnapshots => Set<NetWorthSnapshot>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
    public DbSet<SecurityState> SecurityStates => Set<SecurityState>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
    public DbSet<MedicalBill> MedicalBills => Set<MedicalBill>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    /// <summary>Der Posteingang: Belege, die noch niemand eingeordnet hat.</summary>
    public DbSet<ScanInboxItem> ScanInbox => Set<ScanInboxItem>();

    /// <summary>Gespeicherte Einstellungen des Auswertungsbereichs, je Benutzer.</summary>
    public DbSet<ReportView> ReportViews => Set<ReportView>();

    /// <summary>Arbeitsverhältnisse und was daran hängt — die Einnahmenseite.</summary>
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<WorkAgreement> WorkAgreements => Set<WorkAgreement>();

    /// <summary>Gelesene Werte samt Herkunft — leer, solange keine Analyse angebunden ist.</summary>
    public DbSet<DocumentExtraction> DocumentExtractions => Set<DocumentExtraction>();

    /// <summary>Vorsorge und Absicherung in einer Tabelle, getrennt durch das Flag.</summary>
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

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
        ConfigureAuth(b);
        ConfigureFinance(b);
        ConfigureDocuments(b);
        ConfigureDomain(b);
        ConfigureSharing(b);
        ApplyHouseholdFilter(b);
        ApplyAccountVisibility(b);
    }

    /// <summary>
    /// Die zweite Stufe der Mandantentrennung: nicht freigegebene Konten und deren Buchungen.
    /// </summary>
    /// <remarks>
    /// <para>Sichtbar ist ein Konto, wenn der angemeldete Benutzer Eigentümer ist, die Freigabe
    /// auf „Haushalt“ steht oder er namentlich benannt ist. Steht der Filter hier statt in den
    /// einzelnen Diensten, kann ihn kein Dienst vergessen — und ein direkter API-Aufruf umgeht
    /// ihn nicht.</para>
    /// <para>Buchungen tragen die Bedingung ihres Kontos noch einmal ausdrücklich. Sich darauf zu
    /// verlassen, dass EF den Filter über die Navigation mitzieht, wäre genau die Annahme, an der
    /// so ein Leck entsteht.</para>
    /// <para>Diese beiden Aufrufe müssen die Haushaltsbedingung wiederholen: <c>HasQueryFilter</c>
    /// ersetzt, was die Schleife zuvor gesetzt hat.</para>
    /// </remarks>
    private void ApplyAccountVisibility(ModelBuilder b)
    {
        b.Entity<Account>().HasQueryFilter(a =>
            a.HouseholdId == CurrentHouseholdId
            && (a.OwnerUserId == CurrentUserId
                || a.Sharing == AccountSharing.Household
                || (a.Sharing == AccountSharing.Named
                    && a.Shares.Any(s => s.UserId == CurrentUserId))));

        b.Entity<Transaction>().HasQueryFilter(t =>
            t.HouseholdId == CurrentHouseholdId
            && (t.Account!.OwnerUserId == CurrentUserId
                || t.Account.Sharing == AccountSharing.Household
                || (t.Account.Sharing == AccountSharing.Named
                    && t.Account.Shares.Any(s => s.UserId == CurrentUserId))));
    }

    private static void ConfigureSharing(ModelBuilder b)
    {
        b.Entity<AccountShare>(e =>
        {
            e.HasOne(x => x.Account).WithMany(a => a.Shares)
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            // Zweimal dieselbe Freigabe waere keine zweite Berechtigung, nur eine zweite Zeile.
            e.HasIndex(x => new { x.AccountId, x.UserId }).IsUnique();
        });

        b.Entity<Account>()
            .HasOne(a => a.Owner).WithMany()
            .HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureAuth(ModelBuilder b)
    {
        b.Entity<Household>(e => e.Property(x => x.Name).HasMaxLength(120).IsRequired());

        b.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(400).IsRequired();

            // Eine Adresse meldet sich genau einmal an, über alle Haushalte hinweg.
            e.HasIndex(x => x.Email).IsUnique();

            e.HasOne(x => x.Household).WithMany(h => h.Users)
                .HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserSession>(e =>
        {
            e.Property(x => x.Device).HasMaxLength(200);
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany(u => u.Sessions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Invitation>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Household).WithMany(h => h.Invitations)
                .HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureFinance(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.ShortName).HasMaxLength(60).IsRequired();
            e.Property(x => x.BankName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Iban).HasMaxLength(34);
            e.Property(x => x.OpeningBalance).HasConversion(MoneyConverter);
            e.Property(x => x.InterestYearToDate).HasConversion(NullableMoneyConverter);
            e.HasIndex(x => x.HouseholdId);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.HouseholdId, x.Name, x.Direction }).IsUnique();
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Payee).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ImportReference).HasMaxLength(120);
            e.Property(x => x.Amount).HasConversion(MoneyConverter);
            e.HasIndex(x => new { x.HouseholdId, x.BookingDate });
            e.HasIndex(x => new { x.HouseholdId, x.ImportReference });

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
            e.HasIndex(x => new { x.HouseholdId, x.PayeePattern }).IsUnique();
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

        b.Entity<Depot>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Broker).HasMaxLength(120);
            e.Property(x => x.Number).HasMaxLength(60);
            e.Property(x => x.DepotKind).HasMaxLength(60);
            e.Property(x => x.QuoteSource).HasMaxLength(120);

            // Geld liegt in dieser Datenbank überall als Cent-Ganzzahl, auch hier.
            e.Property(x => x.StatedValue).HasConversion(NullableMoneyConverter);

            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<PortfolioPosition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Isin).HasMaxLength(12).IsRequired();
            e.Property(x => x.Price).HasConversion(MoneyConverter);
            e.Property(x => x.CostBasis).HasConversion(MoneyConverter);
            e.HasOne(x => x.Depot).WithMany(d => d.Positions)
                .HasForeignKey(x => x.DepotId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Loan>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Lender).HasMaxLength(120).IsRequired();
            e.Property(x => x.RemainingDebt).HasConversion(MoneyConverter);
            e.Property(x => x.Installment).HasConversion(MoneyConverter);
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
            e.HasIndex(x => new { x.HouseholdId, x.Month }).IsUnique();
        });

        b.Entity<PortfolioSnapshot>(e =>
        {
            e.Property(x => x.Value).HasConversion(MoneyConverter);
            e.HasIndex(x => new { x.HouseholdId, x.Month }).IsUnique();
        });

        b.Entity<SecurityState>(e => e.HasIndex(x => x.HouseholdId).IsUnique());
    }

    private static void ConfigureDocuments(ModelBuilder b)
    {
        b.Entity<DocumentType>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();

            // Nur gepflegte Typen belegen ihren Namen. Ein stillgelegter steht in keiner Liste
            // — bliebe er im Index, käme die Meldung „Einen Dokumenttyp ‚Police‘ gibt es schon“
            // über einer Liste, in der keiner steht.
            e.HasIndex(x => new { x.HouseholdId, x.Name })
                .IsUnique()
                .HasFilter("\"IsRetired\" = 0");
        });

        b.Entity<Document>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.Extension).HasMaxLength(16);
            e.Property(x => x.Tags).HasMaxLength(400);

            e.HasIndex(x => new { x.HouseholdId, x.DocumentDate });

            // Dieselbe Datei zweimal einzuhängen wäre ein Fehler, kein Merkmal.
            e.HasIndex(x => new { x.HouseholdId, x.RelativePath }).IsUnique();

            e.HasOne(x => x.DocumentType).WithMany(t => t.Documents)
                .HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DocumentLink>(e =>
        {
            e.HasIndex(x => new { x.HouseholdId, x.TargetType, x.TargetId });
            e.HasIndex(x => new { x.DocumentId, x.TargetType, x.TargetId }).IsUnique();
            e.HasOne(x => x.Document).WithMany(d => d.Links)
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDomain(ModelBuilder b)
    {
        b.Entity<MedicalBill>(e =>
        {
            e.Property(x => x.Provider).HasMaxLength(160).IsRequired();
            e.Property(x => x.BillNumber).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.GrossAmount).HasConversion(MoneyConverter);
            e.Property(x => x.OwnShare).HasConversion(MoneyConverter);
            e.Property(x => x.ExpectedReimbursement).HasConversion(MoneyConverter);
            e.Property(x => x.ActualReimbursement).HasConversion(NullableMoneyConverter);
            e.HasIndex(x => new { x.HouseholdId, x.Status });
        });

        b.Entity<Vehicle>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Plate).HasMaxLength(20).IsRequired();
            e.Property(x => x.Usage).HasMaxLength(80);

            // Die Versicherung wird verknüpft, nicht besessen — sie überlebt das Fahrzeug.
            e.HasOne(x => x.Policy).WithMany()
                .HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ScanInboxItem>(e =>
        {
            e.Property(x => x.Sender).HasMaxLength(160);
            e.HasOne(x => x.Document).WithMany()
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FiledAt);
        });

        b.Entity<Employment>(e =>
        {
            e.Property(x => x.Employer).HasMaxLength(160).IsRequired();
            e.Property(x => x.Position).HasMaxLength(160);
            e.Property(x => x.GrossMonthly).HasConversion(MoneyConverter);
            e.Property(x => x.NetMonthly).HasConversion(NullableMoneyConverter);
            e.HasIndex(x => new { x.HouseholdId, x.IsActive });
        });

        b.Entity<Payslip>(e =>
        {
            e.Property(x => x.Gross).HasConversion(MoneyConverter);
            e.Property(x => x.Net).HasConversion(MoneyConverter);
            e.Property(x => x.Payout).HasConversion(MoneyConverter);

            e.HasOne(x => x.Employment).WithMany(x => x.Payslips)
                .HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.SetNull);

            // Beleg und Buchung bleiben stehen, wenn die Abrechnung geht — und umgekehrt:
            // eine gelöschte Buchung nimmt keine Abrechnung mit, sie verliert nur ihren Verweis.
            e.HasOne(x => x.Document).WithMany()
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Transaction).WithMany()
                .HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.SetNull);

            // Ein Monat, eine Abrechnung je Verhältnis.
            e.HasIndex(x => new { x.EmploymentId, x.Month }).IsUnique();
        });

        b.Entity<WorkAgreement>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();

            e.HasOne(x => x.Employment).WithMany(x => x.Agreements)
                .HasForeignKey(x => x.EmploymentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany()
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ReportView>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasIndex(x => new { x.HouseholdId, x.OwnerUserId, x.Name }).IsUnique();

            // Kommagetrennt in einer Spalte. Der Vergleicher gehört dazu: ohne ihn hält EF eine
            // Liste für unverändert, solange sie dieselbe Instanz ist, und eine Ausschlusswahl
            // ginge beim Speichern still verloren.
            e.Property(x => x.ExcludedTransactionIds)
                .HasConversion(
                    v => string.Join(',', v),
                    s => s.Length == 0
                        ? new List<int>()
                        : s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
                    new ValueComparer<List<int>>(
                        (a, b) => a != null && b != null && a.SequenceEqual(b),
                        v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i)),
                        v => v.ToList()))
                .HasMaxLength(4000);
        });

        b.Entity<DocumentExtraction>(e =>
        {
            e.Property(x => x.FieldKey).HasMaxLength(60).IsRequired();
            e.Property(x => x.Label).HasMaxLength(120).IsRequired();
            e.Property(x => x.Value).HasMaxLength(400).IsRequired();
            e.HasOne(x => x.Document).WithMany()
                .HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocumentId);
        });

        b.Entity<Policy>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(120).IsRequired();
            e.Property(x => x.PolicyNumber).HasMaxLength(60);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Premium).HasConversion(MoneyConverter);
            e.Property(x => x.CurrentValue).HasConversion(NullableMoneyConverter);
            e.Property(x => x.MaturityValue).HasConversion(NullableMoneyConverter);
            e.Property(x => x.SumInsured).HasConversion(NullableMoneyConverter);
            e.Property(x => x.Deductible).HasConversion(NullableMoneyConverter);
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);

            // Die beiden Bereiche fragen immer nur ihre Hälfte ab.
            e.HasIndex(x => x.IsCapitalForming);
        });

        b.Entity<Property>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Address).HasMaxLength(240);
            e.Property(x => x.PurchasePrice).HasConversion(NullableMoneyConverter);
            e.Property(x => x.MarketValue).HasConversion(MoneyConverter);

            // Der Verweis auf das bestehende Darlehen — ohne Kopie und ohne Löschwirkung.
            e.HasOne(x => x.Loan).WithMany()
                .HasForeignKey(x => x.LoanId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Contract>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(120).IsRequired();
            e.Property(x => x.ContractNumber).HasMaxLength(60);
            e.Property(x => x.MonthlyAmount).HasConversion(MoneyConverter);
            e.HasOne(x => x.Property).WithMany(p => p.Contracts)
                .HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            e.Property(x => x.Number).HasMaxLength(60);
            e.Property(x => x.Amount).HasConversion(MoneyConverter);
            e.HasIndex(x => new { x.HouseholdId, x.Status, x.DueOn });
            e.HasOne(x => x.Contract).WithMany(c => c.Invoices)
                .HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TaskItem>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(500);
            e.HasIndex(x => new { x.HouseholdId, x.State, x.DueOn });

            // Eine automatisch erzeugte Aufgabe darf nicht bei jedem Lauf erneut entstehen.
            e.HasIndex(x => new { x.HouseholdId, x.Source, x.SourceType, x.SourceId }).IsUnique()
                .HasFilter("\"SourceId\" IS NOT NULL");
        });
    }

    /// <summary>
    /// Hängt den Mandantenfilter an jede Entität, die <see cref="IHouseholdOwned"/> trägt.
    /// </summary>
    /// <remarks>
    /// Bewusst über eine Schleife statt je Entität von Hand: eine neue Tabelle bekommt den Filter
    /// dadurch automatisch. Wer ihn von Hand setzen müsste, vergisst irgendwann eine — und genau
    /// das wäre das Datenleck, vor dem der Handoff warnt.
    /// Die Anmeldedaten (Benutzer, Sitzungen, Einladungen, Reset-Token) bleiben ungefiltert: sie
    /// werden gebraucht, <em>bevor</em> ein Haushalt feststeht. Ihre Abfragen tragen die
    /// Haushaltsbedingung ausdrücklich.
    /// </remarks>
    private void ApplyHouseholdFilter(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes()
                     .Where(t => typeof(IHouseholdOwned).IsAssignableFrom(t.ClrType)))
        {
            var entity = Expression.Parameter(entityType.ClrType, "e");
            var householdId = Expression.Property(entity, nameof(IHouseholdOwned.HouseholdId));
            var current = Expression.Property(Expression.Constant(this), nameof(CurrentHouseholdId));

            entityType.SetQueryFilter(Expression.Lambda(Expression.Equal(householdId, current), entity));
        }
    }

    /// <summary>
    /// Stempelt neue Datensätze auf den aktuellen Haushalt, sofern sie noch keinen tragen. Ein
    /// vergessenes <c>HouseholdId = …</c> in einem Dienst landet damit nicht als Waise in der
    /// Datenbank, sondern beim richtigen Haushalt.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampHousehold();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc cref="SaveChangesAsync(CancellationToken)"/>
    public override int SaveChanges()
    {
        // Auch der synchrone Weg muss stempeln — sonst hinge es vom Aufruf ab, ob ein Datensatz
        // beim richtigen Haushalt landet oder als Waise.
        StampHousehold();
        return base.SaveChanges();
    }

    private void StampHousehold()
    {
        if (CurrentHouseholdId == 0)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<IHouseholdOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.HouseholdId == 0)
            {
                entry.Entity.HouseholdId = CurrentHouseholdId;
            }
        }
    }
}
