using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Fahrzeuge als Objekte mit Kosten, Vertrag und Dokumenten.
/// </summary>
/// <remarks>
/// <para>Strukturgleich zur Immobilie — dieselbe Frage, dieselbe Antwort: was kostet das Ding im
/// Jahr, was hängt daran, welche Frist läuft. Die Kosten werden aus <em>echten Buchungen</em>
/// gerechnet, nicht gepflegt; eine gepflegte Zahl wäre nach zwei Monaten falsch.</para>
/// <para>Die Kfz-Versicherung bleibt eine <c>Policy</c> unter Absicherung und wird hier nur
/// verwiesen. Sie zweimal zu führen hieße, zwei Wahrheiten über denselben Vertrag zu haben.</para>
/// </remarks>
public sealed class VehicleService(FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    private const int NoticeWindowDays = 90;

    /// <summary>Monatssatz der privaten Nutzung — die 1 % der 1-%-Regelung.</summary>
    private const decimal MonthlyRate = 0.01m;

    /// <summary>Zuschlag je Kilometer einfacher Entfernung und Monat.</summary>
    private const decimal CommuteRate = 0.0003m;

    public async Task<IReadOnlyList<VehicleListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var rows = await db.Vehicles.AsNoTracking()
            .Include(v => v.Policy)
            .Include(v => v.Employment)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        var result = new List<VehicleListItemDto>();
        foreach (var vehicle in rows)
        {
            var (costs, _) = await CostsAsync(vehicle, ct);
            result.Add(new VehicleListItemDto
            {
                Id = vehicle.Id,
                Name = vehicle.Name,
                Plate = vehicle.Plate,
                Meta = Meta(vehicle),
                CostsLastTwelveMonths = costs,
                HasDeadline = NoticeIsDue(vehicle.Policy),
            });
        }

        return result;
    }

    public async Task<VehicleDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var vehicle = await db.Vehicles.AsNoTracking()
            .Include(v => v.Policy)
            .Include(v => v.Employment)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vehicle is null)
        {
            return null;
        }

        var (costs, parts) = await CostsAsync(vehicle, ct);

        return new VehicleDetailDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Plate = vehicle.Plate,
            Usage = vehicle.Usage,
            FirstRegistration = vehicle.FirstRegistration,
            Mileage = vehicle.Mileage,
            CostsLastTwelveMonths = costs,
            CostParts = parts,
            Benefit = Benefit(vehicle),
            Policy = vehicle.Policy is { } policy
                ? new VehiclePolicyRefDto
                {
                    PolicyId = policy.Id,
                    Name = policy.Name,
                    Provider = policy.Provider,
                    AnnualPremium = Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero),
                    NoticeDeadline = policy.NoticeDeadline,
                    NoticeIsDue = NoticeIsDue(policy),
                }
                : null,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Vehicle, vehicle.Id, ct),
        };
    }

    /// <summary>
    /// Kosten der letzten zwölf Monate: der geldwerte Vorteil, der Versicherungsbeitrag und
    /// alles, was im Empfänger oder Verwendungszweck nach diesem Fahrzeug aussieht — Steuer,
    /// Werkstatt, Tanken.
    /// </summary>
    /// <remarks>
    /// <para>Gesucht wird über das Kennzeichen und den Fahrzeugnamen. Das ist eine Heuristik und
    /// wird als solche behandelt: sie ergänzt die Kosten, sie ersetzt keine Zuordnung. Was sie
    /// nicht findet, fehlt in der Summe — besser als eine Zahl, die mehr behauptet als sie
    /// weiß.</para>
    /// <para>Der geldwerte Vorteil ist der eine Posten, der nicht vom Konto geht. Er steht
    /// trotzdem in der Summe, denn sonst zeigte der Dienstwagen 0 € — und das ist die eine
    /// Zahl, die er sicher nicht kostet. Welcher Posten woher kommt, sagen die
    /// <c>Parts</c>.</para>
    /// </remarks>
    private async Task<(decimal Total, IReadOnlyList<string> Parts)> CostsAsync(
        Vehicle vehicle, CancellationToken ct)
    {
        var parts = new List<string>();
        var total = 0m;

        if (Benefit(vehicle) is { } vorteil)
        {
            total += vorteil.PerYear;
            parts.Add("Geldwerter Vorteil");
        }

        if (vehicle.Policy is { } policy)
        {
            total += Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero);
            parts.Add("Versicherung");
        }

        var from = clock.Today.AddMonths(-12);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense && t.BookingDate >= from)
            .Select(t => new { t.Payee, t.Note, t.Amount })
            .ToListAsync(ct);

        var plate = vehicle.Plate.Replace(" ", string.Empty);
        var word = vehicle.Name.Split(' ')[0];

        var matched = rows
            .Where(t => Contains(t.Payee, plate) || Contains(t.Note, plate)
                        || (word.Length > 2 && (Contains(t.Payee, word) || Contains(t.Note, word))))
            .Sum(t => -t.Amount);

        if (matched > 0)
        {
            total += matched;
            parts.Add("Steuer, Werkstatt");
        }

        return (total, parts);
    }

    /// <summary>
    /// Der geldwerte Vorteil eines Dienstwagens nach der 1-%-Regelung.
    /// </summary>
    /// <remarks>
    /// <para>Gerechnet wird auf den Bruttolistenpreis, abgerundet auf volle hundert Euro
    /// (§ 6 Abs. 1 Nr. 4 EStG) und bei Hybrid oder Elektro auf die Hälfte oder ein Viertel
    /// gemindert: ein Prozent davon je Monat für die private Nutzung, dazu 0,03 % je Kilometer
    /// einfacher Entfernung für Fahrten zwischen Wohnung und Arbeitsstätte.</para>
    /// <para>Der Wegzuschlag entsteht nur, wenn am verknüpften Arbeitsverhältnis eine
    /// Entfernung steht. Eine geratene Strecke wäre keine Schätzung mehr, sondern eine
    /// Erfindung — dieselbe Regel, nach der die Entfernungspauschale ohne Entfernung gar nicht
    /// entsteht.</para>
    /// <para>Nicht gerechnet wird die Einzelbewertung mit 0,002 % je tatsächlicher Fahrt. Sie
    /// setzt ein Fahrtenbuch über das Jahr voraus; was die Anwendung dazu hätte, wären
    /// geratene Tage.</para>
    /// </remarks>
    private static CompanyCarBenefitDto? Benefit(Vehicle vehicle)
    {
        if (vehicle.GrossListPrice is not { } listenpreis || listenpreis <= 0m)
        {
            return null;
        }

        // Ein beendetes Arbeitsverhältnis trägt keinen Dienstwagen mehr. Dieselbe Regel wie bei
        // der Jahreslast der Arbeit: Beendetes ist keine laufende Last.
        if (vehicle.Employment is { IsActive: false })
        {
            return null;
        }

        var anteil = vehicle.TaxableSharePercent ?? 100m;
        var bemessung = Round(Math.Floor(listenpreis / 100m) * 100m * anteil / 100m);

        var nutzung = Round(bemessung * MonthlyRate);
        var strecke = vehicle.Employment?.CommuteKilometres;
        var weg = strecke is { } km ? Round(bemessung * CommuteRate * km) : 0m;

        return new CompanyCarBenefitDto
        {
            GrossListPrice = listenpreis,
            TaxableSharePercent = anteil,
            Basis = bemessung,
            UsePerMonth = nutzung,
            CommuteKilometres = strecke,
            CommutePerMonth = weg,
            EmployerName = vehicle.Employment?.Employer,
            PerMonth = nutzung + weg,
            PerYear = Round((nutzung + weg) * 12m),
        };
    }

    private static decimal Round(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool Contains(string? text, string needle)
        => text is { Length: > 0 } && text.Replace(" ", string.Empty)
            .Contains(needle, StringComparison.OrdinalIgnoreCase);

    private bool NoticeIsDue(Policy? policy)
    {
        if (policy?.NoticeDeadline is not { } deadline)
        {
            return false;
        }

        var days = deadline.DayNumber - clock.Today.DayNumber;
        if (days < 0)
        {
            return false;
        }

        return days <= NoticeWindowDays
               || (policy.NoticeReminderOn is { } remind
                   && remind.DayNumber - clock.Today.DayNumber <= NoticeWindowDays);
    }

    /// <summary>
    /// Der Untertitel kommt aus dem gemeinsamen Builder — dieselbe Zeile wie im Bestand.
    /// </summary>
    private static string Meta(Vehicle vehicle) => HoldingMeta.ForVehicle(vehicle);
}
