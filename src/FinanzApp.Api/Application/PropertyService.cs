using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Immobilien, ihre Verträge und deren Rechnungen.
/// </summary>
/// <remarks>
/// Die Immobilie <em>verweist</em> auf das bestehende Darlehen und kopiert es nicht — es gibt
/// genau einen Darlehensbereich mit genau einem Tilgungsplan. Die Kostenrechnung summiert echte
/// Buchungen; eine Rechnung gilt erst als bezahlt, wenn ihr eine Buchung zugeordnet wurde.
/// </remarks>
public sealed class PropertyService(
    FinanzAppDbContext db,
    DocumentService documents,
    IClock clock,
    ParticipationService participation)
{
    private const int NoticeWindowDays = 90;

    public async Task<IReadOnlyList<PropertyListItemDto>> GetListAsync(CancellationToken ct = default)
        => await db.Properties.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PropertyListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                Address = p.Address,
                MarketValue = p.MarketValue,
                ContractCount = p.Contracts.Count,
                Participants = p.Shares
                    .OrderByDescending(a => a.Percent)
                    .Select(a => new PropertyParticipantDto
                    {
                        UserId = a.UserId,
                        Name = a.User!.Name,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);

    public async Task<PropertyDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var property = await db.Properties.AsNoTracking()
            .Include(p => p.Loan)
            .Include(p => p.Contracts)
            .Include(p => p.Shares).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (property is null)
        {
            return null;
        }

        var (costs, parts) = await CalculateCostsAsync(property, ct);
        var contracts = new List<ContractListItemDto>();
        foreach (var contract in property.Contracts.OrderBy(c => c.Name))
        {
            contracts.Add(await ToContractListItemAsync(contract, ct));
        }

        return new PropertyDetailDto
        {
            Id = property.Id,
            Name = property.Name,
            Address = property.Address,
            MarketValue = property.MarketValue,
            PurchaseDate = property.PurchaseDate,
            PurchasePrice = property.PurchasePrice,
            Loan = property.Loan is { } loan
                ? new PropertyLoanRefDto
                {
                    LoanId = loan.Id,
                    RemainingDebt = loan.RemainingDebt,
                    Installment = loan.Installment,
                    InterestRatePercent = loan.InterestRatePercent,
                }
                : null,
            CostsLastTwelveMonths = costs,
            CostParts = parts,
            Contracts = contracts,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Property, property.Id, ct),
            Participation = await participation.ForPropertyAsync(property.Id, ct),
        };
    }

    public async Task<ContractDetailDto?> GetContractAsync(int id, CancellationToken ct = default)
    {
        var contract = await db.Contracts.AsNoTracking()
            .Include(c => c.Property)
            .Include(c => c.Account)
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (contract is null)
        {
            return null;
        }

        return new ContractDetailDto
        {
            Id = contract.Id,
            Name = contract.Name,
            Provider = contract.Provider,
            ContractNumber = contract.ContractNumber,
            MonthlyAmount = contract.MonthlyAmount,
            AccountName = contract.Account?.Name,
            StartsOn = contract.StartsOn,
            EndsOn = contract.EndsOn,
            NoticePeriodWeeks = contract.NoticePeriodWeeks,
            NoticeToDate = contract.NoticeToDate,
            NoticeDeadline = contract.NoticeDeadline,
            NoticeIsDue = NoticeIsDue(contract),
            PropertyId = contract.PropertyId,
            PropertyName = contract.Property?.Name,
            PropertyRelated = contract.PropertyRelated,
            Invoices =
            [
                .. contract.Invoices
                    .OrderByDescending(i => i.DueOn)
                    .Select(ToInvoiceListItem),
            ],
            Documents = await documents.GetForTargetAsync(LinkTargetType.Contract, contract.Id, ct),
        };
    }

    public async Task<InvoiceDetailDto?> GetInvoiceAsync(int id, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Contract).ThenInclude(c => c!.Property)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (invoice is null)
        {
            return null;
        }

        return new InvoiceDetailDto
        {
            Id = invoice.Id,
            Subject = invoice.Subject,
            Number = invoice.Number,
            Amount = invoice.Amount,
            IssuedOn = invoice.IssuedOn,
            DueOn = invoice.DueOn,
            Status = invoice.Status,
            DaysUntilDue = invoice.DueOn.DayNumber - clock.Today.DayNumber,
            ContractId = invoice.ContractId,
            ContractName = invoice.Contract?.Name,
            PropertyId = invoice.Contract?.PropertyId,
            PropertyName = invoice.Contract?.Property?.Name,
            TransactionId = invoice.TransactionId,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Invoice, invoice.Id, ct),
        };
    }

    /// <summary>
    /// Buchungen, die zu einer offenen Rechnung passen könnten. Dieselbe Mechanik wie beim
    /// PKV-Vorgang: die Bewertung schlägt vor, bestätigt wird von Hand.
    /// </summary>
    public async Task<IReadOnlyList<PaymentCandidateDto>> GetPaymentCandidatesAsync(
        int invoiceId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Contract)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null)
        {
            return [];
        }

        var from = invoice.IssuedOn.AddDays(-7);
        var to = invoice.DueOn.AddDays(45);
        var keyword = invoice.Contract?.Provider.Split(' ', '-')[0].ToLowerInvariant();

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Expense && t.BookingDate >= from && t.BookingDate <= to)
            .OrderByDescending(t => t.BookingDate)
            .Take(80)
            .ToListAsync(ct);

        var scored = new List<PaymentCandidateDto>();
        foreach (var transaction in rows)
        {
            var amount = Math.Abs(transaction.Amount);
            var deviation = invoice.Amount == 0 ? 1m : Math.Abs(amount - invoice.Amount) / invoice.Amount;

            var score = 0;
            var reasons = new List<string>();

            if (deviation < 0.001m)
            {
                score += 50;
                reasons.Add("Betrag stimmt");
            }
            else if (deviation <= 0.05m)
            {
                score += 25;
                reasons.Add("Betrag fast gleich");
            }
            else
            {
                reasons.Add("Betrag weicht ab");
            }

            if (keyword is { Length: > 2 }
                && transaction.Payee.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 30;
                reasons.Insert(0, "Anbieter passt");
            }

            var distance = Math.Abs(transaction.BookingDate.DayNumber - invoice.DueOn.DayNumber);
            if (distance <= 14)
            {
                score += 20;
            }
            else if (distance <= 40)
            {
                score += 10;
            }

            if (score > 0)
            {
                scored.Add(new PaymentCandidateDto
                {
                    TransactionId = transaction.Id,
                    BookingDate = transaction.BookingDate,
                    Payee = transaction.Payee,
                    Amount = amount,
                    AccountName = transaction.Account?.Name ?? string.Empty,
                    Score = Math.Min(100, score),
                    Reason = string.Join(" · ", reasons),
                    IsBestMatch = false,
                });
            }
        }

        var best = scored.OrderByDescending(c => c.Score).ThenByDescending(c => c.BookingDate).Take(6).ToList();
        return best.Count == 0 ? [] : [.. best.Select((c, index) => c with { IsBestMatch = index == 0 })];
    }

    /// <summary>Markiert eine Rechnung als bezahlt, mit oder ohne zugeordnete Buchung.</summary>
    public async Task<InvoiceDetailDto?> PayAsync(
        int invoiceId, PayInvoiceRequest request, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null)
        {
            return null;
        }

        if (request.TransactionId is { } transactionId)
        {
            if (!await db.Transactions.AnyAsync(t => t.Id == transactionId, ct))
            {
                throw new ArgumentException("Die Buchung gibt es nicht.");
            }

            invoice.TransactionId = transactionId;
        }

        invoice.Status = InvoiceStatus.Paid;
        await db.SaveChangesAsync(ct);
        return await GetInvoiceAsync(invoiceId, ct);
    }

    /// <summary>Summe der offenen Rechnungen — speist Liquidität und Dashboard-Banner.</summary>
    public async Task<IReadOnlyList<Invoice>> GetOpenInvoicesAsync(CancellationToken ct = default)
        => await db.Invoices.AsNoTracking()
            .Include(i => i.Contract)
            .Where(i => i.Status == InvoiceStatus.Open)
            .OrderBy(i => i.DueOn)
            .ToListAsync(ct);

    /// <summary>
    /// Kosten der letzten zwölf Monate: Darlehensraten, Verträge und passende Buchungen.
    /// Gerechnet auf dem Bestand, nicht eingegeben.
    /// </summary>
    private async Task<(decimal Total, IReadOnlyList<string> Parts)> CalculateCostsAsync(
        Property property, CancellationToken ct)
    {
        var parts = new List<string>();
        var total = 0m;

        if (property.Loan is { } loan)
        {
            total += loan.Installment * 12m;
            parts.Add("Darlehen");
        }

        var contractTotal = property.Contracts.Sum(c => c.MonthlyAmount) * 12m;
        if (contractTotal > 0)
        {
            total += contractTotal;
            parts.Add("Verträge");
        }

        // Gebäude- und Hausratversicherungen zählen mit, wenn es sie gibt — nach Vertragsart,
        // nicht mehr nach dem Namen geraten.
        var housingInsurance = (await db.Policies.AsNoTracking()
                .Where(p => p.Kind == PolicyKind.Building || p.Kind == PolicyKind.HouseholdContents)
                .ToListAsync(ct))
            .Sum(p => p.MonthlyPremium * 12m);

        if (housingInsurance > 0)
        {
            total += housingInsurance;
            parts.Add("Versicherung");
        }

        return (Math.Round(total, 2, MidpointRounding.AwayFromZero), parts);
    }

    private async Task<ContractListItemDto> ToContractListItemAsync(Contract contract, CancellationToken ct)
        => new()
        {
            Id = contract.Id,
            Name = contract.Name,
            Provider = contract.Provider,
            MonthlyAmount = contract.MonthlyAmount,
            NoticeDeadline = contract.NoticeDeadline,
            NoticeIsDue = NoticeIsDue(contract),
            OpenInvoiceCount = await db.Invoices
                .CountAsync(i => i.ContractId == contract.Id && i.Status == InvoiceStatus.Open, ct),
        };

    private InvoiceListItemDto ToInvoiceListItem(Invoice invoice) => new()
    {
        Id = invoice.Id,
        Subject = invoice.Subject,
        Amount = invoice.Amount,
        DueOn = invoice.DueOn,
        Status = invoice.Status,
        DaysUntilDue = invoice.DueOn.DayNumber - clock.Today.DayNumber,
    };

    private bool NoticeIsDue(Contract contract)
        => contract.NoticeDeadline is { } deadline
           && deadline.DayNumber - clock.Today.DayNumber is >= 0 and <= NoticeWindowDays;
}
