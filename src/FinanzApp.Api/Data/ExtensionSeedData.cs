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
/// <para><b>Die Beträge sind erfunden</b> — wie im übrigen Bestand und aus demselben Grund,
/// siehe <see cref="SeedData"/>. Die Monatssummen des Augusts bleiben dabei unangetastet: die
/// Buchungen dieser Klasse liegen im Juli und früher, damit Einnahmen (4.180 €) und Ausgaben
/// (2.975,40 €) des Augusts weiterhin glatt herauskommen.</para>
/// <para>Zu den Dokumenten werden echte Platzhalterdateien geschrieben — nur so lässt sich prüfen,
/// dass die Pfadauflösung greift. Eine Datei fehlt <em>mit Absicht</em>: der Zustand „Datei nicht
/// gefunden“ ist gestaltet und soll vorführbar sein.</para>
/// </remarks>
public static class ExtensionSeedData
{
    /// <summary>Dieses Dokument hat bewusst keine Datei auf der Platte.</summary>
    public const string MissingFileDocument = "Lohn_07_2026.pdf";

    public static async Task SeedAsync(
        FinanzAppDbContext db, DocumentPathService paths, int householdId, DateOnly currentMonth,
        CancellationToken ct = default)
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
        await SeedDepositsAsync(db, property, currentMonth, ct);
        var bills = await SeedMedicalBillsAsync(db, ct);
        await SeedHistoryAsync(db, ct);
        await db.SaveChangesAsync(ct);

        await SeedDocumentsAsync(db, paths, types, policies, property, bills, ct);
        await db.SaveChangesAsync(ct);

