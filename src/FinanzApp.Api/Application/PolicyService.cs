using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Vorsorge- und Absicherungsverträge. Ein Dienst für beide Bereiche — sie unterscheiden sich
/// in der Kopfzahl, nicht im Modell.
/// </summary>
/// <remarks>
/// <para>Die Kündigungsfrist wird nicht eingegeben, sondern <em>abgeleitet</em>: Vertragsende
/// minus Frist. Ein von Hand gepflegtes Datum liefe irgendwann der Verlängerung hinterher.
/// Beiträge sind Verweise auf Buchungen, keine eigenen Geldsätze.</para>
/// <para>Was ins Vermögen zählt, entscheidet allein <c>Policy.AssetValue</c> — hier wird nie
/// selbst gerechnet, ob ein Vertrag einen Wert hat.</para>
/// </remarks>
public sealed class PolicyService(FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    /// <summary>Wie früh eine Frist als „läuft“ gilt.</summary>
    private const int NoticeWindowDays = 90;

    /// <summary>Einer der beiden Bereiche, mit seiner Kopfzahl.</summary>
    public async Task<PolicyOverviewDto> GetOverviewAsync(
        bool capitalForming, CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming == capitalForming)
            .ToListAsync(ct);

        // Vorsorge nach Wert, Absicherung nach Beitrag — beides absteigend, das Gewichtige oben.
        rows = capitalForming
            ? [.. rows.OrderByDescending(p => p.AssetValue ?? 0m).ThenBy(p => p.Name)]
            : [.. rows.OrderByDescending(p => p.AnnualPremium).ThenBy(p => p.Name)];

        return new PolicyOverviewDto
        {
            CapitalForming = capitalForming,
            Title = capitalForming ? "Vorsorge & Kapital" : "Absicherung",

            // Eine Absicherung hat keinen Wert. Dort eine Summe zu zeigen, wäre falsch.
            TotalValue = capitalForming ? rows.Sum(p => p.AssetValue ?? 0m) : null,
            OldestValuationDate = capitalForming
                ? rows.Select(p => p.ValuationDate).Where(d => d is not null).Min()
                : null,
            TotalAnnualPremium = capitalForming ? null : rows.Sum(p => p.AnnualPremium),
            Items = [.. rows.Select(ToListItem)],
        };
    }

    public async Task<PolicyDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var policy = await db.Policies.AsNoTracking()
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (policy is null)
        {
            return null;
        }

        return new PolicyDetailDto
        {
            Id = policy.Id,
            Name = policy.Name,
            Provider = policy.Provider,
            Kind = policy.Kind,
            KindLabel = KindLabel(policy.Kind),
            IsCapitalForming = policy.IsCapitalForming,
            PolicyNumber = policy.PolicyNumber,
            Premium = policy.Premium,
            PremiumInterval = policy.PremiumInterval,
            MonthlyPremium = Math.Round(policy.MonthlyPremium, 2, MidpointRounding.AwayFromZero),
            AnnualPremium = Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero),
            StartsOn = policy.StartsOn,
            EndsOn = policy.EndsOn,
            NoticePeriodMonths = policy.NoticePeriodMonths,
            NoticeDeadline = policy.NoticeDeadline,
            DaysUntilNotice = DaysUntilNotice(policy),
            NoticeReminderOn = policy.NoticeReminderOn,
            DaysUntilReminder = DaysUntilReminder(policy),
            NoticeIsDue = NoticeIsDue(policy),
            CurrentValue = policy.AssetValue,
            ValuationDate = policy.IsCapitalForming ? policy.ValuationDate : null,
            MaturityValue = policy.IsCapitalForming ? policy.MaturityValue : null,
            MaturesOn = policy.IsCapitalForming ? policy.MaturesOn : null,
            SumInsured = policy.IsCapitalForming ? null : policy.SumInsured,
            Deductible = policy.IsCapitalForming ? null : policy.Deductible,
            AccountName = policy.Account?.Name,
            Notes = policy.Notes,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Policy, policy.Id, ct),
            Payments = await LoadPaymentsAsync(policy, ct),
        };
    }

    /// <summary>
    /// Was die kapitalbildenden Verträge zum Bruttovermögen beitragen, plus den ältesten Stichtag.
    /// </summary>
    /// <remarks>
    /// Der Stichtag gehört zwingend dazu: ein Jahresstand ist kein Tageskurs, und die Kachel im
    /// Vermögen muss das sagen dürfen.
    /// </remarks>
    public async Task<(decimal Total, DateOnly? AsOf, string Provider)> GetCapitalTotalAsync(
        CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming)
            .Select(p => new { p.CurrentValue, p.ValuationDate, p.Provider })
            .ToListAsync(ct);

        return (
            rows.Sum(r => r.CurrentValue ?? 0m),
            rows.Select(r => r.ValuationDate).Where(d => d is not null).Min(),
            rows.Count == 1 ? rows[0].Provider : $"{rows.Count} Verträge");
    }

    /// <summary>
    /// Summe der Monatsbeiträge <b>der Absicherung</b> — für die Kostenrechnung der Immobilie und
    /// das Sparpotential.
    /// </summary>
    /// <remarks>
    /// Vorsorgebeiträge bleiben bewusst draußen: sie sind Sparen, keine Ausgabe (Handoff v4,
    /// Abschnitt 10). Wer sie mitzählte, würde die eigene Sparquote als Kosten ausweisen.
    /// </remarks>
    public async Task<decimal> GetMonthlyPremiumTotalAsync(CancellationToken ct = default)
        => (await db.Policies.AsNoTracking().Where(p => !p.IsCapitalForming).ToListAsync(ct))
            .Sum(p => p.MonthlyPremium);

    private int? DaysUntilNotice(Policy policy)
        => policy.NoticeDeadline is { } deadline
            ? deadline.DayNumber - clock.Today.DayNumber
            : null;

    /// <summary>
    /// Ob die Frist jetzt auf den Tisch gehört — entweder weil der Termin nah ist, oder weil
    /// die gesetzte Erinnerung erreicht wurde. Verstrichene Termine zählen nicht mehr.
    /// </summary>
    private bool NoticeIsDue(Policy policy)
    {
        if (DaysUntilNotice(policy) is not { } days || days < 0)
        {
            return false;
        }

        // Die Erinnerung zählt schon, wenn sie in Sicht ist — nicht erst am Tag selbst. Ein
        // Vergleich braucht Vorlauf, und genau dafür ist sie da.
        return days <= NoticeWindowDays
               || (DaysUntilReminder(policy) is { } remind && remind <= NoticeWindowDays);
    }

    /// <summary>Tage bis zur gesetzten Erinnerung. Negativ heißt: sie ist bereits gefallen.</summary>
    private int? DaysUntilReminder(Policy policy)
        => policy.NoticeReminderOn is { } remind
            ? remind.DayNumber - clock.Today.DayNumber
            : null;

    /// <summary>
    /// Beitragszahlungen. Gesucht wird in den Buchungen nach dem Namen des Anbieters — die
    /// Zahlungen selbst bleiben Buchungen und werden nicht ein zweites Mal geführt.
    /// </summary>
    private async Task<IReadOnlyList<LinkedPaymentDto>> LoadPaymentsAsync(
        Policy policy, CancellationToken ct)
    {
        var keyword = policy.Provider.Split(' ', '-')[0];
        if (keyword.Length < 3)
        {
            return [];
        }

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Expense)
            .OrderByDescending(t => t.BookingDate)
            .Take(200)
            .ToListAsync(ct);

        return
        [
            .. rows
                .Where(t => t.Payee.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(12)
                .Select(t => new LinkedPaymentDto
                {
                    TransactionId = t.Id,
                    BookingDate = t.BookingDate,
                    Amount = Math.Abs(t.Amount),
                    Payee = t.Payee,
                    AccountName = t.Account?.Name ?? string.Empty,
                }),
        ];
    }

    private PolicyListItemDto ToListItem(Policy policy) => new()
    {
        Id = policy.Id,
        Name = policy.Name,
        Provider = policy.Provider,
        Kind = policy.Kind,
        IsCapitalForming = policy.IsCapitalForming,
        Meta = Meta(policy),
        Premium = policy.Premium,
        PremiumInterval = policy.PremiumInterval,
        AnnualPremium = Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero),
        Value = policy.AssetValue,
        ValuationDate = policy.IsCapitalForming ? policy.ValuationDate : null,
        EndsOn = policy.EndsOn,
        NoticeDeadline = policy.NoticeDeadline,
        DaysUntilNotice = DaysUntilNotice(policy),
        DaysUntilReminder = DaysUntilReminder(policy),
        NoticeIsDue = NoticeIsDue(policy),
    };

    /// <summary>Zweite Zeile der Liste: Vertragsart, dann das Kennzeichnende.</summary>
    private static string Meta(Policy policy)
        => string.IsNullOrWhiteSpace(policy.Notes)
            ? KindLabel(policy.Kind)
            : $"{KindLabel(policy.Kind)} · {policy.Notes}";

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
        _ => "Vertrag",
    };
}
