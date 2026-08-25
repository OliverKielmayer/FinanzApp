using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Übersetzt die polymorphe Verknüpfung (Zieltyp plus Id) in etwas Lesbares.
/// </summary>
/// <remarks>
/// <c>DocumentLink</c> verzichtet bewusst auf einen Fremdschlüssel je Zieltyp — dafür gibt es
/// keine Prüfung durch die Datenbank. Diese Klasse trägt beides nach: sie beschafft den Namen
/// für die Anzeige und beantwortet, ob ein Ziel im eigenen Haushalt überhaupt existiert. Alle
/// Abfragen laufen über den Mandantenfilter, ein fremdes Ziel findet sie deshalb nicht.
/// </remarks>
public sealed class ObjectLabelService(FinanzAppDbContext db)
{
    /// <summary>Art des Objekts im Klartext.</summary>
    public static string TargetLabel(LinkTargetType type) => type switch
    {
        LinkTargetType.Account => "Konto",
        LinkTargetType.Transaction => "Buchung",
        LinkTargetType.Portfolio => "Depot",
        LinkTargetType.Policy => "Vertrag",
        LinkTargetType.Loan => "Darlehen",
        LinkTargetType.Property => "Immobilie",
        LinkTargetType.Contract => "Vertrag",
        LinkTargetType.Invoice => "Rechnung",
        LinkTargetType.Employer => "Arbeitgeber",
        LinkTargetType.EmploymentContract => "Arbeitsvertrag",
        LinkTargetType.Payslip => "Lohnabrechnung",
        LinkTargetType.MedicalBill => "Arztrechnung",
        LinkTargetType.Vehicle => "Fahrzeug",
        _ => "Objekt",
    };

    /// <summary>Pfad zum Objekt in der Oberfläche, oder <c>null</c>, wenn es keinen Screen gibt.</summary>
    public static string? RouteFor(LinkTargetType type, int id) => type switch
    {
        LinkTargetType.Account or LinkTargetType.Transaction => "/konten",
        LinkTargetType.Portfolio => "/depot",
        // Eine Detailseite für beide Bereiche - welcher es ist, steht am Vertrag.
        LinkTargetType.Policy => $"/police/{id}",
        LinkTargetType.Loan => $"/darlehen?id={id}",
        LinkTargetType.Property => $"/wohnen/{id}",
        LinkTargetType.Contract => $"/vertraege/{id}",
        LinkTargetType.Invoice => $"/rechnungen/{id}",
        LinkTargetType.MedicalBill => $"/gesundheit/{id}",
        LinkTargetType.Vehicle => $"/fahrzeuge/{id}",
        _ => null,
    };

    /// <summary>Name des Objekts, oder <c>null</c>, wenn es nicht (mehr) im Haushalt existiert.</summary>
    public async Task<string?> ResolveAsync(LinkTargetType type, int id, CancellationToken ct = default)
        => type switch
        {
            LinkTargetType.Account => await db.Accounts.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Transaction => await db.Transactions.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Payee).FirstOrDefaultAsync(ct),

            LinkTargetType.Portfolio => await db.Depots.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Policy => await db.Policies.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Loan => await db.Loans.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Property => await db.Properties.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Contract => await db.Contracts.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            LinkTargetType.Invoice => await db.Invoices.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Subject).FirstOrDefaultAsync(ct),

            LinkTargetType.MedicalBill => await db.MedicalBills.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Provider).FirstOrDefaultAsync(ct),

            LinkTargetType.Vehicle => await db.Vehicles.AsNoTracking()
                .Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct),

            _ => null,
        };

    /// <summary>Ob das Ziel im Haushalt des Aufrufers existiert. Trägt die Prüfung, die dem
    /// polymorphen Verweis in der Datenbank fehlt.</summary>
    public async Task<bool> ExistsAsync(LinkTargetType type, int id, CancellationToken ct = default)
        => await ResolveAsync(type, id, ct) is not null;
}
