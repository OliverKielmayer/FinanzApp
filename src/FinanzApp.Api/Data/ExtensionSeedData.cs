using System.Text;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Data;

/// <summary>
/// Beispieldaten der Erweiterung: Dokumente, PKV-Vorgänge, Versicherungen, Immobilie, Verträge.
/// </summary>
/// <remarks>
/// <para>Zahlen aus den Wireframes, soweit sie sich mit dem Bestand vertragen. Die Monatssummen des
/// ersten Handoffs bleiben unangetastet: die neuen Buchungen liegen im Juli, damit Einnahmen
/// (5.240 €) und Ausgaben (3.612 €) des August unverändert herauskommen.</para>
/// <para>Zu den Dokumenten werden echte Platzhalterdateien geschrieben — nur so lässt sich prüfen,
/// dass die Pfadauflösung greift. Eine Datei fehlt <em>mit Absicht</em>: der Zustand „Datei nicht
/// gefunden“ ist gestaltet und soll vorführbar sein.</para>
/// </remarks>
public static class ExtensionSeedData
{
    /// <summary>Dieses Dokument hat bewusst keine Datei auf der Platte.</summary>
    public const string MissingFileDocument = "Lohn_07_2026.pdf";

    public static async Task SeedAsync(
        FinanzAppDbContext db, DocumentPathService paths, int householdId, CancellationToken ct = default)
    {
        if (await db.DocumentTypes.IgnoreQueryFilters().AnyAsync(t => t.HouseholdId == householdId, ct))
        {
            return;
        }

        db.CurrentHouseholdId = householdId;

        var types = SeedDocumentTypes(db);
        var policies = SeedProtection(db);
        await db.SaveChangesAsync(ct);

        var property = await SeedPropertyAsync(db, policies, ct);
        var bills = await SeedMedicalBillsAsync(db, ct);
        await SeedHistoryAsync(db, ct);
        await db.SaveChangesAsync(ct);

        await SeedDocumentsAsync(db, paths, types, policies, property, bills, ct);
        await db.SaveChangesAsync(ct);

        await SeedVehiclesAsync(db, policies, ct);
        await SeedScanInboxAsync(db, paths, types, ct);
        await db.SaveChangesAsync(ct);
    }

    private static Dictionary<string, DocumentType> SeedDocumentTypes(FinanzAppDbContext db)
    {
        (string Name, DocumentArea Area)[] rows =
        [
            ("Versicherungsschein", DocumentArea.Insurance),
            ("Beitragsrechnung", DocumentArea.Insurance),
            ("Beitragsanpassung", DocumentArea.Insurance),
            ("Arztrechnung", DocumentArea.Health),
            ("PKV-Abrechnung", DocumentArea.Health),
            ("Kaufvertrag", DocumentArea.Housing),
            ("Grundbuchauszug", DocumentArea.Housing),
            ("Energieausweis", DocumentArea.Housing),
            ("Stromrechnung", DocumentArea.Housing),
            ("Darlehensvertrag", DocumentArea.Finance),
            ("Bankdokument", DocumentArea.Finance),
            ("Arbeitsvertrag", DocumentArea.Work),
            ("Lohnabrechnung", DocumentArea.Work),
        ];

        var map = new Dictionary<string, DocumentType>();
        var order = 0;
        foreach (var row in rows)
        {
            var type = new DocumentType { Name = row.Name, Area = row.Area, SortOrder = order++ };
            db.DocumentTypes.Add(type);
            map[row.Name] = type;
        }

        return map;
    }

