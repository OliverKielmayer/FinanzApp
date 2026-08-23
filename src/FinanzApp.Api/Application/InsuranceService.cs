using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Versicherungsverträge mit Beitrag, Kündigungsfrist und Dokumenten.
/// </summary>
/// <remarks>
/// Die Frist wird nicht eingegeben, sondern <em>abgeleitet</em>: Vertragsende minus
/// Kündigungsfrist. Ein von Hand gepflegtes Datum liefe irgendwann der Verlängerung hinterher.
/// Beiträge sind Verweise auf Buchungen, keine eigenen Geldsätze.
/// </remarks>
public sealed class InsuranceService(FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    /// <summary>Wie früh eine Frist als „läuft“ gilt.</summary>
    private const int NoticeWindowDays = 90;

    public async Task<IReadOnlyList<InsuranceListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var rows = await db.Insurances.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct);
        return [.. rows.Select(ToListItem)];
    }

    public async Task<InsuranceDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var insurance = await db.Insurances.AsNoTracking()
            .Include(i => i.Account)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (insurance is null)
        {
            return null;
        }

        return new InsuranceDetailDto
        {
            Id = insurance.Id,
            Name = insurance.Name,
            Insurer = insurance.Insurer,
            PolicyNumber = insurance.PolicyNumber,
            Premium = insurance.Premium,
            PremiumInterval = insurance.PremiumInterval,
            MonthlyPremium = Math.Round(insurance.MonthlyPremium, 2, MidpointRounding.AwayFromZero),
            StartsOn = insurance.StartsOn,
            EndsOn = insurance.EndsOn,
            NoticePeriodMonths = insurance.NoticePeriodMonths,
            NoticeDeadline = insurance.NoticeDeadline,
            DaysUntilNotice = DaysUntilNotice(insurance),
            NoticeIsDue = NoticeIsDue(insurance),
            AccountName = insurance.Account?.Name,
            Notes = insurance.Notes,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Insurance, insurance.Id, ct),
            Payments = await LoadPaymentsAsync(insurance, ct),
        };
    }

    /// <summary>
    /// Beitragszahlungen. Gesucht wird in den Buchungen nach dem Namen des Versicherers — die
    /// Zahlungen selbst bleiben Buchungen und werden nicht ein zweites Mal geführt.
    /// </summary>
    private async Task<IReadOnlyList<LinkedPaymentDto>> LoadPaymentsAsync(
        Insurance insurance, CancellationToken ct)
    {
        var keyword = insurance.Insurer.Split(' ', '-')[0];
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

    /// <summary>Summe der Monatsbeiträge — für die Kostenrechnung der Immobilie und das Sparpotential.</summary>
    public async Task<decimal> GetMonthlyPremiumTotalAsync(CancellationToken ct = default)
        => (await db.Insurances.AsNoTracking().ToListAsync(ct)).Sum(i => i.MonthlyPremium);

    private int? DaysUntilNotice(Insurance insurance)
        => insurance.NoticeDeadline is { } deadline
            ? deadline.DayNumber - clock.Today.DayNumber
            : null;

    private bool NoticeIsDue(Insurance insurance)
        => DaysUntilNotice(insurance) is { } days && days is >= 0 and <= NoticeWindowDays;

    private InsuranceListItemDto ToListItem(Insurance insurance) => new()
    {
        Id = insurance.Id,
        Name = insurance.Name,
        Insurer = insurance.Insurer,
        Premium = insurance.Premium,
        PremiumInterval = insurance.PremiumInterval,
        EndsOn = insurance.EndsOn,
        NoticeDeadline = insurance.NoticeDeadline,
        DaysUntilNotice = DaysUntilNotice(insurance),
        NoticeIsDue = NoticeIsDue(insurance),
    };
}
