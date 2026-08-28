using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Untertitelzeile eines Objekts — eine Funktion je Klasse, für alle Ansichten dieselbe.
/// </summary>
/// <remarks>
/// <para>Der v5-Handoff verlangt sie an einer Stelle: Klassenliste, Bestand-Liste und
/// Suchtreffer sollen für dasselbe Objekt denselben Satz zeigen. Vorher baute jede Ansicht
/// ihren eigenen — die Policenliste nannte nur die Vertragsart, die Suche nur den Anbieter, und
/// der Bestand hatte einen dritten. Drei Antworten auf dieselbe Frage.</para>
/// <para>Gebaut wird aus <b>Rohfeldern</b>, nie aus einem Anzeigefeld. Und was ein Objekt nicht
/// hat, steht nicht da: eine Zeile „Vertrag · ohne Konto“ behauptet etwas über ein Feld, das
/// schlicht leer ist. <see cref="Join"/> lässt Leeres weg, statt seine Abwesenheit zu
/// formulieren.</para>
/// </remarks>
public static class HoldingMeta
{
    /// <summary>Fügt zusammen, was vorhanden ist — mit Mittelpunkt, ohne Lücken.</summary>
    public static string Join(params string?[] parts)
        => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    public static string ForAccount(Account account)
        => Join(
            account.BankName,
            account.Iban,
            account.Kind == AccountKind.Savings ? "Tagesgeld" : "Girokonto");

    public static string ForDepot(Depot depot, int positionCount)
        => Join(
            depot.Broker,
            Number(depot.Number),
            positionCount == 0
                ? null
                : $"{positionCount} {(positionCount == 1 ? "Position" : "Positionen")}");

    /// <summary>Vorsorge oder Absicherung — die Trennung liegt am Flag, nicht an der Art.</summary>
    public static string ForPolicy(Policy policy)
        => policy.IsCapitalForming ? ForPension(policy) : ForProtection(policy);

    public static string ForPension(Policy policy)
        => Join(
            KindLabel(policy.Kind),
            policy.Provider,
            Number(policy.PolicyNumber),
            policy.EndsOn is { } ablauf ? $"Ablauf {GermanFormat.Date(ablauf)}" : null,
            policy.Notes);

    public static string ForProtection(Policy policy)
        => Join(
            KindLabel(policy.Kind),
            policy.Provider,
            Number(policy.PolicyNumber),
            policy.EndsOn is { } ende ? $"bis {GermanFormat.Date(ende)}" : null,
            Period(policy.NoticePeriodMonths, "Monat", "Monate"),
            policy.Notes);

    public static string ForProperty(Property property)
        => Join(
            PropertyLabel(property.Kind),
            property.Address,
            property.PurchaseDate is { } kauf ? $"Kauf {GermanFormat.Date(kauf)}" : null,
            property.LoanId is null ? null : "mit Darlehen");

    public static string ForContract(Contract contract)
        => Join(
            "Vertrag",
            contract.Provider,
            Number(contract.ContractNumber),
            Period(contract.NoticePeriodWeeks, "Woche", "Wochen"),
            contract.NoticeToDate is { } termin ? $"zum {GermanFormat.Date(termin)}" : null);

    /// <summary>
    /// Ohne Kennzeichen: das steht neben dem Namen, nicht im Untertitel.
    /// </summary>
    /// <remarks>
    /// Die Fahrzeugliste zeigt „VW Passat · HD-AB 123“ als Titel. Das Kennzeichen hier noch
    /// einmal zu nennen hieße, dieselbe Angabe zweimal in dieselbe Zeile zu setzen.
    /// </remarks>
    public static string ForVehicle(Vehicle vehicle)
        => Join(
            vehicle.Usage,
            vehicle.FirstRegistration is { } ez ? $"EZ {GermanFormat.Date(ez)}" : null,
            vehicle.Mileage is { } km ? $"{GermanFormat.Quantity(km)} km" : null,
            vehicle.Policy is null ? null : "Versicherung verknüpft");

