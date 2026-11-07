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

    /// <summary>
    /// Wie viele Buchungen die Zahlungssuche je Richtung aus dem Fenster holt.
    /// </summary>
    /// <remarks>
    /// Eine Obergrenze bleibt nötig: Bei einer Jahresrechnung, die im Januar kommt und im
    /// Dezember fällig wird, spannt das Fenster elf Monate auf. Bei rund sechzig Ausgaben im
    /// Monat über zwei Girokonten deckt dieser Vorrat gut zwei Monate je Seite ab — mehr, als
    /// ein übliches Zahlungsziel je umfasst.
    /// </remarks>
    private const int CandidatePoolPerSide = 150;

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

        // Getrennt gerechnet und getrennt gezeigt: Was einmal gekauft wurde, gehört nicht in
        // eine Jahreslast. Ohne Preis zählt eine Anschaffung in der Anzahl mit, aber nicht in
        // der Summe — ein fehlender Preis ist keine Null.
        var investitionen = await db.Purchases.AsNoTracking()
            .Where(p => p.PropertyId == property.Id)
            .Select(p => p.Price)
            .ToListAsync(ct);
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
            Investments = decimal.Round(investitionen.Sum(p => p ?? 0m), 2),
            InvestmentCount = investitionen.Count,
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
            Amount = contract.Amount,
            PaymentInterval = contract.PaymentInterval,
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
            Payee = invoice.Payee ?? invoice.Contract?.Provider,
            PaidOn = invoice.PaidOn,
            PaymentNote = invoice.PaymentNote,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Invoice, invoice.Id, ct),
        };
    }

    /// <summary>
    /// Buchungen, die zu einer offenen Rechnung passen könnten. Dieselbe Mechanik wie beim
    /// PKV-Vorgang: die Bewertung schlägt vor, bestätigt wird von Hand.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Vorauswahl sucht, wonach die Bewertung sucht.</b> Sie nahm vorher die
    /// jüngsten achtzig Buchungen des Fensters — und schnitt damit genau dessen ältestes Ende
    /// ab. Dort liegt die gesuchte Zahlung, sobald nach der Fälligkeit noch ein Monat
    /// Kontoauszüge dazugekommen ist: Rund sechzig Ausgaben im Monat über zwei Girokonten
    /// füllen ein Fenster von sieben Wochen mühelos über achtzig. Der Posten meldete dann
    /// „keine passende Buchung“, während sie im Bestand stand.</para>
    /// <para>Begrenzt wird deshalb nicht mehr nach Buchungstag, sondern von der Fälligkeit aus
    /// nach beiden Seiten — und ein eigener Griff holt die Buchungen mit passendem Betrag,
    /// wo immer im Fenster sie liegen. Genau diese beiden Merkmale gewichtet die Bewertung
    /// darunter am höchsten; die Vorauswahl trifft jetzt dieselbe Wahl wie sie.</para>
    /// </remarks>
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

        // Der Anbieter stand bisher nur am Vertrag. Eine Rechnung vom Prüfplatz hat keinen —
        // ihr Empfänger steht an ihr selbst, und ohne ihn fände sie ihre Buchung nur über den
        // Betrag. Das erste Wort genügt: „Musterelektro Handels GmbH“ bucht als „Musterelektro“.
        var keyword = (invoice.Payee ?? invoice.Contract?.Provider)
            ?.Split(' ', '-')[0].ToLowerInvariant();

        var due = invoice.DueOn;
        var window = db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Expense && t.BookingDate >= from && t.BookingDate <= to);

        // Von der Fälligkeit aus nach beiden Seiten, jede Seite mit eigenem Vorrat. Nach
        // |Buchungstag − Fälligkeit| ließe sich in SQL nicht sortieren — DateOnly.DayNumber
        // übersetzt kein Anbieter; zwei gegenläufige Sortierungen ergeben dieselbe Reihenfolge.
        var before = await window
            .Where(t => t.BookingDate <= due)
            .OrderByDescending(t => t.BookingDate)
            .Take(CandidatePoolPerSide)
            .ToListAsync(ct);

        var after = await window
            .Where(t => t.BookingDate > due)
            .OrderBy(t => t.BookingDate)
            .Take(CandidatePoolPerSide)
            .ToListAsync(ct);

        // Das zweite, worauf die Bewertung schaut: der Betrag. Eine Buchung über die
        // Rechnungssumme gehört auch dann in die Auswahl, wenn sie weit von der Fälligkeit
        // entfernt im Fenster liegt. Die Spanne ist die, die unten noch Punkte gibt, um einen
        // Cent geweitet — in der Datenbank liegen Beträge auf Cent gerundet, und eine Grenze
        // genau dazwischen soll nicht entscheiden. Das Vorzeichen bleibt offen: gewertet wird
        // ohnehin der Betrag ohne es.
        var sameAmount = new List<Transaction>();
        if (invoice.Amount > 0m)
        {
            var low = (invoice.Amount * 0.95m) - 0.01m;
            var high = (invoice.Amount * 1.05m) + 0.01m;

            sameAmount = await window
                .Where(t => (t.Amount >= low && t.Amount <= high)
                            || (t.Amount <= -low && t.Amount >= -high))
                .OrderByDescending(t => t.BookingDate)
                .Take(CandidatePoolPerSide)
                .ToListAsync(ct);
        }

        // Drei Abfragen, eine Liste: dieselbe Buchung kann in zweien stecken, und ungetrackt
        // ist sie dann zweimal dasselbe Objekt mit zwei Instanzen. Die Id trennt sie.
        var rows = before.Concat(after).Concat(sameAmount)
            .DistinctBy(t => t.Id)
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.Id)
            .ToList();

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

    /// <summary>
    /// Markiert eine Rechnung als bezahlt, mit oder ohne zugeordnete Buchung.
    /// </summary>
    /// <remarks>
    /// <para><b>Beides ist erlaubt, und beides bleibt unterscheidbar.</b> Eine zugeordnete
    /// Buchung belegt die Zahlung; eine bestätigte Barzahlung behauptet sie. Der Status heißt in
    /// beiden Fällen „bezahlt“ — welcher der beiden Wege es war, steht danach an der Rechnung
    /// und nicht nur im Gedächtnis dessen, der geklickt hat.</para>
    /// <para><b>Mit Datum.</b> Vorher blieb von einer Barzahlung ein Status und sonst nichts:
    /// dass bezahlt wurde, aber nicht wann. Bei einer Buchung ist es deren Buchungstag — die
    /// Zahlung ist an dem Tag geflossen, an dem die Bank sie gebucht hat, und nicht an dem, an
    /// dem jemand sie zugeordnet hat.</para>
    /// </remarks>
    public async Task<InvoiceDetailDto?> PayAsync(
        int invoiceId, PayInvoiceRequest request, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is null)
        {
            return null;
        }

        DateOnly? gebucht = null;

        if (request.TransactionId is { } transactionId)
        {
            var buchung = await db.Transactions.AsNoTracking()
                              .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
                          ?? throw new ArgumentException("Die Buchung gibt es nicht.");

            invoice.TransactionId = transactionId;
            gebucht = buchung.BookingDate;
        }

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaidOn = request.PaidOn ?? gebucht ?? clock.Today;

        if (Clean(request.Note) is { } notiz)
        {
            invoice.PaymentNote = notiz;
        }

        await db.SaveChangesAsync(ct);
        return await GetInvoiceAsync(invoiceId, ct);
    }

    private static string? Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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

        var contractTotal = property.Contracts.Sum(c => c.YearAmount);
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