        await SeedWorkAsync(db, ct);
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
            ("Lieferschein Gerät", DocumentArea.Health),
            ("Kaufvertrag", DocumentArea.Housing),
            ("Grundbuchauszug", DocumentArea.Housing),
            ("Energieausweis", DocumentArea.Housing),
            ("Stromrechnung", DocumentArea.Housing),
            ("Gebührenbescheid", DocumentArea.Housing),
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
    /// sich auf 9.840,00 € — die Kopfzahl des Bereichs.
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
            ("Krankenversicherung", "Krankenversicherung Musterkasse", "Musterkasse", PolicyKind.Health,
                "PKV · Erstattungen unter Gesundheit", 585m, PremiumInterval.Monthly,
                new DateOnly(2016, 1, 1), null, 3, null),
            ("Kfz", "Kfz MGV", "MGV", PolicyKind.Vehicle,
                "HD-OA 17 · Wechselfrist 30.11.2026", 494m, PremiumInterval.Yearly,
                new DateOnly(2021, 1, 1), new DateOnly(2026, 12, 31), 1, null),
            ("Berufsunfähigkeit", "Berufsunfähigkeit", "Erste Musterkasse",
                PolicyKind.DisabilityInsurance, "BU-Rente 2.950,00 € monatlich", 96m,
                PremiumInterval.Monthly, new DateOnly(2017, 9, 1), null, 1, null),
            ("Risikoleben", "Risikoleben", "Musterleben", PolicyKind.TermLife,
                "kein Rückkaufswert", 35m, PremiumInterval.Monthly,
                new DateOnly(2020, 7, 1), null, 1, 164000m),
            ("Wohngebäude", "Wohngebäude", "Nordstern Versicherung", PolicyKind.Building,
                "Haus Hauptstraße 118", 348m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2027, 3, 31), 3, null),
            ("Rechtsschutz", "Rechtsschutz", "Rechtsbund", PolicyKind.LegalExpenses,
                "bis 31.05.2027", 198m, PremiumInterval.Yearly,
                new DateOnly(2022, 5, 1), new DateOnly(2027, 8, 31), 3, null),
            ("Hausrat", "Hausrat Nordstern", "Nordstern Versicherung", PolicyKind.HouseholdContents,
                "Wohnfläche 128 m²", 132m, PremiumInterval.Yearly,
                new DateOnly(2019, 4, 1), new DateOnly(2027, 12, 31), 3, null),
            ("Privathaftpflicht", "Privathaftpflicht", "Zahlwerk Direkt", PolicyKind.Liability,
                "bis 31.12.2027", 76m, PremiumInterval.Yearly,
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
    /// Zwei Jahre Vorgeschichte — September 2024 bis Juli 2026.
    /// </summary>
    /// <remarks>
    /// <para>Ohne Historie kann „Wohin fließt es“ nicht zwischen fix und variabel unterscheiden
    /// und das Sparpotential nichts erkennen. Der Auswertungsbereich braucht mehr: er zeigt eine
    /// 24-Monats-Sparkline und vergleicht gegen das Vorjahr. Mit acht Monaten Bestand stünde
    /// dort „kein Vergleichszeitraum“ — richtig, aber nichts zum Ansehen.</para>
    /// <para>Die fünf Monate März bis Juli 2026 sind auf die Wireframes kalibriert und stehen
    /// weiter als Tabelle. Davor liegen achtzehn gerechnete Monate.</para>
    /// <para>Der August bleibt unberührt, damit die Monatssummen des ersten Handoffs
    /// (5.240 € / 3.612 €) weiterhin genau herauskommen.</para>
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
        var raiffeisen = accounts["Musterbank Giro"].Id;

        // Monat -> (Lebensmittel, Freizeit, Auto). Freizeit liegt bewusst dauerhaft über dem
        // Budget von 180 €, damit das Sparpotential eine echte Überschreitung findet.
        (int Month, decimal Food, decimal Leisure, decimal Car)[] variable =
        [
            (3, 362.40m, 228.00m, 148.30m),
            (4, 325.75m, 184.50m, 110.90m),
            (5, 378.60m, 245.40m, 158.20m),
            (6, 334.30m, 206.80m, 121.50m),
            (7, 356.90m, 217.60m, 143.70m),
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

            Add(1, "Gehalt Nordwind", "Gehalt", CategoryDirection.Income, 4180m, sparkasse);
            Add(1, "Miete Wohnung Heidelberg", "Wohnen", CategoryDirection.Expense, -1165m, sparkasse);
            Add(1, "Nebenkosten Hausgeld", "Wohnen", CategoryDirection.Expense, -198m, sparkasse);
            Add(15, "Telekom Internet und Mobilfunk", "Wohnen", CategoryDirection.Expense, -51.90m, sparkasse);
            Add(17, "KFZ-Versicherung Musterschutz", "Versicherung", CategoryDirection.Expense, -66.90m, raiffeisen);
            Add(18, "Musterleben Beitrag", "Versicherung", CategoryDirection.Expense, -184m, raiffeisen);
            Add(10, "REWE Markt Heidelberg", "Lebensmittel", CategoryDirection.Expense, -month.Food, sparkasse);
            Add(12, "Freizeit und Kultur", "Freizeit", CategoryDirection.Expense, -month.Leisure, sparkasse);
            Add(13, "ARAL Tankstelle", "Auto", CategoryDirection.Expense, -month.Car, sparkasse);

            // Gesundheit gehört zu den Kategorien, die im Kostentrend steigen. Ohne einen Posten
            // in diesen fünf Monaten kläffte zwischen Februar und August ein Loch in der Kurve.
            Add(20, "Apotheke am Markt", "Gesundheit", CategoryDirection.Expense, -39m, sparkasse);

            // Zwei wiederkehrende Buchungen ohne hinterlegten Vertrag — der Fall „Abos“ aus
            // Wireframe 1g.
            Add(5, "Streaming Abo", "Sonstiges", CategoryDirection.Expense, -14.99m, sparkasse);
            Add(5, "Cloud-Speicher", "Sonstiges", CategoryDirection.Expense, -8.49m, sparkasse);
        }

        SeedDeepHistory(db, Category, sparkasse, raiffeisen);
    }

    /// <summary>
    /// Der Saisonfaktor je Kalendermonat, Januar bis Dezember.
    /// </summary>
    /// <remarks>
    /// Er hängt am <b>Kalendermonat</b> und ist damit jahresperiodisch — im Vorjahresvergleich
    /// kürzt er sich exakt heraus. Genau das verlangt die zweite Regel, die der Handoff zu
    /// Abschnitt 10b aus dem Prototypenbau mitgibt: dort hatte der Saisonterm eine beliebige
    /// Periode, „August gegen August“ verglich Rauschen statt Trend, und <em>jede</em> Kategorie
    /// galt als steigend. Die Amplitude bleibt zudem unter dem Jahrestrend, damit auch der
    /// Vergleich mit dem Vormonat nicht kippt.
    /// </remarks>
    private static readonly decimal[] Season =
    [
        1.06m, 1.04m, 1.00m, 0.97m, 0.95m, 0.96m,
        0.98m, 1.01m, 0.99m, 1.00m, 1.02m, 1.05m,
    ];

    /// <summary>
    /// Achtzehn gerechnete Monate: September 2024 bis Februar 2026.
    /// </summary>
    /// <remarks>
    /// Der Trend steckt in den Jahreswerten, nicht im Zufall: Wohnen, Freizeit und Gesundheit
    /// steigen spürbar, Lebensmittel mäßig, Auto und Versicherung kaum. Der Bericht findet
    /// damit etwas, das sich nachrechnen lässt — und die drei Namen in seiner Kopfzeile sind
    /// dieselben, die der Handoff als Beispiel nennt.
    /// </remarks>
    private static void SeedDeepHistory(
        FinanzAppDbContext db, Func<string, CategoryDirection, int?> category,
        int sparkasse, int raiffeisen)
    {
        var reference = 4600;

        for (var i = 0; i < 18; i++)
        {
            var monat = new DateOnly(2024, 9, 1).AddMonths(i);
            var saison = Season[monat.Month - 1];

            decimal Jahr(decimal y2024, decimal y2025, decimal y2026)
                => monat.Year == 2024 ? y2024 : monat.Year == 2025 ? y2025 : y2026;

            void Add(int day, string payee, string cat, CategoryDirection direction,
                decimal amount, int accountId, string? zweck = null)
            {
                db.Transactions.Add(new Transaction
                {
                    BookingDate = new DateOnly(monat.Year, monat.Month, day),
                    Payee = payee,
                    Kind = amount >= 0 ? TransactionKind.Income : TransactionKind.Expense,
                    Amount = decimal.Round(amount, 2),
                    AccountId = accountId,
                    CategoryId = category(cat, direction),

                    // Der Verwendungszweck trägt die Vertragsnummer. Über sie und nur über sie
                    // ordnet der Vertragsschirm eine Beitragsbuchung zu — bei vier Verträgen
                    // desselben Hauses wäre der Anbietername keine Zuordnung.
                    Purpose = zweck,
                    ImportReference = "SEED-" + reference++,
                    CreatedAt = new DateTime(monat.Year, monat.Month, day, 6, 0, 0, DateTimeKind.Local),
                });
            }

            Add(1, "Gehalt Nordwind", "Gehalt", CategoryDirection.Income,
                Jahr(3960m, 4060m, 4180m), sparkasse);

            // Fixe Posten ohne Saison — eine Miete schwankt nicht mit dem Monat.
            Add(1, "Miete Wohnung Heidelberg", "Wohnen", CategoryDirection.Expense,
                -Jahr(1085m, 1125m, 1165m), sparkasse);
            Add(15, "Telekom Internet und Mobilfunk", "Wohnen", CategoryDirection.Expense,
                -Jahr(47.90m, 47.90m, 51.90m), sparkasse);
            Add(17, "KFZ-Versicherung Musterschutz", "Versicherung", CategoryDirection.Expense,
                -Jahr(61.40m, 64.20m, 66.90m), raiffeisen);
            // Vier Verträge, vier Beiträge, jeder mit seiner Nummer im Verwendungszweck —
            // sonst ließe sich die Zuordnung über die Nummer nirgends ansehen.
            Add(18, "Musterleben Beitrag", "Versicherung", CategoryDirection.Expense,
                -184m, raiffeisen,
                $"BEITRAG {monat:MM/yyyy} VERTRAG LV-4471-01 KAPITALLEBENSVERSICHERUNG");
            Add(18, "Musterbank Lebensversicherung", "Versicherung", CategoryDirection.Expense,
                -70m, raiffeisen,
                $"MONATSBEITRAG {monat:MM/yyyy} POLICE RB-2019-8842");
            Add(20, "Musterkasse Riesterrente", "Versicherung", CategoryDirection.Expense,
                -141.50m, sparkasse,
                $"RIESTER EIGENBEITRAG {monat:MM/yyyy} VERTRAG DB-RI-330914");
            Add(20, "Bausparkasse Musterstadt", "Versicherung", CategoryDirection.Expense,
                -100m, sparkasse,
                $"BAUSPARBEITRAG BSH 7712 4409 {monat:MM/yyyy}");
            Add(5, "Streaming Abo", "Sonstiges", CategoryDirection.Expense,
                -Jahr(10.99m, 12.49m, 14.99m), sparkasse);
            Add(5, "Cloud-Speicher", "Sonstiges", CategoryDirection.Expense, -8.49m, sparkasse);

            // Saisonabhängige Posten — Heizkosten im Winter, Sprit im Sommer, und was der
            // Haushalt über das Jahr verschieden oft kauft.
            Add(1, "Nebenkosten Hausgeld", "Wohnen", CategoryDirection.Expense,
                -Jahr(182m, 190m, 198m) * saison, sparkasse);
            Add(10, "REWE Markt Heidelberg", "Lebensmittel", CategoryDirection.Expense,
                -Jahr(308m, 330m, 352m) * saison, sparkasse);
            Add(12, "Freizeit und Kultur", "Freizeit", CategoryDirection.Expense,
                -Jahr(148m, 178m, 210m) * saison, sparkasse);
            Add(13, "ARAL Tankstelle", "Auto", CategoryDirection.Expense,
                -Jahr(126m, 131m, 136m) * saison, sparkasse);
            Add(20, "Apotheke am Markt", "Gesundheit", CategoryDirection.Expense,
                -Jahr(17m, 27m, 39m) * saison, sparkasse);

            // Steuerlich eingeordnete Posten: einmal im Jahr die Heizungswartung, monatlich der
            // Berufsverband. Beide speisen den Steuerjahr-Bericht — ohne sie hätte er zwei
            // Abschnitte weniger und die Regeln dahinter ließen sich nicht ansehen.
            if (monat.Month == 3)
            {
                Add(14, "Heizungswartung Grau", "Handwerker", CategoryDirection.Expense,
                    -Jahr(152m, 158m, 164m), sparkasse);
            }

            if (monat.Month == 9)
            {
                Add(9, "Schornsteinfeger", "Handwerker", CategoryDirection.Expense,
                    -Jahr(76m, 80m, 82m), sparkasse);
            }

            Add(2, "Beitrag Berufsverband", "Beruf", CategoryDirection.Expense, -12m, sparkasse);
        }
    }

    /// <summary>
    /// Einlagen auf das Gemeinschaftskonto — Handoff „Gemeinsame Immobilie“, 3.3.
    /// </summary>
    /// <remarks>
    /// <para>Drei Monate, damit der Jahresstand mehr ist als der laufende Monat. Im laufenden
    /// Monat genau der Fall aus dem Handoff: der eine erfüllt sein Soll, die andere liegt 200 €
    /// darunter. Ohne diesen Zustand wäre am Schirm nie zu sehen, was der Vergleich leistet.</para>
    /// <para>Das Vorzeichen ist positiv: auf dem Gemeinschaftskonto kommt die Einlage an. Vom
    /// eigenen Konto aus wäre dieselbe Buchung ein Abfluss — siehe
    /// <c>TransactionService.Vorzeichen</c>.</para>
    /// </remarks>
    private static async Task SeedDepositsAsync(
        FinanzAppDbContext db, Property property, DateOnly currentMonth, CancellationToken ct)
    {
        var konto = await db.Accounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.HouseholdId == db.CurrentHouseholdId
                                      && a.Sharing == AccountSharing.Shared, ct);

        var beteiligte = await db.AccountShares.IgnoreQueryFilters()
            .Where(a => konto != null && a.AccountId == konto.Id)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);

        if (konto is null || beteiligte.Count != 2)
        {
            return;
        }

        // Wer am Objekt keinen Anteil hat, legt nichts ein — dieselbe Regel, die der Dienst prüft.
        var anteile = await db.PropertyShares.IgnoreQueryFilters()
            .Where(a => a.PropertyId == property.Id)
            .Select(a => a.UserId)
            .ToListAsync(ct);

        void Einlage(DateOnly monat, int tag, AccountShare beteiligter, decimal betrag)
        {
            if (!anteile.Contains(beteiligter.UserId))
            {
                return;
            }

            var datum = monat.AddDays(tag - 1);

            db.Transactions.Add(new Transaction
            {
                BookingDate = datum,
                Payee = "Einlage Gemeinschaftskonto",
                Kind = TransactionKind.Deposit,
                Amount = betrag,
                AccountId = konto.Id,
                DepositUserId = beteiligter.UserId,
                PropertyId = property.Id,
                CreatedAt = datum.ToDateTime(new TimeOnly(6, 0)),
            });
        }

        var (erster, zweite) = (beteiligte[0], beteiligte[1]);

        foreach (var zurueck in (int[])[2, 1])
        {
            var monat = currentMonth.AddMonths(-zurueck);
            Einlage(monat, 1, erster, erster.MonthlyTarget ?? 0m);
            Einlage(monat, 2, zweite, zweite.MonthlyTarget ?? 0m);
        }

        Einlage(currentMonth, 1, erster, erster.MonthlyTarget ?? 0m);
        Einlage(currentMonth, 3, zweite, (zweite.MonthlyTarget ?? 200m) - 200m);

        await db.SaveChangesAsync(ct);
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
            PurchasePrice = 268000m,
            MarketValue = 312000m,

            // Verweis auf das vorhandene Darlehen — es wird nicht kopiert.
            LoanId = loan?.Id,

            // Beide für den Objektbericht (Handoff 3.5): ohne Fläche keine €/m²-Zahl, ohne
            // Rücklage fehlt der Posten, an dem Objektkosten und Kontoabfluss auseinanderfallen.
            // Die 128 m² stehen schon in der Hausratpolice — zwei verschiedene Flächen im
            // Bestand wären ein Widerspruch, den niemand auflösen könnte.
            LivingArea = 128m,
            MonthlyReserve = 380m,
        };
        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);

        // Beteiligung zu zweit: Eigentum halbe-halbe, Eigenkapital ungleich. Genau der Fall aus
        // dem Handoff — ohne ihn wäre der Ausgleichsstand im Beispiel immer null, und der
        // interessante Zustand nie zu sehen.
        var beteiligte = await db.Users.AsNoTracking()
            .Where(u => u.HouseholdId == db.CurrentHouseholdId)
            .Where(u => u.Role == HouseholdRole.Owner || u.Role == HouseholdRole.Member)
            .OrderBy(u => u.Id)
            .Take(2)
            .ToListAsync(ct);

        if (beteiligte.Count == 2)
        {
            decimal[] eigenkapital = [62000m, 38000m];

            for (var i = 0; i < beteiligte.Count; i++)
            {
                db.PropertyShares.Add(new PropertyShare
                {
                    PropertyId = property.Id,
                    UserId = beteiligte[i].Id,
                    Percent = 50m,
                    Equity = eigenkapital[i],
                });
            }
        }

        // Das Kennzeichen „objektbezogen“ (Handoff 3.4) ist nicht die Zuordnung zum Objekt: der
        // Internetanschluss hängt am Haus und zieht doch mit den Leuten um. Er ist der eine Fall,
        // an dem der Unterschied im Bestand zu sehen ist.
        (string Name, string Provider, string? Number, decimal Monthly, int NoticeWeeks,
            DateOnly? NoticeTo, bool PropertyRelated)[] contracts =
        [
            ("Strom", "Stadtwerke Musterstadt", "SW-993 210", 117.60m, 6, new DateOnly(2027, 3, 31), true),
            ("Internet", "Telekom", "DTAG-40 118", 34.99m, 12, new DateOnly(2027, 6, 30), false),
            ("Wasser", "Stadtwerke Musterstadt", "SW-771 004", 31.50m, 6, new DateOnly(2027, 3, 31), true),
            ("Abfallentsorgung", "Stadt Heidelberg", "AE-2019-118", 19.80m, 8, new DateOnly(2026, 12, 31), true),
            ("Heizungswartung", "Sanitär Brenner", "HW-556", 15.00m, 4, new DateOnly(2026, 10, 31), true),
            ("Schornsteinfeger", "Bezirk Heidelberg Nord", null, 8.20m, 0, null, true),
        ];

        foreach (var row in contracts)
        {
            property.Contracts.Add(new Contract
            {
                Name = row.Name,
                Provider = row.Provider,
                ContractNumber = row.Number,
                Amount = row.Monthly,
                AccountId = account?.Id,
                StartsOn = new DateOnly(2019, 4, 1),
                NoticePeriodWeeks = row.NoticeWeeks,
                NoticeToDate = row.NoticeTo,
                PropertyRelated = row.PropertyRelated,
                Area = DocumentArea.Housing,
            });
        }

        await db.SaveChangesAsync(ct);

        var strom = property.Contracts.First(c => c.Name == "Strom");
        var paidTransaction = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.HouseholdId == db.CurrentHouseholdId && t.Payee.StartsWith("Stadtwerke"))
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
                Amount = 117.60m,
                Status = InvoiceStatus.Open,
            },
            new Invoice
            {
                ContractId = strom.Id,
                Subject = "Stromabschlag 07/2026",
                Number = "SW-2026-07",
                IssuedOn = new DateOnly(2026, 7, 1),
                DueOn = new DateOnly(2026, 8, 15),
                Amount = 88.00m,
                Status = InvoiceStatus.Paid,
                TransactionId = paidTransaction,
            });

        // Die Wohngebäude- und Hausratversicherung gehören zur Immobilie. Verknüpft sind sie
        // nicht — der Objektbericht liest sie über ihre Art (Gebäude, Hausrat) und sagt in der
        // Zeile, dass sie keinem Objekt zugeordnet sind.
        _ = policies;
        return property;
    }

    private static async Task<List<MedicalBill>> SeedMedicalBillsAsync(
        FinanzAppDbContext db, CancellationToken ct)
    {
        var dentist = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.HouseholdId == db.CurrentHouseholdId && t.Payee.StartsWith("Zahnarzt"))
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
                GrossAmount = 640m,
                OwnShare = 80m,
                ExpectedReimbursement = 560m,
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
                GrossAmount = 690m,
                OwnShare = 164.20m,
                ExpectedReimbursement = 525.80m,
                Status = MedicalBillStatus.Submitted,
                SubmittedAt = new DateTime(2026, 8, 19, 17, 30, 0, DateTimeKind.Local),
                OwnShareTransactionId = dentist,
                CreatedAt = new DateTime(2026, 8, 19, 17, 0, 0, DateTimeKind.Local),
            },

            // Noch nicht eingereicht — die Zeile „Arztrechnung 175 €“ aus Wireframe 1a.
            new()
            {
                Provider = "Radiologie Neuenheim",
                BillDate = new DateOnly(2026, 8, 20),
                BillNumber = "RN-2026-441",
                GrossAmount = 175m,
                OwnShare = 0m,
                ExpectedReimbursement = 175m,
                Status = MedicalBillStatus.Recorded,
                CreatedAt = new DateTime(2026, 8, 20, 11, 0, 0, DateTimeKind.Local),
            },

            // Abgeschlossen, voll erstattet: elf Tage von der Einreichung bis zur Zahlung.
            new()
            {
                Provider = "Dr. Meyer, Zahnarzt",
                BillDate = new DateOnly(2026, 2, 6),
                BillNumber = "R-2026-014",
                GrossAmount = 340m,
                OwnShare = 0m,
                ExpectedReimbursement = 340m,
                ActualReimbursement = 340m,
                Status = MedicalBillStatus.Completed,
                SubmittedAt = new DateTime(2026, 2, 9, 8, 0, 0, DateTimeKind.Local),
                PaidAt = new DateTime(2026, 2, 20, 8, 0, 0, DateTimeKind.Local),
                CreatedAt = new DateTime(2026, 2, 6, 8, 0, 0, DateTimeKind.Local),
            },

            // Abgerechnet mit weniger als beantragt: 544 statt 640 € Anspruch. Der Rest ist
            // keine offene Forderung mehr, sondern Eigenanteil — der Vorgang ist zu Ende, und
            // der Balken darf kein „erwartet“ zeigen, auf das niemand mehr wartet.
            new()
            {
                Provider = "Orthopädie am Neckar",
                BillDate = new DateOnly(2026, 4, 22),
                BillNumber = "ON-2026-207",
                GrossAmount = 760m,
                OwnShare = 120m,
                ExpectedReimbursement = 640m,
                ActualReimbursement = 544m,
                Status = MedicalBillStatus.Completed,
                SubmittedAt = new DateTime(2026, 4, 27, 8, 0, 0, DateTimeKind.Local),
                PaidAt = new DateTime(2026, 5, 16, 8, 0, 0, DateTimeKind.Local),
                CreatedAt = new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Local),
            },

            // Voriges Jahr — der Jahresfilter braucht ein zweites Jahr, sonst prüft ihn niemand.
            new()
            {
                Provider = "Hautarztpraxis Bergheim",
                BillDate = new DateOnly(2025, 9, 11),
                BillNumber = "HB-2025-338",
                GrossAmount = 280m,
                OwnShare = 30m,
                ExpectedReimbursement = 250m,
                ActualReimbursement = 250m,
                Status = MedicalBillStatus.Completed,
                SubmittedAt = new DateTime(2025, 9, 14, 8, 0, 0, DateTimeKind.Local),
                PaidAt = new DateTime(2025, 9, 28, 8, 0, 0, DateTimeKind.Local),
                CreatedAt = new DateTime(2025, 9, 11, 8, 0, 0, DateTimeKind.Local),
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
                Amount = 560m,
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
    /// des Prototyps herauskommen (Passat 2.816 €, Fabia 1.475 €), liegen hier Steuer, Werkstatt
    /// und Tanken — alle in der Vorgeschichte März bis Juli, damit der August und seine
    /// kalibrierten Monatssummen unberührt bleiben. Das Kennzeichen steht in der Notiz: daran
    /// findet der Dienst sie wieder.
    /// </remarks>
    /// <summary>
    /// Arbeit &amp; Beruf: zwei Verhältnisse, vier Abrechnungen, zwei Vereinbarungen.
    /// </summary>
    /// <remarks>
    /// <para>Der Auszahlungsbetrag wird <b>aus der Gehaltsbuchung gelesen</b>, nicht danebengesetzt.
    /// Das ist die erste Regel, gegen die der Prototyp verstoßen hat: dort hing die Abrechnung
    /// 08/2026 an einer Buchung über 4.180 €, führte selbst aber 3.040 € Auszahlung — eine
    /// Paarung, die der eigene Matcher (±15 %) nie vorgeschlagen hätte. Wer die Zahl aus der
    /// Buchung nimmt, kann diesen Fehler nicht machen.</para>
    /// <para>Das zweite Verhältnis ist beendet. Es steht in der Liste, trägt aber keine
    /// Jahreslast — ohne einen solchen Fall bliebe die Regel ungeprüft.</para>
    /// </remarks>
    private static async Task SeedWorkAsync(FinanzAppDbContext db, CancellationToken ct)
    {
        var nordwind = new Employment
        {
            Employer = "Nordwind Messtechnik",
            Position = "Softwareentwicklung",
            Kind = EmploymentKind.Permanent,
            StartsOn = new DateOnly(2019, 3, 1),
            HoursPerWeek = 40m,
            GrossMonthly = 6700m,
            NetMonthly = 4180m,
            NoticePeriodMonths = 3,
            IsActive = true,

            // Grundlage der Entfernungspauschale im Steuerjahr-Bericht. Ohne beide Felder
            // entsteht die Position nicht — geraten wird sie nicht.
            CommuteKilometres = 24m,
            WorkDaysPerYear = 220,
        };

        var klinikum = new Employment
        {
            Employer = "Rheinpark Klinikum",
            Position = "Pflege",
            Kind = EmploymentKind.PartTime,
            StartsOn = new DateOnly(2015, 9, 1),
            EndsOn = new DateOnly(2019, 2, 28),
            HoursPerWeek = 24m,
            GrossMonthly = 2050m,

            // Kein Netto erfasst: die Anzeige schätzt und sagt, dass sie schätzt.
            NetMonthly = null,
            NoticePeriodMonths = 1,
            IsActive = false,
        };

        db.Employments.AddRange(nordwind, klinikum);
        await db.SaveChangesAsync(ct);

        var belege = await db.Documents
            .Where(d => d.Area == DocumentArea.Work)
            .ToDictionaryAsync(d => d.Title, ct);

        // Vier Monate, und jeder holt sich seine eigene Gehaltsbuchung.
        foreach (var monat in new[] { 4, 5, 6, 7 })
        {
            var ersten = new DateOnly(2026, monat, 1);

            var buchung = await db.Transactions.FirstOrDefaultAsync(
                t => t.Payee == "Gehalt Nordwind" && t.BookingDate == ersten, ct);

            if (buchung is null)
            {
                continue;
            }

            var auszahlung = Math.Abs(buchung.Amount);

            db.Payslips.Add(new Payslip
            {
                EmploymentId = nordwind.Id,
                Month = ersten,
                Gross = 6700m,

                // 05/2026 ohne erfasstes Netto: die Zeile zeigt dann die Schätzung samt
                // Kennzeichen. Ohne diesen einen Fall ließe sich der Unterschied zwischen
                // erfasst und geschätzt nirgends ansehen.
                Net = monat == 5 ? null : auszahlung,
                Payout = auszahlung,

                // 04/2026 ohne Beleg, 07/2026 ohne Zahlung: beide Akzentzustände des Bereichs
                // sollen sich vorführen lassen, ohne dass man erst etwas anlegen muss.
                DocumentId = belege.TryGetValue($"Lohnabrechnung 0{monat}/2026", out var beleg)
                    ? beleg.Id
                    : null,
                TransactionId = monat == 7 ? null : buchung.Id,
            });
        }

        db.WorkAgreements.AddRange(
            new WorkAgreement
            {
                Employment = nordwind,
                Name = "Gehaltsanpassung 2026",
                SignedOn = new DateOnly(2026, 1, 15),
                Kind = WorkAgreementKind.SalaryChange,
            },
            new WorkAgreement
            {
                Employment = nordwind,
                Name = "Entgeltumwandlung Direktversicherung",
                SignedOn = new DateOnly(2023, 6, 1),
                Kind = WorkAgreementKind.OccupationalPension,
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedVehiclesAsync(
        FinanzAppDbContext db, Dictionary<string, Policy> policies, CancellationToken ct)
    {
        var kfz = policies.TryGetValue("Kfz", out var policy) ? policy.Id : (int?)null;

        // Der Dienstwagen hängt am laufenden Arbeitsverhältnis: nur so entsteht der Zuschlag
        // für den Arbeitsweg, und nur so ist die 1-%-Regelung überhaupt vorführbar.
        var stelle = await db.Employments.IgnoreQueryFilters()
            .Where(e => e.Employer == "Nordwind Messtechnik")
            .Select(e => (int?)e.Id)
            .FirstOrDefaultAsync(ct);

        var passat = new Vehicle
        {
            Name = "Opel Astra Sports Tourer",
            Plate = "HD-OA 17",
            Usage = "Erstwagen",
            FirstRegistration = new DateOnly(2019, 3, 1),
            Mileage = 112300,
            PolicyId = kfz,
        };

        db.Vehicles.AddRange(
            passat,
            new Vehicle
            {
                Name = "Fiat Panda",
                Plate = "HD-FP 8",
                Usage = "Zweitwagen",
                FirstRegistration = new DateOnly(2016, 9, 1),
                Mileage = 86400,
            },
            new Vehicle
            {
                Name = "Firmenwagen Nordwind",
                Plate = "HD-NW 41",
                Usage = "Dienstwagen · 1 % Regelung",

                // 48.900 € voll angesetzt: 489,00 € private Nutzung plus 352,08 € für
                // 24 km Arbeitsweg — 841,08 € im Monat, 10.092,96 € im Jahr.
                GrossListPrice = 48900m,
                TaxableSharePercent = 100m,
                EmploymentId = stelle,
            });

        var account = await db.Accounts.IgnoreQueryFilters().FirstAsync(a => a.Name == "Sparkasse Giro", ct);
        var autoCategory = await db.Categories
            .Where(c => c.Name == "Auto" && c.Direction == CategoryDirection.Expense)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        (int Month, int Day, string Payee, string Note, decimal Amount)[] costs =
        [
            (3, 12, "Hauptzollamt Heidelberg", "Kfz-Steuer HD-OA 17", -236m),
            (4, 18, "Autohaus Krause", "Inspektion HD-OA 17", -940m),
            (5, 6, "Tankstellen 12 Monate", "Kraftstoff HD-OA 17", -1640m),
            (4, 22, "Werkstatt Sauer", "Bremsen HD-FP 8", -720m),
            (6, 9, "Tankstellen 12 Monate", "Kraftstoff HD-FP 8", -755m),
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
            ("Beitragsanpassung_2027.pdf", "Nordstern Versicherung", 2, true, 1),
            ("Abrechnung_Musterkasse_08.pdf", "Musterkasse", 4, true, 2),
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

                // Ohne sie ginge der Beleg als „application/octet-stream" hinaus, und die
                // Vorschau könnte ihn nicht zeigen.
                Extension = Path.GetExtension(row.File).ToLowerInvariant(),
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

            ("Lohnabrechnung 05/2026", "Lohnabrechnung", DocumentArea.Work,
                "Arbeit/Lohn/2026/Lohn_05_2026.pdf", new DateOnly(2026, 5, 31), "lohn,2026",
                null, null, true),

            ("Lohnabrechnung 06/2026", "Lohnabrechnung", DocumentArea.Work,
                "Arbeit/Lohn/2026/Lohn_06_2026.pdf", new DateOnly(2026, 6, 30), "lohn,2026",
                null, null, true),

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
    /// Schreibt eine kleine Datei an den Dokumentpfad. Damit prüft die Vorführung echte
    /// Pfadauflösung statt einer erfundenen Existenz.
    /// </summary>
    /// <remarks>
    /// Endet der Pfad auf <c>.pdf</c>, entsteht ein <b>gültiges</b> einseitiges PDF. Vorher stand
    /// dort Text unter PDF-Namen: die Vorschau bekam <c>application/pdf</c> und einen Inhalt, der
    /// keines war, und zeigte im Rahmen einen Ladefehler. Eine Vorführung, die den gebauten
    /// Zustand nicht vorführt, ist schlimmer als keine.
    /// </remarks>
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

        if (!absolute.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(absolute, text, Encoding.UTF8, ct);
            return;
        }

        await File.WriteAllBytesAsync(absolute, PlaceholderPdf(title), ct);
    }

    /// <summary>
    /// Ein einseitiges PDF mit dem Titel darauf, von Hand zusammengesetzt.
    /// </summary>
    /// <remarks>
    /// Ohne Bibliothek: fünf Objekte, eine Querverweistabelle, fertig. Für einen Platzhalter ist
    /// das billiger als eine Abhängigkeit — und es muss nur eines können, nämlich sich öffnen
    /// lassen.
    /// </remarks>
    private static byte[] PlaceholderPdf(string title)
    {
        // Klammern und Rückstriche sind in einem PDF-Textliteral Steuerzeichen.
        var beschriftung = title
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        var inhalt = Encoding.Latin1.GetBytes(
            $"BT /F1 16 Tf 60 760 Td (FinanzApp - Platzhalter) Tj ET\n"
            + $"BT /F1 12 Tf 60 730 Td ({beschriftung}) Tj ET\n");

        string[] koerper =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] "
                + "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {inhalt.Length} >>\nstream\n{Encoding.Latin1.GetString(inhalt)}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var datei = new MemoryStream();
        var kopf = Encoding.Latin1.GetBytes("%PDF-1.4\n");
        datei.Write(kopf);

        var stellen = new List<long>(koerper.Length);
        for (var nummer = 1; nummer <= koerper.Length; nummer++)
        {
            stellen.Add(datei.Position);
            datei.Write(Encoding.Latin1.GetBytes($"{nummer} 0 obj\n{koerper[nummer - 1]}\nendobj\n"));
        }

        var tabelle = datei.Position;
        datei.Write(Encoding.Latin1.GetBytes($"xref\n0 {koerper.Length + 1}\n0000000000 65535 f \n"));
        foreach (var stelle in stellen)
        {
            datei.Write(Encoding.Latin1.GetBytes($"{stelle:D10} 00000 n \n"));
        }

        datei.Write(Encoding.Latin1.GetBytes(
            $"trailer\n<< /Size {koerper.Length + 1} /Root 1 0 R >>\nstartxref\n{tabelle}\n%%EOF\n"));

        return datei.ToArray();
    }
}