    /// <summary>
    /// Ein Arbeitsverhältnis — ohne Gehalt.
    /// </summary>
    /// <remarks>
    /// Das Gehalt steht rechts an der Zeile und wird von „Beträge verbergen“ maskiert. Hier
    /// noch einmal genannt, käme es an der Maskierung vorbei und stünde zweimal da.
    /// </remarks>
    public static string ForEmployment(Employment employment)
        => Join(
            employment.Position,
            EmploymentLabel(employment.Kind),
            $"seit {GermanFormat.Date(employment.StartsOn)}",
            employment.EndsOn is { } ende ? $"bis {GermanFormat.Date(ende)}" : null,
            employment.HoursPerWeek is { } stunden
                ? $"{GermanFormat.Quantity(stunden)} Std./Woche"
                : null,
            Period(employment.NoticePeriodMonths, "Monat", "Monate"));

    public static string ForLoan(Loan loan)
        => Join(
            loan.Lender,
            $"{GermanFormat.Percent(loan.InterestRatePercent, 2)} Sollzins",
            $"Rate {GermanFormat.EuroRounded(loan.Installment)}");

    // ── Bausteine ──────────────────────────────────────────────────────────────────────────

    private static string? Number(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"Nr. {value}";

    /// <summary>
    /// Eine Kündigungsfrist, oder nichts.
    /// </summary>
    /// <remarks>
    /// Null Monate sind keine Frist. „Kündigungsfrist 0 Monate“ läse sich wie jederzeitige
    /// Kündbarkeit — hier steht dann lieber gar nichts, und wo die Angabe gebraucht wird,
    /// nennt der Fixkostenbericht sie ausdrücklich als unbekannt.
    /// </remarks>
    private static string? Period(int length, string singular, string plural)
        => length <= 0
            ? null
            : $"Kündigungsfrist {length} {(length == 1 ? singular : plural)}";

    public static string KindLabel(PolicyKind kind) => kind switch
    {
        PolicyKind.CapitalLife => "Kapital-LV",
        PolicyKind.Pension => "Rentenversicherung",
        PolicyKind.Riester => "Riester-Rente",
        PolicyKind.BuildingSociety => "Bausparvertrag",
        PolicyKind.OccupationalPension => "Betriebliche Altersvorsorge",
        PolicyKind.TermLife => "Risikoleben",
        PolicyKind.DisabilityInsurance => "Berufsunfähigkeit",
        PolicyKind.Liability => "Haftpflicht",
        PolicyKind.HouseholdContents => "Hausrat",
        PolicyKind.Building => "Wohngebäude",
        PolicyKind.Vehicle => "Kfz",
        PolicyKind.Accident => "Unfall",
        PolicyKind.LegalExpenses => "Rechtsschutz",
        PolicyKind.Health => "Krankenversicherung",

        // Nur für Other. Jede benannte Art gehört oben hin: eine Krankenversicherung stand
        // in der Suche als „Vertrag · Vertrag“ da, weil sie hier durchfiel.
        _ => "Vertrag",
    };

    public static string EmploymentLabel(EmploymentKind kind) => kind switch
    {
        EmploymentKind.FixedTerm => "befristet",
        EmploymentKind.PartTime => "Teilzeit",
        EmploymentKind.Freelance => "Werkvertrag",
        _ => "unbefristet",
    };

    public static string AgreementLabel(WorkAgreementKind kind) => kind switch
    {
        WorkAgreementKind.SalaryChange => "Gehaltsänderung",
        WorkAgreementKind.Bonus => "Bonusvereinbarung",
        WorkAgreementKind.OccupationalPension => "Betriebliche Altersvorsorge",
        _ => "Vereinbarung",
    };

    public static string PropertyLabel(PropertyKind kind) => kind switch
    {
        PropertyKind.Apartment => "Wohnung",
        PropertyKind.Land => "Grundstück",
        _ => "Haus",
    };
}
