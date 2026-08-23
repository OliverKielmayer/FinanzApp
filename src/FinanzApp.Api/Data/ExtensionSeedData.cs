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
        var insurances = SeedInsurances(db);
        await db.SaveChangesAsync(ct);

        var property = await SeedPropertyAsync(db, insurances, ct);
        var bills = await SeedMedicalBillsAsync(db, ct);
        await SeedHistoryAsync(db, ct);
        await db.SaveChangesAsync(ct);

        await SeedDocumentsAsync(db, paths, types, insurances, property, bills, ct);
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

    private static Dictionary<string, Insurance> SeedInsurances(FinanzAppDbContext db)
    {
        // „Hausrat“ trägt bewusst eine laufende Kündigungsfrist: der Akzentzustand aus den
        // Wireframes soll sich am Stichtag 23.08.2026 auch wirklich zeigen.
        (string Name, string Insurer, string? Number, decimal Premium, PremiumInterval Interval,
            DateOnly? Starts, DateOnly? Ends, int NoticeMonths)[] rows =
        [
            ("Hausrat", "HUK-Coburg", "HR-88 421", 156m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2026, 12, 10), 3),
            ("Privathaftpflicht", "HUK-Coburg", "PH-41 220", 89m, PremiumInterval.Yearly,
                new DateOnly(2018, 1, 1), new DateOnly(2027, 12, 31), 3),
            ("Risikoleben", "Heidelberger Leben", "RL-77 903", 42m, PremiumInterval.Monthly,
                new DateOnly(2020, 7, 1), null, 1),
            ("Kfz", "Allianz", "KFZ-55 108", 618m, PremiumInterval.Yearly,
                new DateOnly(2021, 1, 1), new DateOnly(2026, 12, 31), 1),
            ("Rechtsschutz", "ARAG", "RS-31 664", 245m, PremiumInterval.Yearly,
                new DateOnly(2022, 5, 1), new DateOnly(2028, 4, 30), 3),
            ("Berufsunfähigkeit", "Alte Leipziger", "BU-90 552", 78m, PremiumInterval.Monthly,
                new DateOnly(2017, 9, 1), null, 1),
            ("Wohngebäude", "HUK-Coburg", "WG-12 470", 384m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2027, 3, 31), 3),
        ];

        var map = new Dictionary<string, Insurance>();
        foreach (var row in rows)
        {
            var insurance = new Insurance
            {
                Name = row.Name,
                Insurer = row.Insurer,
                PolicyNumber = row.Number,
                Premium = row.Premium,
                PremiumInterval = row.Interval,
                StartsOn = row.Starts,
                EndsOn = row.Ends,
                NoticePeriodMonths = row.NoticeMonths,
            };

            db.Insurances.Add(insurance);
            map[row.Name] = insurance;
        }

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
        var accounts = await db.Accounts.ToDictionaryAsync(a => a.Name, ct);
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
        FinanzAppDbContext db, Dictionary<string, Insurance> insurances, CancellationToken ct)
    {
        var loan = await db.Loans.OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == "Sparkasse Giro", ct);

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
        _ = insurances;
        return property;
    }

    private static async Task<List<MedicalBill>> SeedMedicalBillsAsync(
        FinanzAppDbContext db, CancellationToken ct)
    {
        var dentist = await db.Transactions
            .Where(t => t.Payee.StartsWith("Zahnarzt"))
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(ct);

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Name == "Sparkasse Giro", ct);
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

    private static async Task SeedDocumentsAsync(
        FinanzAppDbContext db,
        DocumentPathService paths,
        Dictionary<string, DocumentType> types,
        Dictionary<string, Insurance> insurances,
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
                LinkTargetType.Insurance, insurances["Risikoleben"].Id, true),

            ("Versicherungsschein Hausrat", "Versicherungsschein", DocumentArea.Insurance,
                "Versicherungen/Hausrat/Schein_2024.pdf", new DateOnly(2024, 1, 12), "police,hausrat",
                LinkTargetType.Insurance, insurances["Hausrat"].Id, true),

            ("Beitragsanpassung Hausrat 2026", "Beitragsanpassung", DocumentArea.Insurance,
                "Versicherungen/Hausrat/Beitragsanpassung_2026.pdf", new DateOnly(2026, 2, 4), "hausrat",
                LinkTargetType.Insurance, insurances["Hausrat"].Id, true),

            ("Versicherungsschein Kfz", "Versicherungsschein", DocumentArea.Insurance,
                "Versicherungen/Kfz/Schein_2026.pdf", new DateOnly(2026, 1, 5), "police,kfz",
                LinkTargetType.Insurance, insurances["Kfz"].Id, true),

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
