using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Aus einer Kontobuchung eine Anschaffung erfassen.
/// </summary>
/// <remarks>
/// <para><b>Der Weg kommt aus dem Alltag:</b> Eine Bestellung fällt meist erst auf, wenn die
/// Abbuchung im Konto steht. Dann stehen Betrag, Tag und Händler schon da, und die Buchung
/// ist bekannt — sie muss nicht hinterher gesucht werden.</para>
/// <para><b>Das ist die festeste Verknüpfung, die es hier gibt.</b> Sonst findet eine
/// Anschaffung ihre Zahlung über einen Abgleich: Betrag auf den Cent, Zeitfenster,
/// Belegnummer im Verwendungszweck — und der schweigt, wo kein Beleg hängt. Wer die Buchung
/// vor sich hatte, weiß es besser als jeder Abgleich.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class PurchaseFromTransactionTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 7, 5);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int konto;

    public PurchaseFromTransactionTests()
    {
        using var context = database.Context();

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
        konto = giro.Id;
    }

    private CreateFormService Formulare()
    {
        var context = database.Context();

        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        return new CreateFormService(
            context, clock, documents, new ObjectLabelService(context),
            new NoPolicyDocumentAnalyzer());
    }

    private PurchaseService Anschaffungen()
    {
        var context = database.Context();

        return new PurchaseService(
            context,
            new DocumentService(
                context, TestDatabase.PathService(root), new ObjectLabelService(context),
                clock, new PdfPigTextReader(), NullLogger<DocumentService>.Instance),
            clock);
    }

    /// <summary>Eine Abbuchung, aus der eine Anschaffung werden kann.</summary>
    private int Abbuchung(
        decimal betrag = 73.42m, string zahler = "Musterversand Payments", int tag = 28)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            AccountId = konto,
            BookingDate = new DateOnly(2026, 6, tag),
            Payee = zahler,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            Purpose = "307-4412908-5530174 MV Mktp DE",
            CreatedAt = clock.Now,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return buchung.Id;
    }

    private int Gutschrift(decimal betrag = 73.42m)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            AccountId = konto,
            BookingDate = new DateOnly(2026, 6, 28),
            Payee = "Musterversand Payments",
            Kind = TransactionKind.Income,
            Amount = betrag,
            CreatedAt = clock.Now,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return buchung.Id;
    }

    private static string? Vorgabe(CreateFormDto form, string key)
        => form.Fields.FirstOrDefault(f => f.Key == key)?.DefaultValue;

    // ── Die Vorbefüllung ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Betrag, Tag und Händler kommen aus der Buchung — mehr weiß sie nicht.
    /// </summary>
    /// <remarks>
    /// Der Name ist derselbe Empfänger: Was gekauft wurde, sagt kein Kontoauszug, und der
    /// Händlername ist ehrlicher als ein geratener Gegenstand. Wer es besser weiß,
    /// überschreibt ihn im Feld.
    /// </remarks>
    [Fact]
    public async Task Die_Maske_kommt_aus_der_Buchung_vorbefuellt()
    {
        var buchung = Abbuchung(betrag: 73.42m);

        var form = await Formulare().GetFormAsync(
            CreateObjectType.Purchase, null, null, buchung);

        Assert.NotNull(form);
        Assert.Equal("Musterversand Payments", Vorgabe(form, "name"));
        Assert.Equal("Musterversand Payments", Vorgabe(form, "vendor"));
        Assert.Equal("2026-06-28", Vorgabe(form, "purchase"));

        // Ohne Vorzeichen: Der Kaufpreis ist eine positive Zahl, auch wenn die Buchung
        // negativ ist.
        Assert.Equal("73,42", Vorgabe(form, "price"));
    }

    /// <summary>
    /// <b>Eine Gutschrift ist keine Anschaffung.</b> Aus ihr wird nichts vorbefüllt — sonst
    /// entstünde aus einer Erstattung ein Kauf mit positivem Preis.
    /// </summary>
    [Fact]
    public async Task Aus_einer_Gutschrift_wird_nichts_vorbefuellt()
    {
        var buchung = Gutschrift();

        var form = await Formulare().GetFormAsync(
            CreateObjectType.Purchase, null, null, buchung);

        Assert.NotNull(form);
        Assert.Null(Vorgabe(form, "name"));
        Assert.Null(Vorgabe(form, "price"));
    }

    // ── Das Anlegen ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Die_Anschaffung_merkt_sich_ihre_Buchung()
    {
        var buchung = Abbuchung();

        var ergebnis = await Formulare().CreateAsync(
            CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Vogelhaus",
                ["purchase"] = "2026-06-28",
                ["price"] = "73,42",
            },
            fromTransactionId: buchung);

        Assert.True(ergebnis.Ok);

        using var pruef = database.Context();
        Assert.Equal(buchung, pruef.Purchases.Single().TransactionId);
    }

    /// <summary>
    /// Eine Kennziffer, zu der es keine Abbuchung gibt, wird nicht übernommen — sonst hinge
    /// die Anschaffung an einer Buchung, die niemand gesehen hat.
    /// </summary>
    [Fact]
    public async Task Eine_erfundene_Kennziffer_wird_nicht_vermerkt()
    {
        var ergebnis = await Formulare().CreateAsync(
            CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Vogelhaus",
                ["purchase"] = "2026-06-28",
                ["price"] = "73,42",
            },
            fromTransactionId: 99999);

        Assert.True(ergebnis.Ok);

        using var pruef = database.Context();
        Assert.Null(pruef.Purchases.Single().TransactionId);
    }

    /// <summary>Von Hand erfasst bleibt die Buchung leer — wie bisher.</summary>
    [Fact]
    public async Task Von_Hand_erfasst_bleibt_sie_leer()
    {
        var ergebnis = await Formulare().CreateAsync(
            CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Vogelhaus",
                ["purchase"] = "2026-06-28",
                ["price"] = "73,42",
            });

        Assert.True(ergebnis.Ok);

        using var pruef = database.Context();
        Assert.Null(pruef.Purchases.Single().TransactionId);
    }

    // ── Was der Zahlungsabgleich daraus macht ──────────────────────────────────────────

    /// <summary>
    /// <b>Die vermerkte Buchung steht über jedem Abgleich</b> — und sie braucht weder Beleg
    /// noch passenden Betrag.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Vorrang bliebe die Liste leer: Der Abgleich verlangt einen eingelesenen
    /// Belegtext, und eine aus der Buchung erfasste Anschaffung hat keinen. Die Zahlung wäre
    /// bekannt und würde trotzdem verschwiegen.
    /// </remarks>
    [Fact]
    public async Task Die_vermerkte_Buchung_erscheint_ohne_Beleg()
    {
        var buchung = Abbuchung();

        await Formulare().CreateAsync(
            CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Vogelhaus",
                ["purchase"] = "2026-06-28",
                ["price"] = "73,42",
            },
            fromTransactionId: buchung);

        int id;
        using (var context = database.Context())
        {
            id = context.Purchases.Single().Id;
        }

        var zeile = Assert.Single((await Anschaffungen().GetAsync(id))!.Payments);

        Assert.Equal(buchung, zeile.TransactionId);
        Assert.Equal("Aus dieser Buchung erfasst", zeile.MatchReason);
        Assert.Equal(73.42m, zeile.Amount);
    }

    /// <summary>
    /// <b>Und sie steht allein.</b> Eine zweite Buchung derselben Höhe erscheint nicht
    /// daneben: Eine Gewissheit mit Vermutungen zu garnieren machte sie nicht sicherer.
    /// </summary>
    [Fact]
    public async Task Neben_der_vermerkten_Buchung_steht_keine_zweite()
    {
        var buchung = Abbuchung(tag: 28);
        Abbuchung(tag: 14, zahler: "Anderer Haendler");

        await Formulare().CreateAsync(
            CreateObjectType.Purchase,
            new Dictionary<string, string?>
            {
                ["name"] = "Vogelhaus",
                ["purchase"] = "2026-06-28",
                ["price"] = "73,42",
            },
            fromTransactionId: buchung);

        int id;
        using (var context = database.Context())
        {
            id = context.Purchases.Single().Id;
        }

        var zeile = Assert.Single((await Anschaffungen().GetAsync(id))!.Payments);
        Assert.Equal(buchung, zeile.TransactionId);
    }

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
