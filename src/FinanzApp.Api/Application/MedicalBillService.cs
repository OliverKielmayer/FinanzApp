using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// PKV-Vorgänge: Arztrechnung erfassen, Statuskette führen, Erstattung einer Buchung zuordnen.
/// </summary>
/// <remarks>
/// Die Regel, die überall durchschlägt: <strong>der Eigenanteil ist keine offene Forderung.</strong>
/// Er ist eine gebuchte Ausgabe. Offen ist ausschließlich die erwartete Erstattung, und auch die
/// nur, solange keine Zahlung zugeordnet ist.
/// </remarks>
public sealed class MedicalBillService(
    FinanzAppDbContext db,
    DocumentService documents,
    IClock clock)
{
    /// <summary>Übliche Bearbeitungsdauer der Versicherung. Maßstab für „überfällig“.</summary>
    public const int UsualProcessingDays = 14;

    private static readonly (MedicalBillStatus Status, string Label)[] Chain =
    [
        (MedicalBillStatus.Recorded, "Erfasst"),
        (MedicalBillStatus.Submitted, "Eingereicht"),
        (MedicalBillStatus.SettlementReceived, "Abrechnung erhalten"),
        (MedicalBillStatus.PaymentReceived, "Zahlung eingegangen"),
        (MedicalBillStatus.Completed, "Abgeschlossen"),
    ];

    public async Task<IReadOnlyList<MedicalBillListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var rows = await db.MedicalBills.AsNoTracking()
            .OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.Id)
            .ToListAsync(ct);

        return [.. rows.Select(ToListItem)];
    }

    public async Task<MedicalBillDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var bill = await db.MedicalBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
        {
            return null;
        }

        var (nextStatus, nextLabel) = NextStep(bill.Status);

        return new MedicalBillDetailDto
        {
            Id = bill.Id,
            Provider = bill.Provider,
            BillDate = bill.BillDate,
            BillNumber = bill.BillNumber,
            GrossAmount = bill.GrossAmount,
            OwnShare = bill.OwnShare,
            ExpectedReimbursement = bill.ExpectedReimbursement,
            ActualReimbursement = bill.ActualReimbursement,
            OpenAmount = bill.OpenAmount,
            Status = bill.Status,
            DaysWaiting = DaysWaiting(bill),
            UsualProcessingDays = UsualProcessingDays,
            Notes = bill.Notes,
            Steps = BuildSteps(bill),
            Documents = await documents.GetForTargetAsync(LinkTargetType.MedicalBill, bill.Id, ct),
            ReimbursementTransactionId = bill.ReimbursementTransactionId,
            NextStatus = nextStatus,
            NextActionLabel = nextLabel,
        };
    }

    public async Task<MedicalBillDetailDto> CreateAsync(
        CreateMedicalBillRequest request, CancellationToken ct = default)
    {
        if (request.GrossAmount <= 0)
        {
            throw new ArgumentException("Der Rechnungsbetrag muss größer als null sein.");
        }

        if (request.OwnShare < 0 || request.OwnShare > request.GrossAmount)
        {
            throw new ArgumentException("Der Eigenanteil muss zwischen null und dem Rechnungsbetrag liegen.");
        }

        // Ohne ausdrückliche Angabe ist die erwartete Erstattung der Rest nach Eigenanteil.
        var expected = request.ExpectedReimbursement ?? request.GrossAmount - request.OwnShare;
        if (expected < 0 || expected > request.GrossAmount)
        {
            throw new ArgumentException("Die erwartete Erstattung passt nicht zum Rechnungsbetrag.");
        }

        var bill = new MedicalBill
        {
            Provider = request.Provider.Trim(),
            BillDate = request.BillDate,
            BillNumber = string.IsNullOrWhiteSpace(request.BillNumber) ? null : request.BillNumber.Trim(),
            GrossAmount = request.GrossAmount,
            OwnShare = request.OwnShare,
            ExpectedReimbursement = expected,
            Status = MedicalBillStatus.Recorded,
            Notes = request.Notes,
            CreatedAt = clock.Now,
        };

        db.MedicalBills.Add(bill);
        await db.SaveChangesAsync(ct);

        if (request.DocumentId is { } documentId)
        {
            await documents.LinkAsync(documentId, LinkTargetType.MedicalBill, bill.Id, ct);
        }

        return (await GetAsync(bill.Id, ct))!;
    }

    /// <summary>Setzt den Vorgang auf die nächste Station und hält den Zeitpunkt fest.</summary>
    public async Task<MedicalBillDetailDto?> AdvanceAsync(
        int id, MedicalBillStatus status, CancellationToken ct = default)
    {
        var bill = await db.MedicalBills.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
        {
            return null;
        }

        var now = clock.Now;
        bill.Status = status;

        switch (status)
        {
            case MedicalBillStatus.Submitted:
                bill.SubmittedAt ??= now;
                break;
            case MedicalBillStatus.SettlementReceived:
                bill.SubmittedAt ??= now;
                bill.SettlementReceivedAt ??= now;
                break;
            case MedicalBillStatus.PaymentReceived or MedicalBillStatus.Completed:
                bill.PaidAt ??= now;
                break;
            case MedicalBillStatus.Rejected:
                // Abgelehnt heißt: es kommt nichts mehr. Die Forderung ist damit erledigt.
                bill.ActualReimbursement = 0m;
                break;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Buchungen, die zur erwarteten Erstattung passen könnten — bewertet nach Betrag, Datum und
    /// Verwendungszweck.
    /// </summary>
    /// <remarks>
    /// Die Bewertung <em>schlägt vor</em>, sie entscheidet nicht. Zugeordnet wird erst, wenn
    /// jemand bestätigt; eine automatische Verknüpfung würde bei jedem Fehltreffer stillschweigend
    /// die Buchhaltung verfälschen.
    /// </remarks>
    public async Task<IReadOnlyList<PaymentCandidateDto>> GetPaymentCandidatesAsync(
        int id, CancellationToken ct = default)
    {
        var bill = await db.MedicalBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
        {
            return [];
        }

        var from = DateOnly.FromDateTime(bill.SubmittedAt ?? bill.BillDate.ToDateTime(TimeOnly.MinValue));
        var to = from.AddDays(120);

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Income && t.BookingDate >= from && t.BookingDate <= to)
            .OrderByDescending(t => t.BookingDate)
            .Take(50)
            .ToListAsync(ct);

        var scored = rows
            .Select(t => Score(bill, t, from))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.BookingDate)
            .Take(6)
            .ToList();

        return scored.Count == 0
            ? []
            : [.. scored.Select((c, index) => c with { IsBestMatch = index == 0 })];
    }

    /// <summary>Verknüpft die Erstattung mit einer Buchung und schließt den Vorgang ab.</summary>
    public async Task<MedicalBillDetailDto?> LinkPaymentAsync(
        int id, LinkPaymentRequest request, CancellationToken ct = default)
    {
        var bill = await db.MedicalBills.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bill is null)
        {
            return null;
        }

        var transaction = await db.Transactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct);

        if (transaction is null)
        {
            throw new ArgumentException("Die Buchung gibt es nicht.");
        }

        var actual = request.ActualAmount ?? Math.Abs(transaction.Amount);
        bill.ReimbursementTransactionId = transaction.Id;
        bill.ActualReimbursement = actual;
        bill.PaidAt ??= clock.Now;

        // Weniger als erwartet heißt teilweise erstattet — der Rest bleibt sichtbar, statt still
        // unter den Tisch zu fallen.
        bill.Status = actual + 0.005m < bill.ExpectedReimbursement
            ? MedicalBillStatus.PartiallyReimbursed
            : MedicalBillStatus.Completed;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Summe der noch offenen Erstattungen. Ohne Eigenanteile.</summary>
    public async Task<decimal> GetOpenReimbursementTotalAsync(CancellationToken ct = default)
        => (await db.MedicalBills.AsNoTracking()
                .Where(b => b.Status != MedicalBillStatus.Completed && b.Status != MedicalBillStatus.Rejected)
                .ToListAsync(ct))
            .Sum(b => b.OpenAmount);

    private PaymentCandidateDto Score(MedicalBill bill, Transaction transaction, DateOnly from)
    {
        var amount = Math.Abs(transaction.Amount);
        var expected = bill.ExpectedReimbursement;
        var haystack = (transaction.Payee + " " + (transaction.Note ?? string.Empty)).ToLowerInvariant();

        var score = 0;
        var reasons = new List<string>();

        var deviation = expected == 0 ? 1m : Math.Abs(amount - expected) / expected;
        if (deviation < 0.001m)
        {
            score += 50;
            reasons.Add("Betrag stimmt");
        }
        else if (deviation <= 0.05m)
        {
            score += 30;
            reasons.Add("Betrag fast gleich");
        }
        else if (deviation <= 0.25m)
        {
            score += 10;
            reasons.Add("Betrag weicht ab");
        }
        else
        {
            reasons.Add("Betrag weicht deutlich ab");
        }

        var days = transaction.BookingDate.DayNumber - from.DayNumber;
        if (days is >= 0 and <= 30)
        {
            score += 30;
        }
        else if (days is > 30 and <= 60)
        {
            score += 15;
        }

        if (bill.BillNumber is { Length: > 3 } number
            && haystack.Contains(number.ToLowerInvariant(), StringComparison.Ordinal))
        {
            score += 20;
            reasons.Insert(0, "Rechnungsnummer im Verwendungszweck");
        }
        else if (ProviderKeyword(bill.Provider) is { } keyword
                 && haystack.Contains(keyword, StringComparison.Ordinal))
        {
            score += 10;
            reasons.Insert(0, "Name des Rechnungsstellers");
        }

        return new PaymentCandidateDto
        {
            TransactionId = transaction.Id,
            BookingDate = transaction.BookingDate,
            Payee = transaction.Payee,
            Amount = amount,
            AccountName = transaction.Account?.Name ?? string.Empty,
            Score = Math.Min(100, score),
            Reason = string.Join(" · ", reasons),
            IsBestMatch = false,
        };
    }

    /// <summary>Das längste Wort des Rechnungsstellers — „Dr.“ und „Praxis“ helfen beim Suchen nicht.</summary>
    private static string? ProviderKeyword(string provider)
        => provider
            .Split([' ', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 3)
            .OrderByDescending(part => part.Length)
            .FirstOrDefault()
            ?.ToLowerInvariant();

    private int? DaysWaiting(MedicalBill bill)
    {
        if (bill.SubmittedAt is not { } submitted
            || bill.Status is MedicalBillStatus.Completed or MedicalBillStatus.Rejected)
        {
            return null;
        }

        return Math.Max(0, (clock.Today.DayNumber - DateOnly.FromDateTime(submitted).DayNumber));
    }

    private static IReadOnlyList<MedicalBillStepDto> BuildSteps(MedicalBill bill)
    {
        // Abgelehnt und teilweise erstattet stehen neben der Kette; die Kette selbst zeigt dann,
        // wie weit der Vorgang gekommen ist.
        var reached = bill.Status switch
        {
            MedicalBillStatus.Rejected => MedicalBillStatus.Submitted,
            MedicalBillStatus.PartiallyReimbursed => MedicalBillStatus.PaymentReceived,
            var status => status,
        };

        return
        [
            .. Chain.Select(step => new MedicalBillStepDto
            {
                Status = step.Status,
                Label = step.Label,
                Done = step.Status <= reached,
                Current = step.Status == reached,
                At = DateOf(bill, step.Status),
            }),
        ];
    }

    private static DateOnly? DateOf(MedicalBill bill, MedicalBillStatus status) => status switch
    {
        MedicalBillStatus.Recorded => bill.BillDate,
        MedicalBillStatus.Submitted => bill.SubmittedAt is { } at ? DateOnly.FromDateTime(at) : null,
        MedicalBillStatus.SettlementReceived =>
            bill.SettlementReceivedAt is { } at ? DateOnly.FromDateTime(at) : null,
        MedicalBillStatus.PaymentReceived or MedicalBillStatus.Completed =>
            bill.PaidAt is { } at ? DateOnly.FromDateTime(at) : null,
        _ => null,
    };

    /// <summary>Die Primäraktion ist immer der nächste Schritt der Kette.</summary>
    private static (MedicalBillStatus? Status, string? Label) NextStep(MedicalBillStatus status) => status switch
    {
        MedicalBillStatus.Recorded => (MedicalBillStatus.Submitted, "Als eingereicht markieren"),
        MedicalBillStatus.Submitted => (MedicalBillStatus.SettlementReceived, "Abrechnung erhalten"),
        MedicalBillStatus.SettlementReceived => (MedicalBillStatus.PaymentReceived, "Zahlung zuordnen"),
        MedicalBillStatus.PaymentReceived => (MedicalBillStatus.Completed, "Vorgang abschließen"),
        _ => (null, null),
    };

    private MedicalBillListItemDto ToListItem(MedicalBill bill) => new()
    {
        Id = bill.Id,
        Provider = bill.Provider,
        BillDate = bill.BillDate,
        GrossAmount = bill.GrossAmount,
        OwnShare = bill.OwnShare,
        ExpectedReimbursement = bill.ExpectedReimbursement,
        OpenAmount = bill.OpenAmount,
        Status = bill.Status,
        DaysWaiting = DaysWaiting(bill),
    };
}
