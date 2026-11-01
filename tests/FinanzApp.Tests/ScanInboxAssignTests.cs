using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Das Zuordnen im Scaneingang — mit Objekt und ohne.
/// </summary>
/// <remarks>
/// <para>Der Eingang verlangte früher beides: eine Art <em>und</em> ein Objekt. Das klang nach
/// Sorgfalt und war eine Sackgasse. Ein Notarschreiben zum Hauskauf kommt an, bevor das Haus
/// angelegt ist; ein Lieferschein bekommt nie ein Objekt. Beide blieben für immer liegen.</para>
/// <para>Und die Sackgasse erzeugte den Schaden, den sie verhindern sollte: Wer sie zweimal
/// erlebt hat, hängt den nächsten Beleg an irgendetwas Naheliegendes, damit er verschwindet.
/// Eine falsche Verknüpfung ist schlechter als keine, weil sie wie eine Auskunft aussieht.</para>
/// </remarks>
public sealed class ScanInboxAssignTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private ScanInboxService Inbox(FinanzAppDbContext context)
        => new(context, Documents(context), clock);

    /// <summary>Eine Art, wie sie der Haushalt pflegt.</summary>
    private static int Art(FinanzAppDbContext context, string name, DocumentArea bereich)
    {
        var typ = new DocumentType { Name = name, Area = bereich };
        context.DocumentTypes.Add(typ);
        context.SaveChanges();
        return typ.Id;
    }

    /// <summary>
    /// Ein wartender Beleg, wie ihn die Einlieferung wirklich hinterlässt.
    /// </summary>
    /// <remarks>
    /// <para><b>Flach im Bereichsordner, ohne Objektsegment.</b> Ein Beleg, für den keine
    /// Leseregel greift, geht über <c>DocumentScanService.UnknownAsync</c> herein, und das ruft
    /// <c>UploadAsync</c> ohne Unterordner auf — daraus wird <c>Sonstiges/Scannen0042.pdf</c>.
    /// Genau diese Belege landen am Prüfplatz.</para>
    /// <para>Hier stand einmal <c>Sonstiges/Unbekannt/…</c>, mit dem Kommentar, so lege der
    /// Ordnerdienst ab. Das stimmte nicht, und weil das Umlegen genau auf dieses Segment
    /// prüfte, waren die Tests grün und die Ablage falsch. Der Platzhalter kommt aus den
    /// Leseregeln und hat seinen eigenen Aufbau — <see cref="WartendMitPlatzhalter"/>.</para>
    /// </remarks>
    private int Wartend(FinanzAppDbContext context, string dateiname = "Scannen0042.pdf")
        => Wartend(context, $"Sonstiges/{dateiname}", dateiname);

    /// <summary>
    /// Ein Beleg, den die Leseregeln erkannt haben, ohne das Objekt zu finden.
    /// </summary>
    /// <remarks>
    /// Nur dieser Weg erzeugt den Platzhalter: <c>DocumentKind.FolderTemplate</c> setzt für
    /// <c>{ziel}</c> „Unbekannt“ ein, wenn kein Vertrag zur Nummer gefunden wurde.
    /// </remarks>
    private int WartendMitPlatzhalter(
        FinanzAppDbContext context, string dateiname = "Scannen0042.pdf")
        => Wartend(
            context,
            $"Versicherungen/Lebensversicherung/{DocumentKind.UnknownTarget}/2026/{dateiname}",
            dateiname);

    private int Wartend(FinanzAppDbContext context, string relativ, string dateiname)
    {
        var absolut = Path.Combine(root, relativ.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolut)!);
        File.WriteAllText(absolut, "Beleg");

        var dokument = new Document
        {
            Title = Path.GetFileNameWithoutExtension(dateiname),
            RelativePath = relativ,
            FileName = dateiname,
            Extension = Path.GetExtension(dateiname),
            Area = DocumentArea.Other,
            DocumentDate = new DateOnly(2026, 10, 20),
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        var eingang = new ScanInboxItem { DocumentId = dokument.Id, CreatedAt = clock.Now };
        context.ScanInbox.Add(eingang);
        context.SaveChanges();
        return eingang.Id;
    }

    private static Document Dokument(FinanzAppDbContext context, int eingangId)
    {
        var eingang = context.ScanInbox.Single(x => x.Id == eingangId);
        return context.Documents.Single(d => d.Id == eingang.DocumentId);
    }

    // ── Ohne Objekt ────────────────────────────────────────────────────────────────────────

    /// <summary>Eine Art genügt: Der Beleg ist abgelegt und aus dem Eingang heraus.</summary>
    [Fact]
    public async Task Ein_Beleg_ohne_Objekt_wird_trotzdem_abgelegt()
    {
        using var context = database.Context();
        var art = Art(context, "Notarschreiben", DocumentArea.Housing);
        var eingang = Wartend(context);

        var abgelegt = await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        Assert.True(abgelegt);
        Assert.NotNull(context.ScanInbox.Single(x => x.Id == eingang).FiledAt);
        Assert.Equal(art, Dokument(context, eingang).DocumentTypeId);
    }

    /// <summary>
    /// Der Bereich zieht um, <c>Unbekannt</c> bleibt stehen.
    /// </summary>
    /// <remarks>
    /// Der Ordner soll sagen, was ist: Bereich bekannt, Objekt offen. Ein ausgedachter
    /// Objektordner wäre die schlechtere Auskunft, weil er nach einer Zuordnung aussähe.
    /// </remarks>
    [Fact]
    public async Task Ohne_Objekt_bleibt_der_Ordner_bei_Unbekannt()
    {
        using var context = database.Context();
        var art = Art(context, "Notarschreiben", DocumentArea.Housing);
        var eingang = WartendMitPlatzhalter(context);

        await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        var pfad = Dokument(context, eingang).RelativePath;

        Assert.StartsWith("Wohnen/", pfad, StringComparison.Ordinal);
        Assert.Contains(DocumentKind.UnknownTarget, pfad, StringComparison.Ordinal);
    }

    // ── Wohin die Datei wandert ────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Bereich wandert auch ohne Platzhalter mit.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall, der in der echten Ablage zählt und an dem das Umlegen zuvor scheiterte:
    /// Ein unerkannter Beleg liegt flach unter <c>Sonstiges</c>, und die am Prüfplatz gewählte
    /// Art gehört in einen anderen Bereich. Stand im Eintrag „Wohnen“ und im Ordner weiter
    /// „Sonstiges“, widersprachen sich beide — und wer im Dateimanager sucht, findet nichts.</para>
    /// <para>Gefunden wurde das nicht im Code, sondern an vier echten Belegen, die nach einem
    /// Arbeitstag auseinanderliefen.</para>
    /// </remarks>
    [Fact]
    public async Task Der_Bereich_wandert_auch_ohne_Platzhalter_mit()
    {
        using var context = database.Context();
        var art = Art(context, "Gebührenbescheid", DocumentArea.Housing);
        var eingang = Wartend(context);

        await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        var dokument = Dokument(context, eingang);

        Assert.Equal(DocumentArea.Housing, dokument.Area);
        Assert.StartsWith("Wohnen/", dokument.RelativePath, StringComparison.Ordinal);
        Assert.True(
            File.Exists(Path.Combine(
                root, dokument.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
            $"Die Datei liegt nicht unter {dokument.RelativePath}.");
    }

    /// <summary>
    /// Was die Einlieferung sich unter dem Bereich ausgedacht hat, bleibt.
    /// </summary>
    /// <remarks>
    /// <c>Kontoauszuege/2026</c> ist eine Ordnung, keine Verlegenheit. Ein Umzug, der sie
    /// einebnet, wirft dreißig Auszüge in einen Ordner — der Bereich wechselt, der Rest nicht.
    /// </remarks>
    [Fact]
    public async Task Unterordner_der_Einlieferung_ueberleben_den_Umzug()
    {
        using var context = database.Context();
        var art = Art(context, "Kontoauszug", DocumentArea.Finance);
        var eingang = Wartend(context, "Sonstiges/Kontoauszuege/2026/auszug.xml", "auszug.xml");

        await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        Assert.Equal(
            "Finanzen/Kontoauszuege/2026/auszug.xml",
            Dokument(context, eingang).RelativePath);
    }

    /// <summary>
    /// Eine Ablage, die nicht von hier stammt, wird nicht angefasst.
    /// </summary>
    /// <remarks>
    /// Steht vorn kein Bereichsordner, hat ein Mensch die Datei dorthin gelegt. Sie ihm
    /// wegzunehmen wäre ein Übergriff — und die Zuordnung in der Anwendung gelingt trotzdem.
    /// </remarks>
    [Fact]
    public async Task Eine_fremde_Ablage_bleibt_unberuehrt()
    {
        using var context = database.Context();
        var art = Art(context, "Notarschreiben", DocumentArea.Housing);
        var eingang = Wartend(context, "Archiv 2019/notar.pdf", "notar.pdf");

        Assert.True(await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art }));

        Assert.Equal("Archiv 2019/notar.pdf", Dokument(context, eingang).RelativePath);
    }

    /// <summary>
    /// Auch ohne Objekt bekommt der Beleg einen Namen, der etwas sagt.
    /// </summary>
    /// <remarks>
    /// Ein Scanner nennt jede Seite „Scannen0042“. Art und Datum unterscheiden sie wenigstens
    /// voneinander — im Dateimanager ist das der ganze Unterschied zwischen Ablage und Haufen.
    /// </remarks>
    [Fact]
    public async Task Ohne_Objekt_traegt_der_Name_Art_und_Datum()
    {
        using var context = database.Context();
        var art = Art(context, "Notarschreiben", DocumentArea.Housing);
        var eingang = Wartend(context);

        await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        Assert.Equal("Notarschreiben_2026-10-20.pdf", Dokument(context, eingang).FileName);
    }

    /// <summary>Ohne Objekt entsteht auch keine Verknüpfung — die Liste zeigt ihn als offen.</summary>
    [Fact]
    public async Task Ohne_Objekt_gibt_es_keine_Verknuepfung()
    {
        using var context = database.Context();
        var art = Art(context, "Lieferschein", DocumentArea.Other);
        var eingang = Wartend(context);

        await Inbox(context).AssignAsync(
            eingang, new AssignScanInboxRequest { DocumentTypeId = art });

        var dokument = Dokument(context, eingang);

        Assert.Empty(context.DocumentLinks.Where(l => l.DocumentId == dokument.Id));

        var seite = await Documents(context).GetPageAsync(withoutLink: true);
        Assert.Contains(seite.Items, d => d.Id == dokument.Id);
        Assert.Equal(1, seite.WithoutLinkCount);
    }

    // ── Mit Objekt ─────────────────────────────────────────────────────────────────────────

    /// <summary>Mit Objekt bleibt alles, wie es war: Der Beleg zieht zu seinem Vertrag.</summary>
    [Fact]
    public async Task Mit_Objekt_zieht_der_Beleg_zum_Objekt()
    {
        using var context = database.Context();
        var art = Art(context, "Versicherungsschein", DocumentArea.Insurance);

        var vertrag = new Policy
        {
            Name = "Hausrat",
            Provider = "Musterkasse",
            Kind = PolicyKind.HouseholdContents,
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();

        var eingang = Wartend(context);

        await Inbox(context).AssignAsync(eingang, new AssignScanInboxRequest
        {
            DocumentTypeId = art,
            TargetType = LinkTargetType.Policy,
            TargetId = vertrag.Id,
        });

        var dokument = Dokument(context, eingang);

        Assert.StartsWith("Versicherungen/", dokument.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DocumentKind.UnknownTarget, dokument.RelativePath, StringComparison.Ordinal);

        Assert.NotEmpty(context.DocumentLinks.Where(l => l.DocumentId == dokument.Id));
    }

    /// <summary>
    /// Der Platzhalter bekommt den Namen des Objekts — und nur er.
    /// </summary>
    /// <remarks>
    /// Die Leseregeln haben die Schublade gebaut, bloß das Ziel gefehlt. Wird es nachgetragen,
    /// heißt der Ordner wie das Objekt; <c>Lebensversicherung</c> und das Jahr bleiben stehen.
    /// </remarks>
    [Fact]
    public async Task Der_Platzhalter_weicht_dem_Objektnamen()
    {
        using var context = database.Context();
        var art = Art(context, "Versicherungsschein", DocumentArea.Insurance);

        var vertrag = new Policy
        {
            Name = "Hausrat",
            Provider = "Musterkasse",
            Kind = PolicyKind.HouseholdContents,
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();

        var eingang = WartendMitPlatzhalter(context);

        await Inbox(context).AssignAsync(eingang, new AssignScanInboxRequest
        {
            DocumentTypeId = art,
            TargetType = LinkTargetType.Policy,
            TargetId = vertrag.Id,
        });

        Assert.StartsWith(
            "Versicherungen/Lebensversicherung/Hausrat/2026/",
            Dokument(context, eingang).RelativePath,
            StringComparison.Ordinal);
    }

    // ── Und die Grenzen ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine halbe Objektangabe ist ein Fehler und keine fehlende Angabe.
    /// </summary>
    /// <remarks>
    /// Art ohne Kennung — oder umgekehrt — meint ein Ziel, das es nicht gibt. Sie stillschweigend
    /// als „ohne Objekt“ durchzuwinken hieße, einen Programmfehler als Absicht auszugeben.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Eine_halbe_Objektangabe_ist_ein_Fehler(bool mitArt, bool mitKennung)
    {
        using var context = database.Context();
        var art = Art(context, "Lieferschein", DocumentArea.Other);
        var eingang = Wartend(context);

        var anfrage = new AssignScanInboxRequest
        {
            DocumentTypeId = art,
            TargetType = mitArt ? LinkTargetType.Policy : null,
            TargetId = mitKennung ? 1 : null,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Inbox(context).AssignAsync(eingang, anfrage));
    }

    /// <summary>Ohne Art bleibt der Beleg liegen — die Schwelle ist die Art, nicht das Objekt.</summary>
    [Fact]
    public async Task Ohne_Art_bleibt_der_Beleg_im_Eingang()
    {
        using var context = database.Context();
        var eingang = Wartend(context);

        Assert.False(await Inbox(context).FileAsync(eingang));
        Assert.Null(context.ScanInbox.Single(x => x.Id == eingang).FiledAt);
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
