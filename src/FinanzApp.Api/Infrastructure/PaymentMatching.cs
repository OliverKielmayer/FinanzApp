using System.Text.RegularExpressions;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Was ein Schreiben verlangt, soweit ein Abgleich es braucht.</summary>
/// <param name="Amount">Der geforderte Betrag, positiv — gezahlt wird er als Abgang.</param>
/// <param name="Due">Die Frist, wenn das Papier eine nennt.</param>
/// <param name="PaperDate">Das Datum des Belegs.</param>
/// <param name="PaperText">Der gelesene Text des Belegs. Aus ihm kommt der Beweis.</param>
public sealed record PaymentClaim(
    decimal Amount, DateOnly? Due, DateOnly? PaperDate, string? PaperText);

/// <summary>Eine gebuchte Zahlung, soweit ein Abgleich sie braucht.</summary>
public sealed record PaymentRecord(
    int Id, DateOnly BookedOn, decimal Amount, string? Payee, string? Purpose);

/// <summary>Eine Buchung, die zu der Forderung passt — und woran man es sieht.</summary>
/// <param name="Booking">Die Buchung.</param>
/// <param name="Evidence">
/// Die Nummer, die in Verwendungszweck <em>und</em> Beleg steht. <c>null</c> heisst: Es passt
/// nur der Betrag im Zeitraum, und das kann jede andere Zahlung derselben Höhe auch.
/// </param>
public sealed record PaymentMatch(PaymentRecord Booking, string? Evidence)
{
    /// <summary>Ob über den Betrag hinaus etwas für genau diese Buchung spricht.</summary>
    public bool Corroborated => Evidence is not null;
}

/// <summary>
/// Sucht zu einer Forderung aus einem Schreiben die Buchung, die sie bezahlt hat.
/// </summary>
/// <remarks>
/// <para><b>Warum das überhaupt nötig ist.</b> Ein Vorgang aus einem Schreiben kennt keinen
/// Status. Eine erfasste Rechnung weiß, ob sie bezahlt ist; ein Bescheid weiß gar nichts — da
/// steht ein Satz auf Papier, und ob ihm jemand nachgekommen ist, stand bisher nirgends. Der
/// Vorgang blieb deshalb offen, bis ein Mensch ihn abhakte oder bis seine Frist aus dem
/// Sichtfenster fiel. Das Geld war da längst weg.</para>
/// <para><b>Der Betrag allein entscheidet nichts.</b> Gemessen an der echten Ablage: Zwei
/// Bescheide an zwei Personen, derselbe Betrag, am selben Tag an denselben Empfänger
/// überwiesen — der Name einmal in Grossbuchstaben, einmal gemischt. Wer auf Betrag, Tag und
/// Empfänger abgleicht, hat für jeden der beiden Vorgänge zwei Kandidaten und ordnet sie mit
/// gleicher Wahrscheinlichkeit über Kreuz zu. Auffallen würde das nie: Die Summe stimmt, jeder
/// Vorgang ist geschlossen, und nur die Zuordnung ist vertauscht.</para>
/// <para><b>Was sie trennt, ist eine Nummer.</b> Im Verwendungszweck steht eine Kennung, die
/// auch im Beleg steht — ein Aktenzeichen, eine Rechnungs- oder Steuernummer. Sie ist der
/// einzige Faden zwischen diesem Papier und dieser Buchung, und nur wo er hält, darf eine
/// Zuordnung ohne Rückfrage geschehen.</para>
/// <para><b>Rückwärts weiter als vorwärts.</b> Eine Zahlung kann vor dem Beleg liegen: Bezahlt
/// wird, wenn das Geld bereit ist, abgelegt wird, wenn jemand Zeit hat — in der echten Ablage
/// liegen zwischen beidem Tage in der falschen Richtung. Ein Fenster „vom Belegdatum bis zur
/// Frist“ fände solche Zahlungen nie.</para>
/// </remarks>
public static class PaymentMatching
{
    /// <summary>
    /// Wie weit vor der Frist eine Zahlung noch dazugehören kann.
    /// </summary>
    /// <remarks>
    /// Ein Vierteljahr. Weiter zurück steht der Betrag irgendwann zufällig noch einmal auf dem
    /// Konto — eine Miete, eine Rate, ein Dauerauftrag treffen jede Höhe irgendwann wieder.
    /// </remarks>
    private const int BackDays = 90;

    /// <summary>
    /// Wie lange nach der Frist eine Zahlung noch dazugehören kann.
    /// </summary>
    /// <remarks>
    /// Ein Monat: Wer zu spät zahlt, zahlt meist nach der ersten Mahnung. Weiter zu suchen
    /// brächte nichts, denn so lange steht der Vorgang ohnehin nicht mehr in der Liste.
    /// </remarks>
    private const int GraceDays = 30;

    /// <summary>
    /// Wie viele Ziffern eine Kennung tragen muss, um als Beweis zu zählen.
    /// </summary>
    /// <remarks>
    /// Vier Ziffern sind ein Jahr, ein Betrag, eine Hausnummer mit Zusatz. Fünf können sich
    /// immer noch zufällig treffen, und das ist hingenommen: Der Beweis entscheidet nie allein,
    /// sondern erst, nachdem der Betrag auf den Cent stimmt und die Buchung im Zeitraum liegt.
    /// </remarks>
    private const int MinimumDigits = 5;

