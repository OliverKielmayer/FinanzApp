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

    /// <summary>Wie früh vor einer Frist aus einem Schreiben erinnert wird.</summary>
    private const int DocumentDueLeadDays = 30;

    /// <summary>
    /// Wie viele Zahlungsvorschläge höchstens an einer Zeile stehen.
    /// </summary>
    /// <remarks>
    /// Ein runder Betrag trifft auf einem Konto mit Jahren an Buchungen viele Male. Alle
    /// anzubieten hiesse, die Entscheidung durch eine Suchaufgabe zu ersetzen; die drei
    /// nächsten an der Frist sind die, die überhaupt in Frage kommen. Wer dort nichts findet,
    /// hakt den Vorgang von Hand ab — das kann er bei einem Schreiben ohnehin.
    /// </remarks>
    private const int MostSuggestions = 3;

    /// <summary>
    /// Wie lange eine abgelaufene Frist aus einem Schreiben noch auf dem Tisch bleibt.
    /// </summary>
    /// <remarks>
    /// Anders als bei einer erfassten Rechnung weiß die Anwendung hier nicht, ob bezahlt wurde
    /// — es gibt keinen Status, nur den Satz auf dem Papier. Ohne untere Grenze stünde der
    /// Vorgänge-Tab irgendwann voller Fristen aus abgelegten Jahren: Archäologie statt Arbeit.
    /// Einen Monat bleibt sie liegen, danach gilt sie als erledigt oder als verpasst, und
    /// beides ändert eine Aufgabe nicht mehr.
    /// </remarks>
    private const int DocumentDueGraceDays = 30;

    public async Task<TaskListDto> GetListAsync(TaskState? state = null, CancellationToken ct = default)
    {
        await SynchroniseAsync(ct);

        var today = clock.Today;
        var rows = await db.TaskItems.AsNoTracking().ToListAsync(ct);
        var amounts = await LoadAmountsAsync(rows, ct);

        // Was nach dem Abgleich offen blieb: Treffer, die sich nicht von allein entscheiden
        // liessen. Sie gehören an die Zeile, zu der sie gehören, und nicht in eine eigene Liste
        // — der Vorgang ist die Frage, der Vorschlag ist die mögliche Antwort.
        var vorschlaege = await PaymentMatchesAsync(
            rows.Where(r => r.State != TaskState.Done).ToList(), ct);

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
                PaymentSuggestions = vorschlaege.TryGetValue(row.Id, out var treffer)
                    ? [.. treffer.Take(MostSuggestions).Select(Zahlung)]
                    : [],
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

        // Belege, deren gelesene Werte niemand übernommen hat.
        //
        // Der Scaneingang zeigt sie, solange sie dort stehen. Weggeräumt verschwinden sie
        // daraus, und danach zeigte nichts mehr auf sie: die Zahl steht am Beleg, aber nur,
        // wer ihn ohnehin öffnet, sieht sie. Genau so sind dreizehn Statusreporte eines
        // Vertrags durchgerutscht, und am Vertrag stand hinterher eine Null.
        //
        // Ohne Termin, als einzige Quelle hier — es läuft nichts ab, es bleibt nur etwas
        // liegen. Ein erfundenes Fälligkeitsdatum machte daraus eine Dringlichkeit, die das
        // Papier nicht hergibt.
        var offenJeBeleg = await db.DocumentExtractions.AsNoTracking()
            .Where(x => !x.Confirmed)
            .GroupBy(x => x.DocumentId)
            .Select(g => new { DocumentId = g.Key, Offen = g.Count() })
            .ToDictionaryAsync(g => g.DocumentId, g => g.Offen, ct);

        var belege = await db.Documents.AsNoTracking()
            .Where(d => offenJeBeleg.Keys.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Title, ct);

        foreach (var (belegId, offen) in offenJeBeleg)
        {
            // Ein Leseabdruck ohne Beleg ist nicht vorgesehen — der Fremdschlüssel räumt mit.
            // Kommt er doch vor, entsteht kein Vorgang, der ins Leere führt.
            if (!belege.TryGetValue(belegId, out var titel))
            {
                continue;
            }

            Upsert(
                TaskSource.DocumentValuesOpen, LinkTargetType.Document, belegId,
                $"Werte übernehmen: {titel}",
                offen == 1
                    ? "1 gelesene Angabe steht noch nicht im Objekt"
                    : $"{offen} gelesene Angaben stehen noch nicht im Objekt",
                due: null);
        }

        // Geschlossen wird über die vorhandenen Vorgänge und nicht über die Belege: ein
        // gelöschter Beleg taucht in keiner Schleife über Belege mehr auf, und sein Vorgang
        // bliebe für immer offen und führte nirgendwohin.
        foreach (var vorgang in existing.Where(t =>
                     t.Source == TaskSource.DocumentValuesOpen
                     && t.SourceId is { } belegId
                     && !offenJeBeleg.ContainsKey(belegId)))
        {
            Close(TaskSource.DocumentValuesOpen, LinkTargetType.Document, vorgang.SourceId!.Value);
        }

        // Schreiben, die etwas verlangen.
        //
        // Die Forderung steht auf dem Papier und sonst nirgends — keine erfasste Rechnung
        // kennt sie, kein Vertrag, kein Status. Deshalb trägt der Vorgang die Zeile mit, aus
        // der sie stammt: Eine Forderung ohne ihren Satz ist eine Behauptung; mit ihm sieht
        // jeder in zwei Sekunden nach, ob richtig gelesen wurde, ohne den Beleg zu öffnen.
        var mitForderung = await db.Documents.AsNoTracking()
            .Where(d => d.ActionLine != null && d.ActionLine != "")
            .ToListAsync(ct);

        var imFenster = new HashSet<int>();

        foreach (var beleg in mitForderung)
        {
            // Mit Termin gilt das Fenster. Ohne Termin bleibt der Vorgang stehen, bis ihn
            // jemand erledigt: Es läuft nichts ab, es liegt nur etwas — und eine erfundene
            // Frist machte daraus eine Dringlichkeit, die das Papier nicht hergibt.
            //
            // Das Fenster gilt nur für Termine, die auf dem Papier stehen. Eine Frist, die aus
            // einer Spanne gerechnet wurde, ist unsere Rechnung und nicht die Aussage des
            // Absenders. Sie einen Vorgang schließen zu lassen wäre das Gegenteil dessen,
            // wofür sie da ist: Eine Arztrechnung vom Frühjahr mit „innerhalb von 14 Tagen“
            // liegt beim ersten Nachziehen weit außerhalb des Fensters, verschwände aus der
            // Liste — und erledigt bleibt erledigt. Vorher stand sie ohne Frist da und blieb
            // sichtbar; das Nachtragen darf sie nicht kosten.
            //
            // Sie steht danach überfällig in der Liste, und das ist richtig: Das Papier
            // verlangt etwas, der Termin ist verstrichen, und abhaken kann es nur ein Mensch.
            var genannt = ActionEvidence.StatesDate(beleg.ActionLine);

            if (beleg.DueDate is { } frist && genannt)
            {
                var tage = frist.DayNumber - today.DayNumber;
                if (tage > DocumentDueLeadDays || tage < -DocumentDueGraceDays)
                {
                    continue;
                }
            }

            imFenster.Add(beleg.Id);

            // Der hergeleitete Name und nicht der gespeicherte Titel: Ein Scanner nennt seine
            // Seiten durch, und „Zu erledigen: Scannen0003“ zwingt dazu, den Beleg zu öffnen,
            // nur um zu sehen, worum es geht. Elf der hundertfünfundfünfzig Belege im echten
            // Bestand heißen so.
            var name = BelegName.Fuer(
                beleg.Title, beleg.FileName, beleg.HasGeneratedName, beleg.DocumentDate);

            // Eine Frist, die der Satz nicht nennt, ist gerechnet — und das gehört dazu. Wer
            // sie am Papier nachschlägt, findet dort „innerhalb von 14 Tagen“ und kein Datum.
            var errechnet = beleg.DueDate is not null && !genannt;

            Upsert(
                TaskSource.DocumentAction, LinkTargetType.Document, beleg.Id,
                beleg.DueDate is null ? $"Zu erledigen: {name}" : $"Frist: {name}",
                (beleg.DueDate is { } termin
                        ? $"fällig {Format(termin)}"
                            + (errechnet ? " (vom Belegdatum errechnet)" : string.Empty)
                            + " · "
                        : string.Empty)
                    + $"gelesen aus: „{beleg.ActionLine}“"
                    + (beleg.ActionFromModel ? " · vom Sprachmodell gefunden" : string.Empty),
                beleg.DueDate);
        }

        // Auch hier über die vorhandenen Vorgänge und nicht über die Belege: Ein gelöschter
        // Beleg taucht in keiner Schleife über Belege mehr auf, und sein Vorgang bliebe für
        // immer offen und führte nirgendwohin.
        foreach (var vorgang in existing.Where(t =>
                     t.Source == TaskSource.DocumentAction
                     && t.SourceId is { } belegId
                     && !imFenster.Contains(belegId)))
        {
            Close(TaskSource.DocumentAction, LinkTargetType.Document, vorgang.SourceId!.Value);
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }

        // Erst nach dem Speichern: Ein eben angelegter Vorgang hat vorher keine Id, und ohne
        // sie liesse er sich nicht schliessen.
        await MatchPaymentsAsync(ct);
    }

    /// <summary>
    /// Schliesst Vorgänge aus Schreiben, deren Zahlung eindeutig in den Buchungen steht.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Lücke, die das schliesst:</b> Eine erfasste Rechnung weiss, ob sie bezahlt
    /// ist. Ein Bescheid weiss nichts — da steht ein Satz auf Papier. Der Vorgang blieb deshalb
    /// offen, bis ein Mensch ihn abhakte oder seine Frist aus dem Sichtfenster fiel, und das
    /// Geld war längst weg.</para>
    /// <para><b>Von allein nur, wo es nichts zu entscheiden gibt:</b> genau ein Treffer, und
    /// belegt durch eine Nummer, die in Verwendungszweck und Beleg steht. Alles andere bleibt
    /// stehen und wird in der Liste vorgeschlagen. Eine falsch geschlossene Forderung sieht
    /// nämlich genauso aus wie eine richtig geschlossene — siehe
    /// <see cref="PaymentMatching"/>.</para>
    /// </remarks>
    private async Task MatchPaymentsAsync(CancellationToken ct)
    {
        var offene = await db.TaskItems
            .Where(t => t.Source == TaskSource.DocumentAction
                        && t.State != TaskState.Done
                        && t.SourceId != null)
            .ToListAsync(ct);

        if (offene.Count == 0)
        {
            return;
        }

        var treffer = await PaymentMatchesAsync(offene, ct);
        var belegIds = offene.Select(t => t.SourceId!.Value).ToList();

        var verlinkt = (await db.DocumentLinks.AsNoTracking()
                .Where(l => l.TargetType == LinkTargetType.Transaction
                            && belegIds.Contains(l.DocumentId))
                .Select(l => new { l.DocumentId, l.TargetId })
                .ToListAsync(ct))
            .Select(l => (l.DocumentId, l.TargetId))
            .ToHashSet();

        var geschlossen = false;

        foreach (var vorgang in offene)
        {
            if (!treffer.TryGetValue(vorgang.Id, out var kandidaten)
                || PaymentMatching.Certain(kandidaten) is not { } sicher)
            {
                continue;
            }

            Verbuche(vorgang, sicher, verlinkt);
            geschlossen = true;
        }

        if (geschlossen)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Schreibt die gefundene Zahlung an den Vorgang und schliesst ihn.
    /// </summary>
    /// <remarks>
    /// <para>Der Grund steht dabei. Ein Vorgang, der von selbst zuging und niemandem sagen kann
    /// warum, ist schlimmer als einer, der offen blieb: Beim offenen sieht man wenigstens, dass
    /// etwas fehlt.</para>
    /// <para>Der Beleg hängt sich an die Buchung — dasselbe, was jemand von Hand täte. Danach
    /// zeigt die Buchung das Schreiben, das sie verlangt hat, und nicht nur umgekehrt.</para>
    /// </remarks>
    private void Verbuche(
        TaskItem vorgang, PaymentMatch treffer, HashSet<(int Beleg, int Buchung)> verlinkt)
    {
        vorgang.State = TaskState.Done;
        vorgang.CompletedAt = clock.Now;
        vorgang.Detail = (vorgang.Detail is { Length: > 0 } bisher ? bisher + " · " : string.Empty)
                         + $"bezahlt: Buchung vom {Format(treffer.Booking.BookedOn)}"
                         + $", erkannt an {treffer.Evidence}";

        var belegId = vorgang.SourceId!.Value;

        if (verlinkt.Add((belegId, treffer.Booking.Id)))
        {
            db.DocumentLinks.Add(new DocumentLink
            {
                DocumentId = belegId,
                TargetType = LinkTargetType.Transaction,
                TargetId = treffer.Booking.Id,
                CreatedAt = clock.Now,
            });
        }
    }

    /// <summary>
    /// Zu jedem Vorgang aus einem Schreiben die Buchungen, die ihn bezahlt haben könnten.
    /// </summary>
    /// <remarks>
    /// Gelesen wird der Betrag von demselben Leser, der auch den Termin der Zeile liest — und
    /// notfalls den ganzen Beleg, siehe <see cref="ClaimedAmount"/>. Ohne Betrag gibt es nichts
    /// abzugleichen; das ist der Normalfall bei „Bitte stellen Sie Ihren Einzug um“ und
    /// bleibt es.
    /// </remarks>
    private async Task<Dictionary<int, IReadOnlyList<PaymentMatch>>> PaymentMatchesAsync(
        IReadOnlyCollection<TaskItem> vorgaenge, CancellationToken ct)
    {
        var ausSchreiben = vorgaenge
            .Where(t => t.Source == TaskSource.DocumentAction && t.SourceId is not null)
            .ToList();

        if (ausSchreiben.Count == 0)
        {
            return [];
        }

        var belegIds = ausSchreiben.Select(t => t.SourceId!.Value).Distinct().ToList();

        var belege = await db.Documents.AsNoTracking()
            .Where(d => belegIds.Contains(d.Id) && d.ActionLine != null && d.ActionLine != "")
            .Select(d => new { d.Id, d.ActionLine, d.DocumentDate, d.ContentText })
            .ToListAsync(ct);

        var belegJeId = belege.ToDictionary(d => d.Id);
        var forderungen = new Dictionary<int, PaymentClaim>();

        foreach (var vorgang in ausSchreiben)
        {
            if (!belegJeId.TryGetValue(vorgang.SourceId!.Value, out var beleg)
                || ClaimedAmount(beleg.ActionLine, beleg.ContentText) is not { } betrag
                || betrag <= 0m)
            {
                continue;
            }

            forderungen[vorgang.Id] =
                new PaymentClaim(betrag, vorgang.DueOn, beleg.DocumentDate, beleg.ContentText);
        }

        if (forderungen.Count == 0)
        {
            return [];
        }

        // Nur Buchungen, die der Höhe nach überhaupt in Frage kommen. Den ganzen Kontoauszug zu
        // laden, um ihn dann wegzuwerfen, hiesse ihn bei jedem Blick in die Liste zu lesen.
        var abgaenge = forderungen.Values.Select(f => -f.Amount).Distinct().ToList();

        var buchungen = await db.Transactions.AsNoTracking()
            .Where(t => abgaenge.Contains(t.Amount))
            .Select(t => new PaymentRecord(t.Id, t.BookingDate, t.Amount, t.Payee, t.Purpose))
            .ToListAsync(ct);

        if (buchungen.Count == 0)
        {
            return [];
        }

        var heute = clock.Today;

        return forderungen.ToDictionary(
            p => p.Key,
            p => PaymentMatching.For(p.Value, buchungen, heute));
    }

    /// <summary>
    /// Der geforderte Betrag: aus der Forderungszeile, sonst aus dem ganzen Beleg.
    /// </summary>
    /// <remarks>
    /// <para>Die Zeile zuerst, denn sie <em>ist</em> die Forderung. Steht der Betrag in ihr,
    /// sagt sie genau, worum es geht, und was sonst noch auf dem Blatt steht, geht sie nichts
    /// an.</para>
    /// <para><b>Nur sagt sie es oft nicht.</b> „Dieser Betrag ist am … zur Zahlung fällig“ zeigt
    /// auf eine Zahl, die eine Zeile höher steht; „den Rechnungsbetrag“ ebenso. An der echten
    /// Ablage gemessen: Ein Gebührenbescheid blieb offen, obwohl die Zahlung seit Wochen
    /// gebucht war — der Abgleich hat nicht eine einzige Buchung angesehen, weil er keinen
    /// Betrag hatte.</para>
    /// <para><b>Der Beleg wird nicht abgesucht, sondern gelesen.</b> Der Rechnungsleser sucht
    /// die Zeile an ihrer Beschriftung — Gesamtbetrag, Rechnungsbetrag, noch zu zahlen —,
    /// dieselbe Lesart, aus der die Anwendung auch sonst Beträge übernimmt. Die grösste Zahl
    /// im Blatt zu nehmen wäre das Gegenteil davon: Ein Bescheid trägt ein Dutzend Zahlen, und
    /// auf einem Steuerbescheid ist die grösste der Kaufpreis.</para>
    /// <para>Findet auch er nichts, bleibt es dabei. Ein geratener Betrag fände entweder gar
    /// nichts oder das Falsche, und das Falsche sieht aus wie das Richtige.</para>
    /// </remarks>
    private static decimal? ClaimedAmount(string? actionLine, string? paperText)
        => (actionLine is { Length: > 0 } zeile ? BillTextReader.AmountInLine(zeile) : null)
           ?? BillTextReader.Read(paperText).GrossAmount;

    /// <summary>Ein Treffer, wie ihn die Oberfläche braucht.</summary>
    private static TaskPaymentDto Zahlung(PaymentMatch treffer) => new()
    {
        TransactionId = treffer.Booking.Id,
        BookedOn = treffer.Booking.BookedOn,
        Amount = treffer.Booking.Amount,
        Payee = treffer.Booking.Payee,
        Purpose = treffer.Booking.Purpose,
        Evidence = treffer.Evidence,
    };

    /// <summary>
    /// Ordnet eine Buchung von Hand zu und schliesst den Vorgang.
    /// </summary>
    /// <remarks>
    /// Der Weg für alles, was sich nicht von allein entschied. Geprüft wird trotzdem, ob die
    /// Buchung überhaupt in Frage kommt: Ein Mensch, der auf eine Schaltfläche drückt, hat die
    /// Liste gesehen und nicht den Betrag nachgerechnet.
    /// </remarks>
    /// <returns><c>false</c>, wenn es den Vorgang oder den Treffer nicht gibt.</returns>
    public async Task<bool> AssignPaymentAsync(
        int taskId, int transactionId, CancellationToken ct = default)
    {
        var vorgang = await db.TaskItems.FirstOrDefaultAsync(
            t => t.Id == taskId && t.State != TaskState.Done, ct);

        if (vorgang is null || vorgang.Source != TaskSource.DocumentAction || vorgang.SourceId is null)
        {
            return false;
        }

        var treffer = await PaymentMatchesAsync([vorgang], ct);

        if (!treffer.TryGetValue(taskId, out var kandidaten)
            || kandidaten.FirstOrDefault(m => m.Booking.Id == transactionId) is not { } gewaehlt)
        {
            return false;
        }

        var verlinkt = (await db.DocumentLinks.AsNoTracking()
                .Where(l => l.TargetType == LinkTargetType.Transaction
                            && l.DocumentId == vorgang.SourceId)
                .Select(l => l.TargetId)
                .ToListAsync(ct))
            .Select(id => (vorgang.SourceId!.Value, id))
            .ToHashSet();

        Verbuche(vorgang, gewaehlt, verlinkt);
        await db.SaveChangesAsync(ct);

        return true;
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
