using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Dokumenttypen pflegen — v5-Handoff, Abschnitt 9.
/// </summary>
/// <remarks>
/// Ein Typ steuert Ablagepfad und Beleganalyse. Die beiden Eingriffe, die weh tun, sind darum
/// Umbenennen und Löschen — und für beide gilt: die Dokumente dürfen nicht darunter leiden.
/// </remarks>
public sealed class DocumentTypeTests : IDisposable
{
    private readonly TestDatabase database = new();

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Der Dienst samt Ablage.
    /// </summary>
    /// <remarks>
    /// Die Ablage hängt mit dran, seit ein Bereichswechsel die Belege mitzieht: Ohne sie
    /// stünde der Eintrag auf dem neuen Bereich und die Datei im alten Ordner.
    /// </remarks>
    private DocumentTypeService Service()
    {
        var context = database.Context();

        return new DocumentTypeService(
            context,
            new DocumentService(
                context, TestDatabase.PathService(root), new ObjectLabelService(context),
                TestDatabase.ClockAt(2026, 11, 1), new PdfPigTextReader(),
                NullLogger<DocumentService>.Instance));
    }

    private int Typ(string name, DocumentArea area = DocumentArea.Other, int dokumente = 0)
    {
        using var context = database.Context();

        var typ = new DocumentType { Name = name, Area = area };
        context.DocumentTypes.Add(typ);
        context.SaveChanges();

        for (var i = 0; i < dokumente; i++)
        {
            context.Documents.Add(new Document
            {
                Title = $"{name} {i}", RelativePath = $"Sonstiges/{name}-{i}.pdf",
                FileName = $"{name}-{i}.pdf", DocumentTypeId = typ.Id, Area = area,
            });
        }

        context.SaveChanges();

        return typ.Id;
    }

    /// <summary>Legt zu einem Typ einen Beleg an, dessen Datei es auch wirklich gibt.</summary>
    private int Beleg(int typId, string pfad)
    {
        using var context = database.Context();

        var absolut = Path.Combine(root, pfad.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolut)!);
        File.WriteAllText(absolut, "Beleg");

        var dokument = new Document
        {
            Title = Path.GetFileNameWithoutExtension(pfad),
            RelativePath = pfad,
            FileName = Path.GetFileName(pfad),
            Extension = Path.GetExtension(pfad),
            DocumentTypeId = typId,
            Area = context.DocumentTypes.Single(t => t.Id == typId).Area,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        return dokument.Id;
    }

    private (DocumentArea Area, string Path) Stand(int dokumentId)
    {
        using var context = database.Context();
        var d = context.Documents.AsNoTracking().Single(x => x.Id == dokumentId);
        return (d.Area, d.RelativePath);
    }

    // ── Anlegen ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_neuer_Typ_landet_im_gewaehlten_Bereich()
    {
        var typ = await Service().CreateAsync(new DocumentTypeNameRequest("Police", DocumentArea.Insurance));

        Assert.Equal("Police", typ.Name);
        Assert.Equal(DocumentArea.Insurance, typ.Area);
        Assert.Equal(0, typ.DocumentCount);
        Assert.False(typ.IsUsed);
    }

    /// <summary>
    /// Doppelte Namen fallen auch bei anderer Schreibweise durch.
    /// </summary>
    /// <remarks>
    /// „Police“ und „police“ wären in jeder Liste dasselbe, und die Ablage bekäme zwei Ordner,
    /// die niemand auseinanderhält.
    /// </remarks>
    [Fact]
    public async Task Derselbe_Name_in_anderer_Schreibweise_wird_abgewiesen()
    {
        Typ("Police");

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().CreateAsync(new DocumentTypeNameRequest("police")));

