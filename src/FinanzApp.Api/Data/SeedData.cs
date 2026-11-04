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
/// Stück mal Kurs. Die Sätze sind so gewählt, dass die Kopfzahlen dabei glatt aufgehen.</para>
/// <para><b>Die Beträge sind erfunden</b> und stammen nicht mehr aus dem Handoff: dessen
/// Beispielbestand war aus einer echten Ablage abgeleitet, und dieses Repository ist öffentlich.
/// Maßgeblich bleibt der Handoff für den <em>Aufbau</em> — welche Konten, welche Verträge, welche
/// Zustände vorzuführen sind. Für die Zahlen ist er es nicht mehr. Wer sie ändert, prüft deshalb
/// nicht gegen den Prototyp, sondern gegen die Stände, die <c>SeedBalanceTests</c> festhält.</para>
/// </remarks>
public static class SeedData
{
    private static readonly DateOnly Today = new(2026, 8, 23);

    /// <summary>Der erste Tag des Monats, in dem der Demo-Bestand steht.</summary>
    /// <remarks>
    /// Aus <see cref="Today"/> abgeleitet und nicht aus dem Kalender: „heute“ ist für die
    /// Anwendung der Stichtag aus <c>Demo:Today</c>. Am echten Datum ausgerichtet lägen die
    /// Einlagen in einem Monat, den die Anwendung noch nicht erreicht hat.
    /// </remarks>
    private static DateOnly CurrentMonth => new(Today.Year, Today.Month, 1);

    /// <summary>Zielsalden laut Handoff. Der Anfangsbestand ergibt sich daraus rückwärts.</summary>
    private static readonly Dictionary<string, decimal> TargetBalances = new()
    {
        ["Sparkasse Giro"] = 3964.20m,
        ["Musterbank Giro"] = 1284.55m,
        ["Tagesgeld Raiffeisen"] = 38000.00m,
    };

    /// <summary>Name des Demo-Haushalts.</summary>
    public const string DemoHouseholdName = "Haushalt Mustermann";

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

        SeedSharing(db, accounts);
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
        await ExtensionSeedData.SeedAsync(db, paths, household.Id, CurrentMonth, ct);

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
            ("Oliver W.", "oliver@haushalt-mustermann.de", HouseholdRole.Owner,
                new DateTime(2026, 8, 23, 8, 24, 0, DateTimeKind.Local)),
            ("Sabine K.", "sabine@haushalt-mustermann.de", HouseholdRole.Member,
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

        // Objektbezogen — Handoff „Gemeinsame Immobilie“, 3.4. Wohnen gehört zum Haus (Handwerker
        // weiter unten, bei seiner steuerlichen Einordnung); Lebensmittel, Freizeit und Mobilität
        // nicht. Welche es sind, steht nicht im Namen — im Bestand wird es gesetzt, nicht geraten.
        string[] objektbezogen = ["Wohnen"];

        foreach (var name in expenses)
        {
            yield return new Category
            {
                Name = name,
                Direction = CategoryDirection.Expense,
                PropertyRelated = objektbezogen.Contains(name),
            };
        }

        // Zwei Kategorien mit steuerlicher Einordnung. Sie stehen getrennt, weil die Einordnung
        // an der Kategorie hängt und nicht an ihrem Namen — wer „Handwerker“ in „Haus“ umbenennt,
        // soll die Zuordnung behalten.
        yield return new Category
        {
            Name = "Handwerker", Direction = CategoryDirection.Expense,
            TaxCategory = TaxCategory.Handwerkerleistung,
            PropertyRelated = true,
        };

        yield return new Category
        {
            Name = "Beruf", Direction = CategoryDirection.Expense,
            TaxCategory = TaxCategory.Werbungskosten,
        };

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

    /// <summary>
    /// Gibt den Demo-Konten einen Eigentümer und führt alle drei Freigabestufen vor.
    /// </summary>
    /// <remarks>
    /// Ohne Eigentümer stünden alle Konten auf „Haushalt“ und der Screen zeigte nichts. Die
    /// Verteilung ist so gewählt, dass jede Stufe einmal vorkommt — sonst lässt sich die Wirkung
    /// nicht ansehen, sondern nur nachlesen.
    /// </remarks>
    private static void SeedSharing(FinanzAppDbContext db, Dictionary<string, Account> accounts)
    {
        var oliver = db.Users.OrderBy(u => u.Id).First();
        var sabine = db.Users.OrderBy(u => u.Id).Skip(1).FirstOrDefault();

        foreach (var account in accounts.Values)
        {
            account.OwnerUserId = oliver.Id;
        }

        if (accounts.TryGetValue("Musterbank Giro", out var privat))
        {
            privat.Sharing = AccountSharing.Private;
        }

        // Die vierte Stufe am Tagesgeld — und ausdrücklich nicht am Hauptkonto: ein
        // Gemeinschaftskonto sieht nur, wer in seiner Liste steht. Das Girokonto des Haushalts
        // umzustellen nähme dem Steuerbüro die Sicht darauf, und das wäre eine Nebenwirkung, die
        // niemand bestellt hat.
        //
        // Ungleiche Sollbeträge zeigen, dass der Vergleich je Person läuft und nicht über die
        // Summe: 1.100 gegen 900, und Sabine zahlt im laufenden Monat 200 € zu wenig.
        if (sabine is not null && accounts.TryGetValue("Tagesgeld Raiffeisen", out var tagesgeld))
        {
            tagesgeld.Sharing = AccountSharing.Shared;

            db.AccountShares.Add(new AccountShare
            {
                AccountId = tagesgeld.Id, UserId = oliver.Id, MonthlyTarget = 1100m, DueDay = 1,
            });

            db.AccountShares.Add(new AccountShare
            {
                AccountId = tagesgeld.Id, UserId = sabine.Id, MonthlyTarget = 900m, DueDay = 1,
            });
        }
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
                Name = "Musterbank Giro", ShortName = "Musterbank", BankName = "Musterbank",
                Kind = AccountKind.Checking, Iban = "DE12 6706 2366 0009 1140 07",
                BalanceAsOf = Today,
            },
            new()
            {
                Name = "Tagesgeld Raiffeisen", ShortName = "Tagesgeld", BankName = "Musterbank",
                Kind = AccountKind.Savings, InterestRatePercent = 2.10m, InterestYearToDate = 74.65m,
                BalanceAsOf = new DateOnly(2026, 8, 19),
            },
        ];

