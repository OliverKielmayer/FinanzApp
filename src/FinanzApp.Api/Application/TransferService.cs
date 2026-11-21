using FinanzApp.Shared.Formatting;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Überweisungen, von dieser Anwendung aus beauftragt.
/// </summary>
/// <remarks>
/// <para><b>Hier wird Geld bewegt, und das ändert die Reihenfolge.</b> Jede Prüfung steht
/// <em>vor</em> dem Bankaufruf, und die Zeile in der Tabelle entsteht vor ihm. Was danach
/// passiert, lässt sich nicht mehr zurücknehmen — eine Überweisung holt niemand zurück,
/// ohne den Empfänger zu fragen.</para>
/// <para><b>Was geprüft wird und was nicht.</b> Die Prüfziffer der IBAN fängt vertauschte
/// Ziffern ab; dass es das Konto gibt und wem es gehört, weiß sie nicht. Die Obergrenze fängt
/// die Null zu viel ab. Die Dublettenschranke fängt den zweiten Klick ab. Was keine davon
/// fängt: die richtige IBAN des falschen Empfängers.</para>
/// <para><b>Die PIN wird durchgereicht, nie gespeichert.</b> Sie kommt mit dem Auftrag und
/// lebt nur für den Dialog — dieselbe Regel wie beim Abruf.</para>
/// </remarks>
public sealed class TransferService(
    FinanzAppDbContext db,
    IBankTransferSource bank,
    BankAccessOptions options,
    IClock clock)
{
    /// <summary>
    /// Wie lange ein gleicher Auftrag als Dublette gilt.
    /// </summary>
    /// <remarks>
    /// Der zweite Klick kommt innerhalb von Sekunden, ein zweiter Versuch nach einem Fehler
    /// innerhalb von Minuten. Zehn Minuten decken beides ab, ohne eine zweite Rate an denselben
    /// Empfänger im selben Monat zu behindern.
    /// </remarks>
    public static readonly TimeSpan DoubleWindow = TimeSpan.FromMinutes(10);

    /// <summary>Was zur Überweisung eines offenen Postens schon bekannt ist.</summary>
    public async Task<TransferDraftDto?> PrepareAsync(int invoiceId, CancellationToken ct = default)
    {
        var rechnung = await db.Invoices.AsNoTracking()
            .Include(i => i.Contract)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (rechnung is null)
        {
            return null;
        }

        // Das Konto, von dem dieser Posten üblicherweise abgeht: das des Vertrags. Ohne
        // Vertrag bleibt es offen — geraten wird keines, und ein falsches Konto wäre hier
        // teurer als eine zweite Frage.
        var konto = rechnung.Contract?.AccountId;

        return new TransferDraftDto
        {
            InvoiceId = rechnung.Id,
            AccountId = konto,
            ReceiverName = rechnung.Payee ?? rechnung.Contract?.Provider ?? string.Empty,
            Amount = rechnung.Amount,

            // Der Verwendungszweck, wie ihn der Empfänger wiedererkennt: seine eigene Nummer.
            Purpose = rechnung.Number is { Length: > 0 } nummer
                ? $"Rechnung {nummer}"
                : rechnung.Subject,

            AlreadyOrdered = await db.BankTransfers.AsNoTracking()
                .AnyAsync(t => t.InvoiceId == invoiceId && t.State != TransferState.Rejected, ct),

            IbanSuggestions = await IbanSuggestionsAsync(rechnung, ct),
        };
    }

    /// <summary>
    /// Welche IBAN gemeint sein könnte — aus zwei Quellen, die es schon gibt.
    /// </summary>
    /// <remarks>
    /// <para><b>Zuerst die eigenen Buchungen.</b> Wer schon einmal Geld von diesem Haushalt
    /// bekommen hat, steht mit IBAN im Kontoauszug — am gemessenen Bestand bei fast jeder
    /// Buchung. Eine solche IBAN ist durch die Wirklichkeit geprüft und nicht nur durch
    /// ihre Prüfziffer; deshalb steht sie vorn.</para>
    /// <para><b>Dann der Beleg.</b> Auf der Rechnung steht, wohin sie bezahlt werden will.
    /// Gefiltert wird mit der Prüfziffer, und die reicht: Am gemessenen Beleg fielen die
    /// Gläubiger-Identifikation und eine maskierte eigene IBAN genau an ihr durch.</para>
    /// <para><b>Die eigenen Konten bleiben draußen.</b> Auf einem Beleg steht oft auch, von
    /// welchem Konto eingezogen wird — an sich selbst zu überweisen ist kein Vorschlag.</para>
    /// <para><b>Vorgeschlagen, nicht vorbelegt.</b> Ein falscher Vorschlag kostet einen Blick;
    /// eine falsche Vorbelegung, die niemand mehr ansieht, kostet das Geld.</para>
    /// </remarks>
    private async Task<IReadOnlyList<IbanSuggestionDto>> IbanSuggestionsAsync(
        Invoice rechnung, CancellationToken ct)
    {
        var eigene = await db.Accounts.AsNoTracking()
            .Where(a => a.Iban != null && a.Iban != string.Empty)
            .Select(a => a.Iban!)
            .ToListAsync(ct);

        var tabu = eigene.Select(IbanCheck.Normalise).ToHashSet(StringComparer.Ordinal);

        List<IbanSuggestionDto> vorschlaege = [];

        // ── Erste Quelle: wohin schon überwiesen wurde ─────────────────────────────
        var gesucht = PolicyService.Normalise(
            rechnung.Payee ?? rechnung.Contract?.Provider);

        if (gesucht is { Length: >= 5 })
        {
            // Nur Ausgaben: Eine Gutschrift nennt die IBAN dessen, der gezahlt hat — das ist
            // nicht, wohin man überweist.
            var buchungen = await db.Transactions.AsNoTracking()
                .Where(t => t.Amount < 0
                            && t.CounterpartyIban != null && t.CounterpartyIban != string.Empty
                            && t.Payee != null && t.Payee != string.Empty)
                .OrderByDescending(t => t.BookingDate)
                .Take(2000)
                .Select(t => new { t.Payee, t.CounterpartyIban, t.BookingDate, t.Amount })
                .ToListAsync(ct);

            foreach (var buchung in buchungen)
            {
                var name = PolicyService.Normalise(buchung.Payee);

                // Der eine enthält den anderen: Die Bank schreibt „EDI ENERGIE-DIREKT
                // HOHENLOHE GMBH“, die Rechnung „EDi Energie-Direkt Hohenlohe GmbH“, und ein
                // Vertrag nennt oft nur den Kern. Fünf Zeichen ist die Untergrenze — darunter
                // träfe jeder jeden.
                if (name is not { Length: >= 5 }
                    || (!name.Contains(gesucht, StringComparison.Ordinal)
                        && !gesucht.Contains(name, StringComparison.Ordinal)))
                {
                    continue;
                }

                var iban = IbanCheck.Normalise(buchung.CounterpartyIban);

                if (tabu.Contains(iban) || vorschlaege.Any(v => v.Iban == iban))
                {
                    continue;
                }

                vorschlaege.Add(new IbanSuggestionDto
                {
                    Iban = iban,
                    Origin = "Buchung",
                    Detail = $"zuletzt am {GermanFormat.Date(buchung.BookingDate)} · "
                             + $"{GermanFormat.Euro(Math.Abs(buchung.Amount))} an "
                             + $"{buchung.Payee}",
                    FromHistory = true,
                });
            }
        }

        // ── Zweite Quelle: was auf dem Beleg steht ───────────────────────────────
        var belege = await (
            from link in db.DocumentLinks
            join dokument in db.Documents on link.DocumentId equals dokument.Id
            where link.TargetType == LinkTargetType.Invoice
                  && link.TargetId == rechnung.Id
                  && dokument.ContentText != null
            select new { dokument.Title, dokument.ContentText }).ToListAsync(ct);

        foreach (var beleg in belege)
        {
            foreach (var iban in IbanCheck.FindAll(beleg.ContentText))
            {
                if (tabu.Contains(iban) || vorschlaege.Any(v => v.Iban == iban))
                {
                    continue;
                }

                vorschlaege.Add(new IbanSuggestionDto
                {
                    Iban = iban,
                    Origin = "Beleg",
                    Detail = $"steht in „{beleg.Title}“",
                    FromHistory = false,
                });
            }
        }

        return vorschlaege;
    }

    /// <summary>Die Aufträge dieses Kontos, jüngster zuerst.</summary>
    public async Task<IReadOnlyList<TransferDto>> GetListAsync(
        int accountId, CancellationToken ct = default)
        => await db.BankTransfers.AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.Id)
            .Take(50)
            .Select(t => new TransferDto
            {
                Id = t.Id,
                AccountId = t.AccountId,
                ReceiverName = t.ReceiverName,
                ReceiverIban = t.ReceiverIban,
                Amount = t.Amount,
                Purpose = t.Purpose,
                State = t.State,
                BankMessage = t.BankMessage,
                InvoiceId = t.InvoiceId,
                CreatedAt = t.CreatedAt,
            })
            .ToListAsync(ct);

    /// <summary>
    /// Beauftragt eine Überweisung.
    /// </summary>
    /// <remarks>
    /// <para>Die Zeile entsteht <b>vor</b> dem Bankaufruf und wird danach fortgeschrieben.
    /// Bricht etwas dazwischen ab, steht sie auf <see cref="TransferState.Prepared"/> — und
    /// das ist die Auskunft, die dann gebraucht wird: Es war etwas unterwegs, und ob es ankam,
    /// sagt der Kontoauszug.</para>
    /// <para>Wirft der Bankaufruf, wird die Zeile auf <see cref="TransferState.Unknown"/>
    /// gesetzt und die Ausnahme weitergereicht. <b>Nicht auf „abgelehnt“</b>: Eine Verbindung,
    /// die abreißt, sagt nichts darüber, ob der Auftrag die Bank erreicht hat.</para>
    /// </remarks>
    public async Task<TransferDto> SubmitAsync(
        TransferRequest request, CancellationToken ct = default)
    {
        var konto = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
                    ?? throw new RuleViolationException("Dieses Konto gibt es nicht.");

        if (BankAccessReadiness.Problems(konto, options) is { Count: > 0 } fehlt)
        {
            throw new RuleViolationException(
                "Für dieses Konto ist der Bankzugang nicht vollständig: " + string.Join(" ", fehlt));
        }

        if (string.IsNullOrWhiteSpace(request.Pin))
        {
            throw new RuleViolationException("Ohne PIN lässt sich kein Auftrag erteilen.");
        }

        var empfaenger = (request.ReceiverName ?? string.Empty).Trim();
        var zweck = (request.Purpose ?? string.Empty).Trim();
        var iban = IbanCheck.Normalise(request.ReceiverIban);
        var bic = IbanCheck.Normalise(request.ReceiverBic);

        if (empfaenger.Length == 0)
        {
            throw new RuleViolationException("Der Empfänger fehlt.");
        }

        if (empfaenger.Length > 70)
        {
            throw new RuleViolationException(
                "Der Name des Empfängers ist länger als die 70 Zeichen, die SEPA zulässt.");
        }

        if (!IbanCheck.IsValid(iban))
        {
            throw new RuleViolationException(
                "Die IBAN stimmt nicht — ihre Prüfziffer geht nicht auf. Meist sind zwei "
                + "Ziffern vertauscht.");
        }

        if (!IbanCheck.BicIsValid(bic))
        {
            throw new RuleViolationException(
                "Die BIC hat weder acht noch elf Zeichen. Im SEPA-Raum darf sie auch leer "
                + "bleiben — die Bank findet das Institut über die IBAN.");
        }

        if (request.Amount <= 0m)
        {
            throw new RuleViolationException("Der Betrag muss größer als null sein.");
        }

        if (options.TransferLimit > 0m && request.Amount > options.TransferLimit)
        {
            throw new RuleViolationException(
                $"{GermanFormat.Euro(request.Amount)} liegt über der hier eingestellten "
                + $"Obergrenze von {GermanFormat.Euro(options.TransferLimit)}. Das ist eine "
                + "Schranke dieser Anwendung gegen den Vertipper, kein Limit der Bank.");
        }

        if (zweck.Length == 0)
        {
            throw new RuleViolationException("Ohne Verwendungszweck erkennt der Empfänger nicht, wofür.");
        }

        if (zweck.Length > 140)
        {
            throw new RuleViolationException(
                $"Der Verwendungszweck ist {zweck.Length} Zeichen lang. SEPA trägt 140.");
        }

        // Die Dublettenschranke. Sie steht zuletzt unter den Prüfungen, weil sie die einzige
        // ist, die auf den Bestand sieht — und als letzte vor dem Schreiben.
        var seit = clock.Now - DoubleWindow;

        var schonDa = await db.BankTransfers.AsNoTracking()
            .Where(t => t.AccountId == konto.Id
                        && t.ReceiverIban == iban
                        && t.Amount == request.Amount
                        && t.Purpose == zweck
                        && t.CreatedAt >= seit
                        && t.State != TransferState.Rejected)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (schonDa is not null)
        {
            throw new RuleViolationException(
                $"Derselbe Auftrag ist am {GermanFormat.DateTime(schonDa.CreatedAt)} schon "
                + "erteilt worden. Wenn er wirklich ein zweites Mal hinaus soll, ändere den "
                + "Verwendungszweck — sonst wäre nachher nicht zu unterscheiden, welcher "
                + "welcher war.");
        }

        var auftrag = new BankTransfer
        {
            AccountId = konto.Id,
            ReceiverName = empfaenger,
            ReceiverIban = iban,
            ReceiverBic = bic.Length == 0 ? null : bic,
            Amount = request.Amount,
            Purpose = zweck,
            State = TransferState.Prepared,
            InvoiceId = request.InvoiceId,
            CreatedAt = clock.Now,
        };

        db.BankTransfers.Add(auftrag);
        await db.SaveChangesAsync(ct);

        try
        {
            var antwort = await bank.SubmitAsync(
                konto, request.Pin, empfaenger, iban, auftrag.ReceiverBic,
                request.Amount, zweck, ct);

            auftrag.State = antwort.Accepted ? TransferState.Accepted : TransferState.Rejected;
            auftrag.BankMessage = Gekuerzt(antwort.Message);
        }
        catch (Exception ex)
        {
            // Nicht „abgelehnt“: Eine abgerissene Verbindung sagt nicht, ob der Auftrag die
            // Bank erreicht hat. Wer das verwechselt, erteilt ihn ein zweites Mal.
            auftrag.State = TransferState.Unknown;
            auftrag.BankMessage = Gekuerzt(ex.Message);
            auftrag.AnsweredAt = clock.Now;

            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        auftrag.AnsweredAt = clock.Now;
        await db.SaveChangesAsync(ct);

        return new TransferDto
        {
            Id = auftrag.Id,
            AccountId = auftrag.AccountId,
            ReceiverName = auftrag.ReceiverName,
            ReceiverIban = auftrag.ReceiverIban,
            Amount = auftrag.Amount,
            Purpose = auftrag.Purpose,
            State = auftrag.State,
            BankMessage = auftrag.BankMessage,
            InvoiceId = auftrag.InvoiceId,
            CreatedAt = auftrag.CreatedAt,
        };
    }

    /// <summary>Die Meldung der Bank, auf das gekürzt, was die Spalte trägt.</summary>
    private static string Gekuerzt(string? message)
    {
        var text = (message ?? string.Empty).Trim();

        return text.Length <= 1000 ? text : text[..999] + "…";
    }
}
