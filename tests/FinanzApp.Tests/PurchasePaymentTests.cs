using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Welche Buchung eine Anschaffung bezahlt hat.
/// </summary>
/// <remarks>
/// <para>Dieselbe Regel wie am Vorgang, und bewusst derselbe Baustein
/// (<see cref="PaymentMatching"/>): Betrag auf den Cent, Buchung im Zeitfenster, und eine
/// Nummer aus dem Belegtext im Verwendungszweck. Bei einer Amazon-Bestellung ist das die
/// Bestellnummer — sie steht auf der Rechnung, und die Bank schreibt sie in den
/// Verwendungszweck.</para>
/// <para><b>Der Beleg ist die Bedingung.</b> Ohne eingelesenen Text gibt es nichts, worin
/// eine Nummer stehen könnte — dann bleibt die Liste leer, statt einen Betragstreffer als
/// Zuordnung auszugeben. In einer echten Ablage hängt an genau dieser Grenze der Unterschied
/// zwischen einer Zuordnung und einer Bargeldabhebung, die zufällig dieselbe Summe trägt.</para>
/// <para>Die Zahlen und Nummern hier sind erfunden.</para>
/// </remarks>
public sealed class PurchasePaymentTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 12);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int konto;

    public PurchasePaymentTests()
    {
        using var context = database.Context();

        var account = new Account
        {
            Name = "Musterbank Giro",
            ShortName = "Musterbank",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 12),
        };

        context.Accounts.Add(account);
        context.SaveChanges();
        konto = account.Id;
    }

    private PurchaseService Service()
    {
        var context = database.Context();

        return new PurchaseService(
            context,
            new DocumentService(
                context, TestDatabase.PathService(root), new ObjectLabelService(context),
                clock, new PdfPigTextReader(), NullLogger<DocumentService>.Instance),
            clock);
    }

    /// <summary>Eine Anschaffung, wahlweise mit Beleg und gelesenem Text.</summary>
    private int Anschaffung(decimal? preis, string? belegtext, DateOnly? gekauft = null)
    {
        using var context = database.Context();

        var kauf = new Purchase
        {
            Name = "Musterware",
            PurchaseDate = gekauft ?? new DateOnly(2026, 11, 6),
            Price = preis,
            Vendor = "Musterversand",
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Purchases.Add(kauf);
        context.SaveChanges();

        if (belegtext is not null)
        {
            var beleg = new Document
            {
                Title = "Rechnung Musterversand",
                RelativePath = "Sonstiges/rechnung.pdf",
                FileName = "rechnung.pdf",
                Extension = ".pdf",
                Area = DocumentArea.Other,
                DocumentDate = kauf.PurchaseDate,
                ContentText = belegtext,
                CreatedAt = clock.Now,
                UpdatedAt = clock.Now,
            };

            context.Documents.Add(beleg);
            context.SaveChanges();

            context.DocumentLinks.Add(new DocumentLink
            {
                DocumentId = beleg.Id,
                TargetType = LinkTargetType.Purchase,
                TargetId = kauf.Id,
                CreatedAt = clock.Now,
            });

            context.SaveChanges();
        }

        return kauf.Id;
    }

    private void Buchung(decimal betrag, string empfaenger, string? zweck, DateOnly tag)
    {
        using var context = database.Context();

        context.Transactions.Add(new Transaction
        {
            AccountId = konto,
            BookingDate = tag,
            Payee = empfaenger,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            Purpose = zweck,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    private async Task<IReadOnlyList<LinkedPaymentDto>> ZahlungenAsync(int id)
        => (await Service().GetAsync(id))!.Payments;

    // ── Der Fall, um den es geht ───────────────────────────────────────────────────────

    /// <summary>
    /// Die Bestellnummer steht auf der Rechnung und im Verwendungszweck. Das ist der Faden.
    /// </summary>
    [Fact]
    public async Task Die_Bestellnummer_bindet_die_Buchung()
    {
        var kauf = Anschaffung(
            preis: 73.42m,
            belegtext: "Rechnung zur Bestellung 307-4412908-5530174\nMusterversand EU S.a r.l.");

        Buchung(73.42m, "MUSTERVERSAND PAYMENTS", "307-4412908-5530174 MV Mktp DE 6QHX0KVDE0X6",
            new DateOnly(2026, 11, 6));

        var zeile = Assert.Single(await ZahlungenAsync(kauf));

        Assert.Equal(73.42m, zeile.Amount);
        Assert.Contains("307-4412908-5530174", zeile.MatchReason);
        Assert.Contains("steht im Beleg und im Verwendungszweck", zeile.MatchReason);
    }

    // ── Wo es aufhört ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Die wichtigste Grenze.</b> Ohne Beleg gibt es keinen Text, in dem eine Nummer stehen
    /// könnte — und ein Betrag allein ist keine Zuordnung. In einer echten Ablage steht genau
    /// hier eine Bargeldabhebung, die zufällig dieselbe Summe trägt wie eine Anschaffung;
    /// ohne diese Grenze erschiene sie als deren Bezahlung.
    /// </summary>
    [Fact]
    public async Task Ohne_Beleg_wird_nichts_zugeordnet()
    {
        var kauf = Anschaffung(preis: 438m, belegtext: null);

        Buchung(438m, "GELDAUTOMAT", "Bargeldabhebung", new DateOnly(2026, 11, 4));

        Assert.Empty(await ZahlungenAsync(kauf));
    }

    [Fact]
    public async Task Ohne_Preis_wird_nichts_zugeordnet()
    {
        var kauf = Anschaffung(preis: null, belegtext: "Lieferschein 307-4412908-5530174");

        Buchung(73.42m, "MUSTERVERSAND", "307-4412908-5530174", new DateOnly(2026, 11, 6));

        Assert.Empty(await ZahlungenAsync(kauf));
    }

    /// <summary>
    /// Der Betrag muss auf den Cent stimmen. Ein Preis, der nicht dem abgebuchten Betrag
    /// entspricht, gehört nicht zu dieser Buchung — und zu raten, welcher Unterschied
    /// zulässig wäre, hiesse eine Auskunft zu erfinden.
    /// </summary>
    [Fact]
    public async Task Ein_anderer_Betrag_bindet_nicht()
    {
        var kauf = Anschaffung(preis: 73.42m, belegtext: "Rechnung 307-4412908-5530174");

        Buchung(87.10m, "MUSTERVERSAND", "307-4412908-5530174", new DateOnly(2026, 11, 6));

        Assert.Empty(await ZahlungenAsync(kauf));
    }

    /// <summary>
    /// Passt nur der Betrag, erscheint die Buchung trotzdem — aber als das, was sie ist. Sie
    /// zu verschweigen nähme dem Inhaber die Entscheidung ab; sie als Zuordnung auszugeben
    /// behauptete mehr, als dasteht.
    /// </summary>
    [Fact]
    public async Task Passt_nur_der_Betrag_sagt_die_Zeile_das()
    {
        var kauf = Anschaffung(preis: 73.42m, belegtext: "Rechnung ohne brauchbare Kennung");

        Buchung(73.42m, "MUSTERVERSAND", "Sammelabbuchung", new DateOnly(2026, 11, 6));

        var zeile = Assert.Single(await ZahlungenAsync(kauf));

        Assert.Contains("Nur der Betrag", zeile.MatchReason);
    }

    /// <summary>
    /// Bezahlt wird, bevor abgelegt wird: Das Fenster reicht rückwärts weiter als vorwärts.
    /// Eine Zahlung lange vor dem Kaufdatum gehört trotzdem nicht dazu.
    /// </summary>
    [Fact]
    public async Task Eine_viel_aeltere_Zahlung_bindet_nicht()
    {
        var kauf = Anschaffung(
            preis: 73.42m,
            belegtext: "Rechnung 307-4412908-5530174",
            gekauft: new DateOnly(2026, 11, 6));

        Buchung(73.42m, "MUSTERVERSAND", "307-4412908-5530174", new DateOnly(2026, 2, 3));

        Assert.Empty(await ZahlungenAsync(kauf));
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
