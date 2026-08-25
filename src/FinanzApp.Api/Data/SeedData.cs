using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
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

    /// <summary>Name des Demo-Haushalts.</summary>
    public const string DemoHouseholdName = "Haushalt Kielmayer";

    /// <summary>
    /// Passwort aller drei Demo-Benutzer.
    /// </summary>
    /// <remarks>
    /// Es steht bewusst im Quelltext, weil dieser Stand eine Vorführung des Design-Handoffs ist
    /// und ohne Zugangsdaten niemand hineinkäme. Vor jedem echten Betrieb gehören diese Konten
    /// gelöscht — sie sind öffentlich bekannt.
    /// </remarks>
    public const string DemoPassword = "Demo-Haushalt-2026!";

    public static async Task EnsureSeededAsync(
        FinanzAppDbContext db,
        IPasswordHasher<User> hasher,
        DocumentPathService paths,
        CancellationToken ct = default)
    {
        if (await db.Households.AnyAsync(ct))
        {
            return;
        }

        var household = new Household { Name = DemoHouseholdName, CreatedAt = Today.ToDateTime(TimeOnly.MinValue) };
        db.Households.Add(household);
        await db.SaveChangesAsync(ct);

        // Ab hier stempelt der DbContext jeden neuen Datensatz auf diesen Haushalt.
        db.CurrentHouseholdId = household.Id;
        SeedUsers(db, hasher, household);

        var categories = SeedCategories(db);
        var accounts = SeedAccounts(db);
        var depot = SeedPortfolio(db);
        await db.SaveChangesAsync(ct);

        SeedTransactions(db, accounts, categories);
        SeedBudgets(db, categories);
        SeedRules(db, categories);
        SeedLoanAndPensions(db);
        SeedImportProfiles(db);

        // Anfangsbestände so setzen, dass die gerechneten Salden den Demo-Ständen entsprechen.
        AlignOpeningBalances(db, accounts);
        SeedSnapshots(db, accounts, depot);

        db.SecurityStates.Add(new SecurityState
        {
            // Der zweite Faktor kommt laut Handoff in einer späteren Runde.
            TwoFactorEnabled = false,
            LastBackup = new DateTime(2026, 8, 23, 3, 0, 0, DateTimeKind.Local),
        });

        await db.SaveChangesAsync(ct);

        // Die Erweiterung setzt auf denselben Haushalt auf.
        await ExtensionSeedData.SeedAsync(db, paths, household.Id, ct);

        // Sie bringt eigene Buchungen mit. Die Anfangsbestände müssen deshalb noch einmal
        // nachgezogen werden, damit die Salden weiterhin den Demo-Ständen entsprechen.
        await RealignOpeningBalancesAsync(db, ct);
    }

    /// <summary>Die drei Profile des Handoffs: Inhaber, Mitglied und der lesende Zugang
    /// für das Steuerbüro.</summary>
    private static void SeedUsers(FinanzAppDbContext db, IPasswordHasher<User> hasher, Household household)
    {
        (string Name, string Email, HouseholdRole Role, DateTime LastSeen)[] rows =
        [
            ("Oliver W.", "oliver@haushalt-kielmayer.de", HouseholdRole.Owner,
                new DateTime(2026, 8, 23, 8, 24, 0, DateTimeKind.Local)),
            ("Sabine K.", "sabine@haushalt-kielmayer.de", HouseholdRole.Member,
                new DateTime(2026, 8, 22, 21, 5, 0, DateTimeKind.Local)),
            ("Steuerbüro Haas", "kanzlei@haas-stb.de", HouseholdRole.ReadOnly,
                new DateTime(2026, 8, 1, 10, 12, 0, DateTimeKind.Local)),
        ];

        foreach (var row in rows)
        {
            var user = new User
            {
                Household = household,
                Name = row.Name,
                Email = row.Email,
                PasswordHash = "-",
                Role = row.Role,
                CreatedAt = household.CreatedAt,
                LastSeenAt = row.LastSeen,
                TwoFactorEnabled = false,
            };

            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            db.Users.Add(user);
        }

        db.Invitations.Add(new Invitation
        {
            HouseholdId = household.Id,
            Code = "HH-4K2P-9XQ1",
            Role = HouseholdRole.Member,
            CreatedAt = household.CreatedAt,
            ExpiresAt = new DateTime(2026, 8, 30, 23, 59, 59, DateTimeKind.Local),
        });
    }

    /// <summary>
    /// Grundausstattung eines frisch angelegten Haushalts: die Kategorien, ohne die sich keine
    /// Buchung zuordnen ließe. Konten, Budgets und Depots legt der Benutzer selbst an.
    /// </summary>
    public static async Task SeedNewHouseholdAsync(
        FinanzAppDbContext db, int householdId, CancellationToken ct = default)
    {
        if (await db.Categories.IgnoreQueryFilters().AnyAsync(c => c.HouseholdId == householdId, ct))
        {
            return;
        }

        foreach (var category in DefaultCategories())
        {
            category.HouseholdId = householdId;
            db.Categories.Add(category);
        }

        db.SecurityStates.Add(new SecurityState
        {
            HouseholdId = householdId,
            TwoFactorEnabled = false,
            LastBackup = default,
        });

        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<Category> DefaultCategories()
    {
        string[] expenses =
            ["Wohnen", "Lebensmittel", "Auto", "Freizeit", "Reisen", "Gesundheit", "Versicherung", "Sonstiges"];
        string[] income = ["Gehalt", "Dividenden", "Zinsen", "Miete", "Sonstiges"];

        foreach (var name in expenses)
        {
            yield return new Category { Name = name, Direction = CategoryDirection.Expense };
        }

        foreach (var name in income)
        {
            yield return new Category { Name = name, Direction = CategoryDirection.Income };
        }
    }

    private static Dictionary<string, Category> SeedCategories(FinanzAppDbContext db)
    {
        var map = new Dictionary<string, Category>();
        foreach (var category in DefaultCategories())
        {
            db.Categories.Add(category);

            // Ausgaben- und Einnahmenkategorien dürfen denselben Namen tragen. Im Seed-Schlüssel
            // bekommt die Einnahmenseite deshalb ein Pluszeichen vorangestellt.
            var key = category.Direction == CategoryDirection.Income ? "+" + category.Name : category.Name;
            map[key] = category;
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

    /// <summary>Richtet die Anfangsbestände am Bestand der Datenbank neu aus.</summary>
    private static async Task RealignOpeningBalancesAsync(FinanzAppDbContext db, CancellationToken ct)
    {
        var accounts = await db.Accounts.ToListAsync(ct);
        var booked = (await db.Transactions.AsNoTracking()
                .Select(t => new { t.AccountId, t.Amount })
                .ToListAsync(ct))
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        foreach (var account in accounts)
        {
            if (TargetBalances.TryGetValue(account.Name, out var target))
            {
                account.OpeningBalance = target - booked.GetValueOrDefault(account.Id);
            }
        }

        await db.SaveChangesAsync(ct);
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

    private static void SeedLoanAndPensions(FinanzAppDbContext db)
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

        // Vorsorge & Kapital — die vier Verträge aus Handoff v4, Abschnitt 4. Ihre Summe ist
        // 58.940,00 € und damit genau der Betrag, der im Prototyp im Bruttovermögen steht.
        // Jeder trägt seinen eigenen Stichtag; das älteste Datum bestimmt, wie alt die Summe ist.
        db.Policies.AddRange(
            new Policy
            {
                Kind = PolicyKind.CapitalLife,
                IsCapitalForming = true,
                Name = "Heidelberger Leben",
                Provider = "Heidelberger Leben",
                Notes = "MLP bestpartner classic",
                CurrentValue = 20481.52m,
                ValuationDate = new DateOnly(2025, 7, 31),
            },
            new Policy
            {
                Kind = PolicyKind.CapitalLife,
                IsCapitalForming = true,
                Name = "Raiffeisenbank LV",
                Provider = "Raiffeisenbank",
                Notes = "Ablauf 2034",
                CurrentValue = 14208m,
                ValuationDate = new DateOnly(2025, 12, 31),
                MaturesOn = new DateOnly(2034, 12, 1),
            },
            new Policy
            {
                Kind = PolicyKind.Riester,
                IsCapitalForming = true,
                Name = "Riester Debeka",
                Provider = "Debeka",
                Notes = "Zulagen 2025 gebucht",
                CurrentValue = 11930.40m,
                ValuationDate = new DateOnly(2025, 12, 31),
            },
            new Policy
            {
                Kind = PolicyKind.BuildingSociety,
                IsCapitalForming = true,
                Name = "Bausparen BSK SHA",
                Provider = "Bausparkasse Schwäbisch Hall",
                Notes = "Zuteilung möglich",
                CurrentValue = 12320.08m,
                ValuationDate = new DateOnly(2025, 12, 31),
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
        // Elf historische Monatswerte, gegenüber der Fassung vor Handoff v4 um 25.960 € nach
        // unten verschoben — so viel weniger trägt die Vorsorge zum Vermögen bei, seit die
        // Risikoverträge nicht mehr mitzählen. Verschoben und nicht skaliert: der Unterschied
        // steckte schon immer in der Reihe, er war nur falsch zugeordnet. Der Monatszuwachs von
        // +2.140,80 € bleibt dadurch erhalten.
        decimal[] netWorth =
        [
            87002.25m, 87662.85m, 88488.35m, 89643.55m, 89313.75m, 91129.55m,
            92450.85m, 94266.55m, 94927.15m, 96742.95m, 97739.15m,
        ];

        var cash = accounts.Values.Sum(a => a.OpeningBalance) + db.Transactions.Local.Sum(t => t.Amount);
        var portfolioValue = depot.Positions.Sum(p => p.Quantity * p.Price);
        var pension = db.Policies.Local.Sum(p => p.AssetValue ?? 0m);
        var debt = db.Loans.Local.Sum(l => l.RemainingDebt);
        var currentNetWorth = cash + portfolioValue + pension - debt;

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
