using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Wann eine Lohnabrechnung ihre Gutschrift von selbst findet.
/// </summary>
/// <remarks>
/// <para><b>Eindeutig heißt dreierlei zugleich:</b> der Betrag stimmt auf den Cent, der Name
/// des Arbeitgebers steht in der Buchung, und es gibt genau <em>eine</em> solche. Jede dieser
/// Bedingungen allein ist zu schwach — zwei Gutschriften derselben Höhe im selben Monat kommen
/// vor, und ein Betrag ohne Namen trifft auch eine Erstattung.</para>
/// <para><b>„Fast“ zählt nicht.</b> Der Kandidatenschirm kennt Abstufungen bis fünfzehn
/// Prozent Abweichung; die taugen für eine Liste, aus der ein Mensch wählt, nicht für eine
/// Entscheidung, die von selbst fällt. In der echten Ablage hängt genau daran ein Fall: Eine
/// Abrechnung weist gut zwanzig Euro mehr aus, als überwiesen wurde. Die Automatik lässt ihn
/// liegen — und das ist der Zweck, denn dort ist etwas anzusehen.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class PayslipAutoPaymentTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 7, 5);
    private readonly int stelleId;
    private readonly int konto;

    public PayslipAutoPaymentTests()
    {
        using var context = database.Context();

        var stelle = new Employment
        {
            Employer = "Musterbetrieb GmbH",
            Position = "Sachbearbeitung",
            StartsOn = new DateOnly(2019, 10, 1),
            IsActive = true,
            GrossMonthly = 4500m,
        };

        context.Employments.Add(stelle);

        var giro = new Account
        {
            Name = "Musterbank Giro",
            ShortName = "Musterbank",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 7, 5),
        };

        context.Accounts.Add(giro);
        context.SaveChanges();

        stelleId = stelle.Id;
        konto = giro.Id;
    }

    private EmploymentService Dienst() => new(database.Context(), clock);

    /// <summary>Eine Abrechnung für Juni, wahlweise mit abweichendem Auszahlungsbetrag.</summary>
    private int Abrechnung(decimal auszahlung = 2913.64m)
    {
        using var context = database.Context();

        var zeile = new Payslip
        {
            EmploymentId = stelleId,
            Month = new DateOnly(2026, 6, 1),
            Gross = 4827.50m,
            Net = 3012.80m,
            Payout = auszahlung,
        };

        context.Payslips.Add(zeile);
        context.SaveChanges();

        return zeile.Id;
    }

    /// <summary>Eine Gutschrift auf dem Konto.</summary>
    private int Gutschrift(decimal betrag, string zahler = "Musterbetrieb GmbH", int tag = 28)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            AccountId = konto,
            BookingDate = new DateOnly(2026, 6, tag),
            Payee = zahler,
            Kind = TransactionKind.Income,
            Amount = betrag,
            CreatedAt = clock.Now,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return buchung.Id;
    }

    // ── Was von selbst zugeht ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Betrag_und_Name_zusammen_ordnen_zu()
    {
        var abrechnung = Abrechnung();
        var buchung = Gutschrift(2913.64m);

        Assert.True(await Dienst().AutoAssignPaymentAsync(abrechnung));

        using var pruef = database.Context();
        Assert.Equal(buchung, pruef.Payslips.Single(p => p.Id == abrechnung).TransactionId);
    }

    // ── Was liegen bleibt ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Der Fall aus der echten Ablage.</b> Zwanzig Euro Unterschied zwischen ausgewiesenem
    /// und überwiesenem Betrag: Der Kandidatenschirm zeigt die Buchung mit hoher Bewertung,
    /// die Automatik rührt sie nicht an. Dort ist etwas anzusehen — eine Korrektur, ein
    /// Vorschuss oder ein Lesefehler —, und das entscheidet kein Dienst.
    /// </summary>
    [Fact]
    public async Task Ein_fast_passender_Betrag_wird_nicht_zugeordnet()
    {
        var abrechnung = Abrechnung(auszahlung: 2913.64m);
        Gutschrift(2893.64m);

        Assert.False(await Dienst().AutoAssignPaymentAsync(abrechnung));

        using var pruef = database.Context();
        Assert.Null(pruef.Payslips.Single(p => p.Id == abrechnung).TransactionId);

        // Angeboten wird sie trotzdem — liegen lassen heißt nicht verschweigen.
        var kandidaten = await Dienst().GetPaymentCandidatesAsync(abrechnung);
        Assert.Single(kandidaten);
    }

    /// <summary>
    /// Ohne den Namen des Arbeitgebers bleibt es offen. Ein Betrag allein trifft auch eine
    /// Erstattung, eine Rückzahlung oder den Übertrag vom eigenen Konto.
    /// </summary>
    [Fact]
    public async Task Ein_fremder_Zahler_wird_nicht_zugeordnet()
    {
        var abrechnung = Abrechnung();
        Gutschrift(2913.64m, zahler: "Finanzamt Musterstadt");

        Assert.False(await Dienst().AutoAssignPaymentAsync(abrechnung));
    }

    /// <summary>
    /// <b>Zwei gleich hohe Gutschriften desselben Zahlers im selben Monat.</b> Beide passen,
    /// also passt keine: Welche gemeint ist, weiß nur der Inhaber. Eine zu greifen hieße, die
    /// Entscheidung zu verstecken.
    /// </summary>
    [Fact]
    public async Task Zwei_gleiche_Gutschriften_bleiben_offen()
    {
        var abrechnung = Abrechnung();
        Gutschrift(2913.64m, tag: 15);
        Gutschrift(2913.64m, tag: 28);

        Assert.False(await Dienst().AutoAssignPaymentAsync(abrechnung));

        using var pruef = database.Context();
        Assert.Null(pruef.Payslips.Single(p => p.Id == abrechnung).TransactionId);
    }

    /// <summary>
    /// Was ein Mensch entschieden hat, überschreibt keine Automatik.
    /// </summary>
    /// <remarks>
    /// <b>Der Testaufbau ist mit Absicht so gestellt, dass die Automatik etwas zu tun hätte:</b>
    /// Von Hand zugeordnet ist die Buchung mit dem abweichenden Betrag — das darf man, der
    /// Kandidatenschirm lässt bis fünfzehn Prozent zu. Daneben liegt genau eine, die auf den
    /// Cent stimmt. Ohne den Riegel würde die Automatik auf sie wechseln und die Entscheidung
    /// des Inhabers stillschweigend ersetzen.
    /// </remarks>
    [Fact]
    public async Task Eine_zugeordnete_Abrechnung_bleibt_wie_sie_ist()
    {
        var abrechnung = Abrechnung();
        var vonHand = Gutschrift(2893.64m, tag: 15);
        Gutschrift(2913.64m, tag: 28);

        using (var context = database.Context())
        {
            context.Payslips.Single(p => p.Id == abrechnung).TransactionId = vonHand;
            context.SaveChanges();
        }

        Assert.False(await Dienst().AutoAssignPaymentAsync(abrechnung));

        using var pruef = database.Context();
        Assert.Equal(vonHand, pruef.Payslips.Single(p => p.Id == abrechnung).TransactionId);
    }

    // ── Der Weg für einen gewachsenen Bestand ──────────────────────────────────────────

    /// <summary>
    /// Alles auf einmal: zugeordnet wird, was eindeutig ist — der Rest bleibt liegen und wird
    /// mitgezählt.
    /// </summary>
    [Fact]
    public async Task Alle_eindeutigen_auf_einmal()
    {
        var klar = Abrechnung();
        Gutschrift(2913.64m);

        int unklar;

        using (var context = database.Context())
        {
            var zweite = new Payslip
            {
                EmploymentId = stelleId,
                Month = new DateOnly(2026, 5, 1),
                Gross = 4827.50m,
                Payout = 3140.25m,
            };

            context.Payslips.Add(zweite);
            context.SaveChanges();
            unklar = zweite.Id;
        }

        Assert.Equal(1, await Dienst().AutoAssignAllAsync());

        using var pruef = database.Context();
        Assert.NotNull(pruef.Payslips.Single(p => p.Id == klar).TransactionId);
        Assert.Null(pruef.Payslips.Single(p => p.Id == unklar).TransactionId);
    }

    public void Dispose() => database.Dispose();
}
