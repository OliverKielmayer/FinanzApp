using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Beitragszahlungen am Vertrag — v5-Handoff, Abschnitt 19.2.
/// </summary>
/// <remarks>
/// <para>Zugeordnet wird <b>ausschließlich über die Vertragsnummer</b>. Vorher lief die Suche
/// über den Namen des Anbieters, und bei vier Verträgen desselben Hauses hing damit jede
/// Beitragsbuchung an jedem Vertrag: eine Buchung über 212 € stand unter einem Vertrag, der
/// 42 € im Monat kostet.</para>
/// <para>Der erste Test hier ist der, der das verhindert.</para>
/// </remarks>
public sealed class PolicyPaymentTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 30);

    private readonly int konto;

    public PolicyPaymentTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Raiffeisen Giro", ShortName = "Raiffeisen", BankName = "Raiffeisenbank",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 1, 1),
        };

        context.Accounts.Add(giro);
        context.SaveChanges();

        konto = giro.Id;
    }

    private PolicyService Service()
    {
        var context = database.Context();

        return new PolicyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(Path.Combine(Path.GetTempPath(), "finanzapp-tests", "policy")),
                new ObjectLabelService(context),
                clock,
                NullLogger<DocumentService>.Instance),
            clock);
    }

    private int Policy(string name, string anbieter, string? nummer, decimal beitrag)
    {
        using var context = database.Context();
        var vertrag = new Policy
        {
            Name = name,
            Provider = anbieter,
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            PolicyNumber = nummer,
            Premium = beitrag,
            PremiumInterval = PremiumInterval.Monthly,
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();
        return vertrag.Id;
    }

    private void Booking(string empfaenger, decimal betrag, string? zweck = null, string? text = null)
    {
        using var context = database.Context();
        context.Transactions.Add(new Transaction
        {
            BookingDate = new DateOnly(2026, 8, 18),
            Payee = empfaenger,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            AccountId = konto,
            Purpose = zweck,
            BookingText = text,
            CreatedAt = new DateTime(2026, 8, 18, 6, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    // ── Die Regel ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Anbietername ordnet nichts zu.
    /// </summary>
    /// <remarks>
    /// Zwei Verträge desselben Hauses, zwei Buchungen mit je ihrer Nummer: jede gehört zu genau
    /// einem Vertrag. Über den Namen gehörten beide zu beiden.
    /// </remarks>
    [Fact]
    public async Task Zugeordnet_wird_ueber_die_Nummer_nicht_ueber_den_Namen()
    {
        var eins = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 212m);
        var zwei = Policy("Risikoleben", "Heidelberger Leben", "01511104-02", 42m);

        Booking("Heidelberger Leben Beitrag", 212m, "BEITRAG 08/2026 VERTRAG 01511104-01");
        Booking("Heidelberger Leben Beitrag", 42m, "BEITRAG 08/2026 VERTRAG 01511104-02");

        var ersterVertrag = await Service().GetAsync(eins);
        var zweiterVertrag = await Service().GetAsync(zwei);

        var a = Assert.Single(ersterVertrag!.Payments);
        var b = Assert.Single(zweiterVertrag!.Payments);

        Assert.Equal(212m, a.Amount);
        Assert.Equal(42m, b.Amount);
    }

    /// <summary>
    /// Die Schreibweise der Nummer ist gleichgültig.
    /// </summary>
    /// <remarks>
    /// „01511104-01“, „01511104 01“ und „01511104/01“ sind dieselbe Nummer. Ohne Normalisierung
    /// fände die Zuordnung nur die eine Schreibweise, die die Bank gerade verwendet.
    /// </remarks>
    [Theory]
    [InlineData("BEITRAG VERTRAG 01511104-01")]
    [InlineData("BEITRAG VERTRAG 01511104 01")]
    [InlineData("BEITRAG VERTRAG 01511104/01")]
    [InlineData("beitrag vertrag 0151110401")]
    public async Task Die_Schreibweise_der_Nummer_ist_gleichgueltig(string zweck)
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 212m);
        Booking("Irgendein Empfänger", 212m, zweck);

        Assert.Single((await Service().GetAsync(id))!.Payments);
    }

    /// <summary>Die Nummer darf auch im Buchungstext oder beim Empfänger stehen.</summary>
    [Fact]
    public async Task Die_Nummer_zaehlt_auch_ausserhalb_des_Verwendungszwecks()
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 212m);
        Booking("Heidelberger", 212m, zweck: null, text: "DAUERAUFTRAG 01511104-01");

        var zahlung = Assert.Single((await Service().GetAsync(id))!.Payments);
        Assert.Equal("Vertragsnummer 01511104-01 im Buchungstext", zahlung.MatchReason);
    }

    /// <summary>Ohne Nummer am Vertrag gibt es keine Zuordnung — und keine geratene.</summary>
    [Fact]
    public async Task Ohne_Vertragsnummer_wird_nichts_zugeordnet()
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", nummer: null, 212m);
        Booking("Heidelberger Leben Beitrag", 212m, "BEITRAG 08/2026");

        Assert.Empty((await Service().GetAsync(id))!.Payments);
    }

    /// <summary>Eine fremde Buchung bleibt fremd, auch wenn der Betrag passt.</summary>
    [Fact]
    public async Task Ein_passender_Betrag_allein_ordnet_nichts_zu()
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 212m);
        Booking("Heidelberger Leben Beitrag", 212m, "BEITRAG 08/2026 VERTRAG 99999999-99");

        Assert.Empty((await Service().GetAsync(id))!.Payments);
    }

    // ── Die Begründung ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Jede Zeile trägt ihre Begründung und den Verwendungszweck im Original.
    /// </summary>
    /// <remarks>
    /// Eine Zuordnung ohne Begründung ist eine Behauptung. Wer den Verwendungszweck sieht, kann
    /// sie nachlesen statt sie zu glauben.
    /// </remarks>
    [Fact]
    public async Task Jede_Zeile_nennt_ihren_Grund_und_den_Zweck()
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 212m);
        Booking("Heidelberger Leben Beitrag", 212m, "BEITRAG 08/2026 VERTRAG 01511104-01 KAPITAL-LV");

        var zahlung = Assert.Single((await Service().GetAsync(id))!.Payments);

        Assert.Equal("Vertragsnummer 01511104-01 im Verwendungszweck", zahlung.MatchReason);
        Assert.Equal("BEITRAG 08/2026 VERTRAG 01511104-01 KAPITAL-LV", zahlung.Reference);
    }

    /// <summary>
    /// Ein abweichender Betrag ändert nichts an der Zuordnung.
    /// </summary>
    /// <remarks>
    /// Die Nummer stimmt, also gehört die Buchung zum Vertrag. Abweichend ist der Betrag — und
    /// das ist eine Aussage über einen veralteten Wert, kein Grund, die Zeile wegzulassen.
    /// </remarks>
    [Fact]
    public async Task Ein_abweichender_Betrag_bleibt_zugeordnet()
    {
        var id = Policy("Kapital-LV", "Heidelberger Leben", "01511104-01", 42m);
        Booking("Heidelberger Leben Beitrag", 212m, "BEITRAG 08/2026 VERTRAG 01511104-01");

        var zahlung = Assert.Single((await Service().GetAsync(id))!.Payments);

        Assert.Equal(212m, zahlung.Amount);
        Assert.NotNull(zahlung.MatchReason);
    }

    public void Dispose() => database.Dispose();
}
