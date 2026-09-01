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
    /// <summary>
    /// Beschreibt das Formular eines Typs, samt der Auswahlwerte aus dem Bestand. Mit
    /// <paramref name="id"/> wird daraus das Bearbeiten-Formular: dieselben Felder, vorbefüllt,
    /// andere Beschriftungen — und der Löschabschnitt.
    /// </summary>
    public async Task<CreateFormDto?> GetFormAsync(
        CreateObjectType type, int? id = null, CancellationToken ct = default)
    {
        var form = await BuildFormAsync(type, ct);
        if (form is null || id is not { } editing)
        {
            return form;
        }

        var values = await ReadValuesAsync(type, editing, ct);
        if (values is null)
        {
            return null;
        }

        var impact = await DeleteImpactAsync(type, editing, ct);

        return form with
        {
            Kicker = "Bearbeiten",
            Title = form.Title.Replace(" anlegen", " bearbeiten"),
            SubmitLabel = "Änderungen speichern",
            Hint = EditHint(type),
            EditingId = editing,
            Values = values,
            DeleteImpact = impact,

            // Ein gepflegter Name wird beim Bearbeiten nicht neu zusammengesetzt — sonst würde
            // aus „Risikoleben“ beim bloßen Öffnen und Speichern „Risikoleben Hannoversche“.
            // Deshalb gibt es die Bezeichnung hier als echtes Feld.
            Fields = NameField(type) is { } name ? [name, .. form.Fields] : form.Fields,
        };
    }

    private async Task<CreateFormDto?> BuildFormAsync(CreateObjectType type, CancellationToken ct)
        => type switch
        {
            CreateObjectType.Account => await AccountFormAsync(ct),
            CreateObjectType.Depot => await DepotFormAsync(ct),
            CreateObjectType.Pension => PensionForm(),
            CreateObjectType.Protection => ProtectionForm(),
            CreateObjectType.Property => await PropertyFormAsync(ct),
            CreateObjectType.Contract => await ContractFormAsync(ct),
            CreateObjectType.Budget => await BudgetFormAsync(ct),
            CreateObjectType.Vehicle => await VehicleFormAsync(ct),
            CreateObjectType.Employment => EmploymentForm(),
            _ => null,
        };

    public async Task<CreateResultDto> CreateAsync(
        CreateObjectType type, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        var form = await BuildFormAsync(type, ct);
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
            CreateObjectType.Vehicle => await CreateVehicleAsync(values, ct),
            CreateObjectType.Employment => await CreateEmploymentAsync(values, ct),
            _ => Fail(null, "Diesen Objekttyp gibt es noch nicht."),
        };
    }

    // ── Bearbeiten und Löschen ──────────────────────────────────────────────

    /// <summary>
    /// Ändert ein vorhandenes Objekt. Geprüft wird gegen dieselbe Feldliste wie beim Anlegen —
    /// bis auf die Bezeichnung, die es nur hier gibt.
    /// </summary>
    public async Task<CreateResultDto> UpdateAsync(
        CreateObjectType type, int id, IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var form = await GetFormAsync(type, id, ct);
        if (form is null)
        {
            return Fail(null, "Diesen Datensatz gibt es nicht (mehr).");
        }

        foreach (var field in form.Fields.Where(f => f.Required))
        {
            if (string.IsNullOrWhiteSpace(Value(values, field.Key)))
            {
                return Fail(field.Key, $"{field.Label} fehlt");
            }
        }

        return type switch
        {
            CreateObjectType.Account => await UpdateAccountAsync(id, values, ct),
            CreateObjectType.Depot => await UpdateDepotAsync(id, values, ct),
            CreateObjectType.Pension => await UpdatePolicyAsync(id, values, capitalForming: true, ct),
            CreateObjectType.Protection => await UpdatePolicyAsync(id, values, capitalForming: false, ct),
            CreateObjectType.Property => await UpdatePropertyAsync(id, values, ct),
            CreateObjectType.Contract => await UpdateContractAsync(id, values, ct),
            CreateObjectType.Budget => await UpdateBudgetAsync(id, values, ct),
            CreateObjectType.Vehicle => await UpdateVehicleAsync(id, values, ct),
            CreateObjectType.Employment => await UpdateEmploymentAsync(id, values, ct),
            _ => Fail(null, "Diesen Objekttyp gibt es noch nicht."),
        };
    }

    /// <summary>Löscht ein Objekt und räumt auf, was daran hängt.</summary>
    public async Task<DeleteResultDto> DeleteAsync(
        CreateObjectType type, int id, CancellationToken ct = default)
        => type switch
        {
            CreateObjectType.Account => await DeleteAccountAsync(id, ct),
            CreateObjectType.Depot => await DeleteSimpleAsync(db.Depots, id, "Depot gelöscht", "/depot", ct),
            CreateObjectType.Pension => await DeleteSimpleAsync(db.Policies, id, "Vertrag gelöscht", "/vorsorge", ct),
            CreateObjectType.Protection => await DeleteSimpleAsync(db.Policies, id, "Vertrag gelöscht", "/absicherung", ct),
            CreateObjectType.Property => await DeleteSimpleAsync(db.Properties, id, "Immobilie gelöscht", "/wohnen", ct),
            CreateObjectType.Contract => await DeleteSimpleAsync(db.Contracts, id, "Vertrag gelöscht", "/wohnen", ct),
            CreateObjectType.Budget => await DeleteSimpleAsync(db.Budgets, id, "Budget gelöscht", "/budgets", ct),
            CreateObjectType.Vehicle => await DeleteSimpleAsync(db.Vehicles, id, "Fahrzeug gelöscht", "/fahrzeuge", ct),
            CreateObjectType.Employment => await DeleteSimpleAsync(
                db.Employments, id, "Arbeitsverhältnis gelöscht", "/arbeit", ct),
            _ => new DeleteResultDto { Ok = false, Message = "Diesen Objekttyp gibt es nicht." },
        };

    private async Task<DeleteResultDto> DeleteSimpleAsync<T>(
        DbSet<T> set, int id, string message, string route, CancellationToken ct)
        where T : class
    {
        var entity = await set.FindAsync([id], ct);
        if (entity is null)
        {
            return new DeleteResultDto { Ok = false, Message = "Der Datensatz ist bereits fort." };
        }

        set.Remove(entity);
        await db.SaveChangesAsync(ct);
        return new DeleteResultDto { Ok = true, Message = message, Route = route };
    }

    private const string OrphanAccountName = "Ohne Konto";

    /// <summary>
    /// Ein Konto zu löschen darf keine Buchungen mitnehmen — sie sind Tatsachen, das Konto war
    /// nur ihre Schublade. Sie werden deshalb auf „Ohne Konto“ umgeschrieben.
    /// </summary>
    private async Task<DeleteResultDto> DeleteAccountAsync(int id, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return new DeleteResultDto { Ok = false, Message = "Das Konto ist bereits fort." };
        }

        var affected = await db.Transactions.CountAsync(t => t.AccountId == id, ct);
        if (affected > 0)
        {
            var orphan = await db.Accounts.FirstOrDefaultAsync(a => a.Name == OrphanAccountName, ct);
            if (orphan is null)
            {
                orphan = new Account
                {
                    Name = OrphanAccountName,
                    ShortName = OrphanAccountName,
                    BankName = "—",
                    Kind = AccountKind.Checking,
                    BalanceAsOf = clock.Today,
                };

                db.Accounts.Add(orphan);
                await db.SaveChangesAsync(ct);
            }

            foreach (var row in await db.Transactions.Where(t => t.AccountId == id).ToListAsync(ct))
            {
                row.AccountId = orphan.Id;
            }
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(ct);

        return new DeleteResultDto
        {
            Ok = true,
            Route = "/konten",
            Message = affected == 0
                ? "Konto gelöscht"
                : $"Konto gelöscht · {Plural(affected)} auf „{OrphanAccountName}“ gesetzt",
        };
    }

    /// <summary>
    /// Die Bezeichnung gibt es nur im Bearbeiten-Formular. Beim Anlegen wird sie abgeleitet, beim
    /// Bearbeiten nie wieder — sonst überschriebe ein bloßes Öffnen und Speichern einen
    /// gepflegten Namen: aus „Risikoleben“ würde „Risikoleben Hannoversche“.
    /// </summary>
    private static CreateFieldDto? NameField(CreateObjectType type) => type switch
    {
        // Nur wo die Bezeichnung beim Anlegen abgeleitet wurde. Immobilie, Fahrzeug und Vertrag
        // tragen sie ohnehin als eigenes Feld; ein Budget heißt wie seine Kategorie, ein
        // eigener Name wäre dort eine zweite Wahrheit.
        CreateObjectType.Account or CreateObjectType.Depot
            or CreateObjectType.Pension or CreateObjectType.Protection
            => Text("displayName", "Bezeichnung", required: true),
        _ => null,
    };

    /// <summary>Der Einleitungstext sagt im Bearbeiten-Modus, was die Änderung bewirkt.</summary>
    private static string EditHint(CreateObjectType type) => type switch
    {
        CreateObjectType.Account => "Ein neuer Startsaldo gilt ab seinem Stichtag; Buchungen davor bleiben unberührt.",
        CreateObjectType.Depot => "Der angegebene Wert zählt nur, solange keine Positionen erfasst sind.",
        CreateObjectType.Pension => "Ein neuer Wert mit Stichtag ersetzt den bisherigen im Vermögen.",
        CreateObjectType.Protection => "Beitrag und Frist gelten ab sofort; gebuchte Beiträge bleiben, wie sie sind.",
        CreateObjectType.Property => "Das verknüpfte Darlehen bleibt unverändert.",
        CreateObjectType.Contract => "Erfasste Rechnungen bleiben erhalten.",
        CreateObjectType.Budget => "Die bisher verbrauchte Summe bleibt erhalten.",
        CreateObjectType.Vehicle => "Die verknüpfte Versicherung bleibt unverändert unter Absicherung.",
        CreateObjectType.Employment => "Ein neues Gehalt gilt ab sofort; erfasste Abrechnungen bleiben, wie sie sind.",
        _ => "Änderungen gelten ab sofort.",
    };

    /// <summary>
    /// Was das Löschen nach sich zieht — mit <b>gezählten</b> Bezügen, nicht mit behaupteten.
    /// Ein Satz wie „Sind noch Buchungen verknüpft?“ ohne nachzusehen klingt nach Sorgfalt und
    /// ist keine.
    /// </summary>
    private async Task<DeleteImpactDto?> DeleteImpactAsync(
        CreateObjectType type, int id, CancellationToken ct)
    {
        switch (type)
        {
            case CreateObjectType.Account:
            {
                var bookings = await db.Transactions.CountAsync(t => t.AccountId == id, ct);
                return Impact("Konto löschen", bookings == 0
                    ? "An diesem Konto hängt keine Buchung."
                    : $"{Plural(bookings)} hängen an diesem Konto — sie bleiben erhalten und werden auf "
                      + $"„{OrphanAccountName}“ gesetzt.");
            }

            case CreateObjectType.Depot:
            {
                var positions = await db.PortfolioPositions.CountAsync(x => x.DepotId == id, ct);
                return Impact("Depot löschen",
                    "Das Depot verschwindet aus dem Vermögen"
                    + (positions == 0 ? string.Empty : $" samt seinen {positions} Positionen")
                    + ". Buchungen auf dem Verrechnungskonto bleiben unberührt.");
            }

            case CreateObjectType.Pension:
                return Impact("Vertrag löschen",
                    "Sein Wert zählt nicht mehr ins Vermögen. Statusberichte bleiben in den Dokumenten.");

            case CreateObjectType.Protection:
            {
                var vehicles = await db.Vehicles.CountAsync(v => v.PolicyId == id, ct);
                return Impact("Vertrag löschen",
                    "Vertrag und Frist entfallen, gebuchte Beiträge bleiben."
                    + (vehicles == 0
                        ? string.Empty
                        : $" {vehicles} {(vehicles == 1 ? "Fahrzeug verliert" : "Fahrzeuge verlieren")} die Verknüpfung."));
            }

            case CreateObjectType.Property:
            {
                var contracts = await db.Contracts.CountAsync(c => c.PropertyId == id, ct);
                var hasLoan = await db.Properties.AnyAsync(x => x.Id == id && x.LoanId != null, ct);
                return Impact("Immobilie löschen",
                    "Das Objekt entfällt."
                    + (hasLoan ? " Das verknüpfte Darlehen bleibt und steht weiter unter Darlehen." : string.Empty)
                    + (contracts == 0
                        ? string.Empty
                        : $" {contracts} {(contracts == 1 ? "Vertrag verliert" : "Verträge verlieren")} die Zuordnung."));
            }

            case CreateObjectType.Contract:
            {
                var invoices = await db.Invoices.CountAsync(x => x.ContractId == id, ct);
                return Impact("Vertrag löschen",
                    "Der Vertrag entfällt."
                    + (invoices == 0
                        ? " Es hängt keine Rechnung daran."
                        : $" {invoices} erfasste {(invoices == 1 ? "Rechnung bleibt" : "Rechnungen bleiben")} erhalten, ebenso die Buchungen."));
            }

            case CreateObjectType.Budget:
            {
                var categoryId = await db.Budgets.AsNoTracking()
                    .Where(x => x.Id == id).Select(x => x.CategoryId).FirstOrDefaultAsync(ct);
                var booked = await db.Transactions.CountAsync(t => t.CategoryId == categoryId, ct);
                return Impact("Budget löschen",
                    $"Nur die Planung entfällt. {Plural(booked)} in dieser Kategorie bleiben.");
            }

            case CreateObjectType.Vehicle:
                return Impact("Fahrzeug löschen",
                    "Die Kostenübersicht entfällt. Versicherung und Dokumente bleiben.");

            case CreateObjectType.Employment:
            {
                var payslips = await db.Payslips.CountAsync(x => x.EmploymentId == id, ct);
                var agreements = await db.WorkAgreements.CountAsync(x => x.EmploymentId == id, ct);

                return Impact("Arbeitsverhältnis löschen",
                    "Das Arbeitsverhältnis entfällt."
                    + (payslips == 0
                        ? " Es ist keine Abrechnung erfasst."
                        : $" {payslips} erfasste {(payslips == 1 ? "Lohnabrechnung bleibt" : "Lohnabrechnungen bleiben")} "
                          + "samt Belegen erhalten.")
                    + (agreements == 0
                        ? string.Empty
                        : $" {agreements} {(agreements == 1 ? "Vereinbarung entfällt" : "Vereinbarungen entfallen")} mit."));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Die Rohfelder eines Objekts, so wie das Formular sie erwartet.
    /// </summary>
    /// <remarks>
    /// Gelesen wird aus den gespeicherten Feldern, <b>nie</b> aus einer Anzeigezeile. Ein
    /// Vertragsname wie „Risikoleben“ trägt keinen Versicherer im Namen — wer ihn dort
    /// herausparsen wollte, ließe das Pflichtfeld leer und das Formular unbenutzbar.
    /// </remarks>
    private async Task<Dictionary<string, string?>?> ReadValuesAsync(
        CreateObjectType type, int id, CancellationToken ct)
    {
        switch (type)
        {
            case CreateObjectType.Account:
            {
                var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                return a is null ? null : new()
                {
                    ["displayName"] = a.Name,
                    ["kind"] = a.Kind == AccountKind.Savings ? "savings" : "checking",
                    ["bank"] = a.BankName,
                    ["iban"] = a.Iban,
                    ["opening"] = Money(a.OpeningBalance),
                    ["asOf"] = Iso(a.BalanceAsOf),
                };
            }

            case CreateObjectType.Depot:
            {
                var d = await db.Depots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                return d is null ? null : new()
                {
                    ["displayName"] = d.Name,
                    ["broker"] = d.Broker,
                    ["number"] = d.Number,
                    ["depotKind"] = d.DepotKind,
                    ["value"] = d.StatedValue is { } v ? Money(v) : null,
                    ["asOf"] = Iso(d.ValuationDate),
                    ["account"] = d.AccountId?.ToString(),
                    ["quotes"] = d.QuoteSource,
                };
            }

            case CreateObjectType.Pension:
            case CreateObjectType.Protection:
            {
                var x = await db.Policies.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
                if (x is null || x.IsCapitalForming != (type == CreateObjectType.Pension))
                {
                    return null;
                }

                return new()
                {
                    ["displayName"] = x.Name,
                    ["kind"] = x.Kind.ToString(),
                    ["provider"] = x.Provider,
                    ["number"] = x.PolicyNumber,
                    ["premium"] = x.Premium == 0m ? null : Money(x.Premium),
                    ["interval"] = x.PremiumInterval.ToString(),

                    // Die Bestandteile gehören in die Maske zurück. Ohne sie stünde die
                    // Bearbeitung leer da, der Anwender speicherte, und aus zwei erfassten
                    // Zahlen würde wieder eine einzelne — Abschnitt 19.4.
                    ["baseValue"] = x.BaseValue is { } basis ? Money(basis) : null,
                    ["accruedBonus"] = x.AccruedBonus is { } bonus ? Money(bonus) : null,

                    ["value"] = x.CurrentValue is { } value ? Money(value) : null,
                    ["asOf"] = Iso(x.ValuationDate),
                    ["maturesOn"] = Iso(x.MaturesOn),
                    ["endsOn"] = Iso(x.EndsOn),
                    ["notice"] = x.NoticePeriodMonths == 0 ? null : x.NoticePeriodMonths.ToString(),
                };
            }

            case CreateObjectType.Property:
            {
                var x = await db.Properties.AsNoTracking()
                    .Include(y => y.Shares)
                    .FirstOrDefaultAsync(y => y.Id == id, ct);

                if (x is null)
                {
                    return null;
                }

                var werte = new Dictionary<string, string?>
                {
                    ["name"] = x.Name,
                    ["address"] = x.Address,
                    ["kind"] = x.Kind.ToString(),
                    ["purchase"] = Iso(x.PurchaseDate),
                    ["market"] = x.MarketValue == 0m ? null : Money(x.MarketValue),
                    ["loan"] = x.LoanId?.ToString(),
                };

                // Die gepflegten Anteile zurück in die Maske: wer nur den Marktwert korrigiert,
                // darf die Beteiligung dabei nicht verlieren.
                foreach (var anteil in x.Shares)
                {
                    werte[$"share.{anteil.UserId}"] = Hours(anteil.Percent);
                    werte[$"equity.{anteil.UserId}"] = anteil.Equity == 0m ? null : Money(anteil.Equity);
                }

                return werte;
            }

            case CreateObjectType.Contract:
            {
                var x = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
                return x is null ? null : new()
                {
                    ["name"] = x.Name,
                    ["provider"] = x.Provider,
                    ["number"] = x.ContractNumber,
                    ["amount"] = Money(x.MonthlyAmount),
                    ["account"] = x.AccountId?.ToString(),
                    ["property"] = x.PropertyId?.ToString(),
                    ["objekt"] = x.PropertyRelated ? FlagOn : FlagOff,
                    ["notice"] = x.NoticePeriodWeeks == 0 ? null : x.NoticePeriodWeeks.ToString(),
                };
            }

            case CreateObjectType.Budget:
            {
                var x = await db.Budgets.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
                if (x is null)
                {
                    return null;
                }

                // Der Plan liegt je Monat; das Formular zeigt ihn im gewählten Zeitraum.
                var shown = x.Period switch
                {
                    PeriodScope.Quarter => x.PlannedPerMonth * 3m,
                    PeriodScope.Year => x.PlannedPerMonth * 12m,
                    _ => x.PlannedPerMonth,
                };

                return new()
                {
                    ["category"] = x.CategoryId.ToString(),
                    ["amount"] = Money(shown),
                    ["period"] = x.Period.ToString(),
                    ["validFrom"] = Iso(x.ValidFrom),
                    ["warn"] = x.WarnThresholdPercent.ToString(),
                };
            }

            case CreateObjectType.Employment:
            {
                var x = await db.Employments.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
                return x is null ? null : new()
                {
                    ["employer"] = x.Employer,
                    ["position"] = x.Position,
                    ["kind"] = x.Kind.ToString(),
                    ["start"] = Iso(x.StartsOn),
                    ["end"] = Iso(x.EndsOn),
                    ["hours"] = Hours(x.HoursPerWeek),
                    ["gross"] = Money(x.GrossMonthly),
                    ["net"] = x.NetMonthly is { } netto ? Money(netto) : null,
                    ["notice"] = x.NoticePeriodMonths == 0 ? null : x.NoticePeriodMonths.ToString(),
                    ["commuteKm"] = Hours(x.CommuteKilometres),
                    ["workDays"] = x.WorkDaysPerYear?.ToString(),
                };
            }

            case CreateObjectType.Vehicle:
            {
                var x = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(y => y.Id == id, ct);
                return x is null ? null : new()
                {
                    ["name"] = x.Name,
                    ["plate"] = x.Plate,
                    ["usage"] = x.Usage,
                    ["first"] = Iso(x.FirstRegistration),
                    ["mileage"] = x.Mileage?.ToString(),
                    ["policy"] = x.PolicyId?.ToString(),
                };
            }

            default:
                return null;
        }
    }

    // ── Ändern je Typ ───────────────────────────────────────────────────────

    private async Task<CreateResultDto> UpdateAccountAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null)
        {
            return Fail(null, "Das Konto gibt es nicht mehr.");
        }

        if (ParseMoney(Value(values, "opening")) is not { } opening)
        {
            return Fail("opening", "Startsaldo ist kein Betrag");
        }

        if (ParseDate(Value(values, "asOf")) is not { } asOf)
        {
            return Fail("asOf", "Stichtag ist kein Datum");
        }

        var name = Value(values, "displayName")!.Trim();
        if (await db.Accounts.AnyAsync(a => a.Id != id && a.Name == name, ct))
        {
            return Fail("displayName", $"Ein Konto „{name}“ besteht bereits.");
        }

        // Der Name wird übernommen, nicht neu gebildet — sonst überschriebe jedes Speichern
        // eine gepflegte Bezeichnung.
        account.Name = name;
        account.ShortName = Value(values, "bank")!.Trim();
        account.BankName = Value(values, "bank")!.Trim();
        account.Kind = Value(values, "kind") == "savings" ? AccountKind.Savings : AccountKind.Checking;
        account.Iban = Value(values, "iban")?.Trim();
        account.OpeningBalance = opening;
        account.BalanceAsOf = asOf;

        await db.SaveChangesAsync(ct);
        return Ok(id, "/konten");
    }

    private async Task<CreateResultDto> UpdateDepotAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var depot = await db.Depots.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (depot is null)
        {
            return Fail(null, "Das Depot gibt es nicht mehr.");
        }

        if (ParseMoney(Value(values, "value")) is not { } value)
        {
            return Fail("value", "Depotwert ist kein Betrag");
        }

        if (ParseDate(Value(values, "asOf")) is not { } asOf)
        {
            return Fail("asOf", "Stichtag ist kein Datum");
        }

        depot.Name = Value(values, "displayName")!.Trim();
        depot.Broker = Value(values, "broker")!.Trim();
        depot.Number = Value(values, "number")?.Trim();
        depot.DepotKind = Value(values, "depotKind")?.Trim();
        depot.StatedValue = value;
        depot.ValuationDate = asOf;
        depot.AccountId = ParseInt(Value(values, "account"));
        depot.QuoteSource = Value(values, "quotes")?.Trim();

        await db.SaveChangesAsync(ct);
        return Ok(id, "/depot");
    }

    private async Task<CreateResultDto> UpdatePolicyAsync(
        int id, IReadOnlyDictionary<string, string?> values, bool capitalForming, CancellationToken ct)
    {
        var policy = await db.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null || policy.IsCapitalForming != capitalForming)
        {
            return Fail(null, "Den Vertrag gibt es nicht mehr.");
        }

        if (!Enum.TryParse<PolicyKind>(Value(values, "kind"), out var kind))
        {
            return Fail("kind", capitalForming ? "Vertragsart fehlt" : "Art fehlt");
        }

        policy.Name = Value(values, "displayName")!.Trim();
        policy.Kind = kind;
        policy.Provider = Value(values, "provider")!.Trim();
        policy.PolicyNumber = Value(values, "number")?.Trim();
        policy.Premium = ParseMoney(Value(values, "premium")) ?? 0m;

        if (capitalForming)
        {
            if (CapitalValue(policy, values) is { } problem)
            {
                return problem;
            }

            await RecordAsync(policy, ct);
        }
        else
        {
            policy.PremiumInterval = Enum.TryParse<PremiumInterval>(Value(values, "interval"), out var interval)
                ? interval
                : PremiumInterval.Yearly;
            policy.EndsOn = ParseDate(Value(values, "endsOn"));
            policy.NoticePeriodMonths = ParseInt(Value(values, "notice")) ?? 0;
        }

        await db.SaveChangesAsync(ct);
        return Ok(id, $"/police/{id}");
    }

    private async Task<CreateResultDto> UpdatePropertyAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var property = await db.Properties
            .Include(p => p.Shares)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (property is null)
        {
            return Fail(null, "Die Immobilie gibt es nicht mehr.");
        }

        if (ParseDate(Value(values, "purchase")) is not { } purchase)
        {
            return Fail("purchase", "Kauf ist kein Datum");
        }

        property.Name = Value(values, "name")!.Trim();
        property.Address = Value(values, "address")?.Trim();
        property.Kind = Enum.TryParse<PropertyKind>(Value(values, "kind"), out var kind) ? kind : property.Kind;
        property.PurchaseDate = purchase;
        property.MarketValue = ParseMoney(Value(values, "market")) ?? 0m;
        property.LoanId = ParseInt(Value(values, "loan"));

        if (await ApplySharesAsync(property, values, ct) is { } problem)
        {
            return problem;
        }

        await db.SaveChangesAsync(ct);
        return Ok(id, $"/wohnen/{id}");
    }

    private async Task<CreateResultDto> UpdateContractAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contract is null)
        {
            return Fail(null, "Den Vertrag gibt es nicht mehr.");
        }

        if (ParseMoney(Value(values, "amount")) is not { } amount)
        {
            return Fail("amount", "Abschlag ist kein Betrag");
        }

        contract.Name = Value(values, "name")!.Trim();
        contract.Provider = Value(values, "provider")!.Trim();
        contract.ContractNumber = Value(values, "number")?.Trim();
        contract.MonthlyAmount = amount;
        contract.AccountId = ParseInt(Value(values, "account"));
        contract.PropertyId = ParseInt(Value(values, "property"));
        contract.PropertyRelated = Flagged(values, "objekt", fallback: contract.PropertyRelated);
        contract.NoticePeriodWeeks = ParseInt(Value(values, "notice")) ?? 0;

        await db.SaveChangesAsync(ct);
        return Ok(id, $"/vertraege/{id}");
    }

    private async Task<CreateResultDto> UpdateBudgetAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (budget is null)
        {
            return Fail(null, "Das Budget gibt es nicht mehr.");
        }

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

        if (await db.Budgets.AnyAsync(b => b.Id != id && b.CategoryId == categoryId, ct))
        {
            return Fail("category", $"Budget für {category.Name} besteht bereits");
        }

        var period = Enum.TryParse<PeriodScope>(Value(values, "period"), out var parsed)
            ? parsed
            : PeriodScope.Month;

        budget.Name = category.Name;
        budget.CategoryId = categoryId;
        budget.Period = period;
        budget.PlannedPerMonth = Math.Round(period switch
        {
            PeriodScope.Quarter => amount / 3m,
            PeriodScope.Year => amount / 12m,
            _ => amount,
        }, 2, MidpointRounding.AwayFromZero);
        budget.ValidFrom = ParseDate(Value(values, "validFrom"));
        budget.WarnThresholdPercent = ParseInt(Value(values, "warn")) ?? budget.WarnThresholdPercent;

        await db.SaveChangesAsync(ct);
        return Ok(id, "/budgets");
    }

    private async Task<CreateResultDto> UpdateVehicleAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null)
        {
            return Fail(null, "Das Fahrzeug gibt es nicht mehr.");
        }

        var plate = Value(values, "plate")!.Trim();
        if (await db.Vehicles.AnyAsync(v => v.Id != id && v.Plate == plate, ct))
        {
            return Fail("plate", $"Ein Fahrzeug mit Kennzeichen „{plate}“ besteht bereits.");
        }

        vehicle.Name = Value(values, "name")!.Trim();
        vehicle.Plate = plate;
        vehicle.Usage = Value(values, "usage")?.Trim();
        vehicle.FirstRegistration = ParseDate(Value(values, "first"));
        vehicle.Mileage = ParseInt(Value(values, "mileage"));
        vehicle.PolicyId = ParseInt(Value(values, "policy"));

        await db.SaveChangesAsync(ct);
        return Ok(id, $"/fahrzeuge/{id}");
    }

    private static string Plural(int count) => count == 1 ? "1 Buchung" : $"{count} Buchungen";

    /// <summary>Betrag im Eingabeformat des Formulars — deutsch, ohne Tausendertrenner.</summary>
    private static string Money(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string? Iso(DateOnly? value)
        => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DeleteImpactDto Impact(string title, string consequence)
        => new() { Title = title, ActionLabel = title, Consequence = consequence };

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
            documentDate: null, ct: ct);

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

            // Die beiden Bestandteile des erreichten Werts — Abschnitt 19.5. Ist einer gefüllt,
            // rechnet die Anwendung die Summe und schreibt sie ins Wertfeld. Die Bezeichnung
            // hängt an der Vertragsart und steht im Hinweis; ein Bausparvertrag hat keinen
            // Rückkaufswert und kein Ansammlungsguthaben.
            Money("baseValue", "Rückkaufswert / Deckungskapital / Sparguthaben", required: false,
                help: "Je nach Vertragsart. Zusammen mit dem Ansammlungsguthaben ergibt er den "
                      + "erreichten Wert."),
            Money("accruedBonus", "Ansammlungsguthaben", required: false,
                help: "Der erreichte Wert der Überschussbeteiligung. Bausparen und Riester "
                      + "führen keines — dann leer lassen."),

            // Nicht als Pflichtfeld beschrieben, aber Pflicht: sind die Bestandteile gefüllt,
            // entsteht er aus ihnen. Die allgemeine Pflichtprüfung kennt nur „gefüllt oder
            // nicht“ und wiese sonst eine vollständige Eingabe ab; die Bedingung „eines von
            // beidem“ prüft CapitalValue und benennt sie im Klartext.
            Money("value", "Erreichter Wert", required: false,
                help: "Zählt so ins Vermögen. Sind die Bestandteile darüber gefüllt, ist er "
                      + "ihre Summe — sonst trage ihn hier ein. Bewertungsreserven und "
                      + "Schlussüberschüsse gehören nicht dazu; der Bericht weist sie als "
                      + "nicht garantiert aus."),
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
            if (CapitalValue(policy, values) is { } problem)
            {
                return problem;
            }

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

        if (capitalForming)
        {
            await RecordAsync(policy, ct);
            await db.SaveChangesAsync(ct);
        }

        return Ok(policy.Id, $"/police/{policy.Id}");
    }

    /// <summary>
    /// Wert, Bestandteile und Stichtag eines wertbildenden Vertrags.
    /// </summary>
    /// <remarks>
    /// <para>Die Bestandteile zuerst: sind sie gefüllt, rechnet die Anwendung die Summe und
    /// schreibt sie ins Wertfeld — Abschnitt 19.5. Eine Kopfzahl, die neben ihrer eigenen Summe
    /// steht und ihr widerspricht, wäre die schlimmere Variante.</para>
    /// <para>Anlegen und Ändern teilen sich diese Stelle; zweimal gerechnet liefen sie
    /// irgendwann auseinander, und die Maske nähme beim Anlegen Zahlen an, aus denen sie nichts
    /// macht.</para>
    /// </remarks>
    private static CreateResultDto? CapitalValue(
        Policy policy, IReadOnlyDictionary<string, string?> values)
    {
        policy.BaseValue = ParseMoney(Value(values, "baseValue"));
        policy.AccruedBonus = ParseMoney(Value(values, "accruedBonus"));

        var summe = policy.BaseValue is null && policy.AccruedBonus is null
            ? (decimal?)null
            : (policy.BaseValue ?? 0m) + (policy.AccruedBonus ?? 0m);

        if ((summe ?? ParseMoney(Value(values, "value"))) is not { } value)
        {
            return Fail("value", "Erreichter Wert fehlt — trage ihn ein oder seine Bestandteile darüber.");
        }

        if (ParseDate(Value(values, "asOf")) is not { } asOf)
        {
            return Fail("asOf", "Stichtag ist kein Datum");
        }

        policy.CurrentValue = value;
        policy.ValuationDate = asOf;
        policy.MaturesOn = ParseDate(Value(values, "maturesOn"));
        return null;
    }

    /// <summary>
    /// Schreibt den erfassten Stand in die Berichtsreihe des Vertrags.
    /// </summary>
    /// <remarks>
    /// Auch von Hand gepflegte Stände sind Stände. Ohne diesen Eintrag überschriebe jede
    /// Bearbeitung den vorigen Wert spurlos, und ein Vertrag, den niemand einliest, bekäme nie
    /// einen Verlauf — obwohl jemand ihn Jahr für Jahr gepflegt hat.
    /// </remarks>
    private Task RecordAsync(Policy policy, CancellationToken ct)
        => (policy.CurrentValue, policy.ValuationDate) is ({ } wert, { } stichtag)
            ? PolicyService.RecordReportAsync(
                db, clock, policy.Id, stichtag, wert, "erfasst", ct,
                policy.BaseValue, policy.AccruedBonus)
            : Task.CompletedTask;

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

                .. await ShareFieldsAsync(ct),
            ],
        };
    }

    /// <summary>
    /// Die Beteiligung am Objekt: je Person Eigentumsanteil und eingebrachtes Eigenkapital.
    /// </summary>
    /// <remarks>
    /// <para>Nur bei mehr als einer schreibberechtigten Person im Haushalt — allein besitzt man
    /// nichts zu Anteilen, und zwei leere Felder wären dort nur Ballast.</para>
    /// <para>Alle Anteile leer heißt: das Objekt gehört dem Haushalt, und der ganze Wert zählt.
    /// Stehen Anteile, müssen sie 100 % ergeben; sonst rechnete das Vermögen mit einer
    /// Teilsumme, ohne dass es jemand merkt.</para>
    /// </remarks>
    private async Task<List<CreateFieldDto>> ShareFieldsAsync(CancellationToken ct)
    {
        var personen = await ShareUsersAsync(ct);

        if (personen.Count < 2)
        {
            return [];
        }

        var felder = new List<CreateFieldDto>();

        foreach (var person in personen)
        {
            felder.Add(Number($"share.{person.Id}", $"Eigentumsanteil {person.Name}", required: false,
                help: "in Prozent — die Anteile eines Objekts ergeben zusammen 100"));

            felder.Add(Money($"equity.{person.Id}", $"Eigenkapital beim Kauf {person.Name}",
                required: false,
                help: "einmalig eingebracht. Daraus entsteht der Ausgleichsstand."));
        }

        return felder;
    }

    /// <summary>Wer als Beteiligter in Frage kommt.</summary>
    /// <remarks>
    /// Nur Eigentümer und Mitglieder: ein Lesezugriff — das Steuerbüro — besitzt keine
    /// Immobilie mit.
    /// </remarks>
    private async Task<List<User>> ShareUsersAsync(CancellationToken ct)
        => await db.Users.AsNoTracking()
            .Where(u => u.Role == HouseholdRole.Owner || u.Role == HouseholdRole.Member)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

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

        if (await ApplySharesAsync(property, values, ct) is { } problem)
        {
            return problem;
        }

        db.Properties.Add(property);
        await db.SaveChangesAsync(ct);

        return Ok(property.Id, $"/wohnen/{property.Id}");
    }

    /// <summary>
    /// Übernimmt die Anteile am Objekt und prüft ihre Summe.
    /// </summary>
    /// <remarks>
    /// <para><b>Entweder ganz oder gar nicht.</b> Stehen Anteile, müssen sie 100 % ergeben — die
    /// Anwendung speichert sonst nicht. Mit 90 % gerechnet fehlte ein Zehntel des Objekts in
    /// jeder Vermögenssumme, und niemand sähe es der Zahl an.</para>
    /// <para>Eigenkapital ohne Anteil wird abgewiesen: es gehört zu einem Anteil, sonst lässt
    /// sich kein Ausgleich daraus rechnen.</para>
    /// </remarks>
    private async Task<CreateResultDto?> ApplySharesAsync(
        Property property, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var personen = await ShareUsersAsync(ct);

        if (personen.Count < 2)
        {
            return null;
        }

        var anteile = new List<PropertyShare>();

        foreach (var person in personen)
        {
            var prozent = ParseMoney(Value(values, $"share.{person.Id}"));
            var eigenkapital = ParseMoney(Value(values, $"equity.{person.Id}")) ?? 0m;

            if (prozent is null && eigenkapital > 0m)
            {
                return Fail($"share.{person.Id}",
                    $"Ohne Eigentumsanteil lässt sich das Eigenkapital von {person.Name} nicht zuordnen.");
            }

            if (prozent is not { } wert || wert <= 0m)
            {
                continue;
            }

            if (wert > 100m)
            {
                return Fail($"share.{person.Id}", "Ein Anteil über 100 % gibt es nicht.");
            }

            anteile.Add(new PropertyShare
            {
                UserId = person.Id,
                Percent = wert,
                Equity = eigenkapital,
            });
        }

        if (anteile.Count == 0)
        {
            property.Shares.Clear();
            return null;
        }

        var summe = anteile.Sum(a => a.Percent);

        if (summe != 100m)
        {
            return Fail($"share.{anteile[0].UserId}",
                $"Die Eigentumsanteile ergeben {Hours(summe)} % statt 100 %.");
        }

        property.Shares.Clear();
        property.Shares.AddRange(anteile);
        return null;
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
                Flag("objekt", "Kosten zählen", "objektbezogen", "Lebenshaltung",
                    help: "Objektbezogen zählt in die Objektkosten und in €/m². "
                          + "Der Internetanschluss hängt am Haus und zieht doch mit um.",
                    defaultOn: true),
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
            PropertyRelated = Flagged(values, "objekt", fallback: true),
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
                    new() { Value = nameof(PeriodScope.Month), Label = "je Monat" },
                    new() { Value = nameof(PeriodScope.Quarter), Label = "je Quartal" },
                    new() { Value = nameof(PeriodScope.Year), Label = "je Jahr" },
                ], defaultValue: nameof(PeriodScope.Month)),
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

        var period = Enum.TryParse<PeriodScope>(Value(values, "period"), out var parsed)
            ? parsed
            : PeriodScope.Month;

        // Intern wird immer je Monat geführt; Quartal und Jahr rechnen herunter.
        var perMonth = period switch
        {
            PeriodScope.Quarter => amount / 3m,
            PeriodScope.Year => amount / 12m,
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

    // ── Fahrzeug ───────────────────────────────────────────────────────────

    private async Task<CreateFormDto> VehicleFormAsync(CancellationToken ct)
    {
        // Nur Kfz-Verträge zur Auswahl — eine Hausratversicherung gehört nicht ans Auto.
        var policies = await db.Policies.AsNoTracking()
            .Where(p => p.Kind == PolicyKind.Vehicle)
            .OrderBy(p => p.Name)
            .Select(p => new CreateOptionDto { Value = p.Id.ToString(), Label = p.Name, Hint = p.Provider })
            .ToListAsync(ct);

        return new CreateFormDto
        {
            Type = CreateObjectType.Vehicle,
            Kicker = "Neu",
            Title = "Fahrzeug anlegen",
            SubmitLabel = "Fahrzeug anlegen",
            Hint = "Die Kfz-Versicherung wird verknüpft, nicht kopiert — sie bleibt unter Absicherung.",
            Fields =
            [
                Text("name", "Bezeichnung", required: true, placeholder: "z. B. VW Passat Variant"),
                Text("plate", "Kennzeichen", required: true, placeholder: "L-2905"),
                Choice("usage", "Typ", required: false,
                [
                    new() { Value = "Erstwagen", Label = "Erstwagen" },
                    new() { Value = "Zweitwagen", Label = "Zweitwagen" },
                    new() { Value = "Dienstwagen", Label = "Dienstwagen" },
                ]),
                Date("first", "Erstzulassung", required: false),
                Number("mileage", "Kilometerstand", required: false),
                Reference("policy", "Versicherung verknüpfen", required: false, policies),
            ],
        };
    }

    private async Task<CreateResultDto> CreateVehicleAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var name = Value(values, "name")!.Trim();
        var plate = Value(values, "plate")!.Trim();

        if (await db.Vehicles.AnyAsync(v => v.Plate == plate, ct))
        {
            return Fail("plate", $"Ein Fahrzeug mit Kennzeichen „{plate}“ besteht bereits.");
        }

        var vehicle = new Vehicle
        {
            Name = name,
            Plate = plate,
            Usage = Value(values, "usage")?.Trim(),
            FirstRegistration = ParseDate(Value(values, "first")),
            Mileage = ParseInt(Value(values, "mileage")),
            PolicyId = ParseInt(Value(values, "policy")),
        };

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        return Ok(vehicle.Id, $"/fahrzeuge/{vehicle.Id}");
    }

    // ── Arbeit & Beruf ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Das Arbeitsverhältnis. Vier Pflichtfelder, der Rest darf fehlen.
    /// </summary>
    /// <remarks>
    /// Das Nettogehalt ist ausdrücklich freiwillig: fehlt es, schätzt die Anzeige und sagt, dass
    /// sie schätzt. Es zum Pflichtfeld zu machen hieße, eine Zahl zu erzwingen, die auf dem
    /// Zettel steht, den man gerade nicht hat.
    /// </remarks>
    private static CreateFormDto EmploymentForm() => new()
    {
        Type = CreateObjectType.Employment,
        Kicker = "Neu",
        Title = "Arbeitsverhältnis anlegen",
        SubmitLabel = "Arbeitsverhältnis anlegen",
        Hint = "Die Lohnzahlung bleibt die Buchung auf dem Konto — sie wird hier nur zugeordnet, "
               + "nicht noch einmal gebucht.",
        Fields =
        [
            Text("employer", "Arbeitgeber", required: true, placeholder: "z. B. Nordlicht Systeme"),
            Text("position", "Position", required: false, placeholder: "z. B. Entwicklerin"),
            Choice("kind", "Beschäftigungsart", required: false,
            [
                new() { Value = nameof(EmploymentKind.Permanent), Label = "unbefristet" },
                new() { Value = nameof(EmploymentKind.FixedTerm), Label = "befristet" },
                new() { Value = nameof(EmploymentKind.PartTime), Label = "Teilzeit" },
                new() { Value = nameof(EmploymentKind.Freelance), Label = "Werkvertrag" },
            ], defaultValue: nameof(EmploymentKind.Permanent)),
            Date("start", "Vertragsbeginn", required: true),

            // Ohne Enddatum bliebe jedes Verhältnis für immer laufend — und eine Jahreslast,
            // die es nicht mehr gibt, stünde weiter in jeder Summe.
            Date("end", "Vertragsende", required: false, help: "Leer lassen, solange es läuft."),
            Number("hours", "Arbeitszeit pro Woche", required: false, help: "in Stunden"),
            Money("gross", "Bruttogehalt monatlich", required: true),
            Money("net", "Nettogehalt monatlich", required: false,
                help: "Ohne Angabe wird geschätzt und als Schätzung ausgewiesen."),
            Number("notice", "Kündigungsfrist", required: false, help: "in Monaten"),

            // Abschnitt 18.3: beide standen bisher nur im Modell und in den Demodaten. Wer den
            // Arbeitsweg nachtrug, kam an sie nicht heran — und die Entfernungspauschale im
            // Steuerjahr blieb ohne Grund leer.
            Number("commuteKm", "Entfernung zur Arbeit", required: false,
                help: "einfache Strecke in Kilometern"),
            Number("workDays", "Arbeitstage im Jahr", required: false,
                help: "Zusammen mit der Entfernung die Grundlage der Pauschale — "
                      + "ohne beides entsteht sie nicht."),
        ],
    };

    private async Task<CreateResultDto> CreateEmploymentAsync(
        IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        if (ParseMoney(Value(values, "gross")) is not { } gross || gross <= 0m)
        {
            return Fail("gross", "Das Bruttogehalt fehlt.");
        }

        if (ParseDate(Value(values, "start")) is not { } start)
        {
            return Fail("start", "Der Vertragsbeginn fehlt.");
        }

        var employment = new Employment { Employer = Value(values, "employer")!.Trim(), StartsOn = start };

        if (Apply(employment, values, gross) is { } problem)
        {
            return problem;
        }

        db.Employments.Add(employment);
        await db.SaveChangesAsync(ct);

        return Ok(employment.Id, "/arbeit");
    }

    private async Task<CreateResultDto> UpdateEmploymentAsync(
        int id, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var employment = await db.Employments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employment is null)
        {
            return Fail(null, "Das Arbeitsverhältnis gibt es nicht mehr.");
        }

        if (ParseMoney(Value(values, "gross")) is not { } gross || gross <= 0m)
        {
            return Fail("gross", "Das Bruttogehalt fehlt.");
        }

        if (ParseDate(Value(values, "start")) is not { } start)
        {
            return Fail("start", "Der Vertragsbeginn fehlt.");
        }

        employment.Employer = Value(values, "employer")!.Trim();
        employment.StartsOn = start;

        if (Apply(employment, values, gross) is { } problem)
        {
            return problem;
        }

        await db.SaveChangesAsync(ct);

        return Ok(employment.Id, "/arbeit");
    }

    /// <summary>
    /// Die Felder, die Anlegen und Ändern teilen — samt der beiden Prüfungen, die sonst
    /// zweimal dastünden und irgendwann auseinanderliefen.
    /// </summary>
    private static CreateResultDto? Apply(
        Employment employment, IReadOnlyDictionary<string, string?> values, decimal gross)
    {
        var ende = ParseDate(Value(values, "end"));

        if (ende is { } bis && bis < employment.StartsOn)
        {
            return Fail("end", "Das Vertragsende liegt vor dem Vertragsbeginn.");
        }

        var netto = ParseMoney(Value(values, "net"));

        if (netto is { } wert && wert > gross)
        {
            return Fail("net", "Das Nettogehalt kann nicht über dem Brutto liegen.");
        }

        employment.Position = Value(values, "position")?.Trim();
        employment.Kind = Enum.TryParse<EmploymentKind>(Value(values, "kind"), out var art)
            ? art
            : EmploymentKind.Permanent;
        employment.EndsOn = ende;
        employment.HoursPerWeek = ParseMoney(Value(values, "hours"));
        employment.GrossMonthly = gross;
        employment.NetMonthly = netto;
        employment.NoticePeriodMonths = ParseInt(Value(values, "notice")) ?? 0;

        if (Commute(employment, values) is { } luecke)
        {
            return luecke;
        }

        // „Beendet“ ist erst dann wahr, wenn das Datum vorbei ist. Wer heute ein Ende für
        // nächsten Monat einträgt, hat noch ein laufendes Verhältnis — die Auswertung fragt
        // ohnehin über IsRunning nach, und beides muss dasselbe sagen.
        employment.IsActive = true;

        return null;
    }

    /// <summary>
    /// Entfernung und Arbeitstage — die beiden Angaben, aus denen die Entfernungspauschale entsteht.
    /// </summary>
    /// <remarks>
    /// <para>Sie werden nur gemeinsam übernommen. Wer nur eine von beiden einträgt, bekäme sonst
    /// eine Maske, die die Eingabe annimmt, und ein Steuerjahr, in dem trotzdem nichts steht —
    /// ohne dass irgendwo stünde warum.</para>
    /// <para>Beide leer ist dagegen in Ordnung: dann gibt es diesen Weg eben nicht.</para>
    /// </remarks>
    private static CreateResultDto? Commute(
        Employment employment, IReadOnlyDictionary<string, string?> values)
    {
        var km = ParseMoney(Value(values, "commuteKm"));
        var tage = ParseInt(Value(values, "workDays"));

        if (km is { } strecke && strecke <= 0m)
        {
            return Fail("commuteKm", "Die Entfernung muss über null liegen.");
        }

        if (tage is { } anzahl && (anzahl <= 0 || anzahl > 366))
        {
            return Fail("workDays", "Ein Jahr hat zwischen einem und 366 Arbeitstagen.");
        }

        if (km is null != tage is null)
        {
            return km is null
                ? Fail("commuteKm", "Ohne Entfernung ergeben die Arbeitstage keine Pauschale.")
                : Fail("workDays", "Ohne Arbeitstage ergibt die Entfernung keine Pauschale.");
        }

        employment.CommuteKilometres = km;
        employment.WorkDaysPerYear = tage;
        return null;
    }

    /// <summary>Stunden im Eingabeformat des Formulars: deutsch, ohne überflüssige Nullen.</summary>
    private static string? Hours(decimal? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

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

    /// <summary>
    /// Die beiden Werte eines Kennzeichens. Sie stehen in der Vorbelegung und im Absenden; was der
    /// Nutzer liest, steht in den Optionen.
    /// </summary>
    private const string FlagOn = "ja";
    private const string FlagOff = "nein";

    /// <summary>
    /// Ein Kennzeichen als Auswahl aus zwei benannten Möglichkeiten.
    /// </summary>
    /// <remarks>
    /// Statt „ja/nein“ steht da, was die Wahl bedeutet: der Nutzer soll nicht überlegen müssen,
    /// worauf sich das Ja bezieht. Zwei Chips brauchen kein neues Feldwerk.
    /// </remarks>
    private static CreateFieldDto Flag(
        string key, string label, string onLabel, string offLabel, string? help, bool defaultOn)
        => new()
        {
            Key = key,
            Label = label,
            Kind = CreateFieldKind.Choice,
            Required = false,
            Help = help,
            Options =
            [
                new() { Value = FlagOn, Label = onLabel },
                new() { Value = FlagOff, Label = offLabel },
            ],
            DefaultValue = defaultOn ? FlagOn : FlagOff,
        };

    /// <summary>
    /// Wie das Kennzeichen gesetzt ist. Ohne Angabe bleibt es, wie es war.
    /// </summary>
    /// <remarks>
    /// Ein fehlender Wert darf nicht als „nein“ gelten: sonst löschte ein Formular, das das Feld
    /// nicht mitschickt, eine gepflegte Angabe.
    /// </remarks>
    private static bool Flagged(
        IReadOnlyDictionary<string, string?> values, string key, bool fallback)
        => Value(values, key) switch
        {
            FlagOn => true,
            FlagOff => false,
            _ => fallback,
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