        db.Accounts.AddRange(accounts);
        return accounts.ToDictionary(a => a.Name);
    }

    /// <summary>Richtet die Anfangsbestände am Bestand der Datenbank neu aus.</summary>
    private static async Task RealignOpeningBalancesAsync(FinanzAppDbContext db, CancellationToken ct)
    {
        // Auch hier ohne Sichtbarkeitsfilter — siehe ExtensionSeedData.
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(a => a.HouseholdId == db.CurrentHouseholdId)
            .ToListAsync(ct);
        // Auch die Buchungen ohne Sichtbarkeitsfilter, nicht nur die Konten. Sonst zählt ein
        // privates Konto mit null Buchungen — sein Anfangsbestand bliebe unausgeglichen, und
        // der Saldo verfehlte den Demo-Stand um genau die Summe seiner Buchungen.
        var booked = (await db.Transactions.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.HouseholdId == db.CurrentHouseholdId)
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
            // Die sieben Buchungen, die der Handoff auf dem Startschirm zeigt. Sie stehen oben
            // in der Liste; zwei davon bleiben ohne Kategorie und speisen das Triage-Banner.
            (22, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -58.35m),
            (21, "Shell Tankstelle", null, "Sparkasse Giro", -71.40m),
            (20, "Stadtwerke Strom", "Wohnen", "Sparkasse Giro", -88.00m),
            (19, "Umbuchung → Tagesgeld", null, "Sparkasse Giro", -1200.00m),
            (18, "Musterleben Beitrag", "Versicherung", "Musterbank Giro", -184.00m),
            (15, "PAYPAL .PAYCOMET SL", null, "Musterbank Giro", -32.75m),
            (1, "Gehalt Nordwind", "+Gehalt", "Sparkasse Giro", 4180.00m),

            // Weitere Buchungen des Monats, damit Budgetauslastung und Monatssummen aufgehen.
            (19, "Zahnarzt Dr. Weber", "Gesundheit", "Musterbank Giro", -164.20m),
            (18, "Spende Tierheim", "Sonstiges", "Sparkasse Giro", -65.00m),
            (17, "KFZ-Versicherung Musterschutz", "Versicherung", "Musterbank Giro", -66.90m),
            (16, "Karlstorbahnhof Kino", "Freizeit", "Musterbank Giro", -24.00m),
            (15, "Telekom Internet und Mobilfunk", "Wohnen", "Sparkasse Giro", -51.90m),
            (14, "EDEKA Neckargemünd", "Lebensmittel", "Sparkasse Giro", -74.65m),
            (13, "ARAL Tankstelle", "Auto", "Sparkasse Giro", -79.80m),
            (12, "Fitnessstudio Beitrag", "Freizeit", "Musterbank Giro", -39.90m),
            (11, "Apotheke am Markt", "Gesundheit", "Sparkasse Giro", -28.45m),
            (10, "Amazon Bestellung", "Sonstiges", "Sparkasse Giro", -53.90m),
            (9, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -55.30m),
            (8, "Restaurant Zum Ritter", "Freizeit", "Sparkasse Giro", -82.10m),
            (7, "Anzahlung Ferienwohnung", "Reisen", "Sparkasse Giro", -75.00m),
            (6, "ALDI SÜD Heidelberg", "Lebensmittel", "Sparkasse Giro", -36.20m),
            (6, "Drogeriemarkt dm", "Sonstiges", "Sparkasse Giro", -39.85m),
            (5, "Kfz-Werkstatt Ölwechsel", "Auto", "Sparkasse Giro", -52.40m),
            (4, "Bäckerei Göbes", "Lebensmittel", "Sparkasse Giro", -10.80m),
            (3, "Buchhandlung Schmitt", "Freizeit", "Sparkasse Giro", -48.70m),
            (2, "REWE Markt Heidelberg", "Lebensmittel", "Sparkasse Giro", -118.60m),
            (2, "Zeitschriften-Abo", "Sonstiges", "Sparkasse Giro", -10.25m),
            (1, "Miete Wohnung Heidelberg", "Wohnen", "Sparkasse Giro", -1165.00m),
            (1, "Nebenkosten Hausgeld", "Wohnen", "Sparkasse Giro", -198.00m),
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
            ("Lebensmittel", "Lebensmittel", 420m),
            ("Freizeit", "Freizeit", 180m),
            ("Auto", "Auto", 260m),
            ("Urlaub", "Reisen", 220m),
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
            ("Musterleben", "Versicherung"),
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
        var depot = new Depot
        {
            Name = "finanzen.net ZERO", Number = "1234567001", TwrorPercent = 7.4m,
        };
        db.Depots.Add(depot);

        var pricesAsOf = new DateTime(2026, 8, 22, 17, 35, 0, DateTimeKind.Local);
        (string Name, string Isin, decimal Quantity, decimal Price, decimal Cost)[] rows =
        [
            ("Vanguard FTSE All-World", "IE00BK5BQT80", 310m, 104.20m, 27980.50m),
            ("iShares Core MSCI World", "IE00B4L5Y983", 285m, 96.40m, 24310.20m),
            ("Xtrackers MSCI EM", "IE00BTJRMP35", 420m, 48.15m, 18640.60m),
            ("Musterschutz SE", "DE0008404005", 52m, 298.75m, 12905.40m),
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
            RemainingDebt = 121500m,
            InterestRatePercent = 2.15m,
            Installment = 960m,
            NextPaymentDate = new DateOnly(2026, 9, 1),
        });

        // Vorsorge & Kapital — die vier Verträge aus Handoff v4, Abschnitt 4. Ihre Summe ist
        // 42.760,00 € und steht so im Bruttovermögen. Jeder trägt seinen eigenen Stichtag; das
        // älteste Datum bestimmt, wie alt die Summe ist.
        db.Policies.AddRange(
            new Policy
            {
                Kind = PolicyKind.CapitalLife,
                IsCapitalForming = true,
                Name = "Musterleben",
                Provider = "Musterleben",
                Notes = "MLP bestpartner classic",

                // Über die Vertragsnummer findet der Beleg-Scan sein Ziel — ohne sie könnte ein
                // eingelesener Statusreport nichts zuordnen.
                PolicyNumber = "LV-4471-01",

                // Die Bestandteile des erreichten Werts — daraus entsteht der Block „So
                // entsteht der Wert" am Vertrag.
                BaseValue = 13240.55m,
                AccruedBonus = 1486.25m,
                CurrentValue = 14726.80m,
                ValuationDate = new DateOnly(2025, 7, 31),

                // Vier gemeldete Stände — daraus entsteht der Verlauf. Ohne sie stünde am
                // Vertrag ein einziger Wert, und jede gezeichnete Kurve wäre erfunden.
                Reports =
                {
                    Report(2022, 7, 31, 12980.40m, "Statusreport", 11800m, 1180.40m),
                    Report(2023, 7, 31, 13515.70m, "Statusreport", 12250m, 1265.70m),
                    Report(2024, 7, 31, 14108.25m, "Statusreport", 12740m, 1368.25m),
                    Report(2025, 7, 31, 14726.80m, "Statusreport", 13240.55m, 1486.25m),
                },
            },
            new Policy
            {
                Kind = PolicyKind.CapitalLife,
                IsCapitalForming = true,
                Name = "Musterbank LV",
                Provider = "Musterbank",
                Notes = "Ablauf 2034",
                PolicyNumber = "RB-2019-8842",
                Premium = 70m,
                PremiumInterval = PremiumInterval.Monthly,
                CurrentValue = 10590m,
                ValuationDate = new DateOnly(2025, 12, 31),
                MaturesOn = new DateOnly(2034, 12, 1),

                // Nur ein Stand: dieser Vertrag zeigt den ehrlichen Fall — ein Bericht ist kein
                // Verlauf, und der Schirm zeichnet dann keine Linie, sondern sagt das.
                Reports = { Report(2025, 12, 31, 10590m, "Statusreport") },
            },
            new Policy
            {
                Kind = PolicyKind.Riester,
                IsCapitalForming = true,
                Name = "Riester Musterkasse",
                Provider = "Musterkasse",
                Notes = "Zulagen 2025 gebucht",
                PolicyNumber = "DB-RI-330914",
                Premium = 141.50m,
                PremiumInterval = PremiumInterval.Monthly,

                // Ein Bestandteil, keine Summe: eine Riesterrente führt kein
                // Ansammlungsguthaben, und eine Summe aus einem Summanden ist keine.
                BaseValue = 8715.60m,
                CurrentValue = 8715.60m,
                ValuationDate = new DateOnly(2025, 12, 31),
                Reports =
                {
                    Report(2023, 12, 31, 5420.80m, "Auszug", 5420.80m),
                    Report(2024, 12, 31, 7012.30m, "Auszug", 7012.30m),
                    Report(2025, 12, 31, 8715.60m, "Auszug", 8715.60m),
                },
            },
            new Policy
            {
                Kind = PolicyKind.BuildingSociety,
                IsCapitalForming = true,
                Name = "Bausparen BSK MST",
                Provider = "Bausparkasse Musterstadt",
                Notes = "Zuteilung möglich",
                PolicyNumber = "BSH 7712 4409",
                Premium = 100m,
                PremiumInterval = PremiumInterval.Monthly,
                BaseValue = 8727.60m,
                CurrentValue = 8727.60m,
                ValuationDate = new DateOnly(2025, 12, 31),
                Reports =
                {
                    Report(2024, 12, 31, 7486.20m, "Auszug", 7486.20m),
                    Report(2025, 12, 31, 8727.60m, "Auszug", 8727.60m),
                },
            });
    }

    /// <summary>
    /// Ein gemeldeter Stand zu seinem Stichtag.
    /// </summary>
    /// <remarks>
    /// <para>Die Quelle heißt, wie das Papier heißt, auf dem der Stand steht: ein Bausparvertrag
    /// bekommt einen Auszug, eine Lebensversicherung einen Statusreport. Das Anlagedatum ist der
    /// Stichtag selbst — die Demodaten tun nicht so, als wäre der Bericht später erfasst
    /// worden.</para>
    /// <para>Die Bestandteile hängen am Bericht, weil der Vertrag seine aus dem neuesten nimmt.
    /// Ohne sie verschwände der Block „So entsteht der Wert", sobald jemand den obersten Stand
    /// entfernt.</para>
    /// </remarks>
    private static PolicyReport Report(
        int jahr, int monat, int tag, decimal wert, string quelle,
        decimal? basis = null, decimal? bonus = null)
    {
        var stichtag = new DateOnly(jahr, monat, tag);

        return new PolicyReport
        {
            AsOf = stichtag,
            Value = wert,
            BaseValue = basis,
            AccruedBonus = bonus,
            Source = quelle,
            CreatedAt = stichtag.ToDateTime(TimeOnly.MinValue),
        };
    }

    private static void SeedImportProfiles(FinanzAppDbContext db)
    {
        db.ImportProfiles.AddRange(
            new ImportProfile { Name = "Sparkasse Standard", BankName = "Sparkasse", Format = "CAMT.053" },
            new ImportProfile { Name = "Musterbank Umsätze", BankName = "Musterbank", Format = "CSV" });
    }

    private static void SeedSnapshots(FinanzAppDbContext db, Dictionary<string, Account> accounts, Depot depot)
    {
        // Elf historische Monatswerte. Der laufende Monat steht nicht darin — er wird weiter
        // unten aus den Beständen gerechnet, damit die Kurve am aktuellen Rand nicht behauptet,
        // was der Bestand nicht trägt. Der letzte historische Wert liegt genau einen
        // Monatszuwachs von +1.580,40 € darunter; so entsteht dort kein Sprung.
        decimal[] netWorth =
        [
            48725.45m, 49266.05m, 49971.55m, 51006.75m, 50756.95m, 52452.75m,
            53654.05m, 55349.75m, 55890.35m, 57586.15m, 58462.35m,
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
            80784.20m, 82818.40m, 81292.75m, 85361.20m, 83835.50m, 87395.60m,
            86378.40m, 89938.45m, 88412.45m, 91972.30m, 92989.90m, 94515.60m,
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
