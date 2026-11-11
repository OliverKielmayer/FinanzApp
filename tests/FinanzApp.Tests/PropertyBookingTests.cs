using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Welche Buchungen am Objekt stehen — und warum.
/// </summary>
/// <remarks>
/// <para>Eine Buchung trägt kein Objekt. <c>Transaction.PropertyId</c> steht nur an Einlagen,
/// für die Beteiligungsrechnung. Alles andere wird hergeleitet, und die Fälle hier sind die
/// aus einer echten Ablage abgelesenen: ein Vertrag mit Nummer, vier ohne, zwei davon beim
/// selben Anbieter, und ein Darlehen, dessen Geldgeber am Vertrag kurz und in der Buchung
/// lang geschrieben steht.</para>
/// <para>Der Grund an jeder Zeile ist Teil der Zusicherung und nicht Beiwerk: Eine
/// Vertragsnummer im Verwendungszweck ist ein Beleg, ein passender Betrag ein Schluss. Die
/// Tests prüfen deshalb den Grund mit.</para>
/// </remarks>
public sealed class PropertyBookingTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 11);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int objekt;
    private readonly int konto;

    public PropertyBookingTests()
    {
        using var context = database.Context();

        var account = new Account
        {
            Name = "Musterbank Giro",
            ShortName = "Musterbank",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 11),
        };

        var property = new Property
        {
            Name = "Haus Musterweg",
            Address = "Musterweg 7",
            MarketValue = 318_400m,
        };

        context.Accounts.Add(account);
        context.Properties.Add(property);
        context.SaveChanges();

        konto = account.Id;
        objekt = property.Id;
    }

    private PropertyService Service()
    {
        var context = database.Context();

        return new PropertyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(root),
                new ObjectLabelService(context),
                clock,
                new PdfPigTextReader(), NullLogger<DocumentService>.Instance),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(null)));
    }

    private void Vertrag(string name, string anbieter, string? nummer)
    {
        using var context = database.Context();

        context.Contracts.Add(new Contract
        {
            Name = name,
            Provider = anbieter,
            ContractNumber = nummer,
            PropertyId = objekt,
            Amount = 48m,
            PaymentInterval = PremiumInterval.Monthly,
        });

        context.SaveChanges();
    }

    private void Darlehen(string name, string geldgeber, decimal rate)
    {
        using var context = database.Context();

        var darlehen = new Loan
        {
            Name = name,
            Lender = geldgeber,
            RemainingDebt = 191_750m,
            InterestRatePercent = 1.9m,
            Installment = rate,
            NextPaymentDate = new DateOnly(2026, 12, 1),
        };

        context.Loans.Add(darlehen);
        context.SaveChanges();

        // Das Darlehen hängt am Objekt und nicht umgekehrt.
        var haus = context.Properties.First(p => p.Id == objekt);
        haus.LoanId = darlehen.Id;
        context.SaveChanges();
    }

    private int Buchung(
        string empfaenger, decimal betrag, string? zweck = null,
        TransactionKind art = TransactionKind.Expense, int? fuerObjekt = null)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            BookingDate = new DateOnly(2026, 9, 27),
            Payee = empfaenger,
            Kind = art,
            Amount = art == TransactionKind.Expense ? -betrag : betrag,
            AccountId = konto,
            Purpose = zweck,
            PropertyId = fuerObjekt,
            CreatedAt = new DateTime(2026, 9, 27, 7, 0, 0, DateTimeKind.Local),
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();
        return buchung.Id;
    }

    private async Task<IReadOnlyList<LinkedPaymentDto>> BuchungenAsync()
        => (await Service().GetAsync(objekt))!.Bookings;

    // ── Die feste Zuordnung ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Die_Vertragsnummer_im_Verwendungszweck_bindet_die_Buchung()
    {
        Vertrag("Abfall", "Landkreis Musterkreis", "4471902");
        Buchung("LANDRATSAMT", 61.80m, "Abfallgebuehren 4471902 Jahr 2026");

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal("Abfall — Nummer im Verwendungszweck", zeile.MatchReason);
    }

    /// <summary>
    /// Die Nummer wird auf ihren Kern verglichen, und zwar auf <b>beiden</b> Seiten.
    /// </summary>
    /// <remarks>
    /// Am Vertrag steht sie mit Bindestrichen, in der Buchung mit Leerzeichen und einem
    /// Schrägstrich — keine der beiden Schreibweisen enthält die andere. Wer nur die eine
    /// Seite normalisiert, findet nichts; und weil beide Seiten für sich plausibel aussehen,
    /// fällt es niemandem auf. Eine frühere Fassung dieses Tests nannte die Nummer im
    /// Verwendungszweck schon in Reinform und ging deshalb auch ohne Normalisierung durch.
    /// </remarks>
    [Fact]
    public async Task Die_Schreibweise_der_Nummer_ist_egal()
    {
        Vertrag("Abfall", "Landkreis Musterkreis", "447-19-02");
        Buchung("LANDRATSAMT", 61.80m, "Abfallgebuehren 4471 902/2026");

        Assert.Single(await BuchungenAsync());
    }

    // ── Der Anbietername, und wo er aufhört ────────────────────────────────────────────

    [Fact]
    public async Task Ohne_Nummer_zieht_der_Anbietername()
    {
        Vertrag("Strom", "Musterstrom", null);
        Buchung("Musterstrom Vertrieb GmbH", 87.40m, "Abschlag Oktober");

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal(
            "Strom — Anbieter im Empfänger, keine Nummer am Vertrag", zeile.MatchReason);
    }

    /// <summary>
    /// Der Fall, an dem die Zuordnung über den Namen scheitert — und deshalb unterbleibt:
    /// Zwei Verträge desselben Anbieters am selben Objekt. Jede Buchung hinge an beiden, und
    /// gemerkt würde es nie, weil beide Zeilen für sich plausibel aussehen.
    /// </summary>
    [Fact]
    public async Task Zwei_Vertraege_desselben_Anbieters_binden_gar_nichts()
    {
        Vertrag("Internet", "Musterfunk", null);
        Vertrag("Fernsehen", "Musterfunk", null);
        Buchung("Musterfunk GmbH", 43.25m, "Rechnung Oktober");

        Assert.Empty(await BuchungenAsync());
    }

    /// <summary>
    /// Die Gegenprobe dazu: Hat einer der beiden eine Nummer, steht seine Buchung wieder da —
    /// die Mehrdeutigkeit betrifft den Namen, nicht den Vertrag.
    /// </summary>
    [Fact]
    public async Task Eine_Nummer_rettet_den_mehrdeutigen_Anbieter()
    {
        Vertrag("Internet", "Musterfunk", "77451290");
        Vertrag("Fernsehen", "Musterfunk", null);
        Buchung("Musterfunk GmbH", 43.25m, "Kundennummer 77451290");

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal("Internet — Nummer im Verwendungszweck", zeile.MatchReason);
    }

    // ── Das Darlehen ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Geldgeber steht am Darlehen in Kurzform und in der Buchung ausgeschrieben — so
    /// liegt es in einer echten Ablage. Dann greift allein der Betrag, und die Zeile sagt
    /// das auch.
    /// </summary>
    [Fact]
    public async Task Die_Rate_bindet_auch_ohne_passenden_Empfaenger()
    {
        Darlehen("Immo Darlehen", "Musterkasse MM", 1_472.18m);
        Buchung("MUSTERKASSE MITTELMARK OST", 1_472.18m, "Darl.-Leistung");

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal(
            "Darlehen Immo Darlehen — Betrag entspricht der Rate, sonst nichts",
            zeile.MatchReason);
    }

    [Fact]
    public async Task Steht_der_Geldgeber_in_der_Buchung_sagt_die_Zeile_das()
    {
        Darlehen("Immo Darlehen", "Musterkasse", 1_472.18m);
        Buchung("Musterkasse Mittelmark", 318.50m, "Sondertilgung");

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal("Darlehensrate an Musterkasse — Empfänger, keine Nummer", zeile.MatchReason);
    }

    // ── Die Einlage ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der einzige echte Verweis. Er steht vorn, weil er kein Schluss ist: Eine Einlage, die
    /// zufällig den Betrag der Rate trägt, bleibt eine Einlage.
    /// </summary>
    [Fact]
    public async Task Die_Einlage_auf_dieses_Objekt_steht_mit_ihrem_eigenen_Grund()
    {
        Darlehen("Immo Darlehen", "Musterkasse", 1_472.18m);
        Buchung("Carmen", 1_472.18m, "Einlage",
            art: TransactionKind.Deposit, fuerObjekt: objekt);

        var zeile = Assert.Single(await BuchungenAsync());

        Assert.Equal("Einlage für dieses Objekt", zeile.MatchReason);
    }

    // ── Was nicht dazugehört ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_fremde_Ausgabe_bleibt_drausen()
    {
        Vertrag("Strom", "Musterstrom", "9930012");
        Buchung("Baeckerei Muster", 4.20m, "Kartenzahlung");

        Assert.Empty(await BuchungenAsync());
    }

    public void Dispose() => database.Dispose();
}
