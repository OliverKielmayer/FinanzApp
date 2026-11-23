using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Beleg darf an mehreren Objekten hängen.
/// </summary>
/// <remarks>
/// <para><b>Der Fall, aus dem es entstand:</b> Ein Gebührenbescheid lag in der Ablage und
/// hing an seiner Buchung — an dem Vertrag, zu dem er gehört, erschien er nicht. Die
/// naheliegende Sorge beim Nachverknüpfen war, die zweite Zuordnung könnte die erste ersetzen
/// und die Buchung damit ihren Beleg verlieren.</para>
/// <para><b>Warum das hier steht und nicht nur im Kopf:</b> Die Regel trug bisher niemand — und
/// genau auf ihr steht der neue Knopf „Dokument anhängen“ an der Vertragsseite. Wer dort klickt,
/// hat einen Beleg vor sich, der fast immer schon woanders hängt.</para>
/// <para><b>Nicht geprüft:</b> Dass die Auswahlliste im Bildschirm das ausblendet, was schon
/// hier hängt. Dafür fehlt dem Testprojekt ein Rahmen für Komponenten; es wäre ein Klick, der
/// nichts tut, und kein Datenverlust.</para>
/// </remarks>
public sealed class DocumentMultiLinkTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 23);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    /// <summary>Beleg, Vertrag und die Buchung, an der er zuerst hing.</summary>
    private (int Beleg, int Vertrag, int Buchung) Bestand(string? text = null)
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = "Girokonto",
            ShortName = "Giro",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 23),
        };

        var vertrag = new Contract { Name = "Gartenwasser", Provider = "Beispielwerke" };

        var beleg = new Document
        {
            Title = "Beispielbescheid 2026",
            RelativePath = "Wohnen/beispielbescheid.pdf",
            FileName = "beispielbescheid.pdf",
            ContentText = text,
        };

        context.Accounts.Add(konto);
        context.Contracts.Add(vertrag);
        context.Documents.Add(beleg);
        context.SaveChanges();

        var buchung = new Transaction
        {
            BookingDate = new DateOnly(2026, 10, 5),
            Payee = "Beispielwerke",
            Kind = TransactionKind.Expense,
            Amount = -120m,
            AccountId = konto.Id,
            CreatedAt = new DateTime(2026, 10, 5, 6, 0, 0, DateTimeKind.Local),
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return (beleg.Id, vertrag.Id, buchung.Id);
    }

    /// <summary>
    /// Die zweite Zuordnung legt eine dazu — die erste bleibt stehen.
    /// </summary>
    /// <remarks>
    /// Die Frage, die vor dem Klicken zu beantworten war. Verlöre die Buchung ihren Beleg,
    /// fiele es niemandem auf: Die Buchung sagt nicht, dass ihr etwas fehlt.
    /// </remarks>
    [Fact]
    public async Task Eine_zweite_Zuordnung_loest_die_erste_nicht()
    {
        var (beleg, vertrag, buchung) = Bestand();

        using (var context = database.Context())
        {
            await Documents(context).LinkAsync(beleg, LinkTargetType.Transaction, buchung);
            await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
        }

        using var pruefen = database.Context();
        var arten = pruefen.DocumentLinks
            .Where(l => l.DocumentId == beleg)
            .Select(l => l.TargetType)
            .ToList();

        Assert.Equal(2, arten.Count);
        Assert.Contains(LinkTargetType.Transaction, arten);
        Assert.Contains(LinkTargetType.Contract, arten);
    }

    /// <summary>Dieselbe Zuordnung zweimal ergibt eine.</summary>
    /// <remarks>
    /// Sonst stünde der Beleg nach einem zweiten Klick doppelt in der Liste des Vertrags, und
    /// das Lösen träfe nur eine der beiden Zeilen.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Zuordnung_zweimal_ergibt_eine()
    {
        var (beleg, vertrag, _) = Bestand();

        DocumentLinkDto? erste, zweite;

        using (var context = database.Context())
        {
            erste = await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
            zweite = await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
        }

        // Nicht null: Der Dienst gibt nur dann nichts heraus, wenn es den Beleg nicht gibt.
        Assert.NotNull(erste);
        Assert.NotNull(zweite);
        Assert.Equal(erste.Id, zweite.Id);

        using var pruefen = database.Context();

        Assert.Equal(1, pruefen.DocumentLinks.Count(l => l.DocumentId == beleg));
    }

    /// <summary>
    /// Danach steht der Beleg bei den Dokumenten des Vertrags.
    /// </summary>
    /// <remarks>
    /// Das Ende der Kette und der eigentliche Zweck: Die Vertragsseite liest genau diese
    /// Verknüpfung. Ohne diese Prüfung wäre zwar die Zeile geschrieben, aber nicht belegt,
    /// dass sie dort ankommt.
    /// </remarks>
    [Fact]
    public async Task Der_Vertrag_zeigt_den_Beleg_danach()
    {
        var (beleg, vertrag, buchung) = Bestand();

        using (var context = database.Context())
        {
            await Documents(context).LinkAsync(beleg, LinkTargetType.Transaction, buchung);
            await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
        }

        using var context2 = database.Context();
        var amVertrag = await Documents(context2)
            .GetForTargetAsync(LinkTargetType.Contract, vertrag);

        var zeile = Assert.Single(amVertrag);
        Assert.Equal("Beispielbescheid 2026", zeile.Title);
    }

    /// <summary>
    /// Die Buchung behält ihren Beleg.
    /// </summary>
    /// <remarks>
    /// Die andere Hälfte derselben Frage, von der anderen Seite gestellt — und die, die ein
    /// Fehler stillschweigend kaputtmachen würde.
    /// </remarks>
    [Fact]
    public async Task Die_Buchung_behaelt_ihren_Beleg()
    {
        var (beleg, vertrag, buchung) = Bestand();

        using (var context = database.Context())
        {
            await Documents(context).LinkAsync(beleg, LinkTargetType.Transaction, buchung);
            await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
        }

        using var context2 = database.Context();
        var anDerBuchung = await Documents(context2)
            .GetForTargetAsync(LinkTargetType.Transaction, buchung);

        Assert.Single(anDerBuchung);
    }

    /// <summary>
    /// Die Liste am Objekt sieht die Art an, nicht nur die Nummer.
    /// </summary>
    /// <remarks>
    /// <para>Buchung 1 und Vertrag 1 sind zwei verschiedene Dinge mit derselben Nummer. Wer
    /// beim Lesen nur die Nummer vergleicht, hängt dem Vertrag die Belege der Buchung an.</para>
    /// <para><b>Diese Prüfung gab es erst nach der Eichung.</b> Ein Probelauf mit genau diesem
    /// Fehler ließ alle anderen grün: Im Bestand trugen beide Ziele denselben einen Beleg, und
    /// damit war die Liste in beiden Lesarten gleich lang. Deshalb steht hier ein zweiter
    /// Beleg, der <em>nur</em> an der Buchung hängt.</para>
    /// </remarks>
    [Fact]
    public async Task Die_Liste_verwechselt_Buchung_und_Vertrag_nicht()
    {
        var (beleg, vertrag, buchung) = Bestand();

        // Ohne Nummerngleichheit träfe der Vergleich ohnehin daneben und die Prüfung wäre
        // blind. Ändert sich der Bestand, soll das hier auffallen und nicht stillschweigend
        // durchgehen.
        Assert.Equal(vertrag, buchung);

        int auszug;
        using (var context = database.Context())
        {
            var zweiter = new Document
            {
                Title = "Kontoauszug Oktober",
                RelativePath = "Bank/auszug-10.pdf",
                FileName = "auszug-10.pdf",
            };

            context.Documents.Add(zweiter);
            context.SaveChanges();
            auszug = zweiter.Id;
        }

        using (var context = database.Context())
        {
            await Documents(context).LinkAsync(beleg, LinkTargetType.Contract, vertrag);
            await Documents(context).LinkAsync(auszug, LinkTargetType.Transaction, buchung);
        }

        using var pruefen = database.Context();
        var amVertrag = await Documents(pruefen)
            .GetForTargetAsync(LinkTargetType.Contract, vertrag);

        var zeile = Assert.Single(amVertrag);
        Assert.Equal("Beispielbescheid 2026", zeile.Title);
    }

    /// <summary>
    /// Die Suche hinter der Auswahlliste findet den Beleg.
    /// </summary>
    /// <remarks>
    /// Was sie nicht findet, gibt es für den Benutzer nicht: Ein Beleg, der nicht in der Liste
    /// steht, lässt sich auch nicht anhängen. Gesucht wird über Titel, Dateiname <b>und</b> den
    /// gelesenen Text — und auf Letzteres kommt es an, denn ein Scan heißt „Scannen0002“.
    /// </remarks>
    [Theory]
    [InlineData("Beispielbescheid")]
    [InlineData("beispielbescheid.pdf")]
    [InlineData("Entnahme")]
    public async Task Die_Suche_findet_den_Beleg(string suche)
    {
        var (beleg, _, _) = Bestand("Gebühr für die Entnahme im Abrechnungsjahr");

        using var context = database.Context();
        var treffer = await Documents(context).SearchAsync(suche);

        Assert.Contains(treffer.Documents, d => d.Id == beleg);
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
