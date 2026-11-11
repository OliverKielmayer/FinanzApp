using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Auszug, den die Krankenversicherung über ihre Arztrechnungen zeigt.
/// </summary>
/// <remarks>
/// <para>Die Frage, die der Vertrag beantworten soll, ist nicht „was kostet er", sondern:
/// Was ist eingereicht, was steht noch aus, und ist der Jahresdeckel des Eigenanteils voll?
/// Die Rechnungen liegen unter „Gesundheit &amp; PKV", ihre Bilanz in den Auswertungen — vom
/// Vertrag aus war beides unsichtbar.</para>
/// <para><b>Die Falle steckt im Deckel.</b> Gegen ihn zählen nur Rechnungen <em>ohne</em>
/// Zahnbehandlung: Zahn hat seinen eigenen Satz und dürfte den Deckel nicht aufbrauchen.
/// „Eigenanteil" in der Aufteilung und „verbraucht" am Deckel sind deshalb zwei Zahlen, und
/// der Test hier hält sie auseinander.</para>
/// </remarks>
public sealed class PolicyHealthTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 11);

    private HealthBalanceService Service() => new(database.Context(), clock);

    /// <summary>Der Vertrag, aus dem die Regel kommt.</summary>
    private void Vertrag(decimal satz = 12m, decimal? deckel = 360m, decimal zahnsatz = 15m)
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "Krankenversicherung",
            Provider = "Musterkasse",
            Kind = PolicyKind.Health,
            IsCapitalForming = false,
            Premium = 389m,
            PremiumInterval = PremiumInterval.Monthly,
            OwnSharePercent = satz,
            OwnShareAnnualCap = deckel,
            DentalOwnSharePercent = zahnsatz,
        });

        context.SaveChanges();
    }

    private void Rechnung(
        decimal brutto, decimal eigenanteil, decimal erwartet,
        decimal? erstattet = null, MedicalBillStatus status = MedicalBillStatus.Submitted,
        bool zahn = false, int jahr = 2026, bool eingereicht = true)
    {
        using var context = database.Context();

        context.MedicalBills.Add(new MedicalBill
        {
            Provider = zahn ? "Dr. Musterzahn" : "Dr. Musterarzt",
            BillDate = new DateOnly(jahr, 9, 27),
            GrossAmount = brutto,
            OwnShare = eigenanteil,
            ExpectedReimbursement = erwartet,
            ActualReimbursement = erstattet,
            Status = status,
            IsDental = zahn,
            SubmittedAt = eingereicht ? new DateTime(jahr, 9, 28, 9, 0, 0, DateTimeKind.Local) : null,
            CreatedAt = new DateTime(jahr, 9, 27, 7, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    // ── Der Jahresdeckel ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Zahnrechnung trägt denselben Eigenanteil wie die andere. In der Aufteilung zählen
    /// beide, gegen den Deckel nur eine. Wären die Beträge gleich gewählt, ginge der Test
    /// auch durch, wenn der Filter fehlte — sie sind deshalb verschieden.
    /// </summary>
    [Fact]
    public async Task Zahnbehandlung_zaehlt_nicht_gegen_den_Jahresdeckel()
    {
        Vertrag(deckel: 360m);
        Rechnung(brutto: 468m, eigenanteil: 46.80m, erwartet: 421.20m);
        Rechnung(brutto: 985m, eigenanteil: 98.50m, erwartet: 886.50m, zahn: true);

        var stand = await Service().ForPolicyAsync();

        Assert.Equal(46.80m, stand.OwnShareUsed);
        Assert.Equal(145.30m, stand.Split.OwnShare);
        Assert.False(stand.CapReached);
    }

    [Fact]
    public async Task Ist_der_Deckel_voll_sagt_der_Stand_das()
    {
        Vertrag(deckel: 360m);
        Rechnung(brutto: 3_600m, eigenanteil: 360m, erwartet: 3_240m);

        var stand = await Service().ForPolicyAsync();

        Assert.True(stand.CapReached);
    }

    /// <summary>
    /// Die Grenze liegt bei „erreicht", nicht bei „überschritten": Ein Cent darunter ist
    /// nicht voll, und genau dort wird die Zeile falsch, wenn jemand das Zeichen dreht.
    /// </summary>
    [Fact]
    public async Task Ein_Cent_unter_dem_Deckel_ist_er_nicht_voll()
    {
        Vertrag(deckel: 360m);
        Rechnung(brutto: 3_599m, eigenanteil: 359.99m, erwartet: 3_239.01m);

        Assert.False((await Service().ForPolicyAsync()).CapReached);
    }

    [Fact]
    public async Task Ohne_gepflegten_Deckel_bleibt_die_Frage_offen()
    {
        Vertrag(deckel: null);
        Rechnung(brutto: 468m, eigenanteil: 46.80m, erwartet: 421.20m);

        var stand = await Service().ForPolicyAsync();

        Assert.Null(stand.OwnShareAnnualCap);
        Assert.False(stand.CapReached);
    }

    // ── Der Stand der Vorgänge ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Nicht_eingereichte_Rechnungen_werden_gezaehlt()
    {
        Vertrag();
        Rechnung(brutto: 468m, eigenanteil: 46.80m, erwartet: 421.20m, eingereicht: false,
            status: MedicalBillStatus.Recorded);
        Rechnung(brutto: 234m, eigenanteil: 23.40m, erwartet: 210.60m);

        var stand = await Service().ForPolicyAsync();

        Assert.Equal(2, stand.BillCount);
        Assert.Equal(1, stand.NotSubmittedCount);
    }

    /// <summary>
    /// Aus der echten Ablage abgelesen: Eine Rechnung kann erstattet und abgeschlossen sein,
    /// ohne dass je ein Einreichdatum gesetzt wurde — wer die Zahlung von Hand zuordnet,
    /// lässt es leer. „Noch nicht eingereicht" über einen bezahlten Vorgang zu melden, wäre
    /// eine Aufforderung zu etwas, das erledigt ist.
    /// </summary>
    [Fact]
    public async Task Ein_abgeschlossener_Vorgang_mahnt_nicht_zur_Einreichung()
    {
        Vertrag();
        Rechnung(brutto: 777m, eigenanteil: 77.70m, erwartet: 699.30m, erstattet: 699.30m,
            status: MedicalBillStatus.Completed, eingereicht: false);

        var stand = await Service().ForPolicyAsync();

        Assert.Equal(1, stand.CompletedCount);
        Assert.Equal(0, stand.NotSubmittedCount);
    }

    [Fact]
    public async Task Abgeschlossen_ist_woran_nichts_mehr_offen_ist()
    {
        Vertrag();
        Rechnung(brutto: 468m, eigenanteil: 46.80m, erwartet: 421.20m, erstattet: 421.20m,
            status: MedicalBillStatus.Completed);
        Rechnung(brutto: 234m, eigenanteil: 23.40m, erwartet: 210.60m);

        var stand = await Service().ForPolicyAsync();

        Assert.Equal(1, stand.CompletedCount);
        Assert.Equal(210.60m, stand.Split.Expected);
        Assert.Equal(421.20m, stand.Split.Paid);
    }

    /// <summary>
    /// Der Zeitraum ist das Kalenderjahr der Rechnung. Eine Rechnung aus dem Vorjahr gehört
    /// in dessen Deckel und nicht in diesen — sonst wäre jeder Deckel irgendwann voll.
    /// </summary>
    [Fact]
    public async Task Das_Vorjahr_zaehlt_nicht_mit()
    {
        Vertrag(deckel: 360m);
        Rechnung(brutto: 3_600m, eigenanteil: 360m, erwartet: 3_240m, jahr: 2025);
        Rechnung(brutto: 468m, eigenanteil: 46.80m, erwartet: 421.20m);

        var stand = await Service().ForPolicyAsync();

        Assert.Equal(2026, stand.Year);
        Assert.Equal(1, stand.BillCount);
        Assert.Equal(46.80m, stand.OwnShareUsed);
        Assert.False(stand.CapReached);
    }

    public void Dispose() => database.Dispose();
}
