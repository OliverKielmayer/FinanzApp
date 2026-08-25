using System.Globalization;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Anlege-Flows aller Objekttypen — ein Dienst, eine Feldliste je Typ.
/// </summary>
/// <remarks>
/// <para>Der Handoff verlangt „ein gemeinsamer Formularscreen, gesteuert über eine Feldliste je
/// Objekttyp“. Genau so ist es gebaut: <see cref="GetFormAsync"/> beschreibt das Formular,
/// <see cref="CreateAsync"/> prüft gegen <em>dieselbe</em> Beschreibung. Damit kann die Meldung
/// das fehlende Feld bei dem Namen nennen, den der Benutzer gesehen hat, und ein neues Feld
/// wird an einer Stelle ergänzt statt an zweien.</para>
/// <para>Jeder Flow schreibt wirklich: ein neues Konto steht danach in der Kontoliste und ist in
/// der Vertragsanlage wählbar, ein neues Budget verändert Plan und Verbleibend. Deshalb gibt es
/// hier keine Attrappen.</para>
/// </remarks>
public sealed class CreateFormService(
    FinanzAppDbContext db,
    IClock clock,
    DocumentService documents,
    IPolicyDocumentAnalyzer analyzer)
{
    /// <summary>Beschreibt das Formular eines Typs, samt der Auswahlwerte aus dem Bestand.</summary>
    public async Task<CreateFormDto?> GetFormAsync(CreateObjectType type, CancellationToken ct = default)
        => type switch
        {
            CreateObjectType.Account => await AccountFormAsync(ct),
            CreateObjectType.Depot => await DepotFormAsync(ct),
            CreateObjectType.Pension => PensionForm(),
            CreateObjectType.Protection => ProtectionForm(),
            CreateObjectType.Property => await PropertyFormAsync(ct),
            CreateObjectType.Contract => await ContractFormAsync(ct),
            CreateObjectType.Budget => await BudgetFormAsync(ct),
            _ => null,
        };

    public async Task<CreateResultDto> CreateAsync(
        CreateObjectType type, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        var form = await GetFormAsync(type, ct);
        if (form is null)
        {
            return Fail(null, "Diesen Objekttyp gibt es noch nicht.");
        }

        // Erst die Pflichtfelder, und zwar in der Reihenfolge des Formulars — so springt die
        // Meldung nicht zwischen den Feldern hin und her.
        foreach (var field in form.Fields.Where(f => f.Required))
        {
            if (string.IsNullOrWhiteSpace(Value(values, field.Key)))
            {
                return Fail(field.Key, $"{field.Label} fehlt");
            }
        }

        return type switch
        {
            CreateObjectType.Account => await CreateAccountAsync(values, ct),
            CreateObjectType.Depot => await CreateDepotAsync(values, ct),
            CreateObjectType.Pension => await CreatePolicyAsync(values, capitalForming: true, ct),
            CreateObjectType.Protection => await CreatePolicyAsync(values, capitalForming: false, ct),
            CreateObjectType.Property => await CreatePropertyAsync(values, ct),
            CreateObjectType.Contract => await CreateContractAsync(values, ct),
            CreateObjectType.Budget => await CreateBudgetAsync(values, ct),
            _ => Fail(null, "Diesen Objekttyp gibt es noch nicht."),
        };
    }

    // ── Police / Beleg einlesen ──────────────────────────────────────────────

    /// <summary>
    /// Legt die Datei ab und lässt sie lesen. Beides in dieser Reihenfolge, denn die Ablage ist
    /// das Verlässliche — die Analyse darf fehlschlagen oder gar nicht da sein.
    /// </summary>
    /// <remarks>
    /// Gespeichert wird nur der relative Pfad, wie überall. Die gelesenen Werte landen als
    /// <c>DocumentExtraction</c> mit Herkunft und dem Vermerk <em>unbestätigt</em> — sie
    /// verändern nichts, solange niemand sie übernommen hat.
    /// </remarks>
    public async Task<DocumentAnalysisDto> AnalyseAsync(
        CreateObjectType type, Stream content, string fileName, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        var upload = await documents.UploadAsync(
            buffer, fileName, DocumentArea.Insurance, title: null, documentTypeId: null,
            documentDate: null, ct);

        buffer.Position = 0;
        var fields = await analyzer.AnalyseAsync(buffer, fileName, type, ct);

        foreach (var field in fields)
        {
            db.DocumentExtractions.Add(new DocumentExtraction
            {
                DocumentId = upload.DocumentId,
                FieldKey = field.Key,
                Label = field.Label,
                Value = field.Value,
                SourcePage = field.SourcePage,
                Confidence = field.Confidence,
                Confirmed = false,
                CreatedAt = clock.Now,
            });
        }

        await db.SaveChangesAsync(ct);

        return new DocumentAnalysisDto
        {
            HasContent = fields.Count > 0,
            FileName = fileName,
            RelativePath = upload.RelativePath,
            Fields = fields,
            Note = fields.Count > 0
                ? null
                : "Es ist keine Analyse angebunden. Die Datei ist abgelegt, die Werte bitte von Hand eintragen.",
        };
    }

    /// <summary>
    /// Vermerkt die gelesenen Werte eines Dokuments als bestätigt — das passiert erst, wenn ein
    /// Mensch „Übernehmen“ gedrückt hat.
    /// </summary>
    public async Task<int> ConfirmExtractionsAsync(int documentId, CancellationToken ct = default)
    {
        var rows = await db.DocumentExtractions
            .Where(x => x.DocumentId == documentId && !x.Confirmed)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.Confirmed = true;
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    // ── Konto ──────────────────────────────────────────────────────────────────────────────

    private async Task<CreateFormDto> AccountFormAsync(CancellationToken ct)
    {
        var profiles = await db.ImportProfiles.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new CreateOptionDto { Value = p.Id.ToString(), Label = p.Name, Hint = p.Format })
            .ToListAsync(ct);

        return new CreateFormDto
        {
            Type = CreateObjectType.Account,
            Kicker = "Neu",
            Title = "Konto anlegen",
            SubmitLabel = "Konto anlegen",
            Fields =
            [
                new CreateFieldDto
                {
                    Key = "kind",
                    Label = "Art",
                    Kind = CreateFieldKind.Choice,
                    Required = true,
                    Options =
                    [
                        new() { Value = "checking", Label = "Girokonto" },
                        new() { Value = "savings", Label = "Tagesgeld" },

                        // Ein Depot ist kein Konto, ein Darlehen erst recht nicht. Beide führen
                        // dorthin, wo sie hingehören, statt hier etwas Falsches anzulegen.
                        new() { Value = "depot", Label = "Depot", Hint = "eigener Flow", RedirectTo = "/neu/depot" },
                        new() { Value = "loan", Label = "Darlehen", Hint = "unter Finanzierungen", RedirectTo = "/darlehen" },
                    ],
                },
                Text("bank", "Bank", required: true, placeholder: "z. B. Sparkasse Heidelberg"),
                Text("iban", "IBAN", required: false, placeholder: "DE.."),
                Money("opening", "Startsaldo", required: true,
                    help: "Der Stand zum Stichtag. Buchungen ab diesem Tag rechnen darauf auf."),
                Date("asOf", "Stichtag", required: true, defaultValue: clock.Today),
                Reference("profile", "Importprofil", required: false, profiles),
            ],
        };
    }

    private async Task<CreateResultDto> CreateAccountAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var kindValue = Value(values, "kind");
        if (kindValue is "depot")
        {
            return Fail("kind", "Ein Depot wird über den Depot-Flow angelegt.");
        }

        if (kindValue is "loan")
        {
            return Fail("kind", "Ein Darlehen steht unter Finanzierungen, nicht unter Konten.");
        }

        var kind = kindValue == "savings" ? AccountKind.Savings : AccountKind.Checking;
        var bank = Value(values, "bank")!.Trim();

        if (ParseMoney(Value(values, "opening")) is not { } opening)
        {
            return Fail("opening", "Startsaldo ist kein Betrag");
        }

        if (ParseDate(Value(values, "asOf")) is not { } asOf)
        {
            return Fail("asOf", "Stichtag ist kein Datum");
        }

        var name = kind == AccountKind.Savings ? $"Tagesgeld {bank}" : $"{bank} Giro";
        if (await db.Accounts.AnyAsync(a => a.Name == name, ct))
        {
            return Fail("bank", $"Ein Konto „{name}“ besteht bereits.");
        }

        var account = new Account
        {
            Name = name,
            ShortName = bank,
            BankName = bank,
            Kind = kind,
            Iban = Value(values, "iban")?.Trim(),
            OpeningBalance = opening,
            BalanceAsOf = asOf,
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return Ok(account.Id, "/konten");
    }

    // ── Depot ──────────────────────────────────────────────────────────────────────────────

    private async Task<CreateFormDto> DepotFormAsync(CancellationToken ct)
        => new()
        {
            Type = CreateObjectType.Depot,
            Kicker = "Neu",
            Title = "Depot anlegen",
            SubmitLabel = "Depot anlegen",
            Hint = "Ohne erfasste Positionen zählt der angegebene Wert — mit Positionen rechnet der Bestand.",
            Fields =
            [
                Text("broker", "Broker", required: true, placeholder: "z. B. finanzen.net ZERO"),
                Text("number", "Depotnummer", required: false),
                new CreateFieldDto
                {
                    Key = "depotKind",
                    Label = "Depotart",
                    Kind = CreateFieldKind.Choice,
                    Required = false,
                    DefaultValue = "Einzeldepot",
                    Options =
                    [
                        new() { Value = "Einzeldepot", Label = "Einzeldepot" },
                        new() { Value = "Gemeinschaftsdepot", Label = "Gemeinschaftsdepot" },
                        new() { Value = "Kinderdepot", Label = "Kinderdepot" },
                    ],
                },
                Money("value", "Depotwert", required: true),
                Date("asOf", "Stichtag", required: true, defaultValue: clock.Today),
                Reference("account", "Verrechnungskonto", required: false, await AccountOptionsAsync(ct)),
                Text("quotes", "Kursdatenquelle", required: false, placeholder: "z. B. Anbieter-Export"),
            ],
        };

    private async Task<CreateResultDto> CreateDepotAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var broker = Value(values, "broker")!.Trim();

        if (ParseMoney(Value(values, "value")) is not { } value)
        {
            return Fail("value", "Depotwert ist kein Betrag");
        }

        if (ParseDate(Value(values, "asOf")) is not { } asOf)
        {
            return Fail("asOf", "Stichtag ist kein Datum");
        }

        if (await db.Depots.AnyAsync(d => d.Name == broker, ct))
        {
            return Fail("broker", $"Ein Depot bei „{broker}“ besteht bereits.");
        }

        var depot = new Depot
        {
            Name = broker,
            Broker = broker,
            Number = Value(values, "number")?.Trim(),
            DepotKind = Value(values, "depotKind")?.Trim(),
            StatedValue = value,
            ValuationDate = asOf,
            AccountId = ParseInt(Value(values, "account")),
            QuoteSource = Value(values, "quotes")?.Trim(),
        };

        db.Depots.Add(depot);
        await db.SaveChangesAsync(ct);

        return Ok(depot.Id, "/depot");
    }

    // ── Vorsorge und Absicherung ───────────────────────────────────────────────────────────

    private static CreateFormDto PensionForm() => new()
    {
        Type = CreateObjectType.Pension,
        Kicker = "Neu",
        Title = "Vorsorgevertrag anlegen",
        SubmitLabel = "Vertrag anlegen",
        Hint = "Der erreichte Wert zählt zum Vermögen — deshalb ist der Stichtag Pflicht.",
        Fields =
        [
            Choice("kind", "Vertragsart", required: true,
            [
                new() { Value = nameof(PolicyKind.CapitalLife), Label = "Kapital-LV" },
                new() { Value = nameof(PolicyKind.Pension), Label = "Rentenversicherung" },
                new() { Value = nameof(PolicyKind.Riester), Label = "Riester-Rente" },
                new() { Value = nameof(PolicyKind.BuildingSociety), Label = "Bausparvertrag" },
                new() { Value = nameof(PolicyKind.OccupationalPension), Label = "bAV" },
            ]),
            Text("provider", "Anbieter", required: true),
            Text("number", "Vertragsnummer", required: false),
            Money("premium", "Beitrag", required: false, help: "Leer lassen, wenn kein laufender Beitrag erfasst ist."),
            Money("value", "Erreichter Wert", required: true),
            Date("asOf", "Stichtag", required: true),
            Date("maturesOn", "Ablauf", required: false),
            Date("report", "Statusbericht", required: false,
                help: "Datum des letzten Berichts. Daraus entsteht eine Frist für den nächsten."),
        ],
    };

    private static CreateFormDto ProtectionForm() => new()
    {
        Type = CreateObjectType.Protection,
        Kicker = "Neu",
        Title = "Versicherung anlegen",
        SubmitLabel = "Versicherung anlegen",
        Hint = "Eine Absicherung leistet im Schadensfall. Sie trägt keinen Wert und erscheint nie im Vermögen.",
        Fields =
        [
            Choice("kind", "Art", required: true,
            [
                new() { Value = nameof(PolicyKind.TermLife), Label = "Risikoleben" },
                new() { Value = nameof(PolicyKind.DisabilityInsurance), Label = "Berufsunfähigkeit" },
                new() { Value = nameof(PolicyKind.Liability), Label = "Haftpflicht" },
                new() { Value = nameof(PolicyKind.HouseholdContents), Label = "Hausrat" },
                new() { Value = nameof(PolicyKind.Building), Label = "Wohngebäude" },
                new() { Value = nameof(PolicyKind.Vehicle), Label = "Kfz" },
                new() { Value = nameof(PolicyKind.Accident), Label = "Unfall" },
                new() { Value = nameof(PolicyKind.LegalExpenses), Label = "Rechtsschutz" },
                new() { Value = nameof(PolicyKind.Health), Label = "Kranken" },
            ]),
            Text("provider", "Versicherer", required: true),
            Text("number", "Versicherungsnummer", required: false),
            Money("premium", "Beitrag", required: true),
            Choice("interval", "Intervall", required: false,
            [
                new() { Value = nameof(PremiumInterval.Monthly), Label = "monatlich" },
                new() { Value = nameof(PremiumInterval.Quarterly), Label = "vierteljährlich" },
                new() { Value = nameof(PremiumInterval.HalfYearly), Label = "halbjährlich" },
                new() { Value = nameof(PremiumInterval.Yearly), Label = "jährlich" },
            ], defaultValue: nameof(PremiumInterval.Yearly)),
            Date("endsOn", "Vertragsende", required: false),
            Number("notice", "Kündigungsfrist in Monaten", required: false,
                help: "Aus Vertragsende minus Frist entsteht der Kündigungstermin. Er wird nicht eingegeben."),
        ],
    };

    private async Task<CreateResultDto> CreatePolicyAsync(
        IReadOnlyDictionary<string, string?> values, bool capitalForming, CancellationToken ct)
    {
        if (!Enum.TryParse<PolicyKind>(Value(values, "kind"), out var kind))
        {
            return Fail("kind", capitalForming ? "Vertragsart fehlt" : "Art fehlt");
        }

        var provider = Value(values, "provider")!.Trim();
        var label = PolicyService.KindLabel(kind);

        var name = capitalForming ? $"{provider}" : label;
        if (await db.Policies.AnyAsync(p => p.Name == name && p.Kind == kind, ct))
        {
            return Fail("provider", $"Ein Vertrag „{name}“ besteht bereits.");
        }

        var policy = new Policy
        {
            Kind = kind,
            IsCapitalForming = capitalForming,
            Name = name,
            Provider = provider,
            PolicyNumber = Value(values, "number")?.Trim(),
            Premium = ParseMoney(Value(values, "premium")) ?? 0m,
        };

        if (capitalForming)
        {
            if (ParseMoney(Value(values, "value")) is not { } value)
            {
                return Fail("value", "Erreichter Wert ist kein Betrag");
            }

            if (ParseDate(Value(values, "asOf")) is not { } asOf)
            {
                return Fail("asOf", "Stichtag ist kein Datum");
            }

            policy.CurrentValue = value;
            policy.ValuationDate = asOf;
            policy.MaturesOn = ParseDate(Value(values, "maturesOn"));

            // Ein Statusbericht ist ein Jahresrhythmus: aus dem letzten entsteht die Erinnerung
            // an den nächsten.
            if (ParseDate(Value(values, "report")) is { } report)
            {
                policy.NoticeReminderOn = report.AddYears(1);
            }
        }
        else
        {
            policy.PremiumInterval = Enum.TryParse<PremiumInterval>(Value(values, "interval"), out var interval)
                ? interval
                : PremiumInterval.Yearly;
            policy.EndsOn = ParseDate(Value(values, "endsOn"));
            policy.NoticePeriodMonths = ParseInt(Value(values, "notice")) ?? 0;
        }

        db.Policies.Add(policy);
        await db.SaveChangesAsync(ct);

        return Ok(policy.Id, $"/police/{policy.Id}");
    }

    // ── Immobilie ──────────────────────────────────────────────────────────────────────────

    private async Task<CreateFormDto> PropertyFormAsync(CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new CreateOptionDto
            {
                Value = l.Id.ToString(),
                Label = l.Name,
                Hint = l.Lender,
            })
            .ToListAsync(ct);

        return new CreateFormDto
        {
            Type = CreateObjectType.Property,
            Kicker = "Neu",
            Title = "Immobilie anlegen",
            SubmitLabel = "Immobilie anlegen",
            Hint = "Ein bestehendes Darlehen wird verknüpft, nicht kopiert — es bleibt unter Finanzierungen.",
            Fields =
            [
                Text("name", "Bezeichnung", required: true, placeholder: "z. B. Haus Kammerstatter Straße 10"),
                Text("address", "Adresse", required: false),
                Choice("kind", "Typ", required: false,
                [
                    new() { Value = nameof(PropertyKind.House), Label = "Haus" },
                    new() { Value = nameof(PropertyKind.Apartment), Label = "Wohnung" },
                    new() { Value = nameof(PropertyKind.Land), Label = "Grundstück" },
                    new() { Value = nameof(PropertyKind.Other), Label = "Sonstiges" },
                ], defaultValue: nameof(PropertyKind.House)),
                Date("purchase", "Kauf", required: true),
                Money("market", "Marktwert", required: false),
                Reference("loan", "Bestehendes Darlehen", required: false, loans),
            ],
        };
    }

    private async Task<CreateResultDto> CreatePropertyAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var name = Value(values, "name")!.Trim();

        if (ParseDate(Value(values, "purchase")) is not { } purchase)
        {
            return Fail("purchase", "Kauf ist kein Datum");
        }

        if (await db.Properties.AnyAsync(p => p.Name == name, ct))
        {
            return Fail("name", $"Eine Immobilie „{name}“ besteht bereits.");
        }

        var property = new Property
        {
            Name = name,
            Address = Value(values, "address")?.Trim(),
            Kind = Enum.TryParse<PropertyKind>(Value(values, "kind"), out var kind) ? kind : PropertyKind.House,
            PurchaseDate = purchase,
            MarketValue = ParseMoney(Value(values, "market")) ?? 0m,
            LoanId = ParseInt(Value(values, "loan")),
        };

        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);

        return Ok(property.Id, $"/wohnen/{property.Id}");
    }

    // ── Vertrag (Wohnen) ───────────────────────────────────────────────────────────────────

    private async Task<CreateFormDto> ContractFormAsync(CancellationToken ct)
    {
        var properties = await db.Properties.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new CreateOptionDto { Value = p.Id.ToString(), Label = p.Name })
            .ToListAsync(ct);

        return new CreateFormDto
        {
            Type = CreateObjectType.Contract,
            Kicker = "Neu",
            Title = "Vertrag anlegen",
            SubmitLabel = "Vertrag anlegen",
            Fields =
            [
                Text("provider", "Anbieter", required: true, placeholder: "z. B. Stadtwerke Heidelberg"),
                Choice("name", "Art", required: true,
                [
                    new() { Value = "Strom", Label = "Strom" },
                    new() { Value = "Gas", Label = "Gas" },
                    new() { Value = "Wasser", Label = "Wasser" },
                    new() { Value = "Internet", Label = "Internet" },
                    new() { Value = "Mobilfunk", Label = "Mobilfunk" },
                    new() { Value = "Abfall", Label = "Abfall" },
                    new() { Value = "Wartung", Label = "Wartung" },
                    new() { Value = "Sonstiges", Label = "Sonstiges" },
                ]),
                Text("number", "Vertragsnummer", required: false),
                Money("amount", "Abschlag", required: true, help: "Monatlicher Abschlag."),
                Reference("account", "Bankkonto", required: false, await AccountOptionsAsync(ct)),
                Reference("property", "Immobilie", required: false, properties),
                Number("notice", "Kündigungsfrist in Wochen", required: false),
            ],
        };
    }

    private async Task<CreateResultDto> CreateContractAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var provider = Value(values, "provider")!.Trim();
        var name = Value(values, "name")!.Trim();

        if (ParseMoney(Value(values, "amount")) is not { } amount)
        {
            return Fail("amount", "Abschlag ist kein Betrag");
        }

        if (await db.Contracts.AnyAsync(c => c.Name == name && c.Provider == provider, ct))
        {
            return Fail("provider", $"Ein Vertrag „{name}“ bei „{provider}“ besteht bereits.");
        }

        var contract = new Contract
        {
            Name = name,
            Provider = provider,
            ContractNumber = Value(values, "number")?.Trim(),
            MonthlyAmount = amount,
            AccountId = ParseInt(Value(values, "account")),
            PropertyId = ParseInt(Value(values, "property")),
            NoticePeriodWeeks = ParseInt(Value(values, "notice")) ?? 0,
        };

        db.Contracts.Add(contract);
        await db.SaveChangesAsync(ct);

        return Ok(contract.Id, $"/vertraege/{contract.Id}");
    }

    // ── Budget ─────────────────────────────────────────────────────────────────────────────

    private async Task<CreateFormDto> BudgetFormAsync(CancellationToken ct)
    {
        // Nur Ausgabekategorien: ein Budget für Einnahmen ergibt keinen Sinn.
        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.Direction == CategoryDirection.Expense)
            .OrderBy(c => c.Name)
            .Select(c => new CreateOptionDto { Value = c.Id.ToString(), Label = c.Name })
            .ToListAsync(ct);

        return new CreateFormDto
        {
            Type = CreateObjectType.Budget,
            Kicker = "Neu",
            Title = "Budget anlegen",
            SubmitLabel = "Budget anlegen",
            Fields =
            [
                Reference("category", "Kategorie", required: true, categories),
                Money("amount", "Betrag", required: true),
                Choice("period", "Zeitraum", required: false,
                [
                    new() { Value = nameof(BudgetPeriod.Month), Label = "je Monat" },
                    new() { Value = nameof(BudgetPeriod.Quarter), Label = "je Quartal" },
                    new() { Value = nameof(BudgetPeriod.Year), Label = "je Jahr" },
                ], defaultValue: nameof(BudgetPeriod.Month)),
                Date("validFrom", "Gilt ab", required: false, defaultValue: FirstOfMonth(clock.Today)),
                Choice("warn", "Warnschwelle", required: false,
                [
                    new() { Value = "80", Label = "80 %" },
                    new() { Value = "90", Label = "90 %" },
                    new() { Value = "100", Label = "100 %" },
                ], defaultValue: "90"),
            ],
        };
    }

    private async Task<CreateResultDto> CreateBudgetAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        if (ParseInt(Value(values, "category")) is not { } categoryId)
        {
            return Fail("category", "Kategorie fehlt");
        }

        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
        {
            return Fail("category", "Diese Kategorie gibt es nicht.");
        }

        if (ParseMoney(Value(values, "amount")) is not { } amount)
        {
            return Fail("amount", "Betrag ist kein Betrag");
        }

        // Zwei Budgets auf dieselbe Kategorie hieße zwei Wahrheiten über dasselbe Geld.
        if (await db.Budgets.AnyAsync(b => b.CategoryId == categoryId, ct))
        {
            return Fail("category", $"Budget für {category.Name} besteht bereits");
        }

        var period = Enum.TryParse<BudgetPeriod>(Value(values, "period"), out var parsed)
            ? parsed
            : BudgetPeriod.Month;

        // Intern wird immer je Monat geführt; Quartal und Jahr rechnen herunter.
        var perMonth = period switch
        {
            BudgetPeriod.Quarter => amount / 3m,
            BudgetPeriod.Year => amount / 12m,
            _ => amount,
        };

        var budget = new Budget
        {
            Name = category.Name,
            CategoryId = categoryId,
            PlannedPerMonth = Math.Round(perMonth, 2, MidpointRounding.AwayFromZero),
            Period = period,
            ValidFrom = ParseDate(Value(values, "validFrom")),
            WarnThresholdPercent = ParseInt(Value(values, "warn")) ?? 90,
            SortOrder = await db.Budgets.CountAsync(ct) + 1,
        };

        db.Budgets.Add(budget);
        await db.SaveChangesAsync(ct);

        return Ok(budget.Id, "/budgets");
    }

    // ── Bausteine ──────────────────────────────────────────────────────────────────────────

    private async Task<List<CreateOptionDto>> AccountOptionsAsync(CancellationToken ct)
        => await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new CreateOptionDto { Value = a.Id.ToString(), Label = a.Name, Hint = a.BankName })
            .ToListAsync(ct);

    private static CreateFieldDto Text(string key, string label, bool required, string? placeholder = null)
        => new() { Key = key, Label = label, Kind = CreateFieldKind.Text, Required = required, Placeholder = placeholder };

    private static CreateFieldDto Money(string key, string label, bool required, string? help = null)
        => new() { Key = key, Label = label, Kind = CreateFieldKind.Money, Required = required, Help = help, Placeholder = "0,00" };

    private static CreateFieldDto Number(string key, string label, bool required, string? help = null)
        => new() { Key = key, Label = label, Kind = CreateFieldKind.Number, Required = required, Help = help };

    private static CreateFieldDto Date(string key, string label, bool required, DateOnly? defaultValue = null)
        => new()
        {
            Key = key,
            Label = label,
            Kind = CreateFieldKind.Date,
            Required = required,
            DefaultValue = defaultValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

    private static CreateFieldDto Date(string key, string label, bool required, string? help)
        => new() { Key = key, Label = label, Kind = CreateFieldKind.Date, Required = required, Help = help };

    private static CreateFieldDto Choice(
        string key, string label, bool required, IReadOnlyList<CreateOptionDto> options, string? defaultValue = null)
        => new()
        {
            Key = key,
            Label = label,
            Kind = CreateFieldKind.Choice,
            Required = required,
            Options = options,
            DefaultValue = defaultValue,
        };

    private static CreateFieldDto Reference(
        string key, string label, bool required, IReadOnlyList<CreateOptionDto> options)
        => new()
        {
            Key = key,
            Label = label,
            Kind = CreateFieldKind.Reference,
            Required = required,
            Options = options,
        };

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    private static string? Value(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Betrag aus deutscher Eingabe. Punkt als Tausendertrenner, Komma als Dezimaltrenner —
    /// aber ein einzelner Punkt als Dezimaltrenner wird auch angenommen, weil ihn Tastaturen
    /// mit Ziffernblock liefern.
    /// </summary>
    private static decimal? ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim().Replace("€", string.Empty).Replace(" ", string.Empty).Trim();

        if (cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateOnly? ParseDate(string? text)
        => DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? ParseInt(string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static CreateResultDto Fail(string? fieldKey, string message)
        => new() { Ok = false, FieldKey = fieldKey, Message = message };

    private static CreateResultDto Ok(int id, string route)
        => new() { Ok = true, Id = id, Route = route };
}