    /// <summary>
    /// Die acht Absicherungsverträge aus Handoff v4, Abschnitt 4. Ihre Jahresbeiträge summieren
    /// sich auf 12.330,00 € — die Kopfzahl des Bereichs.
    /// </summary>
    /// <remarks>
    /// Keiner von ihnen trägt einen Vermögenswert; <c>IsCapitalForming</c> bleibt überall falsch.
    /// Das Risikoleben ist der Grund für die ganze Trennung: es zahlt im Todesfall und gehört
    /// deshalb nie ins Nettovermögen, obwohl es eine Lebensversicherung ist.
    /// </remarks>
    private static Dictionary<string, Policy> SeedProtection(FinanzAppDbContext db)
    {
        (string Key, string Name, string Provider, PolicyKind Kind, string Notes,
            decimal Premium, PremiumInterval Interval, DateOnly? Starts, DateOnly? Ends,
            int NoticeMonths, decimal? SumInsured)[] rows =
        [
            ("Krankenversicherung", "Krankenversicherung Debeka", "Debeka", PolicyKind.Health,
                "PKV · Erstattungen unter Gesundheit", 742m, PremiumInterval.Monthly,
                new DateOnly(2016, 1, 1), null, 3, null),
            ("Kfz", "Kfz WGV", "WGV", PolicyKind.Vehicle,
                "L-2905 · Wechselfrist 30.11.2026", 618m, PremiumInterval.Yearly,
                new DateOnly(2021, 1, 1), new DateOnly(2026, 12, 31), 1, null),
            ("Berufsunfähigkeit", "Berufsunfähigkeit", "Alte Leipziger",
                PolicyKind.DisabilityInsurance, "BU-Rente 3.871,36 € monatlich", 118m,
                PremiumInterval.Monthly, new DateOnly(2017, 9, 1), null, 1, null),
            ("Risikoleben", "Risikoleben", "Heidelberger Leben", PolicyKind.TermLife,
                "kein Rückkaufswert", 42m, PremiumInterval.Monthly,
                new DateOnly(2020, 7, 1), null, 1, 250000m),
            ("Wohngebäude", "Wohngebäude", "HUK-Coburg", PolicyKind.Building,
                "Haus Kammerstatter Straße 10", 412m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2027, 3, 31), 3, null),
            ("Rechtsschutz", "Rechtsschutz", "ARAG", PolicyKind.LegalExpenses,
                "bis 31.05.2027", 231m, PremiumInterval.Yearly,
                new DateOnly(2022, 5, 1), new DateOnly(2027, 8, 31), 3, null),
            ("Hausrat", "Hausrat HUK", "HUK-Coburg", PolicyKind.HouseholdContents,
                "Wohnfläche 142 m²", 156m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2027, 12, 31), 3, null),
            ("Privathaftpflicht", "Privathaftpflicht", "Adam Riese", PolicyKind.Liability,
                "bis 31.12.2027", 89m, PremiumInterval.Yearly,
                new DateOnly(2018, 1, 1), new DateOnly(2027, 12, 31), 0, null),
        ];

        var map = new Dictionary<string, Policy>();
        foreach (var row in rows)
        {
            var policy = new Policy
            {
                Kind = row.Kind,
                IsCapitalForming = false,
                Name = row.Name,
                Provider = row.Provider,
                Notes = row.Notes,
                Premium = row.Premium,
                PremiumInterval = row.Interval,
                StartsOn = row.Starts,
                EndsOn = row.Ends,
                NoticePeriodMonths = row.NoticeMonths,
                SumInsured = row.SumInsured,
            };

            db.Policies.Add(policy);
            map[row.Key] = policy;
        }

        // Hausrat läuft erst zum 30.09.2027 aus — der Vergleich braucht aber Vorlauf, deshalb
        // steht die Erinnerung schon auf dem 10.09.2026, also 18 Tage nach dem Stichtag. So
        // zeigt die Demo den Zustand „Frist läuft“, ohne den Vertrag künstlich früher
        // enden zu lassen.
        map["Hausrat"].NoticeReminderOn = new DateOnly(2026, 9, 10);

        return map;
    }

    /// <summary>
    /// Fünf Monate Vorgeschichte (März bis Juli 2026).
    /// </summary>
    /// <remarks>
    /// Ohne Historie kann „Wohin fließt es“ nicht zwischen fix und variabel unterscheiden und das
    /// Sparpotential nichts erkennen — beides braucht mehrere Monate, um überhaupt ein Muster zu
    /// sehen. Der August bleibt unberührt, damit die Monatssummen des ersten Handoffs
    /// (5.240 € / 3.612 €) weiterhin genau herauskommen.
    /// </remarks>
    private static async Task SeedHistoryAsync(FinanzAppDbContext db, CancellationToken ct)
    {
        // Seeden ist keine Benutzeranfrage: der Sichtbarkeitsfilter haengt am angemeldeten
        // Benutzer, und den gibt es hier nicht. Ohne IgnoreQueryFilters faende diese Tabelle
        // jedes private Konto nicht — und das Seeden braeche an einem fehlenden Schluessel.
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(a => a.HouseholdId == db.CurrentHouseholdId)
            .ToDictionaryAsync(a => a.Name, ct);
        var categories = await db.Categories.ToListAsync(ct);

        int? Category(string name, CategoryDirection direction)
            => categories.FirstOrDefault(c => c.Name == name && c.Direction == direction)?.Id;

        var sparkasse = accounts["Sparkasse Giro"].Id;
        var raiffeisen = accounts["Raiffeisenbank Giro"].Id;

        // Monat -> (Lebensmittel, Freizeit, Auto). Freizeit liegt bewusst dauerhaft über dem
        // Budget von 200 €, damit das Sparpotential eine echte Überschreitung findet.
        (int Month, decimal Food, decimal Leisure, decimal Car)[] variable =
        [
            (3, 438.20m, 268.00m, 174.30m),
            (4, 391.75m, 212.50m, 128.90m),
            (5, 455.60m, 289.40m, 186.20m),
            (6, 402.30m, 241.80m, 141.50m),
            (7, 428.90m, 255.60m, 168.70m),
        ];

        var reference = 4400;
        foreach (var month in variable)
        {
            void Add(int day, string payee, string category, CategoryDirection direction,
                decimal amount, int accountId)
            {
                db.Transactions.Add(new Transaction
                {
                    BookingDate = new DateOnly(2026, month.Month, day),
                    Payee = payee,
                    Kind = amount >= 0 ? TransactionKind.Income : TransactionKind.Expense,
                    Amount = amount,
                    AccountId = accountId,
                    CategoryId = Category(category, direction),
                    ImportReference = "SEED-" + reference++,
                    CreatedAt = new DateTime(2026, month.Month, day, 6, 0, 0, DateTimeKind.Local),
                });
            }

            Add(1, "Gehalt EWV", "Gehalt", CategoryDirection.Income, 5240m, sparkasse);
            Add(1, "Miete Wohnung Heidelberg", "Wohnen", CategoryDirection.Expense, -1480m, sparkasse);
            Add(1, "Nebenkosten Hausgeld", "Wohnen", CategoryDirection.Expense, -245m, sparkasse);
            Add(15, "Telekom Internet und Mobilfunk", "Wohnen", CategoryDirection.Expense, -59.90m, sparkasse);
            Add(17, "KFZ-Versicherung Allianz", "Versicherung", CategoryDirection.Expense, -78.40m, raiffeisen);
            Add(18, "Heidelberger Leben Beitrag", "Versicherung", CategoryDirection.Expense, -212m, raiffeisen);
            Add(10, "REWE Markt Heidelberg", "Lebensmittel", CategoryDirection.Expense, -month.Food, sparkasse);
            Add(12, "Freizeit und Kultur", "Freizeit", CategoryDirection.Expense, -month.Leisure, sparkasse);
            Add(13, "ARAL Tankstelle", "Auto", CategoryDirection.Expense, -month.Car, sparkasse);

            // Zwei wiederkehrende Buchungen ohne hinterlegten Vertrag — der Fall „Abos“ aus
            // Wireframe 1g.
            Add(5, "Streaming Abo", "Sonstiges", CategoryDirection.Expense, -17.99m, sparkasse);
            Add(5, "Cloud-Speicher", "Sonstiges", CategoryDirection.Expense, -9.99m, sparkasse);
        }
    }

    private static async Task<Property> SeedPropertyAsync(
        FinanzAppDbContext db, Dictionary<string, Policy> policies, CancellationToken ct)
    {
        var loan = await db.Loans.OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
        var account = await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Name == "Sparkasse Giro", ct);

        var property = new Property
        {
            Name = "Haus Hauptstraße",
            Address = "Hauptstraße 118, 69117 Heidelberg",
            PurchaseDate = new DateOnly(2019, 4, 1),
            PurchasePrice = 342000m,
            MarketValue = 395000m,

            // Verweis auf das vorhandene Darlehen — es wird nicht kopiert.
            LoanId = loan?.Id,
        };
        db.Properties.Add(property);

        (string Name, string Provider, string? Number, decimal Monthly, int NoticeWeeks,
            DateOnly? NoticeTo)[] contracts =
        [
            ("Strom", "Stadtwerke Heidelberg", "SW-993 210", 142.50m, 6, new DateOnly(2027, 3, 31)),
            ("Internet", "Telekom", "DTAG-40 118", 39.99m, 12, new DateOnly(2027, 6, 30)),
            ("Wasser", "Stadtwerke Heidelberg", "SW-771 004", 38.00m, 6, new DateOnly(2027, 3, 31)),
            ("Abfallentsorgung", "Stadt Heidelberg", "AE-2019-118", 24.50m, 8, new DateOnly(2026, 12, 31)),
            ("Heizungswartung", "Sanitär Brenner", "HW-556", 18.00m, 4, new DateOnly(2026, 10, 31)),
            ("Schornsteinfeger", "Bezirk Heidelberg Nord", null, 9.50m, 0, null),
        ];

        foreach (var row in contracts)
        {
            property.Contracts.Add(new Contract
            {
                Name = row.Name,
                Provider = row.Provider,
                ContractNumber = row.Number,
                MonthlyAmount = row.Monthly,
                AccountId = account?.Id,
                StartsOn = new DateOnly(2019, 4, 1),
                NoticePeriodWeeks = row.NoticeWeeks,
                NoticeToDate = row.NoticeTo,
                Area = DocumentArea.Housing,
            });
        }

        await db.SaveChangesAsync(ct);

        var strom = property.Contracts.First(c => c.Name == "Strom");
        var paidTransaction = await db.Transactions
            .Where(t => t.Payee.StartsWith("Stadtwerke"))
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        db.Invoices.AddRange(
            new Invoice
            {
                ContractId = strom.Id,
                Subject = "Stromabschlag 08/2026",
                Number = "SW-2026-08",
                IssuedOn = new DateOnly(2026, 8, 1),
                DueOn = new DateOnly(2026, 9, 15),
                Amount = 142.50m,
                Status = InvoiceStatus.Open,
            },
            new Invoice
            {
                ContractId = strom.Id,
                Subject = "Stromabschlag 07/2026",
                Number = "SW-2026-07",
                IssuedOn = new DateOnly(2026, 7, 1),
                DueOn = new DateOnly(2026, 8, 15),
                Amount = 96.00m,
                Status = InvoiceStatus.Paid,
                TransactionId = paidTransaction,
            });

        // Die Wohngebäude- und Hausratversicherung gehören zur Immobilie.
        _ = policies;
        return property;
    }

    private static async Task<List<MedicalBill>> SeedMedicalBillsAsync(
        FinanzAppDbContext db, CancellationToken ct)
    {
        var dentist = await db.Transactions
            .Where(t => t.Payee.StartsWith("Zahnarzt"))
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        var account = await db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Name == "Sparkasse Giro", ct);
        var incomeCategory = await db.Categories
            .FirstOrDefaultAsync(c => c.Direction == CategoryDirection.Income && c.Name == "Sonstiges", ct);

        var bills = new List<MedicalBill>
        {
            // Überfällig: eingereicht, aber ohne Zahlungseingang. Trägt den Zustand aus Wireframe 1d.
            new()
            {
                Provider = "Dr. Meyer, Zahnarzt",
                BillDate = new DateOnly(2026, 7, 18),
                BillNumber = "R-2026-098",
                GrossAmount = 780m,
                OwnShare = 100m,
                ExpectedReimbursement = 680m,
                Status = MedicalBillStatus.Submitted,
                SubmittedAt = new DateTime(2026, 7, 25, 9, 0, 0, DateTimeKind.Local),
                CreatedAt = new DateTime(2026, 7, 18, 9, 0, 0, DateTimeKind.Local),
            },

            // Frisch eingereicht, Eigenanteil bereits gebucht.
            new()
            {
                Provider = "Zahnarzt Dr. Weber",
                BillDate = new DateOnly(2026, 8, 19),
                BillNumber = "R-2026-114",
                GrossAmount = 850m,
                OwnShare = 187.60m,
                ExpectedReimbursement = 662.40m,
                Status = MedicalBillStatus.Submitted,
                SubmittedAt = new DateTime(2026, 8, 19, 17, 30, 0, DateTimeKind.Local),
                OwnShareTransactionId = dentist,
                CreatedAt = new DateTime(2026, 8, 19, 17, 0, 0, DateTimeKind.Local),
            },

            // Noch nicht eingereicht — die Zeile „Arztrechnung 210 €“ aus Wireframe 1a.
            new()
            {
                Provider = "Radiologie Neuenheim",
                BillDate = new DateOnly(2026, 8, 20),
                BillNumber = "RN-2026-441",
                GrossAmount = 210m,
                OwnShare = 0m,
                ExpectedReimbursement = 210m,
                Status = MedicalBillStatus.Recorded,
                CreatedAt = new DateTime(2026, 8, 20, 11, 0, 0, DateTimeKind.Local),
            },
        };

        db.MedicalBills.AddRange(bills);

        // Die Erstattung zum überfälligen Vorgang ist tatsächlich eingegangen, aber niemand hat sie
        // zugeordnet — genau der Fall, für den es den Screen „Zahlung zuordnen“ gibt. Sie liegt im
        // Juli, damit die Augustsummen des ersten Handoffs unberührt bleiben.
        if (account is not null)
        {
            db.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 7, 30),
                Payee = "Erstattung PKV R-2026-098",
                Kind = TransactionKind.Income,
                Amount = 680m,
                AccountId = account.Id,
                CategoryId = incomeCategory?.Id,
                ImportReference = "SEED-4300",
                CreatedAt = new DateTime(2026, 7, 30, 6, 0, 0, DateTimeKind.Local),
            });
        }

        return bills;
    }

    /// <summary>
    /// Die drei Fahrzeuge aus Handoff v4, Abschnitt 5 — samt der Buchungen, aus denen sich ihre
    /// Kosten rechnen.
    /// </summary>
    /// <remarks>
    /// Die Kosten werden nicht gepflegt, sondern aus echten Buchungen gerechnet. Damit die Zahlen
    /// des Prototyps herauskommen (Passat 4.120 €, Fabia 1.980 €), liegen hier Steuer, Werkstatt
    /// und Tanken — alle in der Vorgeschichte März bis Juli, damit der August und seine
    /// kalibrierten Monatssummen unberührt bleiben. Das Kennzeichen steht in der Notiz: daran
    /// findet der Dienst sie wieder.
    /// </remarks>
    private static async Task SeedVehiclesAsync(
        FinanzAppDbContext db, Dictionary<string, Policy> policies, CancellationToken ct)
    {
        var kfz = policies.TryGetValue("Kfz", out var policy) ? policy.Id : (int?)null;

        var passat = new Vehicle
        {
            Name = "VW Passat Variant",
            Plate = "L-2905",
            Usage = "Erstwagen",
            FirstRegistration = new DateOnly(2019, 3, 1),
            Mileage = 128400,
            PolicyId = kfz,
        };

        db.Vehicles.AddRange(
            passat,
            new Vehicle
            {
                Name = "Skoda Fabia",
                Plate = "L-1113",
                Usage = "Zweitwagen",
                FirstRegistration = new DateOnly(2016, 9, 1),
                Mileage = 94200,
            },
            new Vehicle
            {
                Name = "Firmenwagen EWV",
                Plate = "HD-EW 41",
                Usage = "Dienstwagen · 1 % Regelung",
            });

        var account = await db.Accounts.IgnoreQueryFilters().FirstAsync(a => a.Name == "Sparkasse Giro", ct);
        var autoCategory = await db.Categories
            .Where(c => c.Name == "Auto" && c.Direction == CategoryDirection.Expense)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        (int Month, int Day, string Payee, string Note, decimal Amount)[] costs =
        [
            (3, 12, "Hauptzollamt Heidelberg", "Kfz-Steuer L-2905", -282m),
            (4, 18, "Autohaus Krause", "Inspektion L-2905", -1220m),
            (5, 6, "Tankstellen 12 Monate", "Kraftstoff L-2905", -2000m),
            (4, 22, "Werkstatt Sauer", "Bremsen L-1113", -980m),
            (6, 9, "Tankstellen 12 Monate", "Kraftstoff L-1113", -1000m),
        ];

        var reference = 4900;
        foreach (var cost in costs)
        {
            db.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, cost.Month, cost.Day),
                Payee = cost.Payee,
                Note = cost.Note,
                Kind = TransactionKind.Expense,
                Amount = cost.Amount,
                AccountId = account.Id,
                CategoryId = autoCategory,
                ImportReference = "SEED-" + reference++,
                CreatedAt = new DateTime(2026, cost.Month, cost.Day, 6, 0, 0, DateTimeKind.Local),
            });
        }
    }

    /// <summary>
    /// Vier Belege im Posteingang — zwei erkannt, zwei zu prüfen.
    /// </summary>
    /// <remarks>
    /// Sie tragen bewusst weder Typ noch Verknüpfung: genau das ist der Zustand, den der
    /// Scaneingang abbildet. Erst wenn beides steht, verschwinden sie daraus.
    /// </remarks>
    private static async Task SeedScanInboxAsync(
        FinanzAppDbContext db,
        DocumentPathService paths,
        Dictionary<string, DocumentType> types,
        CancellationToken ct)
    {
        _ = types;
        var now = new DateTime(2026, 8, 23, 8, 24, 0, DateTimeKind.Local);

        (string File, string Sender, int Pages, bool Recognised, int DaysAgo)[] rows =
        [
            ("Beitragsanpassung_2027.pdf", "HUK-Coburg", 2, true, 1),
            ("Abrechnung_Debeka_08.pdf", "Debeka", 4, true, 2),
            ("Scan_20260820_0001.pdf", null!, 1, false, 3),
            ("Scan_20260819_0007.pdf", null!, 6, false, 4),
        ];

        foreach (var row in rows)
        {
            // Der Ordner heißt wie in der Dateiablage des Nutzers: Scaneingang.
            var relativePath = "Scaneingang/" + row.File;
            await WritePlaceholderAsync(paths, relativePath, row.File, ct);

            var document = new Document
            {
                Title = Path.GetFileNameWithoutExtension(row.File),
                Area = DocumentArea.Other,
                FileName = row.File,
                RelativePath = relativePath,
                DocumentDate = DateOnly.FromDateTime(now.AddDays(-row.DaysAgo)),
                CreatedAt = now.AddDays(-row.DaysAgo),
                UpdatedAt = now.AddDays(-row.DaysAgo),
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);

            db.ScanInbox.Add(new ScanInboxItem
            {
                DocumentId = document.Id,
                Sender = row.Sender,
                PageCount = row.Pages,
                Recognised = row.Recognised,
                CreatedAt = now.AddDays(-row.DaysAgo),
            });
        }
    }

    private static async Task SeedDocumentsAsync(
        FinanzAppDbContext db,
        DocumentPathService paths,
        Dictionary<string, DocumentType> types,
        Dictionary<string, Policy> policies,
        Property property,
        List<MedicalBill> bills,
        CancellationToken ct)
    {
        var now = new DateTime(2026, 8, 23, 8, 24, 0, DateTimeKind.Local);

        (string Title, string Type, DocumentArea Area, string Path, DateOnly Date, string? Tags,
            LinkTargetType? LinkType, int? LinkId, bool WriteFile)[] rows =
        [
            ("Versicherungsschein Risikoleben", "Versicherungsschein", DocumentArea.Insurance,
                "Versicherungen/Risikoleben/Police_2026.pdf", new DateOnly(2026, 1, 12), "police,2026",
                LinkTargetType.Policy, policies["Risikoleben"].Id, true),

            ("Versicherungsschein Hausrat", "Versicherungsschein", DocumentArea.Insurance,
                "Versicherungen/Hausrat/Schein_2024.pdf", new DateOnly(2024, 1, 12), "police,hausrat",
                LinkTargetType.Policy, policies["Hausrat"].Id, true),

            ("Beitragsanpassung Hausrat 2026", "Beitragsanpassung", DocumentArea.Insurance,
                "Versicherungen/Hausrat/Beitragsanpassung_2026.pdf", new DateOnly(2026, 2, 4), "hausrat",
                LinkTargetType.Policy, policies["Hausrat"].Id, true),

            ("Versicherungsschein Kfz", "Versicherungsschein", DocumentArea.Insurance,
                "Versicherungen/Kfz/Schein_2026.pdf", new DateOnly(2026, 1, 5), "police,kfz",
                LinkTargetType.Policy, policies["Kfz"].Id, true),

            ("Arztrechnung Dr. Meyer", "Arztrechnung", DocumentArea.Health,
                "Gesundheit/2026/R-2026-098.pdf", new DateOnly(2026, 7, 18), "pkv,zahnarzt",
                LinkTargetType.MedicalBill, bills[0].Id, true),

            ("Arztrechnung Dr. Weber", "Arztrechnung", DocumentArea.Health,
                "Gesundheit/2026/R-2026-114.pdf", new DateOnly(2026, 8, 19), "pkv,zahnarzt",
                LinkTargetType.MedicalBill, bills[1].Id, true),

            ("Stromrechnung 08/2026", "Stromrechnung", DocumentArea.Housing,
                "Wohnen/Strom/Rechnung_2026_08.pdf", new DateOnly(2026, 8, 1), "strom,stadtwerke",
                LinkTargetType.Contract, property.Contracts.First(c => c.Name == "Strom").Id, true),

            ("Kaufvertrag Hauptstraße", "Kaufvertrag", DocumentArea.Housing,
                "Wohnen/Kaufvertrag_2019.pdf", new DateOnly(2019, 3, 14), "immobilie",
                LinkTargetType.Property, property.Id, true),

            ("Grundbuchauszug", "Grundbuchauszug", DocumentArea.Housing,
                "Wohnen/Grundbuchauszug_2019.pdf", new DateOnly(2019, 4, 2), "immobilie",
                LinkTargetType.Property, property.Id, true),

            ("Energieausweis", "Energieausweis", DocumentArea.Housing,
                "Wohnen/Energieausweis_2019.pdf", new DateOnly(2019, 5, 20), "immobilie",
                LinkTargetType.Property, property.Id, true),

            // Ohne Datei auf der Platte: der Zustand „Datei nicht gefunden“ soll vorführbar sein.
            ("Lohnabrechnung 07/2026", "Lohnabrechnung", DocumentArea.Work,
                "Arbeit/Lohn/2026/" + MissingFileDocument, new DateOnly(2026, 7, 31), "lohn,2026",
                null, null, false),
        ];

        foreach (var row in rows)
        {
            var document = new Document
            {
                Title = row.Title,
                DocumentTypeId = types.TryGetValue(row.Type, out var type) ? type.Id : null,
                Area = row.Area,
                RelativePath = row.Path,
                FileName = Path.GetFileName(row.Path),
                Extension = Path.GetExtension(row.Path).ToLowerInvariant(),
                DocumentDate = row.Date,
                Status = DocumentStatus.Active,
                Tags = row.Tags,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);

            if (row.WriteFile)
            {
                await WritePlaceholderAsync(paths, row.Path, row.Title, ct);
            }

            if (row.LinkType is { } linkType && row.LinkId is { } linkId)
            {
                db.DocumentLinks.Add(new DocumentLink
                {
                    DocumentId = document.Id,
                    TargetType = linkType,
                    TargetId = linkId,
                    CreatedAt = now,
                });
            }
        }
    }

    /// <summary>
    /// Schreibt eine kleine Textdatei an den Dokumentpfad. Damit prüft die Vorführung echte
    /// Pfadauflösung statt einer erfundenen Existenz.
    /// </summary>
    private static async Task WritePlaceholderAsync(
        DocumentPathService paths, string relativePath, string title, CancellationToken ct)
    {
        if (paths.Resolve(relativePath) is not { } absolute)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        if (File.Exists(absolute))
        {
            return;
        }

        var text = $"""
            FinanzApp — Platzhalter für die Vorführung
            {title}

            Diese Datei ersetzt das echte Dokument. Sie entsteht beim ersten Start zusammen mit den
            Beispieldaten, damit die Pfadauflösung an echten Dateien geprüft werden kann.
            """;

        await File.WriteAllTextAsync(absolute, text, Encoding.UTF8, ct);
    }
}