        Assert.Contains("gibt es schon", fehler.Message);
    }

    [Fact]
    public async Task Ein_leerer_Name_wird_abgewiesen()
        => await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().CreateAsync(new DocumentTypeNameRequest("   ")));

    [Fact]
    public async Task Ein_neuer_Typ_haengt_sich_hinten_an()
    {
        Typ("Police");
        await Service().CreateAsync(new DocumentTypeNameRequest("Rechnung"));

        using var context = database.Context();
        var reihenfolge = await context.DocumentTypes
            .OrderBy(t => t.SortOrder).Select(t => t.Name).ToListAsync();

        Assert.Equal(["Police", "Rechnung"], reihenfolge);
    }

    // ── Umbenennen ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Umbenennen wirkt auf alle Dokumente — und die Meldung nennt die Zahl.
    /// </summary>
    /// <remarks>
    /// Dokumente tragen den Typ über seine Kennung, nicht über den Text. Der Eingriff ist
    /// darum unsichtbar, solange niemand sagt, wie viele Zeilen sich gerade geändert haben.
    /// </remarks>
    [Fact]
    public async Task Umbenennen_nennt_die_Zahl_der_betroffenen_Dokumente()
    {
        var id = Typ("Police", DocumentArea.Insurance, dokumente: 3);

        var ergebnis = await Service().RenameAsync(id, "Versicherungsschein");

        Assert.Equal("Versicherungsschein", ergebnis.Name);
        Assert.Equal(3, ergebnis.DocumentCount);

        using var context = database.Context();
        Assert.Equal(3, await context.Documents.CountAsync(d => d.DocumentTypeId == id));
    }

    [Fact]
    public async Task Umbenennen_auf_einen_vergebenen_Namen_wird_abgewiesen()
    {
        var id = Typ("Police");
        Typ("Rechnung");

        await Assert.ThrowsAsync<RuleViolationException>(() => Service().RenameAsync(id, "Rechnung"));
    }

    /// <summary>Der eigene Name bleibt erlaubt — sonst ließe sich nichts umformatieren.</summary>
    [Fact]
    public async Task Der_eigene_Name_blockiert_nicht()
    {
        var id = Typ("police");

        Assert.Equal("Police", (await Service().RenameAsync(id, "Police")).Name);
    }

    // ── Löschen ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Löschen lässt die Dokumente samt ihrer Typzuordnung stehen.
    /// </summary>
    /// <remarks>
    /// Beim ersten Bau war es ein echtes Löschen. Die Fremdschlüsselregel setzt dann die
    /// Typ-Kennung der Dokumente auf null — zwei abgelegte Belege wussten hinterher nicht mehr,
    /// was sie sind. Der Handoff verlangt ausdrücklich, dass die Referenz bestehen bleibt.
    /// </remarks>
    [Fact]
    public async Task Loeschen_laesst_die_Dokumente_und_ihre_Zuordnung_stehen()
    {
        var id = Typ("Police", DocumentArea.Insurance, dokumente: 2);

        var ergebnis = await Service().DeleteAsync(id);

        Assert.Equal("Police", ergebnis.Name);
        Assert.Equal(2, ergebnis.DocumentCount);

        using var context = database.Context();

        // Aus der Pflegeliste verschwunden …
        Assert.Empty((await Service().GetAsync()).Types);

        // … aber die Zeile steht noch, und die Dokumente zeigen weiter auf sie.
        Assert.Equal(1, await context.DocumentTypes.CountAsync());
        Assert.Equal(2, await context.Documents.CountAsync(d => d.DocumentTypeId == id));
    }

    /// <summary>Ein stillgelegter Typ gibt seinen Namen wieder frei.</summary>
    [Fact]
    public async Task Nach_dem_Loeschen_ist_der_Name_wieder_zu_haben()
    {
        var id = Typ("Police");
        await Service().DeleteAsync(id);

        var neu = await Service().CreateAsync(new DocumentTypeNameRequest("Police"));

        Assert.Equal("Police", neu.Name);
        Assert.NotEqual(id, neu.Id);
    }

    [Fact]
    public async Task Ein_stillgelegter_Typ_laesst_sich_nicht_noch_einmal_loeschen()
    {
        var id = Typ("Police");
        await Service().DeleteAsync(id);

        await Assert.ThrowsAsync<RuleViolationException>(() => Service().DeleteAsync(id));
    }

    [Fact]
    public async Task Einen_unbekannten_Typ_zu_loeschen_meldet_sich()
        => await Assert.ThrowsAsync<RuleViolationException>(() => Service().DeleteAsync(9999));

    // ── Übersicht ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Die_Bereichsfilter_zaehlen_und_Alle_zaehlt_alles()
    {
        Typ("Police", DocumentArea.Insurance);
        Typ("Arztrechnung", DocumentArea.Health);
        Typ("Lohnabrechnung", DocumentArea.Work);
        Typ("Mietvertrag", DocumentArea.Housing);

        var uebersicht = await Service().GetAsync();

        Assert.Equal(4, uebersicht.TotalCount);
        Assert.Equal(4, uebersicht.Areas.Single(a => a.Area is null).Count);
        Assert.Equal(1, uebersicht.Areas.Single(a => a.Area == DocumentArea.Insurance).Count);
        Assert.Equal(0, uebersicht.Areas.Single(a => a.Area == DocumentArea.Finance).Count);
    }

    [Fact]
    public async Task Der_Verwendungsnachweis_zaehlt_die_Dokumente()
    {
        Typ("Police", DocumentArea.Insurance, dokumente: 12);
        Typ("Unbenutzt");

        var typen = (await Service().GetAsync()).Types;

        Assert.Equal(12, typen.Single(t => t.Name == "Police").DocumentCount);
        Assert.True(typen.Single(t => t.Name == "Police").IsUsed);
        Assert.False(typen.Single(t => t.Name == "Unbenutzt").IsUsed);
    }

    // ── Den Bereich ändern ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Bereich einer Art lässt sich nachträglich ändern.
    /// </summary>
    /// <remarks>
    /// Beim Anlegen setzt ihn der Filter, der gerade oben steht. Wer danach merkt, dass eine Art
    /// woanders hingehört, hatte nur die Wahl zwischen Stilllegen und Leben damit.
    /// </remarks>
    [Fact]
    public async Task Ein_Typ_laesst_sich_in_einen_anderen_Bereich_schieben()
    {
        var id = Typ("Lieferschein");

        var ergebnis = await Service().MoveAsync(id, DocumentArea.Housing);

        Assert.Equal("Lieferschein", ergebnis.Name);

        using var context = database.Context();
        Assert.Equal(DocumentArea.Housing, context.DocumentTypes.Single(t => t.Id == id).Area);
    }

    /// <summary>
    /// Die Belege ziehen mit — Eintrag <em>und</em> Datei.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Punkt. Ein Typ, dessen Bereich sich ändert, ohne dass seine Belege
    /// umziehen, erzeugt genau den Widerspruch, gegen den das Umlegen gebaut ist: Der Eintrag
    /// sagt Wohnen, der Ordner sagt Sonstiges, und im Dateimanager traut man dem Ordner.
    /// </remarks>
    [Fact]
    public async Task Die_Belege_ziehen_mit()
    {
        var id = Typ("Lieferschein");
        var beleg = Beleg(id, "Sonstiges/Lieferschein_Holz.pdf");

        var ergebnis = await Service().MoveAsync(id, DocumentArea.Housing);

        Assert.Equal(1, ergebnis.DocumentCount);

        var (area, pfad) = Stand(beleg);
        Assert.Equal(DocumentArea.Housing, area);
        Assert.Equal("Wohnen/Lieferschein_Holz.pdf", pfad);
        Assert.True(
            File.Exists(Path.Combine(root, pfad.Replace('/', Path.DirectorySeparatorChar))),
            $"Die Datei liegt nicht unter {pfad}.");
        Assert.False(File.Exists(Path.Combine(root, "Sonstiges", "Lieferschein_Holz.pdf")));
    }

    /// <summary>
    /// Alle Belege der Art ziehen mit, auch die, die niemand im Sinn hatte.
    /// </summary>
    /// <remarks>
    /// Eine Art ist eine Schublade und kein Einzelfall. Die Meldung nennt deshalb die Zahl —
    /// wer nur einen Beleg anderswo haben will, braucht eine eigene Art für ihn.
    /// </remarks>
    [Fact]
    public async Task Der_Bereichswechsel_nimmt_jeden_Beleg_der_Art_mit()
    {
        var id = Typ("Lieferschein");
        var haus = Beleg(id, "Sonstiges/Lieferschein_Holz.pdf");
        var geraet = Beleg(id, "Sonstiges/Lieferschein_Geraet.pdf");

        var ergebnis = await Service().MoveAsync(id, DocumentArea.Housing);

        Assert.Equal(2, ergebnis.DocumentCount);
        Assert.StartsWith("Wohnen/", Stand(haus).Path, StringComparison.Ordinal);
        Assert.StartsWith("Wohnen/", Stand(geraet).Path, StringComparison.Ordinal);
    }

    /// <summary>Derselbe Bereich ist keine Änderung — und rührt keine Datei an.</summary>
    [Fact]
    public async Task Derselbe_Bereich_aendert_nichts()
    {
        var id = Typ("Lieferschein", DocumentArea.Housing);
        var beleg = Beleg(id, "Wohnen/Lieferschein_Holz.pdf");

        var ergebnis = await Service().MoveAsync(id, DocumentArea.Housing);

        Assert.Equal(0, ergebnis.DocumentCount);
        Assert.Equal("Wohnen/Lieferschein_Holz.pdf", Stand(beleg).Path);
    }

    /// <summary>Einen Typ, den es nicht gibt, verschiebt niemand.</summary>
    [Fact]
    public async Task Ein_unbekannter_Typ_laesst_sich_nicht_verschieben()
        => await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().MoveAsync(987654, DocumentArea.Housing));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