    /// <summary>
    /// Was im Verwendungszweck als Kennung durchgeht.
    /// </summary>
    /// <remarks>
    /// Ziffern, Buchstaben, Schrägstrich, Bindestrich und Punkt — die Formen, in denen
    /// Aktenzeichen und Rechnungsnummern geschrieben werden. Getrennt wird an Leerraum und an
    /// allem anderen, damit „Steuernummer 12345/67890“ die Nummer hergibt und nicht das Wort.
    /// </remarks>
    private static readonly Regex Kennung =
        new(@"[0-9A-Za-z][0-9A-Za-z/.\-]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KeineZiffer = new(@"\D+", RegexOptions.Compiled);

    /// <summary>
    /// Alle Buchungen, die zu der Forderung passen — die belegten zuerst.
    /// </summary>
    /// <remarks>
    /// Der Betrag muss auf den Cent stimmen. Eine Teilzahlung erkennt dieser Abgleich bewusst
    /// nicht: Sie schliesst die Forderung nicht, und sie von einem Rest zu unterscheiden, der
    /// zufällig passt, ginge nur mit Raten.
    /// </remarks>
    public static IReadOnlyList<PaymentMatch> For(
        PaymentClaim claim, IEnumerable<PaymentRecord> bookings, DateOnly today)
    {
        if (claim.Amount <= 0m)
        {
            return [];
        }

        var (von, bis) = Window(claim, today);
        var belegziffern = DigitsOf(claim.PaperText);
        var betragsziffern = DigitsOf(claim.Amount.ToString("0.00", Culture));

        return bookings
            .Where(b => b.Amount == -claim.Amount && b.BookedOn >= von && b.BookedOn <= bis)
            .Select(b => new PaymentMatch(b, EvidenceIn(b.Purpose, belegziffern, betragsziffern)))
            .OrderByDescending(m => m.Corroborated)
            .ThenBy(m => Distance(m.Booking.BookedOn, claim.Due ?? today))
            .ThenBy(m => m.Booking.Id)
            .ToList();
    }

    /// <summary>
    /// Die eine Buchung, die ohne Rückfrage zugeordnet werden darf — sonst <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Zwei Bedingungen, und beide müssen halten: Es gibt <em>genau einen</em> belegten Treffer,
    /// und belegt heisst, dass eine Nummer aus dem Papier im Verwendungszweck steht. Passen drei
    /// Buchungen über den Betrag und trägt keine davon eine Nummer, entscheidet ein Mensch. Das
    /// ist keine Vorsicht um ihrer selbst willen: Eine falsch geschlossene Forderung sieht genau
    /// so aus wie eine richtig geschlossene.
    /// </remarks>
    public static PaymentMatch? Certain(
        PaymentClaim claim, IEnumerable<PaymentRecord> bookings, DateOnly today)
        => Certain(For(claim, bookings, today));

    /// <summary>
    /// Dieselbe Entscheidung an einer fertigen Trefferliste.
    /// </summary>
    /// <remarks>
    /// Damit die Regel an einer Stelle steht. Wer sie beim Schliessen und beim Vorschlagen je
    /// einmal ausschreibt, hat zwei Regeln, die eine Weile dieselbe sind.
    /// </remarks>
    public static PaymentMatch? Certain(IEnumerable<PaymentMatch> matches)
    {
        var belegte = matches.Where(m => m.Corroborated).Take(2).ToList();

        return belegte.Count == 1 ? belegte[0] : null;
    }

    /// <summary>Der Zeitraum, in dem eine Zahlung zu dieser Forderung gehören kann.</summary>
    private static (DateOnly Von, DateOnly Bis) Window(PaymentClaim claim, DateOnly today)
    {
        var anker = claim.Due ?? claim.PaperDate ?? today;

        // Rückwärts vom früheren der beiden Daten: Liegt der Beleg vor der Frist, zählt sein
        // Tag, denn die Forderung kann älter sein als das Blatt, das sie ins Haus trägt.
        var frueher = claim.PaperDate is { } papier && papier < anker ? papier : anker;

        var bis = (claim.Due ?? today).AddDays(GraceDays);

        // Eine Buchung aus der Zukunft ist keine Zahlung, sondern ein Vormerk.
        return (frueher.AddDays(-BackDays), bis > today ? today : bis);
    }

    private static int Distance(DateOnly a, DateOnly b) => Math.Abs(a.DayNumber - b.DayNumber);

    /// <summary>
    /// Die Kennung aus dem Verwendungszweck, die auch im Beleg steht.
    /// </summary>
    /// <remarks>
    /// Verglichen werden die blanken Ziffern, nicht die Schreibweise: Im Verwendungszweck steht
    /// „12345/67890“, im gelesenen Beleg vielleicht „12345 / 67890“, und eine Texterkennung
    /// setzt Leerzeichen, wo keine sind. Der Betrag selbst scheidet aus — er hat den Treffer
    /// erst hergestellt und kann ihn nicht zugleich bestätigen. Ein Datum ebenso: Es steht auf
    /// jedem Beleg und in jeder zweiten Buchung.
    /// </remarks>
    private static string? EvidenceIn(string? purpose, string belegziffern, string betragsziffern)
    {
        if (string.IsNullOrWhiteSpace(purpose) || belegziffern.Length == 0)
        {
            return null;
        }

        foreach (Match treffer in Kennung.Matches(purpose))
        {
            var wort = treffer.Value.Trim('.', '-', '/');
            var ziffern = DigitsOf(wort);

            if (ziffern.Length < MinimumDigits
                || ziffern == betragsziffern
                || BillTextReader.DateInLine(wort) is not null)
            {
                continue;
            }

            if (belegziffern.Contains(ziffern, StringComparison.Ordinal))
            {
                return wort;
            }
        }

        return null;
    }

    private static string DigitsOf(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : KeineZiffer.Replace(text, string.Empty);

    private static System.Globalization.CultureInfo Culture
        => System.Globalization.CultureInfo.InvariantCulture;
}
