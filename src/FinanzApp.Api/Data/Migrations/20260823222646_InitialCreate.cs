using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanzApp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Iban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    InterestRatePercent = table.Column<decimal>(type: "TEXT", nullable: true),
                    InterestYearToDate = table.Column<long>(type: "INTEGER", nullable: true),
                    OpeningBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAsOf = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Depots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    TwrorPercent = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Depots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Area = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsurancePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SurrenderValue = table.Column<long>(type: "INTEGER", nullable: false),
                    ValuationDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Lender = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RemainingDebt = table.Column<long>(type: "INTEGER", nullable: false),
                    InterestRatePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    Installment = table.Column<long>(type: "INTEGER", nullable: false),
                    NextPaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicalBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    BillDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    BillNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    GrossAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnShare = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedReimbursement = table.Column<long>(type: "INTEGER", nullable: false),
                    ActualReimbursement = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SettlementReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReimbursementTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnShareTransactionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalBills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NetWorthSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetWorthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastBackup = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Insurances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Insurer = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PolicyNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Premium = table.Column<long>(type: "INTEGER", nullable: false),
                    PremiumInterval = table.Column<int>(type: "INTEGER", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NoticePeriodMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Insurances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Insurances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PlannedPerMonth = table.Column<long>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Budgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CategorizationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    PayeePattern = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorizationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategorizationRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    BookingDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    CounterAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImportReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    RequestKey = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    DepotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Isin = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Price = table.Column<long>(type: "INTEGER", nullable: false),
                    CostBasis = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceAsOf = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortfolioPositions_Depots_DepotId",
                        column: x => x.DepotId,
                        principalTable: "Depots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DocumentTypeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Area = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RedeemedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedAttempts = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PurchasePrice = table.Column<long>(type: "INTEGER", nullable: true),
                    MarketValue = table.Column<long>(type: "INTEGER", nullable: false),
                    LoanId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Properties_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    DocumentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetType = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Device = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContractNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    MonthlyAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NoticePeriodWeeks = table.Column<int>(type: "INTEGER", nullable: false),
                    NoticeToDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Area = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Contracts_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_HouseholdId",
                table: "Accounts",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_HouseholdId_Name_Direction",
                table: "Categories",
                columns: new[] { "HouseholdId", "Name", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationRules_CategoryId",
                table: "CategorizationRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategorizationRules_HouseholdId_PayeePattern",
                table: "CategorizationRules",
                columns: new[] { "HouseholdId", "PayeePattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_AccountId",
                table: "Contracts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_PropertyId",
                table: "Contracts",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentId_TargetType_TargetId",
                table: "DocumentLinks",
                columns: new[] { "DocumentId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_HouseholdId_TargetType_TargetId",
                table: "DocumentLinks",
                columns: new[] { "HouseholdId", "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_HouseholdId_DocumentDate",
                table: "Documents",
                columns: new[] { "HouseholdId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_HouseholdId_RelativePath",
                table: "Documents",
                columns: new[] { "HouseholdId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_HouseholdId_Name",
                table: "DocumentTypes",
                columns: new[] { "HouseholdId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Insurances_AccountId",
                table: "Insurances",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_Code",
                table: "Invitations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_HouseholdId",
                table: "Invitations",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ContractId",
                table: "Invoices",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_HouseholdId_Status_DueOn",
                table: "Invoices",
                columns: new[] { "HouseholdId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalBills_HouseholdId_Status",
                table: "MedicalBills",
                columns: new[] { "HouseholdId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NetWorthSnapshots_HouseholdId_Month",
                table: "NetWorthSnapshots",
                columns: new[] { "HouseholdId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioPositions_DepotId",
                table: "PortfolioPositions",
                column: "DepotId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioSnapshots_HouseholdId_Month",
                table: "PortfolioSnapshots",
                columns: new[] { "HouseholdId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_LoanId",
                table: "Properties",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityStates_HouseholdId",
                table: "SecurityStates",
                column: "HouseholdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_HouseholdId_Source_SourceType_SourceId",
                table: "TaskItems",
                columns: new[] { "HouseholdId", "Source", "SourceType", "SourceId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_HouseholdId_State_DueOn",
                table: "TaskItems",
                columns: new[] { "HouseholdId", "State", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId",
                table: "Transactions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_HouseholdId_BookingDate",
                table: "Transactions",
                columns: new[] { "HouseholdId", "BookingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_HouseholdId_ImportReference",
                table: "Transactions",
                columns: new[] { "HouseholdId", "ImportReference" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RequestKey",
                table: "Transactions",
                column: "RequestKey",
                unique: true,
                filter: "\"RequestKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_HouseholdId",
                table: "Users",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Budgets");

            migrationBuilder.DropTable(
                name: "CategorizationRules");

            migrationBuilder.DropTable(
                name: "DocumentLinks");

            migrationBuilder.DropTable(
                name: "ImportProfiles");

            migrationBuilder.DropTable(
                name: "InsurancePolicies");

            migrationBuilder.DropTable(
                name: "Insurances");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "MedicalBills");

            migrationBuilder.DropTable(
                name: "NetWorthSnapshots");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "PortfolioPositions");

            migrationBuilder.DropTable(
                name: "PortfolioSnapshots");

            migrationBuilder.DropTable(
                name: "SecurityStates");

            migrationBuilder.DropTable(
                name: "TaskItems");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Depots");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DocumentTypes");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Properties");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropTable(
                name: "Loans");
        }
    }
}
