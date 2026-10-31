using FinanzApp.Shared.Contracts;

namespace FinanzApp.Client.Services;

/// <summary>Ein Buchungstag: seine Sätze, ihre Summe und der Kontostand an seinem Ende.</summary>
/// <remarks>
/// So liest sich ein Kontoauszug auf Papier, und so hat ihn der Nutzer im Blick: nicht als
/// fortlaufende Liste, sondern tagweise, mit dem Stand am Rand. Die Tagessumme beantwortet
/// „was ist an dem Tag passiert“, der Stand „wo stand das Konto danach“.
/// </remarks>
public sealed record BookingDay
{
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<TransactionDto> Items { get; init; }

    /// <summary>Was an diesem Tag zu- und abgegangen ist, vorzeichenbehaftet.</summary>
    public required decimal Sum { get; init; }

    /// <summary>
    /// Der Kontostand am Ende dieses Tages — <c>null</c>, wenn er sich nicht belegen lässt.
    /// </summary>
    /// <remarks>
    /// Lieber keine Zahl als eine, die nur fast stimmt: ein Stand neben einem gefilterten
    /// Ausschnitt wäre der Stand eines Kontos, das es nicht gibt.
    /// </remarks>
    public decimal? Balance { get; init; }
}

/// <summary>
/// Fasst eine Buchungsliste zu Tagen zusammen und rechnet den Kontostand zurück.
/// </summary>
/// <remarks>
/// <para>Gerechnet wird <b>rückwärts</b> vom heutigen Saldo des Kontos. Der Saldo ist
/// Anfangsbestand plus alle Buchungen; die Liste steht neueste zuerst. Der Stand am Ende des
/// jüngsten Tages ist damit der Saldo selbst, und jeder Tag davor ist der Stand des jüngeren
/// abzüglich dessen, was an ihm gebucht wurde.</para>
/// <para>Im Client und nicht im Dienst, weil hier beides vorliegt — der Saldo aus der Kontoliste
/// und der sichtbare Ausschnitt. Eine zweite Zahl vom Server wäre eine zweite Quelle für
/// dieselbe Größe.</para>
/// </remarks>
public static class BookingDays
{
    /// <summary>
    /// Der Stand, von dem aus zurückgerechnet werden darf — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>Drei Bedingungen, und jede einzelne verbietet die Zahl:</para>
    /// <para><b>Ein Konto.</b> Über mehrere Konten hinweg gibt es keinen Stand, nur eine Summe
    /// von Ständen — und die ändert sich an einem Tag, an dem auf keinem der Konten etwas
    /// gebucht wurde.</para>
    /// <para><b>Kein weiterer Filter.</b> Fehlt der Liste auch nur eine Buchung des Kontos,
    /// fehlt sie der Rechnung. Der Stand wäre um sie daneben, und niemand sähe es.</para>
    /// <para><b>Die erste Seite.</b> Weiter hinten fehlen die jüngeren Buchungen, von denen aus
    /// gerechnet wird.</para>
    /// </remarks>
    public static decimal? StartBalance(AccountDto? account, bool hasOtherFilter, int skip)
        => account is not null && !hasOtherFilter && skip == 0 ? account.Balance : null;

    /// <summary>
    /// Gruppiert die Sätze nach Buchungstag, neueste zuerst.
    /// </summary>
    /// <param name="items">Die sichtbaren Sätze, nach Buchungstag absteigend.</param>
    /// <param name="startBalance">
    /// Der Saldo des Kontos, oder <c>null</c> — dann bleibt jeder Tag ohne Stand.
    /// </param>
    public static IReadOnlyList<BookingDay> Group(
        IReadOnlyList<TransactionDto> items, decimal? startBalance)
    {
        var tage = new List<BookingDay>();
        var stand = startBalance;

        // Die Reihenfolge der Liste zählt, nicht die des Kalenders: sie kommt schon sortiert
        // vom Dienst, und neu zu sortieren hieße, eine zweite Meinung über dieselbe Ordnung zu
        // haben. GroupBy behält die Reihenfolge des ersten Auftretens und damit ihre.
        foreach (var tag in items.GroupBy(t => t.BookingDate))
        {
            var sätze = tag.ToList();
            var summe = sätze.Sum(t => t.Amount);

            tage.Add(new BookingDay
            {
                Date = tag.Key,
                Items = sätze,
                Sum = summe,
                Balance = stand,
            });

            // Der Tag davor stand um das tiefer, was an diesem Tag dazukam.
            stand -= summe;
        }

        return tage;
    }
}
