using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Der Vorgänge-Tab: alles Unerledigte aus allen Bereichen an einer Stelle.
/// </summary>
/// <remarks>
/// <para>Die meisten Einträge entstehen von selbst — aus Vertragsende minus Kündigungsfrist, aus
/// einer Rechnungsfälligkeit, aus einer Erstattung ohne Zahlungseingang. Erzeugt wird beim Lesen
/// der Liste; der eindeutige Index auf Quelle und Quell-Id sorgt dafür, dass daraus keine
/// Dubletten werden.</para>
/// <para>Der Erzeugungsgrund steht mit in der Aufgabe. Ohne ihn stünde im Vorgänge-Tab eine Zeile,
/// die niemand angelegt hat und deren Herkunft niemand erklären kann.</para>
/// </remarks>
public sealed class TaskService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>Wie früh vor einer Kündigungsfrist erinnert wird.</summary>
    private const int NoticeLeadDays = 90;

    /// <summary>Wie früh vor einer Rechnungsfälligkeit erinnert wird. Ein Monat Vorlauf reicht,
    /// um noch zu widersprechen oder Geld bereitzulegen.</summary>
    private const int InvoiceLeadDays = 30;

    public async Task<TaskListDto> GetListAsync(TaskState? state = null, CancellationToken ct = default)
    {
        await SynchroniseAsync(ct);

        var today = clock.Today;
        var rows = await db.TaskItems.AsNoTracking().ToListAsync(ct);
        var amounts = await LoadAmountsAsync(rows, ct);

        var items = rows
            .Select(row => new TaskItemDto
            {
                Id = row.Id,
                Title = row.Title,
                Detail = row.Detail,
                DueOn = row.DueOn,
                State = row.State,
                Source = row.Source,
                SourceType = row.SourceType,
                SourceId = row.SourceId,
                Amount = amounts.GetValueOrDefault(row.Id),
                DaysOverdue = row.DueOn is { } due ? today.DayNumber - due.DayNumber : 0,
            })
            .OrderBy(i => i.State)
            .ThenByDescending(i => i.IsOverdue)
            .ThenBy(i => i.DueOn ?? DateOnly.MaxValue)
            .ToList();

        return new TaskListDto
        {
            Items = [.. items.Where(i => state is null || i.State == state)],
            OpenCount = items.Count(i => i.State == TaskState.Open),
            WaitingCount = items.Count(i => i.State == TaskState.Waiting),
            DoneCount = items.Count(i => i.State == TaskState.Done),
        };
    }

    /// <summary>Kurzfassung für das Banner auf dem Dashboard.</summary>
    public async Task<OpenWorkSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        await SynchroniseAsync(ct);

        var today = clock.Today;
        var open = await db.TaskItems.AsNoTracking()
            .Where(t => t.State != TaskState.Done)
            .ToListAsync(ct);

        var expected = (await db.MedicalBills.AsNoTracking()
                .Where(b => b.Status != MedicalBillStatus.Completed && b.Status != MedicalBillStatus.Rejected)
                .ToListAsync(ct))
            .Sum(b => b.OpenAmount);

        var dueInvoices = await db.Invoices.AsNoTracking()
            .CountAsync(i => i.Status == InvoiceStatus.Open, ct);

        return new OpenWorkSummaryDto
        {
            OpenCount = open.Count,
            ExpectedReimbursement = expected,
            DueInvoiceCount = dueInvoices,
            OverdueCount = open.Count(t => t.DueOn is { } due && due < today),
        };
    }

    public async Task<TaskItemDto> CreateAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Die Aufgabe braucht einen Titel.");
        }

        var item = new TaskItem
        {
            Title = request.Title.Trim(),
            Detail = request.Detail,
            DueOn = request.DueOn,
            State = TaskState.Open,
            Source = TaskSource.Manual,
            CreatedAt = clock.Now,
        };

        db.TaskItems.Add(item);
        await db.SaveChangesAsync(ct);

        var today = clock.Today;
        return new TaskItemDto
        {
            Id = item.Id,
            Title = item.Title,
            Detail = item.Detail,
            DueOn = item.DueOn,
            State = item.State,
            Source = item.Source,
            DaysOverdue = item.DueOn is { } due ? today.DayNumber - due.DayNumber : 0,
        };
    }

    public async Task<bool> SetStateAsync(int id, TaskState state, CancellationToken ct = default)
    {
        var item = await db.TaskItems.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (item is null)
        {
            return false;
        }

        item.State = state;
        item.CompletedAt = state == TaskState.Done ? clock.Now : null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Leitet Aufgaben aus dem Bestand ab und hält sie aktuell. Läuft vor jedem Lesen der Liste.
    /// </summary>
    private async Task SynchroniseAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var existing = await db.TaskItems.ToListAsync(ct);

        TaskItem? Find(TaskSource source, LinkTargetType type, int id)
            => existing.FirstOrDefault(t => t.Source == source && t.SourceType == type && t.SourceId == id);

        var changed = false;

        void Upsert(
            TaskSource source, LinkTargetType type, int id, string title, string detail, DateOnly? due)
        {
            var item = Find(source, type, id);
            if (item is null)
            {
                db.TaskItems.Add(new TaskItem
                {
                    Title = title,
                    Detail = detail,
                    DueOn = due,
                    State = TaskState.Open,
                    Source = source,
                    SourceType = type,
                    SourceId = id,
                    CreatedAt = clock.Now,
                });
                changed = true;
                return;
            }

            // Eine erledigte Aufgabe wird nicht wiederbelebt; die übrigen bleiben am Bestand.
            if (item.State != TaskState.Done && (item.Title != title || item.Detail != detail || item.DueOn != due))
            {
                item.Title = title;
                item.Detail = detail;
                item.DueOn = due;
                changed = true;
            }
        }

        void Close(TaskSource source, LinkTargetType type, int id)
        {
            var item = Find(source, type, id);
            if (item is { State: not TaskState.Done })
            {
                item.State = TaskState.Done;
                item.CompletedAt = clock.Now;
                changed = true;
            }
        }

        // Kündigungsfristen aus Absicherungsverträgen. Vorsorge bleibt draußen: dort ist das
        // Vertragsende der Ablauf, kein Kündigungstermin.
        foreach (var policy in await db.Policies.AsNoTracking()
                     .Where(p => !p.IsCapitalForming).ToListAsync(ct))
        {
            if (policy.NoticeDeadline is not { } deadline)
            {
                continue;
            }

            // Auf den Tisch kommt die Frist, wenn der Termin nah ist — oder wenn die gesetzte
            // Erinnerung in Sicht ist. Sonst verschwände ein Vertrag, dessen Vergleich jetzt
            // ansteht, nur weil sein Termin noch ein Jahr entfernt liegt.
            var reminderDays = policy.NoticeReminderOn is { } remind
                ? remind.DayNumber - today.DayNumber
                : int.MaxValue;

            var inSight = deadline.DayNumber - today.DayNumber <= NoticeLeadDays
                          || reminderDays <= NoticeLeadDays;

            if (inSight && deadline >= today.AddDays(-30))
            {
                var reason = reminderDays is > 0 and <= NoticeLeadDays
                    ? $"{policy.Provider} · Kündigung bis {Format(deadline)} · in {reminderDays} Tagen erinnern"
                    : $"{policy.Provider} · Vertragsende {Format(policy.EndsOn)}";

                Upsert(
                    TaskSource.ContractNotice, LinkTargetType.Policy, policy.Id,
                    $"Kündigungsfrist {policy.Name}",
                    reason,
                    deadline);
            }
        }

        // Kündigungsfristen aus Versorgungsverträgen.
        foreach (var contract in await db.Contracts.AsNoTracking().ToListAsync(ct))
        {
            if (contract.NoticeDeadline is not { } deadline)
            {
                continue;
            }

            if (deadline.DayNumber - today.DayNumber <= NoticeLeadDays && deadline >= today.AddDays(-30))
            {
                Upsert(
                    TaskSource.ContractNotice, LinkTargetType.Contract, contract.Id,
                    $"Kündigungsfrist {contract.Name}",
                    $"{contract.Provider} · kündbar zum {Format(contract.NoticeToDate)}",
                    deadline);
            }
        }

        // Offene Rechnungen mit näher rückender Fälligkeit.
        foreach (var invoice in await db.Invoices.AsNoTracking().Include(i => i.Contract).ToListAsync(ct))
        {
            if (invoice.Status != InvoiceStatus.Open)
            {
                Close(TaskSource.InvoiceDue, LinkTargetType.Invoice, invoice.Id);
                continue;
            }

            if (invoice.DueOn.DayNumber - today.DayNumber <= InvoiceLeadDays)
            {
                Upsert(
                    TaskSource.InvoiceDue, LinkTargetType.Invoice, invoice.Id,
                    invoice.Subject,
                    invoice.Contract is { } contract ? $"{contract.Provider} · offen" : "offen",
                    invoice.DueOn);
            }
        }

        // PKV-Vorgänge, die noch auf Geld warten.
        foreach (var bill in await db.MedicalBills.AsNoTracking().ToListAsync(ct))
        {
            if (bill.Status is MedicalBillStatus.Completed or MedicalBillStatus.Rejected)
            {
                Close(TaskSource.ReimbursementOverdue, LinkTargetType.MedicalBill, bill.Id);
                Close(TaskSource.MedicalBillOpen, LinkTargetType.MedicalBill, bill.Id);
                continue;
            }

            if (bill.SubmittedAt is { } submitted)
            {
                var due = DateOnly.FromDateTime(submitted).AddDays(MedicalBillService.UsualProcessingDays);
                Upsert(
                    TaskSource.ReimbursementOverdue, LinkTargetType.MedicalBill, bill.Id,
                    $"PKV-Erstattung {bill.Provider}",
                    $"eingereicht {Format(DateOnly.FromDateTime(submitted))} · übliche Dauer "
                        + $"{MedicalBillService.UsualProcessingDays} T",
                    due);
            }
            else
            {
                Upsert(
                    TaskSource.MedicalBillOpen, LinkTargetType.MedicalBill, bill.Id,
                    $"Arztrechnung {bill.Provider}",
                    "noch nicht eingereicht",
                    null);
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Beträge zu den Aufgaben, aus dem jeweiligen Quellobjekt.</summary>
    private async Task<Dictionary<int, decimal?>> LoadAmountsAsync(
        List<TaskItem> items, CancellationToken ct)
    {
        var bills = await db.MedicalBills.AsNoTracking().ToDictionaryAsync(b => b.Id, ct);
        var invoices = await db.Invoices.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var policies = await db.Policies.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var contracts = await db.Contracts.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);

        return items.ToDictionary(item => item.Id, item => (item.SourceType, item.SourceId) switch
        {
            (LinkTargetType.MedicalBill, { } id) when bills.TryGetValue(id, out var bill)
                => (decimal?)(bill.Status == MedicalBillStatus.Recorded ? bill.GrossAmount : bill.OpenAmount),
            (LinkTargetType.Invoice, { } id) when invoices.TryGetValue(id, out var invoice)
                => invoice.Amount,
            (LinkTargetType.Policy, { } id) when policies.TryGetValue(id, out var policy)
                => policy.Premium,
            (LinkTargetType.Contract, { } id) when contracts.TryGetValue(id, out var contract)
                => contract.MonthlyAmount,
            _ => null,
        });
    }

    private static string Format(DateOnly? date)
        => date?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "offen";
}
