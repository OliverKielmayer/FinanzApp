using FinanzApp.Client.Services;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Buchungen tagweise, mit Tagessumme und Kontostand — wie ein Auszug auf Papier.
/// </summary>
/// <remarks>
/// Die Optik ist das eine; hier steht die Zahl. Ein Stand je Tag ist eine Behauptung über das
/// Konto, und sie trägt nur, wenn die Liste vollständig ist. Die Prüfungen halten vor allem
/// fest, <em>wann sie nicht dasteht</em>.
/// </remarks>
public sealed class BookingDayTests
{
    [Fact]
    public void Ein_Tag_fasst_seine_Saetze_zusammen()
    {
        var tage = BookingDays.Group(
            [Satz(1, "2026-10-20", 18m), Satz(2, "2026-10-20", -8m), Satz(3, "2026-10-16", -140m)],
            startBalance: null);

        Assert.Equal(2, tage.Count);
        Assert.Equal(new DateOnly(2026, 10, 20), tage[0].Date);
        Assert.Equal(2, tage[0].Items.Count);
        Assert.Equal(10m, tage[0].Sum);
        Assert.Equal(-140m, tage[1].Sum);
    }

    /// <summary>
    /// Der Stand des jüngsten Tages ist der Saldo des Kontos.
    /// </summary>
    /// <remarks>
    /// Der Saldo ist Anfangsbestand plus alle Buchungen. Nach der jüngsten gibt es keine mehr —
    /// also ist der Stand am Ende dieses Tages genau er.
    /// </remarks>
    [Fact]
    public void Der_juengste_Tag_traegt_den_Saldo_des_Kontos()
    {
        var tage = BookingDays.Group([Satz(1, "2026-10-20", 18m)], startBalance: 9_671.77m);

        Assert.Equal(9_671.77m, tage[0].Balance);
    }

    [Fact]
    public void Jeder_Tag_davor_steht_um_seine_Nachfolger_tiefer()
    {
        var tage = BookingDays.Group(
            [Satz(1, "2026-10-20", 18m), Satz(2, "2026-10-16", -280m), Satz(3, "2026-10-13", 300m)],
            startBalance: 1_000m);

        Assert.Equal(1_000m, tage[0].Balance);
        Assert.Equal(982m, tage[1].Balance);
        Assert.Equal(1_262m, tage[2].Balance);
    }

    [Fact]
    public void Ohne_Ausgangsstand_traegt_kein_Tag_einen()
    {
        var tage = BookingDays.Group(
            [Satz(1, "2026-10-20", 18m), Satz(2, "2026-10-16", -280m)],
            startBalance: null);

        Assert.All(tage, t => Assert.Null(t.Balance));
    }

    [Fact]
    public void Eine_leere_Liste_ergibt_keinen_Tag()
        => Assert.Empty(BookingDays.Group([], startBalance: 1_000m));

    /// <summary>
    /// Über mehrere Konten hinweg gibt es keinen Stand.
    /// </summary>
    /// <remarks>
    /// Eine Summe von Ständen ist kein Stand: sie ändert sich auch an Tagen, an denen auf dem
    /// einen Konto nichts gebucht wurde, und die Rückrechnung über die sichtbaren Sätze würde
    /// das nie bemerken.
    /// </remarks>
    [Fact]
    public void Ohne_gewaehltes_Konto_gibt_es_keinen_Ausgangsstand()
        => Assert.Null(BookingDays.StartBalance(null, hasOtherFilter: false, skip: 0));

    /// <summary>
    /// Ein weiterer Filter nimmt der Rechnung ihre Grundlage.
    /// </summary>
    /// <remarks>
    /// Wer nach „Versicherungen“ filtert, sieht fünf von dreißig Sätzen. Ein daneben gerechneter
    /// Stand wäre der eines Kontos, auf dem es nur Versicherungen gibt — und er sähe so aus wie
    /// der echte.
    /// </remarks>
    [Fact]
    public void Ein_weiterer_Filter_verbietet_den_Ausgangsstand()
        => Assert.Null(BookingDays.StartBalance(Konto(1_000m), hasOtherFilter: true, skip: 0));

    [Fact]
    public void Auf_der_zweiten_Seite_gibt_es_keinen_Ausgangsstand()
        => Assert.Null(BookingDays.StartBalance(Konto(1_000m), hasOtherFilter: false, skip: 100));

    [Fact]
    public void Ein_Konto_ohne_Filter_auf_der_ersten_Seite_traegt_ihn()
        => Assert.Equal(1_000m, BookingDays.StartBalance(Konto(1_000m), hasOtherFilter: false, skip: 0));

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private static TransactionDto Satz(int id, string tag, decimal betrag) => new()
    {
        Id = id,
        BookingDate = DateOnly.Parse(tag),
        Payee = "Empfänger " + id,
        Kind = betrag >= 0 ? TransactionKind.Income : TransactionKind.Expense,
        Amount = betrag,
        AccountId = 1,
        AccountName = "Girokonto",
        AccountShortName = "Giro",
    };

    private static AccountDto Konto(decimal saldo) => new()
    {
        Id = 1,
        Name = "Girokonto",
        ShortName = "Giro",
        Balance = saldo,
        IsMine = true,
        Sharing = AccountSharing.Private,
        BalanceAsOf = new DateOnly(2026, 10, 20),
    };
}
