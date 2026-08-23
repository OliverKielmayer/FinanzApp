using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Data;

/// <summary>
/// Beispieldaten aus dem Design-Handoff, Stichtag 23.08.2026.
/// </summary>
/// <remarks>
/// <para>Alle Summen der Oberfläche werden aus diesen Sätzen <em>gerechnet</em> — Kontosalden aus
/// Anfangsbestand plus Buchungen, Budgetauslastung aus den Buchungen der Kategorie, Depotwert aus
/// Stück mal Kurs. Die Beispieldaten sind so gewählt, dass dabei genau die Zahlen des Handoffs
/// herauskommen.</para>
/// <para>Zwei Detailwerte des Handoffs waren mit seinen eigenen Kopfzahlen nicht vereinbar und
/// wurden angepasst; die Kopfzahlen haben Vorrang, weil sie auf mehreren Screens auftauchen:</para>
/// <list type="bullet">
///   <item>Die vier Depotpositionen summieren sich im Handoff auf 132.440,22 €, der Depotwert steht
///   dort mit 132.480,00 €. Die Position „Allianz SE“ ist deshalb hier 46 St. zu 342,55 € statt
///   52 St. zu 302,26 €.</item>
///   <item>Die Einstandswerte, die die Positionsrenditen des Handoffs ergäben, summieren sich nicht
///   auf den ausgewiesenen G/V von +18.940,20 €. Der Einstand des Xtrackers trägt hier die
///   Differenz, seine Rendite liegt damit bei +19,2 % statt +4,2 %.</item>
/// </list>
/// </remarks>
public static class SeedData
{
    private static readonly DateOnly Today = new(2026, 8, 23);

    /// <summary>Zielsalden laut Handoff. Der Anfangsbestand ergibt sich daraus rückwärts.</summary>
    private static readonly Dictionary<string, decimal> TargetBalances = new()
    {
        ["Sparkasse Giro"] = 4812.60m,
        ["Raiffeisenbank Giro"] = 1947.35m,
        ["Tagesgeld Raiffeisen"] = 50000.00m,
    };

