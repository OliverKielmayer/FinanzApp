using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Einlieferung aus einem überwachten Ordner — der unbeaufsichtigte Weg.
/// </summary>
/// <remarks>
/// <para>Der erste Test ist der wichtigste: <b>ein Dienst, dem niemand zusieht, verändert keine
/// Vermögenszahl.</b> Er darf ablegen und zuordnen — beides ist sichtbar und mit einem Griff
/// änderbar. Ein erreichter Wert im Vertrag wäre das nicht; er stünde danach in einer Summe, die
/// nie jemand geprüft hat.</para>
/// <para>Der zweite Punkt ist die Vollständigkeit: was der Dienst nicht zuordnen konnte, muss im
/// Scaneingang stehen und sich dort nachtragen lassen. Ein Beleg, der still in „Sonstiges“
/// verschwindet, ist verloren, auch wenn die Datei noch da ist.</para>
/// </remarks>
public sealed class ScanIntakeTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 29);
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int vertragId;

    public ScanIntakeTests()
    {
        using var context = database.Context();

        var vertrag = new Policy
        {
            Name = "Muster bestpartner classic",
            Provider = "Nordstern Leben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            PolicyNumber = "77001122-01",
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();

        vertragId = vertrag.Id;
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Leser, der eine vorgegebene Textebene liefert.</summary>
    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    /// <summary>
    /// Die Einlieferung samt ihrer Mitspieler, alle auf <em>einem</em> Kontext.
    /// </summary>
    /// <remarks>
    /// Ein Kontext je Dienst wäre hier falsch: die Einlieferung setzt den Typ an demselben
    /// Dokument, das die Analyse gerade angelegt hat. Im Betrieb teilen sie den Kontext der
    /// Anfrage, und genau das soll geprüft werden.
    /// </remarks>
    private ScanIntakeService Service(PdfContent inhalt, FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);
        var documents = new DocumentService(
            context, paths, new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

        var scans = new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new StubReader(inhalt), clock);

        return new ScanIntakeService(
            context, scans, documents, new ScanInboxService(context, documents, clock));
    }

    private async Task<ScanIntakeResultDto> TakeInAsync(
        PdfContent inhalt, string dateiname = "scan_0001.pdf", string? herkunft = null)
    {
        using var context = database.Context();

        return await Service(inhalt, context).TakeInAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4")), dateiname, herkunft);
    }

    private static PdfLine Line(int seite, params string[] zellen) => new(seite, zellen);

    private static PdfContent Content(params PdfLine[] zeilen) => new()
    {
        PageCount = zeilen.Length == 0 ? 0 : zeilen.Max(z => z.Page),
        Lines = zeilen,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>Ein Statusreport im Aufbau des Originals, mit erfundenen Zahlen.</summary>
    private static PdfContent Statusreport() => Content(
        Line(1, "Nordstern Lebensversicherung AG"),
        Line(1, "Hamburg, 12.08.2025"),
        Line(1, "Versicherungsnummer:", "77001122-01"),
        Line(1, "hiermit übersenden wir Ihnen den jährlichen Statusreport"),
        Line(1, "Ihr Vertragsstand zum 31.07.2025"),
        Line(2, "Wert der Versicherung"),
        Line(2, "Rückkaufswert", "12.345,67 EUR"),
        Line(2, "erreichter Wert der Überschussbeteiligung (Ansammlungsguthaben)*", "1.234,56 EUR"),
        Line(2, "Gesamtleistung*", "13.580,23 EUR"));

    /// <summary>Legt den Dokumenttyp an, der zur erkannten Art passt.</summary>
    private int TypeForStatusreport()
    {
        using var context = database.Context();

        var type = new DocumentType
        {
            Name = DocumentKindLibrary.Statusreport.Label,
            Area = DocumentArea.Insurance,
            SortOrder = 1,
        };

        context.DocumentTypes.Add(type);
        context.SaveChanges();
        return type.Id;
    }

    private Policy Vertrag()
    {
        using var context = database.Context();
        return context.Policies.Single(p => p.Id == vertragId);
    }

    private async Task<ScanInboxDto> EingangAsync()
    {
        using var context = database.Context();
        return await Inbox(context).GetAsync();
    }

    // ── Nichts Unbestätigtes verändert eine Vermögenszahl ──────────────────────────────────

    /// <summary>
    /// Die Einlieferung ordnet ein und übernimmt nichts.
    /// </summary>
    /// <remarks>
    /// Die Regel, an der der ganze Weg hängt. Ein Dienst kann keinen Menschen ersetzen, der die
    /// gelesenen Werte neben ihrer Herkunftsseite gesehen hat — deshalb endet er im Scaneingang
    /// und nicht in der Übernahme.
    /// </remarks>
    [Fact]
    public async Task Die_Einlieferung_veraendert_den_Vertrag_nicht()
    {
        TypeForStatusreport();

        var ergebnis = await TakeInAsync(Statusreport());

        Assert.Equal("statusreport-lv", ergebnis.KindKey);
        Assert.Null(Vertrag().CurrentValue);
        Assert.Null(Vertrag().ValuationDate);

        using var context = database.Context();
        Assert.All(
            context.DocumentExtractions.Where(x => x.DocumentId == ergebnis.DocumentId),
            x => Assert.False(x.Confirmed));
    }

    /// <summary>
    /// Ein erkannter Beleg kommt vollständig zugeordnet an — Bereich, Typ und Objekt stehen.
    /// </summary>
    [Fact]
    public async Task Ein_erkannter_Beleg_wird_zugeordnet()
    {
        var typId = TypeForStatusreport();

        var ergebnis = await TakeInAsync(Statusreport());

        Assert.Equal(ScanIntakeOutcome.Assigned, ergebnis.Outcome);
        Assert.Null(ergebnis.Missing);
        Assert.Equal(DocumentArea.Insurance, ergebnis.Area);
        Assert.Equal(DocumentKindLibrary.Statusreport.Label, ergebnis.TypeName);
        Assert.Equal("Nordstern Leben", ergebnis.TargetName);
        Assert.Equal("Vertrag", ergebnis.TargetNoun);

        using var context = database.Context();
        var dokument = context.Documents.Single(d => d.Id == ergebnis.DocumentId);
        Assert.Equal(typId, dokument.DocumentTypeId);
        Assert.Equal(DocumentArea.Insurance, dokument.Area);
        Assert.StartsWith("Versicherungen/", dokument.RelativePath, StringComparison.Ordinal);

        var link = context.DocumentLinks.Single(l => l.DocumentId == ergebnis.DocumentId);
        Assert.Equal(LinkTargetType.Policy, link.TargetType);
        Assert.Equal(vertragId, link.TargetId);
    }

    /// <summary>
    /// Auch der zugeordnete Beleg steht im Eingang.
    /// </summary>
    /// <remarks>
    /// Sonst käme ein Beleg an, ohne dass irgendwo stünde, dass er angekommen ist — und ein
    /// Ordnerdienst, dessen Ergebnis man nicht sieht, ist einer, dem man nicht trauen kann.
    /// </remarks>
    [Fact]
    public async Task Jede_Einlieferung_steht_im_Scaneingang()
    {
        TypeForStatusreport();

        var ergebnis = await TakeInAsync(Statusreport());
        var eingang = await EingangAsync();

        var eintrag = Assert.Single(eingang.Items);
        Assert.Equal(1, eingang.WaitingCount);
        Assert.Equal(ergebnis.DocumentId, eintrag.DocumentId);
        Assert.True(eintrag.Recognised);
        Assert.Equal("Nordstern Leben", eintrag.Sender);
    }

    // ── Was nicht geht, wartet sichtbar ────────────────────────────────────────────────────

    /// <summary>
    /// Ohne gleichnamigen Dokumenttyp bleibt der Typ leer — angelegt wird keiner.
    /// </summary>
    /// <remarks>
    /// Welche Typen es gibt, entscheidet der Haushalt und nicht der Quelltext. Das Objekt findet
    /// die Einlieferung trotzdem: die halbe Zuordnung ist mehr als keine, und was fehlt, steht
    /// als Satz dabei.
    /// </remarks>
    [Fact]
    public async Task Ohne_passenden_Dokumenttyp_wartet_der_Beleg()
    {
        var ergebnis = await TakeInAsync(Statusreport());

        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.Null(ergebnis.TypeName);
        Assert.NotNull(ergebnis.Missing);
        Assert.Contains("Statusreport Lebensversicherung", ergebnis.Missing);
        Assert.Equal("Nordstern Leben", ergebnis.TargetName);

        using var context = database.Context();
        Assert.Empty(context.DocumentTypes);
        Assert.Single(context.DocumentLinks.Where(l => l.DocumentId == ergebnis.DocumentId));
    }

    /// <summary>
    /// Ein stillgelegter Typ wird nicht vergeben.
    /// </summary>
    /// <remarks>
    /// Er steht in keiner Auswahlliste mehr; ein Dienst soll nicht vergeben, was ein Mensch
    /// nicht mehr vergeben kann.
    /// </remarks>
    [Fact]
    public async Task Ein_stillgelegter_Dokumenttyp_wird_nicht_vergeben()
    {
        using (var context = database.Context())
        {
            context.DocumentTypes.Add(new DocumentType
            {
                Name = DocumentKindLibrary.Statusreport.Label,
                Area = DocumentArea.Insurance,
                SortOrder = 1,
                IsRetired = true,
            });

            context.SaveChanges();
        }

        var ergebnis = await TakeInAsync(Statusreport());

        Assert.Null(ergebnis.TypeName);
        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
    }

    /// <summary>
    /// Ein Beleg zu einem Vertrag, den es nicht gibt, geht nicht verloren.
    /// </summary>
    /// <remarks>
    /// Die Datei ist abgelegt, der Eintrag steht im Eingang, und die Meldung nennt das fehlende
    /// Objekt. Ein Abbruch hätte hier den Scan verworfen — das Schlimmste, was passieren kann.
    /// </remarks>
    [Fact]
    public async Task Ohne_Zielobjekt_wartet_der_Beleg_mit_Grund()
    {
        using (var context = database.Context())
        {
            context.Policies.Remove(context.Policies.Single(p => p.Id == vertragId));
            context.SaveChanges();
        }

        TypeForStatusreport();

        var ergebnis = await TakeInAsync(Statusreport());

        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.Null(ergebnis.TargetName);
        Assert.NotNull(ergebnis.Missing);
        Assert.Contains("Vertrag", ergebnis.Missing);

        using var pruefung = database.Context();
        Assert.Empty(pruefung.DocumentLinks);
        Assert.Single(pruefung.ScanInbox);
    }

    /// <summary>
    /// Eine Datei, deren Art nicht erkennbar ist, landet in „Sonstiges“ und wartet auf prüfen.
    /// </summary>
    [Fact]
    public async Task Ein_unbekanntes_Dokument_landet_in_Sonstiges()
    {
        var ergebnis = await TakeInAsync(Content(Line(1, "Ein Zettel ohne Merkmale")), "zettel.pdf");

        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.Equal(DocumentArea.Other, ergebnis.Area);
        Assert.Null(ergebnis.KindKey);
        Assert.StartsWith("Sonstiges/", ergebnis.RelativePath, StringComparison.Ordinal);

        var eintrag = Assert.Single((await EingangAsync()).Items);
        Assert.False(eintrag.Recognised);
    }

    /// <summary>
    /// Ein reiner Bildscan wird abgelegt und sagt, warum nichts zu holen war.
    /// </summary>
    [Fact]
    public async Task Ein_Scan_ohne_Textebene_nennt_seinen_Grund()
    {
        var ergebnis = await TakeInAsync(Content(), "seite.pdf");

        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.NotNull(ergebnis.Missing);
        Assert.Contains("kein Text", ergebnis.Missing);
    }

    /// <summary>
    /// Die Herkunft steht am Dokument.
    /// </summary>
    /// <remarks>
    /// Wer im Eingang eine Datei findet, die er nicht erwartet hat, soll sehen, aus welchem
    /// Ordner sie kam.
    /// </remarks>
    [Fact]
    public async Task Die_Herkunft_steht_am_Dokument()
    {
        var ergebnis = await TakeInAsync(
            Statusreport(), herkunft: @"\\scanner\eingang\scan_0001.pdf");

        using var context = database.Context();
        var dokument = context.Documents.Single(d => d.Id == ergebnis.DocumentId);

        Assert.NotNull(dokument.Description);
        Assert.Contains(@"\\scanner\eingang\scan_0001.pdf", dokument.Description);
    }

    // ── Nachträglich zuordnen ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Benutzer trägt Typ und Objekt nach; der Beleg ist damit eingeordnet.
    /// </summary>
    /// <remarks>
    /// Der andere Teil der Zusage: was der Dienst nicht konnte, muss von Hand gehen — und danach
    /// aus dem Eingang verschwinden, sonst wäre die Arbeit unsichtbar geblieben.
    /// </remarks>
    [Fact]
    public async Task Nachtragen_ordnet_zu_und_raeumt_weg()
    {
        var ergebnis = await TakeInAsync(Content(Line(1, "Ein Zettel ohne Merkmale")), "zettel.pdf");

        using var context = database.Context();
        var typ = new DocumentType { Name = "Versicherungsschein", Area = DocumentArea.Insurance };
        context.DocumentTypes.Add(typ);
        await context.SaveChangesAsync();

        var eingang = Inbox(context);

        var gelungen = await eingang.AssignAsync(
            ergebnis.InboxId,
            new AssignScanInboxRequest
            {
                DocumentTypeId = typ.Id,
                TargetType = LinkTargetType.Policy,
                TargetId = vertragId,
            });

        Assert.True(gelungen);

        var dokument = context.Documents.Single(d => d.Id == ergebnis.DocumentId);
        Assert.Equal(typ.Id, dokument.DocumentTypeId);

        // Der Bereich folgt dem gewählten Typ: die Datei liegt weiter unter „Sonstiges“, gesucht
        // und gefiltert wird aber nach dem Bereich.
        Assert.Equal(DocumentArea.Insurance, dokument.Area);
        Assert.StartsWith("Sonstiges/", dokument.RelativePath, StringComparison.Ordinal);

        Assert.Single(context.DocumentLinks.Where(l => l.DocumentId == ergebnis.DocumentId));
        Assert.Equal(0, (await eingang.GetAsync()).WaitingCount);
    }

    /// <summary>
    /// Ein Ziel, das es nicht gibt, ändert nichts.
    /// </summary>
    /// <remarks>
    /// Erst verknüpfen, dann den Typ setzen. Andersherum stünde nach einem falschen Ziel ein
    /// geänderter Typ da — halb zugeordnet und weiter im Eingang.
    /// </remarks>
    [Fact]
    public async Task Nachtragen_mit_unbekanntem_Ziel_laesst_den_Beleg_unberuehrt()
    {
        var ergebnis = await TakeInAsync(Content(Line(1, "Ein Zettel ohne Merkmale")), "zettel.pdf");

        using var context = database.Context();
        var typ = new DocumentType { Name = "Versicherungsschein", Area = DocumentArea.Insurance };
        context.DocumentTypes.Add(typ);
        await context.SaveChangesAsync();

        var eingang = Inbox(context);

        await Assert.ThrowsAsync<ArgumentException>(() => eingang.AssignAsync(
            ergebnis.InboxId,
            new AssignScanInboxRequest
            {
                DocumentTypeId = typ.Id,
                TargetType = LinkTargetType.Policy,
                TargetId = vertragId + 999,
            }));

        var dokument = context.Documents.Single(d => d.Id == ergebnis.DocumentId);
        Assert.Null(dokument.DocumentTypeId);
        Assert.Equal(DocumentArea.Other, dokument.Area);
        Assert.Equal(1, (await eingang.GetAsync()).WaitingCount);
    }

    /// <summary>
    /// Wegräumen geht erst, wenn Typ <em>und</em> Objekt stehen — auch über diesen Weg.
    /// </summary>
    [Fact]
    public async Task Ein_unzugeordneter_Beleg_laesst_sich_nicht_wegraeumen()
    {
        var ergebnis = await TakeInAsync(Content(Line(1, "Ein Zettel ohne Merkmale")), "zettel.pdf");

        using var context = database.Context();
        var eingang = Inbox(context);

        Assert.False(await eingang.FileAsync(ergebnis.InboxId));
        Assert.Equal(1, (await eingang.GetAsync()).WaitingCount);
    }

    private ScanInboxService Inbox(FinanzAppDbContext context)
        => new(
            context,
            new DocumentService(
                context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
                NullLogger<DocumentService>.Instance),
            clock);

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
