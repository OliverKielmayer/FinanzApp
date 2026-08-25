using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// „Was bleibt übrig, und wo ist der Hebel?“
/// </summary>
/// <remarks>
/// <para>Rechnet ausschließlich auf dem Bestand — Buchungen, Budgets, Rechnungen, PKV-Vorgänge,
/// Vertragsfristen. Keine neue Eingabe, keine neue Tabelle.</para>
/// <para>Zwei Regeln, die überall gelten: <strong>Eigenanteile zählen als Ausgabe, erstattete
/// Beträge nicht.</strong> Und Umbuchungen zählen weiterhin weder als Einnahme noch als Ausgabe —
/// eine Verschiebung zwischen eigenen Konten ist beides nicht.</para>
/// </remarks>
public sealed class LiquidityService(FinanzAppDbContext db, IClock clock)
{
    /// <summary>Zeitraum der Auswertung „Wohin fließt es“.</summary>
    public const int DefaultMonths = 6;

    public async Task<LiquidityDto> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var booked = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind != TransactionKind.Transfer && t.BookingDate >= from && t.BookingDate <= to)
            .Select(t => new { t.Kind, t.Amount })
            .ToListAsync(ct);

        var income = booked.Where(t => t.Kind == TransactionKind.Income).Sum(t => t.Amount);
        var expenses = Math.Abs(booked.Where(t => t.Kind == TransactionKind.Expense).Sum(t => t.Amount));

        var stillDue = await LoadStillDueAsync(to, ct);
        var expected = await LoadExpectedAsync(ct);

        // Was nach den bekannten Verpflichtungen des Monats noch frei ist. Erwartete Eingänge
        // bleiben außen vor — worauf man noch wartet, kann man nicht ausgeben.
        var available = income - expenses - stillDue.Sum(x => x.Amount);

        return new LiquidityDto
        {
            Year = today.Year,
            Month = today.Month,
            Income = income,
            Expenses = expenses,
            SavingsRatePercent = income == 0 ? 0 : (income - expenses) / income * 100m,
            StillDue = stillDue,
            Expected = expected,
            AvailableAfterFixedCosts = available,
            PeriodEnd = to,
        };
    }

    /// <summary>Bekannt, aber noch nicht gebucht: offene Rechnungen und noch offene Eigenanteile.</summary>
    private async Task<IReadOnlyList<PendingAmountDto>> LoadStillDueAsync(
        DateOnly periodEnd, CancellationToken ct)
    {
        var items = new List<PendingAmountDto>();

        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Contract)
            .Where(i => i.Status == InvoiceStatus.Open)
            .OrderBy(i => i.DueOn)
            .ToListAsync(ct);

        items.AddRange(invoices.Select(invoice => new PendingAmountDto
        {
            Label = invoice.Contract is { } contract ? contract.Name : invoice.Subject,
            Amount = invoice.Amount,
            DueOn = invoice.DueOn,
            SourceType = LinkTargetType.Invoice,
            SourceId = invoice.Id,
        }));

        // Eigenanteile, für die noch keine Buchung hinterlegt ist.
        var bills = await db.MedicalBills.AsNoTracking()
            .Where(b => b.OwnShareTransactionId == null && b.Status != MedicalBillStatus.Rejected)
            .ToListAsync(ct);

        items.AddRange(bills
            .Where(bill => bill.OwnShare > 0)
            .Select(bill => new PendingAmountDto
            {
                Label = "Eigenanteil " + bill.Provider,
                Amount = bill.OwnShare,
                DueOn = bill.BillDate,
                SourceType = LinkTargetType.MedicalBill,
                SourceId = bill.Id,
            }));

        return items;
    }

    /// <summary>Erwartete Eingänge — offene PKV-Erstattungen.</summary>
    private async Task<IReadOnlyList<PendingAmountDto>> LoadExpectedAsync(CancellationToken ct)
    {
        var bills = await db.MedicalBills.AsNoTracking()
            .Where(b => b.Status != MedicalBillStatus.Completed && b.Status != MedicalBillStatus.Rejected)
            .ToListAsync(ct);

        return
        [
            .. bills
                .Where(bill => bill.OpenAmount > 0)
                .Select(bill => new PendingAmountDto
                {
                    Label = "PKV-Erstattung " + bill.Provider,
                    Amount = bill.OpenAmount,
                    DueOn = bill.SubmittedAt is { } at
                        ? DateOnly.FromDateTime(at).AddDays(MedicalBillService.UsualProcessingDays)
                        : null,
                    SourceType = LinkTargetType.MedicalBill,
                    SourceId = bill.Id,
                }),
        ];
    }

    /// <summary>
    /// „Wohin fließt es?“ — Ausgaben je Kategorie über mehrere Monate, getrennt nach fix und variabel.
    /// </summary>
    /// <remarks>
    /// Was „fix“ ist, wird nicht gepflegt, sondern erkannt: eine Kategorie gilt als fix, wenn sie in
    /// fast jedem Monat vorkommt und ihre Monatssummen wenig schwanken. Eine gepflegte Liste liefe
    /// dem Bestand irgendwann hinterher.
    /// </remarks>
    public async Task<CashFlowDto> GetCashFlowAsync(int months = DefaultMonths, CancellationToken ct = default)
    {
        months = Math.Clamp(months, 1, 36);
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));
        var to = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1);

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId != null
                        && t.BookingDate >= from && t.BookingDate <= to)
            .Select(t => new
            {
                CategoryId = t.CategoryId!.Value,
                CategoryName = t.Category!.Name,
                t.BookingDate,
                t.Amount,
            })
            .ToListAsync(ct);

        var budgets = await db.Budgets.AsNoTracking()
            .ToDictionaryAsync(b => b.CategoryId, b => b.PlannedPerMonth, ct);

        var total = rows.Sum(r => Math.Abs(r.Amount));
        var categories = new List<CashFlowCategoryDto>();
        var fixedShare = 0m;

        foreach (var group in rows.GroupBy(r => new { r.CategoryId, r.CategoryName }))
        {
            var amount = group.Sum(r => Math.Abs(r.Amount));
            var perMonth = group
                .GroupBy(r => new { r.BookingDate.Year, r.BookingDate.Month })
                .Select(m => m.Sum(r => Math.Abs(r.Amount)))
                .ToList();

            var isFixed = IsFixed(perMonth, months);
            if (isFixed)
            {
                fixedShare += amount;
            }

            var budget = budgets.TryGetValue(group.Key.CategoryId, out var planned) ? planned : (decimal?)null;
            var average = amount / months;
            var overBudget = budget is { } plan && plan > 0 && average > plan;

            categories.Add(new CashFlowCategoryDto
            {
                CategoryId = group.Key.CategoryId,
                Name = group.Key.CategoryName,
                Amount = amount,
                SharePercent = total == 0 ? 0 : amount / total * 100m,
                BudgetPerMonth = budget,
                OverBudget = overBudget,
                Note = BuildNote(group.Key.CategoryName, isFixed, budget, average),
            });
        }

        return new CashFlowDto
        {
            Months = months,
            FixedShare = fixedShare,
            VariableShare = total - fixedShare,
            Categories = [.. categories.OrderByDescending(c => c.Amount)],
        };
    }

    /// <summary>
    /// „Wo ist der Hebel?“ — Budgetüberschreitungen, kündbare Verträge, wiederkehrende Buchungen
    /// ohne Vertrag.
    /// </summary>
    /// <remarks>
    /// Beziffert wird nur, was sich beziffern lässt. Was ein Anbieterwechsel bringt, weiß die
    /// Anwendung nicht — dort steht die Gelegenheit ohne Zahl, statt eine zu erfinden.
    /// </remarks>
    public async Task<SavingsPotentialDto> GetSavingsPotentialAsync(CancellationToken ct = default)
    {
        const int window = 3;
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(window - 1));
        var items = new List<SavingsItemDto>();

        // 1. Budgets, die im Schnitt gerissen werden.
        var budgets = await db.Budgets.AsNoTracking().Include(b => b.Category).ToListAsync(ct);
        var spend = (await db.Transactions.AsNoTracking()
                .Where(t => t.Kind == TransactionKind.Expense && t.CategoryId != null && t.BookingDate >= from)
                .Select(t => new { CategoryId = t.CategoryId!.Value, t.Amount })
                .ToListAsync(ct))
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => Math.Abs(g.Sum(t => t.Amount)) / window);

        foreach (var budget in budgets)
        {
            if (!spend.TryGetValue(budget.CategoryId, out var average) || average <= budget.PlannedPerMonth)
            {
                continue;
            }

            items.Add(new SavingsItemDto
            {
                Title = budget.Name,
                Detail = $"Budget seit {window} Monaten überschritten",
                AmountPerMonth = Math.Round(average - budget.PlannedPerMonth, 2, MidpointRounding.AwayFromZero),
                CurrentCostPerMonth = Math.Round(average, 2, MidpointRounding.AwayFromZero),
                IsUrgent = true,
            });
        }

        // 2. Verträge und Versicherungen mit laufender Kündigungsfrist.
        foreach (var contract in await db.Contracts.AsNoTracking().ToListAsync(ct))
        {
            if (contract.NoticeDeadline is { } deadline && deadline >= today)
            {
                items.Add(new SavingsItemDto
                {
                    Title = contract.Name,
                    Detail = $"kündbar zum {Format(contract.NoticeToDate)} · Vergleich prüfen",
                    AmountPerMonth = null,
                    CurrentCostPerMonth = contract.MonthlyAmount,
                    IsUrgent = deadline.DayNumber - today.DayNumber <= 30,
                    SourceType = LinkTargetType.Contract,
                    SourceId = contract.Id,
                });
            }
        }

        // Nur Absicherung: eine Kapital-LV zu kündigen ist kein Sparpotential, sondern ein
        // Verlust - der Rückkaufswert liegt fast immer unter dem Eingezahlten.
        foreach (var policy in await db.Policies.AsNoTracking()
                     .Where(p => !p.IsCapitalForming).ToListAsync(ct))
        {
            if (policy.NoticeDeadline is { } deadline && deadline >= today)
            {
                items.Add(new SavingsItemDto
                {
                    Title = policy.Name,
                    Detail = $"Wechselfrist {Format(deadline)}",
                    AmountPerMonth = null,
                    CurrentCostPerMonth = Math.Round(policy.MonthlyPremium, 2, MidpointRounding.AwayFromZero),
                    IsUrgent = deadline.DayNumber - today.DayNumber <= 30,
                    SourceType = LinkTargetType.Policy,
                    SourceId = policy.Id,
                });
            }
        }

        // 3. Wiederkehrende Buchungen, zu denen kein Vertrag hinterlegt ist.
        items.AddRange(await FindUnmatchedRecurringAsync(ct));

        return new SavingsPotentialDto
        {
            Items = [.. items.OrderByDescending(i => i.IsUrgent).ThenByDescending(i => i.AmountPerMonth ?? 0)],
            TotalPerMonth = items.Sum(i => i.AmountPerMonth ?? 0m),
            UnquantifiedCount = items.Count(i => i.AmountPerMonth is null),
        };
    }

    /// <summary>Bis zu diesem Monatsbetrag gilt eine wiederkehrende Buchung als Abo.</summary>
    private const decimal SubscriptionCeiling = 100m;

    /// <summary>Wie stark der Betrag zwischen den Monaten schwanken darf.</summary>
    private const decimal SubscriptionSpread = 0.05m;

    /// <summary>
    /// Abos, die niemand mehr auf dem Schirm hat: Monat für Monat derselbe kleine Betrag beim
    /// selben Empfänger, ohne hinterlegten Vertrag.
    /// </summary>
    /// <remarks>
    /// Die beiden Schranken sind der Unterschied zwischen einem brauchbaren Hinweis und Unsinn.
    /// Ohne Betragsgrenze landet die Miete in der Liste, ohne Schwankungsgrenze der
    /// Wocheneinkauf — beides lässt sich nicht kündigen.
    /// </remarks>
    private async Task<IReadOnlyList<SavingsItemDto>> FindUnmatchedRecurringAsync(CancellationToken ct)
    {
        const int window = 4;
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(window - 1));

        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense && t.BookingDate >= from)
            .Select(t => new { t.Payee, t.BookingDate, t.Amount })
            .ToListAsync(ct);

        var known = (await db.Contracts.AsNoTracking().Select(c => c.Provider).ToListAsync(ct))
            .Concat(await db.Policies.AsNoTracking().Select(p => p.Provider).ToListAsync(ct))
            .Select(name => name.Split(' ', '-')[0])
            .Where(name => name.Length > 2)
            .ToList();

        var found = new List<SavingsItemDto>();
        foreach (var group in rows.GroupBy(r => r.Payee))
        {
            var monthCount = group
                .Select(r => new { r.BookingDate.Year, r.BookingDate.Month })
                .Distinct()
                .Count();

            if (monthCount < window - 1)
            {
                continue;
            }

            if (known.Any(name => group.Key.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var monthlySums = group
                .GroupBy(r => new { r.BookingDate.Year, r.BookingDate.Month })
                .Select(m => Math.Abs(m.Sum(r => r.Amount)))
                .ToList();

            var perMonth = monthlySums.Average();
            if (perMonth > SubscriptionCeiling)
            {
                continue;
            }

            var spread = perMonth == 0 ? 1m : (monthlySums.Max() - monthlySums.Min()) / perMonth;
            if (spread > SubscriptionSpread)
            {
                continue;
            }

            found.Add(new SavingsItemDto
            {
                Title = group.Key,
                Detail = "wiederkehrende Buchung ohne Vertrag",
                AmountPerMonth = Math.Round(perMonth, 2, MidpointRounding.AwayFromZero),
                CurrentCostPerMonth = Math.Round(perMonth, 2, MidpointRounding.AwayFromZero),
                IsUrgent = false,
            });
        }

        return [.. found.OrderByDescending(f => f.AmountPerMonth).Take(5)];
    }

    /// <summary>
    /// Fix heißt: kommt in fast jedem Monat vor und schwankt wenig. Der Schwellwert ist bewusst
    /// großzügig — eine Stromrechnung mit Nachzahlung soll nicht plötzlich als variabel gelten.
    /// </summary>
    private static bool IsFixed(List<decimal> monthlySums, int months)
    {
        if (months < 2 || monthlySums.Count < months - 1)
        {
            return false;
        }

        var average = monthlySums.Average();
        if (average == 0)
        {
            return false;
        }

        var spread = (monthlySums.Max() - monthlySums.Min()) / average;
        return spread <= 0.35m;
    }

    private static string? BuildNote(string category, bool isFixed, decimal? budget, decimal average)
    {
        if (category.Equals("Gesundheit", StringComparison.OrdinalIgnoreCase))
        {
            return "nur Eigenanteile";
        }

        if (budget is { } plan && plan > 0 && average > plan)
        {
            var over = (average - plan) / plan * 100m;
            return GermanFormat.Percent(over) + " über Budget";
        }

        return isFixed ? "fix" : null;
    }

    private static string Format(DateOnly? date)
        => date?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "offen";
}
