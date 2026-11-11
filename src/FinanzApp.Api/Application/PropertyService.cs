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
            Bookings = await LoadBookingsAsync(property, ct),
        };
    }

    /// <summary>
    /// Wie viele Buchungen höchstens an einem Objekt stehen.
    /// </summary>
    /// <remarks>
    /// Die Liste ist ein Beleg, kein Kontoauszug. Wer alle Buchungen eines Zeitraums sehen
    /// will, geht zu den Konten — dort gibt es Suche und Filter, die hier nachzubauen wären.
    /// </remarks>
    private const int MostBookings = 40;

    /// <summary>
    /// Buchungen, die zu diesem Objekt gehören.
    /// </summary>
    /// <remarks>
    /// <para><b>Eine Buchung trägt kein Objekt.</b> <c>Transaction.PropertyId</c> steht nur an
    /// Einlagen, und zwar für die Beteiligungsrechnung — im echten Bestand trug sie am
    /// 11.11.2026 keine einzige der 657 Buchungen. Eine Anzeige, die nur daran hängt, bliebe
    /// darum leer.</para>
    /// <para>Hergeleitet wird aus vier Quellen, und jede nennt ihren Grund:</para>
    /// <list type="bullet">
    /// <item>Einlage auf dieses Objekt — ein echter Verweis, kein Schluss.</item>
    /// <item>Verträge des Objekts über ihre <em>Vertragsnummer</em> im Verwendungszweck,
    /// Buchungstext oder Empfänger. Dieselbe Regel wie am Vorsorgevertrag, und derselbe Grund:
    /// über den Anbieternamen hängt bei mehreren Verträgen jede Buchung an jedem.</item>
    /// <item>Gebäude- und Hausratversicherung über ihre Policennummer. Sie hängen nicht am
    /// Objekt, sondern an ihrer Vertragsart — so zählt sie auch die Kostenrechnung.</item>
    /// <item>Die Darlehensrate über Empfänger <em>und</em> Betrag. Ein Darlehen führt keine
    /// Nummer; der Empfänger allein wäre zu wenig, beide zusammen sind ein begründeter
    /// Schluss. Der Grund sagt das auch so.</item>
    /// </list>
    /// <para>Gelesen wird einmal über die Ausgaben, nicht einmal je Quelle: Eine Buchung, auf
    /// die zwei Gründe passen, stünde sonst zweimal da.</para>
    /// </remarks>
    private async Task<IReadOnlyList<LinkedPaymentDto>> LoadBookingsAsync(
        Property property, CancellationToken ct)
    {
        // Vertragsnummern des Objekts, auf ihren Kern gebracht — dieselbe Normalisierung wie
        // am Vorsorgevertrag, damit „100-20“ und „100 20“ dieselbe Nummer bleiben.
        var nummern = property.Contracts
            .Select(c => (Nummer: PolicyService.Normalise(c.ContractNumber), c.Name))
            .Where(x => x.Nummer is { Length: >= 4 })
            .ToList();

        foreach (var police in await db.Policies.AsNoTracking()
                     .Where(p => p.Kind == PolicyKind.Building
                                 || p.Kind == PolicyKind.HouseholdContents)
                     .Select(p => new { p.PolicyNumber, p.Name })
                     .ToListAsync(ct))
        {
            if (PolicyService.Normalise(police.PolicyNumber) is { Length: >= 4 } nummer)
            {
                nummern.Add((nummer, police.Name));
            }
        }

        // Der Anbietername, aber nur wo er unter den Verträgen dieses Objekts eindeutig ist.
        // Zwei Verträge desselben Hauses — im echten Bestand zweimal derselbe Anbieter für
        // Internet und Fernsehen — hängten sonst jede Buchung an beide. Lieber sieben Zeilen
        // nicht zeigen als sie der falschen Hälfte zuschreiben.
        var anbieter = property.Contracts
            .Select(c => (Name: c.Name, Kern: PolicyService.Normalise(c.Provider)))
            .Where(x => x.Kern is { Length: >= 4 })
            .GroupBy(x => x.Kern!, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .Select(g => (Kern: g.Key, g.First().Name))
            .ToList();

        var darlehen = property.Loan;
        var geber = PolicyService.Normalise(darlehen?.Lender);

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.PropertyId == property.Id || t.Kind == TransactionKind.Expense)
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.Id)
            .Take(600)
            .ToListAsync(ct);

        var gefunden = new List<LinkedPaymentDto>();

        foreach (var buchung in rows)
        {
            if (Grund(buchung) is not { } grund)
            {
                continue;
            }

            gefunden.Add(new LinkedPaymentDto
            {
                TransactionId = buchung.Id,
                BookingDate = buchung.BookingDate,
                Amount = Math.Abs(buchung.Amount),
                Payee = buchung.Payee,
                AccountName = buchung.Account?.Name ?? string.Empty,
                MatchReason = grund,
                Reference = buchung.Purpose,
            });

            if (gefunden.Count == MostBookings)
            {
                break;
            }
        }

        return gefunden;

        // Der erste zutreffende Grund gewinnt, und der feste steht vorn: Eine Einlage, die
        // zufällig auch den Betrag der Rate trägt, ist eine Einlage.
        string? Grund(Transaction buchung)
        {
            if (buchung.PropertyId == property.Id)
            {
                return "Einlage für dieses Objekt";
            }

            foreach (var (nummer, name) in nummern)
            {
                if (Steht(buchung, nummer!) is { } wo)
                {
                    return $"{name} — Nummer {wo}";
                }
            }

            var empfaenger = PolicyService.Normalise(buchung.Payee);

            foreach (var (kern, name) in anbieter)
            {
                if (empfaenger?.Contains(kern, StringComparison.Ordinal) == true)
                {
                    return $"{name} — Anbieter im Empfänger, keine Nummer am Vertrag";
                }
            }

            if (darlehen is null)
            {
                return null;
            }

            // Ein Darlehen führt keine Nummer; zu erkennen ist es am Geldgeber oder an der
            // Rate. Beides fasst nur in eine Richtung: Steht der Geldgeber in Kurzform am
            // Darlehen und in voller Länge in der Buchung, greift allein der Betrag — genau
            // so liegt es im echten Bestand. Welcher der beiden Gründe zog, steht an der
            // Zeile, weil der Betrag allein der schwächere ist.
            if (geber is { Length: > 0 }
                && empfaenger?.Contains(geber, StringComparison.Ordinal) == true)
            {
                return $"Darlehensrate an {darlehen.Lender} — Empfänger, keine Nummer";
            }

            return Math.Abs(buchung.Amount) == darlehen.Installment
                ? $"Darlehen {darlehen.Name} — Betrag entspricht der Rate, sonst nichts"
                : null;
        }
    }

    /// <summary>Wo die Nummer steht — oder <c>null</c>, wenn sie nirgends steht.</summary>
    private static string? Steht(Transaction buchung, string nummer)
    {
        if (PolicyService.Normalise(buchung.Purpose)?.Contains(nummer, StringComparison.Ordinal) == true)
        {
            return "im Verwendungszweck";
        }

        if (PolicyService.Normalise(buchung.BookingText)?.Contains(nummer, StringComparison.Ordinal) == true)
        {
            return "im Buchungstext";
        }

        return PolicyService.Normalise(buchung.Payee)?.Contains(nummer, StringComparison.Ordinal) == true
            ? "im Empfänger"
            : null;
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

        // Buchungen, die schon einen anderen Posten begleichen. Sie fallen nicht aus der Liste:
        // Wer die Abbuchung sucht, die er mit eigenen Augen im Konto gesehen hat, und sie hier
        // nicht findet, sucht weiter. Sie stehen an ihrem Platz, beschriftet, und lassen sich
        // nicht wählen.
        //
        // Gruppiert und nicht als Wörterbuch aus der Abfrage: Es kann zwei Posten auf dieselbe
        // Buchung geben — das ist ja gerade der Zustand, gegen den hier gearbeitet wird.
        var vergeben = (await db.Invoices.AsNoTracking()
                .Where(i => i.Id != invoiceId && i.TransactionId != null
                            && i.Status != InvoiceStatus.Cancelled)
                .Select(i => new { Buchung = i.TransactionId!.Value, i.Subject })
                .ToListAsync(ct))
            .GroupBy(i => i.Buchung)
            .ToDictionary(g => g.Key, g => g.First().Subject);

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
                    Purpose = transaction.Purpose,
                    Score = Math.Min(100, score),
                    Reason = string.Join(" · ", reasons),
                    IsBestMatch = false,
                    AssignedTo = vergeben.GetValueOrDefault(transaction.Id),
                });
            }
        }

        var best = scored.OrderByDescending(c => c.Score).ThenByDescending(c => c.BookingDate).Take(6).ToList();

        // Hervorgehoben wird der beste Vorschlag, der noch zu haben ist. Ein vergebener nie —
        // der Akzent auf einer Zeile, die sich nicht wählen lässt, wäre eine Aufforderung ins
        // Leere.
        //
        // Seinen Platz behält er trotzdem: Die Zahl daneben sagt, wie gut eine Buchung passt,
        // und nicht, ob sie noch frei ist. Sie nach hinten zu sortieren hieße, sie aus der
        // Liste zu drängen, sobald sechs andere da sind — und dann fehlte genau die Buchung,
        // die jemand mit eigenen Augen im Konto gesehen hat.
        var hervorgehoben = best.FirstOrDefault(c => c.AssignedTo is null)?.TransactionId;

        return [.. best.Select(c => c with { IsBestMatch = c.TransactionId == hervorgehoben })];
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

            // Eine Buchung begleicht einen Posten. Zwei gleich hohe Bescheide desselben
            // Absenders am selben Tag ergeben zwei Vorschläge, die Zeile für Zeile gleich
            // aussehen; wer zweimal denselben nimmt, hat einen Posten doppelt belegt und den
            // anderen nie. Beide sähen danach bezahlt aus, und niemandem fiele es auf.
            //
            // Die Liste beschriftet solche Buchungen bereits. Das hier ist die Sperre dahinter:
            // Die Zuordnung ginge auch ohne sie, und eine Beschriftung ist keine Regel.
            var schon = await db.Invoices.AsNoTracking()
                .Where(i => i.Id != invoiceId && i.TransactionId == transactionId
                            && i.Status != InvoiceStatus.Cancelled)
                .Select(i => i.Subject)
                .FirstOrDefaultAsync(ct);

            if (schon is not null)
            {
                throw new RuleViolationException(
                    $"Diese Buchung ist bereits „{schon}“ zugeordnet.");
            }

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
