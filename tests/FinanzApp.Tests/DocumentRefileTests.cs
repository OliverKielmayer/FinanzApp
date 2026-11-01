using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Beleg zieht um, wenn sein Objekt feststeht.
/// </summary>
/// <remarks>
/// <para>Die Einlieferung aus dem überwachten Ordner legt einen Beleg unter <c>Unbekannt</c> ab,
/// solange sie das Objekt nicht bestimmen kann. Wird es später im Scaneingang nachgetragen,
/// stimmt der Ordner nicht mehr: er sagt „unbekannt“ über einen Beleg, der längst an seinem
/// Vertrag hängt — und wer im Dateimanager beim Vertrag sucht, findet ihn dort nicht.</para>
/// <para>Die Grenze ist genauso wichtig wie das Umhängen: <b>was ein Mensch eingerichtet hat,
/// bleibt liegen.</b> Angefasst wird nur der Ordner, den die Einlieferung selbst vergeben hat.</para>
/// </remarks>
public sealed class DocumentRefileTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentPathService Paths() => TestDatabase.PathService(root);

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, Paths(), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    /// <summary>Legt eine Datei an den Pfad und einen Eintrag dazu.</summary>
    private int Ablegen(string relativePath)
    {
        var absolut = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolut)!);
        File.WriteAllText(absolut, "Beleg");

        using var context = database.Context();
        var dokument = new Document
        {
            Title = "Statusreport",
            RelativePath = relativePath,
            FileName = Path.GetFileName(relativePath),
            Extension = Path.GetExtension(relativePath),
            Area = DocumentArea.Insurance,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private bool Exists(string relativePath)
        => File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private string PathOf(int id)
    {
        using var context = database.Context();
        return context.Documents.Single(d => d.Id == id).RelativePath;
    }

    // ── Umhängen ───────────────────────────────────────────────────────────────────────────

    /// <summary>Der Beleg wandert aus „Unbekannt“ in den Ordner seines Vertrags.</summary>
    [Fact]
    public async Task Der_Beleg_zieht_in_den_Ordner_seines_Objekts()
    {
        var alt = "Versicherungen/Lebensversicherung/Unbekannt/2014/Statusreport_2014-07-31.pdf";
        var id = Ablegen(alt);

        using var context = database.Context();
        var neu = await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben");

        Assert.Equal(
            "Versicherungen/Lebensversicherung/Musterleben/2014/Statusreport_2014-07-31.pdf",
            neu);

        Assert.Equal(neu, PathOf(id));
        Assert.True(Exists(neu!));
        Assert.False(Exists(alt));
    }

    /// <summary>
    /// Die leergeräumten Ordner bleiben nicht stehen — auch der darüber nicht.
    /// </summary>
    /// <remarks>
    /// Ein <c>Unbekannt</c> ohne Inhalt sagt im Dateimanager weiter „unbekannt“ über einen Beleg,
    /// der längst zugeordnet ist. Beim ersten Bau blieb genau dieser Ordner stehen: aufgeräumt
    /// wurde nur das Jahr darunter.
    /// </remarks>
    [Fact]
    public async Task Die_leeren_Ordner_verschwinden_bis_nach_oben()
    {
        var id = Ablegen("Versicherungen/Lebensversicherung/Unbekannt/2014/beleg.pdf");

        using var context = database.Context();
        await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben");

        Assert.False(Directory.Exists(
            Path.Combine(root, "Versicherungen", "Lebensversicherung", "Unbekannt", "2014")));

        Assert.False(Directory.Exists(
            Path.Combine(root, "Versicherungen", "Lebensversicherung", "Unbekannt")));

        // Und nur die leeren: der Ordner der Dokumentart trägt jetzt den Vertrag.
        Assert.True(Directory.Exists(
            Path.Combine(root, "Versicherungen", "Lebensversicherung")));
    }

    /// <summary>Ein Ordner mit Inhalt beendet den Aufstieg.</summary>
    /// <remarks>
    /// Zwei Belege lagen unter <c>Unbekannt</c>, einer zieht um: der Ordner bleibt, weil der
    /// zweite noch darin liegt.
    /// </remarks>
    [Fact]
    public async Task Ein_Ordner_mit_Inhalt_bleibt_stehen()
    {
        Ablegen("Versicherungen/Lebensversicherung/Unbekannt/2015/anderer.pdf");
        var id = Ablegen("Versicherungen/Lebensversicherung/Unbekannt/2014/beleg.pdf");

        using var context = database.Context();
        await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben");

        Assert.True(Directory.Exists(
            Path.Combine(root, "Versicherungen", "Lebensversicherung", "Unbekannt")));

        Assert.True(Exists("Versicherungen/Lebensversicherung/Unbekannt/2015/anderer.pdf"));
    }

    /// <summary>Ein belegter Name im Zielordner wird nicht überschrieben.</summary>
    /// <remarks>Zwei Berichte desselben Stichtags sind zwei Berichte.</remarks>
    [Fact]
    public async Task Ein_belegter_Name_im_Ziel_wird_nicht_ueberschrieben()
    {
        var besetzt = "Versicherungen/Lebensversicherung/Musterleben/2014/beleg.pdf";
        Ablegen(besetzt);

        var id = Ablegen("Versicherungen/Lebensversicherung/Unbekannt/2014/beleg.pdf");

        using var context = database.Context();
        var neu = await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben");

        Assert.Equal(
            "Versicherungen/Lebensversicherung/Musterleben/2014/beleg_1.pdf", neu);

        Assert.True(Exists(besetzt));
        Assert.True(Exists(neu!));
    }

    // ── Und die Grenzen ────────────────────────────────────────────────────────────────────

    /// <summary>Ein Pfad ohne „Unbekannt“ wird nicht angefasst.</summary>
    /// <remarks>
    /// Dort hat ein Mensch abgelegt oder einen Pfad korrigiert. Eine Datei unter ihm
    /// wegzuschieben wäre eine Entscheidung, die dem Dienst nicht zusteht.
    /// </remarks>
    [Fact]
    public async Task Eine_eingerichtete_Ablage_bleibt_liegen()
    {
        var pfad = "Versicherungen/Meine Ordnung/2014/beleg.pdf";
        var id = Ablegen(pfad);

        using var context = database.Context();

        Assert.Null(await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben"));
        Assert.Equal(pfad, PathOf(id));
        Assert.True(Exists(pfad));
    }

    /// <summary>Kommt das Wort zweimal vor, ist nicht klar, welcher Ordner gemeint ist.</summary>
    [Fact]
    public async Task Zweimal_Unbekannt_bleibt_unangetastet()
    {
        var pfad = "Versicherungen/Unbekannt/Unbekannt/beleg.pdf";
        var id = Ablegen(pfad);

        using var context = database.Context();

        Assert.Null(await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben"));
        Assert.Equal(pfad, PathOf(id));
    }

    /// <summary>Fehlt die Datei, bleibt der Eintrag auf seinem Pfad stehen.</summary>
    /// <remarks>
    /// Ein Eintrag, der auf ein Nichts zeigt, wäre schlimmer als einer, der auf den alten Ordner
    /// zeigt — der Schirm „Datei nicht gefunden“ nennt wenigstens den Pfad, unter dem sie fehlt.
    /// </remarks>
    [Fact]
    public async Task Ohne_Datei_bleibt_der_Eintrag_stehen()
    {
        var pfad = "Versicherungen/Lebensversicherung/Unbekannt/2014/beleg.pdf";
        var id = Ablegen(pfad);
        File.Delete(Path.Combine(root, pfad.Replace('/', Path.DirectorySeparatorChar)));

        using var context = database.Context();

        Assert.Null(await Documents(context).RefileAsync(id, DocumentArea.Insurance, "Musterleben"));
        Assert.Equal(pfad, PathOf(id));
    }

    /// <summary>
    /// Ein Name, von dem nichts Brauchbares übrig bleibt, erfindet keinen Ordner.
    /// </summary>
    /// <remarks>
    /// Die Entschärfung macht aus einem leeren Namen „Dokument“ — der Beleg läge danach in
    /// einem Ordner, den niemand so genannt hat. Fehlt der Name, bleibt <c>Unbekannt</c>
    /// stehen; am selben Bereich ist damit nichts zu tun.
    /// </remarks>
    [Fact]
    public async Task Ein_leerer_Name_erfindet_keinen_Ordner()
    {
        var pfad = "Versicherungen/Lebensversicherung/Unbekannt/2014/beleg.pdf";
        var id = Ablegen(pfad);

        using var context = database.Context();

        Assert.Null(await Documents(context).RefileAsync(id, DocumentArea.Insurance, "   "));
        Assert.Equal(pfad, PathOf(id));
    }

    // ── Und der Bereich ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Bereichsordner wandert mit dem Bereich.
    /// </summary>
    /// <remarks>
    /// Am Prüfplatz wird eine Art gewählt, und die bringt ihren Bereich mit. Die Erkennung
    /// hatte vielleicht einen anderen geraten. Bliebe die Datei im alten Bereichsordner,
    /// widersprächen sich Eintrag und Ablage: Die Liste zeigte den Beleg unter Wohnen, der
    /// Dateimanager fände ihn unter Versicherungen.
    /// </remarks>
    [Fact]
    public async Task Der_Bereichsordner_wandert_mit()
    {
        var id = Ablegen("Versicherungen/Unbekannt/beleg.pdf");

        using var context = database.Context();
        var neu = await Documents(context).RefileAsync(id, DocumentArea.Housing, "Haus Musterweg");

        Assert.Equal("Wohnen/Haus_Musterweg/beleg.pdf", neu);
        Assert.True(Exists(neu!));
        Assert.False(Exists("Versicherungen/Unbekannt/beleg.pdf"));
    }

    /// <summary>
    /// Ohne Objekt bleibt <c>Unbekannt</c> stehen — nur der Bereich zieht um.
    /// </summary>
    /// <remarks>
    /// Der Fall eines Belegs, der abgelegt wird, bevor sein Objekt existiert. Der Ordner soll
    /// dann sagen, was ist: Bereich bekannt, Objekt offen. Ein ausgedachter Objektordner wäre
    /// die schlechtere Auskunft, weil er nach einer Zuordnung aussähe.
    /// </remarks>
    [Fact]
    public async Task Ohne_Objekt_bleibt_Unbekannt_stehen()
    {
        var id = Ablegen("Versicherungen/Unbekannt/beleg.pdf");

        using var context = database.Context();
        var neu = await Documents(context).RefileAsync(id, DocumentArea.Housing, null);

        Assert.Equal("Wohnen/Unbekannt/beleg.pdf", neu);
        Assert.True(Exists(neu!));
    }

    // ── Und beim Ändern der Angaben ────────────────────────────────────────────────────────

    /// <summary>
    /// Wer die Art ändert, verschiebt den Beleg — die Datei geht mit.
    /// </summary>
    /// <remarks>
    /// Am Dokument lassen sich Art und damit Bereich nachträglich korrigieren. Bliebe die
    /// Datei liegen, sagte der Eintrag „Wohnen“ und der Ordner weiter „Versicherungen“. Der
    /// Widerspruch fällt erst im Dateimanager auf, und dort traut man eher dem Ordner.
    /// </remarks>
    [Fact]
    public async Task Ein_geaenderter_Bereich_nimmt_die_Datei_mit()
    {
        var id = Ablegen("Versicherungen/Unbekannt/beleg.pdf");

        using var context = database.Context();

        await Documents(context).UpdateAsync(id, new UpdateDocumentRequest
        {
            Title = "Notarschreiben",
            Area = DocumentArea.Housing,
        });

        Assert.Equal("Wohnen/Unbekannt/beleg.pdf", PathOf(id));
        Assert.True(Exists("Wohnen/Unbekannt/beleg.pdf"));
    }

    /// <summary>
    /// Ein zugeordneter Beleg nimmt sein Objekt in den neuen Bereich mit.
    /// </summary>
    /// <remarks>
    /// <para>Hier stand einmal das Gegenteil: Ein Beleg, der schon bei seinem Objekt liegt,
    /// bleibe liegen, eine geänderte Art sei kein Grund, eine eingerichtete Ablage umzubauen.
    /// Das klang nach Behutsamkeit und war der Widerspruch selbst — der Eintrag sagte Wohnen,
    /// der Ordner Versicherungen, und im Dateimanager traut man dem Ordner.</para>
    /// <para>Eingerichtet hat diese Ablage niemand von Hand; sie stammt von der Einlieferung,
    /// und was sie angelegt hat, hält sie auch in Ordnung. Der Objektordner überlebt den Umzug,
    /// nur der Bereich davor wechselt. Eine fremde Ablage ist etwas anderes, und sie bleibt
    /// unberührt — <see cref="Unbekannt_ganz_vorn_wird_nicht_angefasst"/>.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_zugeordneter_Beleg_nimmt_sein_Objekt_mit()
    {
        var id = Ablegen("Versicherungen/Nordmuster_Leben/beleg.pdf");

        using var context = database.Context();

        await Documents(context).UpdateAsync(id, new UpdateDocumentRequest
        {
            Title = "Notarschreiben",
            Area = DocumentArea.Housing,
        });

        Assert.Equal("Wohnen/Nordmuster_Leben/beleg.pdf", PathOf(id));
        Assert.True(Exists("Wohnen/Nordmuster_Leben/beleg.pdf"));
        Assert.False(Exists("Versicherungen/Nordmuster_Leben/beleg.pdf"));
    }

    /// <summary>Steht nichts vor <c>Unbekannt</c>, stammt der Pfad nicht aus dieser Ablage.</summary>
    /// <remarks>
    /// Das erste Segment ist immer der Bereichsordner. Liegt <c>Unbekannt</c> dort, hat ihn
    /// nicht die Einlieferung vergeben — und dann wird der Pfad auch nicht umgebaut.
    /// </remarks>
    [Fact]
    public async Task Unbekannt_ganz_vorn_wird_nicht_angefasst()
    {
        var pfad = "Unbekannt/beleg.pdf";
        var id = Ablegen(pfad);

        using var context = database.Context();

        Assert.Null(await Documents(context).RefileAsync(id, DocumentArea.Housing, "Haus Musterweg"));
        Assert.Equal(pfad, PathOf(id));
    }

    // ── Über den Scaneingang ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Das Zuordnen im Scaneingang räumt die Datei mit.
    /// </summary>
    /// <remarks>
    /// Der Weg, um den es geht: der Ordnerdienst konnte den Vertrag nicht bestimmen, ein Mensch
    /// trägt ihn nach — und danach liegt der Beleg dort, wo er hingehört.
    /// </remarks>
    [Fact]
    public async Task Das_Zuordnen_raeumt_die_Datei_mit()
    {
        int vertragId;
        int typId;

        using (var vorbereitung = database.Context())
        {
            var vertrag = new Policy
            {
                Name = "Musterleben",
                Provider = "Musterleben",
                Kind = PolicyKind.CapitalLife,
                IsCapitalForming = true,
            };

            var typ = new DocumentType { Name = "Statusreport", Area = DocumentArea.Insurance };

            vorbereitung.Policies.Add(vertrag);
            vorbereitung.DocumentTypes.Add(typ);
            vorbereitung.SaveChanges();

            vertragId = vertrag.Id;
            typId = typ.Id;
        }

        var id = Ablegen("Versicherungen/Lebensversicherung/Unbekannt/2014/Statusreport_2014-07-31.pdf");

        int eingangId;

        using (var context = database.Context())
        {
            eingangId = await new ScanInboxService(context, Documents(context), clock)
                .AddAsync(id, sender: null, pageCount: 2, recognised: false);
        }

        using (var context = database.Context())
        {
            var eingang = new ScanInboxService(context, Documents(context), clock);

            Assert.True(await eingang.AssignAsync(eingangId, new AssignScanInboxRequest
            {
                DocumentTypeId = typId,
                TargetType = LinkTargetType.Policy,
                TargetId = vertragId,
            }));
        }

        var neu = PathOf(id);

        Assert.Equal(
            "Versicherungen/Lebensversicherung/Musterleben/2014/Statusreport_2014-07-31.pdf",
            neu);

        Assert.True(Exists(neu));
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