    public static async Task EnsureSeededAsync(FinanzAppDbContext db, CancellationToken ct = default)
    {
        if (await db.Accounts.AnyAsync(ct))
        {
            return;
        }

        var categories = SeedCategories(db);
        var accounts = SeedAccounts(db);
        var depot = SeedPortfolio(db);
        await db.SaveChangesAsync(ct);

        SeedTransactions(db, accounts, categories);
        SeedBudgets(db, categories);
        SeedRules(db, categories);
        SeedLoanAndInsurance(db);
        SeedImportProfiles(db);

        // Anfangsbestände so setzen, dass die gerechneten Salden den Demo-Ständen entsprechen.
        AlignOpeningBalances(db, accounts);
        SeedSnapshots(db, accounts, depot);

        db.SecurityStates.Add(new SecurityState
        {
            TwoFactorEnabled = true,
            LastBackup = new DateTime(2026, 8, 23, 3, 0, 0, DateTimeKind.Local),
        });

        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, Category> SeedCategories(FinanzAppDbContext db)
    {
        string[] expenses =
            ["Wohnen", "Lebensmittel", "Auto", "Freizeit", "Reisen", "Gesundheit", "Versicherung", "Sonstiges"];
        string[] income = ["Gehalt", "Dividenden", "Zinsen", "Miete", "Sonstiges"];

        var map = new Dictionary<string, Category>();
        foreach (var name in expenses)
        {
            var category = new Category { Name = name, Direction = CategoryDirection.Expense };
            db.Categories.Add(category);
            map[name] = category;
        }

        foreach (var name in income)
        {
            var category = new Category { Name = name, Direction = CategoryDirection.Income };
            db.Categories.Add(category);

            // Ausgaben- und Einnahmenkategorien dürfen denselben Namen tragen. Im Seed-Schlüssel
            // bekommt die Einnahmenseite deshalb ein Pluszeichen vorangestellt.
            map["+" + name] = category;
        }

        return map;
    }

    private static Dictionary<string, Account> SeedAccounts(FinanzAppDbContext db)
    {
        List<Account> accounts =
        [
            new()
            {
                Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
                Kind = AccountKind.Checking, Iban = "DE44 6725 0020 0034 8891 02",
                BalanceAsOf = Today,
            },
            new()
            {
                Name = "Raiffeisenbank Giro", ShortName = "Raiffeisenbank", BankName = "Raiffeisenbank",
                Kind = AccountKind.Checking, Iban = "DE12 6706 2366 0009 1140 07",
                BalanceAsOf = Today,
            },
            new()
            {
                Name = "Tagesgeld Raiffeisen", ShortName = "Tagesgeld", BankName = "Raiffeisenbank",
                Kind = AccountKind.Savings, InterestRatePercent = 2.35m, InterestYearToDate = 98.12m,
                BalanceAsOf = new DateOnly(2026, 8, 19),
            },
        ];

        db.Accounts.AddRange(accounts);
        return accounts.ToDictionary(a => a.Name);
    }

    private static void AlignOpeningBalances(FinanzAppDbContext db, Dictionary<string, Account> accounts)
    {
        foreach (var (name, account) in accounts)
        {
            var booked = db.Transactions.Local.Where(t => t.AccountId == account.Id).Sum(t => t.Amount);
            account.OpeningBalance = TargetBalances[name] - booked;
        }
    }

    private static void SeedTransactions(
        FinanzAppDbContext db, Dictionary<string, Account> accounts, Dictionary<string, Category> categories)
    {
        (int Day, string Payee, string? Category, string Account, decimal Amount)[] rows =
        [
            // Die sieben Buchungen aus dem Handoff. Sie stehen oben in der Liste.
            (22, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -68.42m),
            (21, "Shell Tankstelle", null, "Sparkasse Giro", -84.10m),
            (20, "Stadtwerke Strom", "Wohnen", "Sparkasse Giro", -96.00m),
            (19, "Umbuchung → Tagesgeld", null, "Sparkasse Giro", -1500.00m),
            (18, "Heidelberger Leben Beitrag", "Versicherung", "Raiffeisenbank Giro", -212.00m),
            (15, "PAYPAL .PAYCOMET SL", null, "Raiffeisenbank Giro", -39.99m),
            (1, "Gehalt EWV", "+Gehalt", "Sparkasse Giro", 5240.00m),

            // Weitere Buchungen des Monats, damit Budgetauslastung und Monatssummen aufgehen.
            (19, "Zahnarzt Dr. Weber", "Gesundheit", "Raiffeisenbank Giro", -187.60m),
            (18, "Spende Tierheim", "Sonstiges", "Sparkasse Giro", -80.00m),
            (17, "KFZ-Versicherung Allianz", "Versicherung", "Raiffeisenbank Giro", -78.40m),
            (16, "Karlstorbahnhof Kino", "Freizeit", "Raiffeisenbank Giro", -28.00m),
            (15, "Telekom Internet und Mobilfunk", "Wohnen", "Sparkasse Giro", -59.90m),
            (14, "EDEKA Neckargemünd", "Lebensmittel", "Sparkasse Giro", -87.20m),
            (13, "ARAL Tankstelle", "Auto", "Sparkasse Giro", -92.30m),
            (12, "Fitnessstudio Beitrag", "Freizeit", "Raiffeisenbank Giro", -49.90m),
            (11, "Apotheke am Markt", "Gesundheit", "Sparkasse Giro", -34.80m),
            (10, "Amazon Bestellung", "Sonstiges", "Sparkasse Giro", -62.71m),
            (9, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -64.15m),
            (8, "Restaurant Zum Ritter", "Freizeit", "Sparkasse Giro", -96.50m),
            (7, "Anzahlung Ferienwohnung", "Reisen", "Sparkasse Giro", -90.00m),
            (6, "ALDI SÜD Heidelberg", "Lebensmittel", "Sparkasse Giro", -41.93m),
            (6, "Drogeriemarkt dm", "Sonstiges", "Sparkasse Giro", -47.25m),
            (5, "Kfz-Werkstatt Ölwechsel", "Auto", "Sparkasse Giro", -61.70m),
            (4, "Bäckerei Göbes", "Lebensmittel", "Sparkasse Giro", -12.40m),
            (3, "Buchhandlung Schmitt", "Freizeit", "Sparkasse Giro", -61.60m),
            (2, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -137.90m),
            (2, "Zeitschriften-Abo", "Sonstiges", "Sparkasse Giro", -12.25m),
            (1, "Miete Wohnung Heidelberg", "Wohnen", "Sparkasse Giro", -1480.00m),
            (1, "Nebenkosten Hausgeld", "Wohnen", "Sparkasse Giro", -245.00m),
        ];

        var reference = 4200;
        foreach (var row in rows)
        {
            var isTransfer = row.Payee.StartsWith("Umbuchung", StringComparison.Ordinal);
            db.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 8, row.Day),
                Payee = row.Payee,
                Kind = isTransfer
                    ? TransactionKind.Transfer
                    : row.Amount >= 0 ? TransactionKind.Income : TransactionKind.Expense,
                Amount = row.Amount,
                AccountId = accounts[row.Account].Id,
                CategoryId = row.Category is null ? null : categories[row.Category].Id,
                CounterAccountId = isTransfer ? accounts["Tagesgeld Raiffeisen"].Id : null,
                ImportReference = "SEED-" + reference++,
                CreatedAt = new DateTime(2026, 8, row.Day, 6, 0, 0, DateTimeKind.Local),
            });
        }
    }

    private static void SeedBudgets(FinanzAppDbContext db, Dictionary<string, Category> categories)
    {
        // Der Budgetname ist frei wählbar. „Urlaub“ plant auf die Kategorie „Reisen“.
        (string Name, string Category, decimal Planned)[] rows =
        [
            ("Lebensmittel", "Lebensmittel", 500m),
            ("Freizeit", "Freizeit", 200m),
            ("Auto", "Auto", 300m),
            ("Urlaub", "Reisen", 250m),
        ];

        var order = 0;
        foreach (var row in rows)
        {
            db.Budgets.Add(new Budget
            {
                Name = row.Name,
                CategoryId = categories[row.Category].Id,
                PlannedPerMonth = row.Planned,
                SortOrder = order++,
            });
        }
    }

    private static void SeedRules(FinanzAppDbContext db, Dictionary<string, Category> categories)
    {
        // Bewusst ohne Regel für „Shell“ und „PAYPAL“ — diese beiden Buchungen bleiben
        // unkategorisiert und speisen das Triage-Banner.
        (string Pattern, string Category)[] rows =
        [
            ("REWE", "Lebensmittel"),
            ("EDEKA", "Lebensmittel"),
            ("ALDI", "Lebensmittel"),
            ("Bäckerei", "Lebensmittel"),
            ("ARAL", "Auto"),
            ("Stadtwerke", "Wohnen"),
            ("Telekom", "Wohnen"),
            ("Heidelberger Leben", "Versicherung"),
            ("Apotheke", "Gesundheit"),
            ("Amazon", "Sonstiges"),
            ("Drogeriemarkt dm", "Sonstiges"),
        ];

        foreach (var row in rows)
        {
            db.CategorizationRules.Add(new CategorizationRule
            {
                PayeePattern = row.Pattern,
                CategoryId = categories[row.Category].Id,
            });
        }
    }

    private static Depot SeedPortfolio(FinanzAppDbContext db)
    {
        var depot = new Depot { Name = "finanzen.net ZERO", TwrorPercent = 9.8m };
        db.Depots.Add(depot);

        var pricesAsOf = new DateTime(2026, 8, 22, 17, 35, 0, DateTimeKind.Local);
        (string Name, string Isin, decimal Quantity, decimal Price, decimal Cost)[] rows =
        [
            ("Vanguard FTSE All-World", "IE00BK5BQT80", 412m, 118.40m, 40182.70m),
            ("iShares Core MSCI World", "IE00B4L5Y983", 386m, 102.15m, 33386.03m),
            ("Xtrackers MSCI EM", "IE00BTJRMP35", 540m, 52.80m, 23925.96m),
            ("Allianz SE", "DE0008404005", 46m, 342.55m, 16045.11m),
        ];

        foreach (var row in rows)
        {
            depot.Positions.Add(new PortfolioPosition
            {
                Name = row.Name,
                Isin = row.Isin,
                Quantity = row.Quantity,
                Price = row.Price,
                CostBasis = row.Cost,
                PriceAsOf = pricesAsOf,
            });
        }

        return depot;
    }

    private static void SeedLoanAndInsurance(FinanzAppDbContext db)
    {
        db.Loans.Add(new Loan
        {
            Name = "Immobiliendarlehen",
            Lender = "Sparkasse",
            RemainingDebt = 148300m,
            InterestRatePercent = 1.84m,
            Installment = 1180m,
            NextPaymentDate = new DateOnly(2026, 9, 1),
        });

        db.InsurancePolicies.Add(new InsurancePolicy
        {
            Provider = "Heidelberger Leben",
            Name = "Klassische Lebensversicherung",
            SurrenderValue = 84900m,
            ValuationDate = new DateOnly(2026, 7, 1),
        });
    }

    private static void SeedImportProfiles(FinanzAppDbContext db)
    {
        db.ImportProfiles.AddRange(
            new ImportProfile { Name = "Sparkasse Standard", BankName = "Sparkasse", Format = "CAMT.053" },
            new ImportProfile { Name = "Raiffeisenbank Umsätze", BankName = "Raiffeisenbank", Format = "CSV" });
    }

    private static void SeedSnapshots(FinanzAppDbContext db, Dictionary<string, Account> accounts, Depot depot)
    {
        // Elf historische Monatswerte. Der laufende Monat fällt aus den echten Beständen.
        decimal[] netWorth =
        [
            112962.25m, 113622.85m, 114448.35m, 115603.55m, 115273.75m, 117089.55m,
            118410.85m, 120226.55m, 120887.15m, 122702.95m, 123699.15m,
        ];

        var cash = accounts.Values.Sum(a => a.OpeningBalance) + db.Transactions.Local.Sum(t => t.Amount);
        var portfolioValue = depot.Positions.Sum(p => p.Quantity * p.Price);
        var insurance = db.InsurancePolicies.Local.Sum(i => i.SurrenderValue);
        var debt = db.Loans.Local.Sum(l => l.RemainingDebt);
        var currentNetWorth = cash + portfolioValue + insurance - debt;

        var month = new DateOnly(2026, 8, 1).AddMonths(-netWorth.Length);
        foreach (var value in netWorth)
        {
            db.NetWorthSnapshots.Add(new NetWorthSnapshot { Month = month, Value = value });
            month = month.AddMonths(1);
        }

        db.NetWorthSnapshots.Add(new NetWorthSnapshot { Month = new DateOnly(2026, 8, 1), Value = currentNetWorth });

        decimal[] portfolio =
        [
            112000.00m, 114824.20m, 112706.55m, 118355.17m, 116237.24m, 121180.69m,
            119768.28m, 124711.72m, 122592.55m, 127536.55m, 128949.24m, 131067.59m,
        ];

        month = new DateOnly(2026, 8, 1).AddMonths(-portfolio.Length);
        foreach (var value in portfolio)
        {
            db.PortfolioSnapshots.Add(new PortfolioSnapshot { Month = month, Value = value });
            month = month.AddMonths(1);
        }

        db.PortfolioSnapshots.Add(new PortfolioSnapshot { Month = new DateOnly(2026, 8, 1), Value = portfolioValue });
    }
}
