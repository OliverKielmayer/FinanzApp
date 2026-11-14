using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Anschaffungen — gekaufte Gegenstände, an denen ihre Papiere hängen.
/// </summary>
/// <remarks>
/// <para>Der Platz, der zwischen den Objekten fehlte. Ein Lieferschein für eine Einbauküche
/// oder ein medizinisches Gerät gehört an keinen Vertrag, an kein Konto und an keine
/// Immobilie. Ohne dieses Objekt landete er im Nirgendwo oder, schlimmer, an irgendetwas
/// Naheliegendem — und eine falsche Verknüpfung sieht aus wie eine Auskunft.</para>
/// <para>Bewusst ohne eigene Auswertung. Was eine Anschaffung kostet, steht auf ihrer
/// Rechnung; sie in eine Vermögensrechnung zu ziehen hieße, über Abschreibung und Restwert zu
/// entscheiden, und das hat niemand verlangt.</para>
/// </remarks>
public sealed class PurchaseService(
    FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    public async Task<IReadOnlyList<PurchaseListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        var heute = DateOnly.FromDateTime(clock.Now);

        var zeilen = await db.Purchases.AsNoTracking()
            .OrderByDescending(p => p.PurchaseDate)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.PurchaseDate,
                p.Vendor,
                p.Price,
                p.WarrantyUntil,
            })
            .ToListAsync(ct);

        // Eine Abfrage für alle Zähler statt einer je Zeile: Bei dreißig Anschaffungen wären
        // das sonst einunddreißig Abfragen für eine Liste.
        var belege = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.TargetType == LinkTargetType.Purchase)
            .GroupBy(l => l.TargetId)
            .Select(g => new { TargetId = g.Key, Anzahl = g.Count() })
            .ToDictionaryAsync(x => x.TargetId, x => x.Anzahl, ct);

        return
        [
            .. zeilen.Select(p => new PurchaseListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                PurchaseDate = p.PurchaseDate,
                Vendor = p.Vendor,
                Price = p.Price,
                WarrantyUntil = p.WarrantyUntil,
                DocumentCount = belege.TryGetValue(p.Id, out var anzahl) ? anzahl : 0,
                WarrantyRunning = Laeuft(p.WarrantyUntil, heute),
            }),
        ];
    }

    public async Task<PurchaseDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var purchase = await db.Purchases.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (purchase is null)
        {
            return null;
        }

        return new PurchaseDetailDto
        {
            Id = purchase.Id,
            Name = purchase.Name,
            PurchaseDate = purchase.PurchaseDate,
            Vendor = purchase.Vendor,
            Price = purchase.Price,
            WarrantyUntil = purchase.WarrantyUntil,
            WarrantyRunning = Laeuft(purchase.WarrantyUntil, DateOnly.FromDateTime(clock.Now)),
            Note = purchase.Note,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Purchase, purchase.Id, ct),
            Payments = await PaymentsAsync(purchase, ct),
        };
    }

    /// <summary>Wie weit zurück Buchungen für den Abgleich geholt werden.</summary>
    /// <remarks>
    /// Das Fenster schneidet <see cref="PaymentMatching"/> selbst zu; hier geht es nur darum,
    /// nicht den ganzen Bestand in den Speicher zu ziehen. Grosszuegig gewählt, weil eine
    /// Anschaffung auch Jahre zurückliegen kann.
    /// </remarks>
    private const int MostBookings = 1500;

    /// <summary>
    /// Die Buchungen, die diese Anschaffung bezahlt haben.
    /// </summary>
    /// <remarks>
    /// <para><b>Dieselbe Regel wie am Vorgang</b>, und bewusst derselbe Baustein: Betrag auf
    /// den Cent, Buchung im Zeitfenster, und eine Nummer aus dem Belegtext im
    /// Verwendungszweck. Zwei Fassungen desselben Abgleichs gängen auseinander, und bemerkt
    /// würde es an der, die seltener jemand ansieht.</para>
    /// <para><b>Wo die Buchung schon vermerkt ist, wird nicht abgeglichen.</b> Eine
    /// Anschaffung, die aus einer Kontobuchung entstanden ist, trägt sie am Datensatz — wer
    /// sie vor sich hatte, <em>weiss</em>, welche Zahlung es war. Dann steht sie da, und zwar
    /// allein: Eine Gewissheit mit Vermutungen zu garnieren machte sie nicht sicherer.</para>
    /// <para><b>Sonst ist der Beleg die Bedingung.</b> Ohne Preis oder ohne eingelesenen Text
    /// gibt es nichts zu vergleichen — dann bleibt die Liste leer statt einen Betragstreffer
    /// als Zuordnung auszugeben.</para>
    /// <para><b>Gezeigt wird auch, was nur dem Betrag nach passt</b>, aber als solches
    /// benannt. Die Entscheidung liegt beim Inhaber; die Anwendung sagt, wie fest der Faden
    /// ist.</para>
    /// </remarks>
    private async Task<IReadOnlyList<LinkedPaymentDto>> PaymentsAsync(
        Purchase purchase, CancellationToken ct)
    {
        // Steht die Buchung schon am Datensatz, ist nichts zu suchen. Sie steht dort, weil
        // jemand sie vor sich hatte und daraus die Anschaffung erfasst hat — eine festere
        // Auskunft als jeder Abgleich, und sie braucht weder Preis noch Beleg. Daneben noch
        // nach Kandidaten zu suchen hiesse, eine Gewissheit mit Vermutungen zu garnieren.
        if (purchase.TransactionId is { } ausBuchung)
        {
            var quelle = await db.Transactions.AsNoTracking()
                .Include(t => t.Account)
                .FirstOrDefaultAsync(t => t.Id == ausBuchung, ct);

            if (quelle is not null)
            {
                return
                [
                    new LinkedPaymentDto
                    {
                        TransactionId = quelle.Id,
                        BookingDate = quelle.BookingDate,
                        Amount = Math.Abs(quelle.Amount),
                        Payee = quelle.Payee,
                        AccountName = quelle.Account?.Name ?? string.Empty,
                        MatchReason = "Aus dieser Buchung erfasst",
                        Reference = quelle.Purpose,
                    },
                ];
            }
        }

        if (purchase.Price is not { } preis || preis <= 0m)
        {
            return [];
        }

        var belegtext = await BelegtextAsync(purchase.Id, ct);
        if (belegtext is null)
        {
            return [];
        }

        var buchungen = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Expense)
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.Id)
            .Take(MostBookings)
            .ToListAsync(ct);

        var treffer = PaymentMatching.For(
            new PaymentClaim(preis, Due: null, purchase.PurchaseDate, belegtext),
            buchungen.Select(t => new PaymentRecord(
                t.Id, t.BookingDate, t.Amount, t.Payee, t.Purpose)),
            DateOnly.FromDateTime(clock.Now));

        var nachKonto = buchungen.ToDictionary(t => t.Id, t => t.Account?.Name ?? string.Empty);

        return
        [
            .. treffer.Select(m => new LinkedPaymentDto
            {
                TransactionId = m.Booking.Id,
                BookingDate = m.Booking.BookedOn,
                Amount = Math.Abs(m.Booking.Amount),
                Payee = m.Booking.Payee ?? string.Empty,
                AccountName = nachKonto.GetValueOrDefault(m.Booking.Id, string.Empty),
                MatchReason = m.Evidence is { } beweis
                    ? $"Nummer {beweis} — steht im Beleg und im Verwendungszweck"
                    : "Nur der Betrag im Zeitraum — das kann jede Zahlung derselben Höhe sein",
                Reference = m.Booking.Purpose,
            }),
        ];
    }

    /// <summary>
    /// Der gelesene Text der Belege dieser Anschaffung, aneinandergehängt.
    /// </summary>
    /// <remarks>
    /// Alle zusammen und nicht nur der erste: Eine Bestellung bringt oft Rechnung und
    /// Lieferschein, und die Nummer steht nicht zwingend auf beiden.
    /// </remarks>
    private async Task<string?> BelegtextAsync(int purchaseId, CancellationToken ct)
    {
        var ids = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.TargetType == LinkTargetType.Purchase && l.TargetId == purchaseId)
            .Select(l => l.DocumentId)
            .ToListAsync(ct);

        if (ids.Count == 0)
        {
            return null;
        }

        var texte = await db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.ContentText != null)
            .Select(d => d.ContentText!)
            .ToListAsync(ct);

        return texte.Count == 0 ? null : string.Join(Environment.NewLine, texte);
    }

    /// <summary>
    /// Ob die Garantie am Stichtag noch läuft.
    /// </summary>
    /// <remarks>
    /// <c>null</c> heißt: Es steht keine Frist da. Das ist nicht dasselbe wie „abgelaufen“ —
    /// eine fehlende Angabe als Ablauf auszugeben, hieße eine Auskunft zu erfinden, nach der
    /// sich jemand richtet. Der Stichtag selbst zählt mit: An dem Tag gilt sie noch.
    /// </remarks>
    private static bool? Laeuft(DateOnly? bis, DateOnly stichtag)
        => bis is { } frist ? frist >= stichtag : null;
}
