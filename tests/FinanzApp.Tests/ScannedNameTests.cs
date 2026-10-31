using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der Name, den ein Gerät vergibt — und was daraus wird, sobald der Beleg zugeordnet ist.
/// </summary>
/// <remarks>
/// <para>Ein Scanner zählt durch: „Scannen0007“. Nach dem Zuordnen im Eingang liegt die Datei
/// beim richtigen Vertrag und heißt weiter so. Der Ordner stimmt dann, der Name nicht, und im
/// Dateimanager unterscheidet nichts die eine Seite von der nächsten.</para>
/// <para>Die Grenze ist das Eigentliche: Angefasst wird <b>nur</b>, was nach einem Gerätenamen
/// aussieht. Ein überschriebener Name ist nicht zurückzuholen, und die Anwendung weiß nicht
/// besser als ein Mensch, wie sein Beleg heißen soll.</para>
/// </remarks>
public sealed class ScannedNameTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 31);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Dienst()
    {
        var context = database.Context();
        return new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);
    }

    /// <summary>Legt eine Datei an den Pfad und einen Eintrag dazu.</summary>
    private int Beleg(string relativerPfad, DateOnly? datum = null)
    {
        var absolut = Path.Combine(root, relativerPfad.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolut)!);
        File.WriteAllText(absolut, "Inhalt");

        using var context = database.Context();
        var dokument = new Document
        {
            Title = Path.GetFileNameWithoutExtension(relativerPfad),
            Area = DocumentArea.Insurance,
            RelativePath = relativerPfad,
            FileName = Path.GetFileName(relativerPfad),
            Extension = Path.GetExtension(relativerPfad),
            DocumentDate = datum,
            Status = DocumentStatus.Active,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private string PfadVon(int id)
    {
        using var context = database.Context();
        return context.Documents.Single(d => d.Id == id).RelativePath;
    }

    // ── Was umbenannt wird ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_Scannername_wird_durch_Typ_Objekt_und_Datum_ersetzt()
    {
        var id = Beleg("Versicherungen/Allianz/Scannen0007.pdf", new DateOnly(2026, 9, 30));

        var ziel = await Dienst().RenameIfScannedAsync(id, "Beitragsrechnung", "Allianz",
            new DateOnly(2026, 9, 30));

        Assert.Equal("Versicherungen/Allianz/Beitragsrechnung_Allianz_2026-09-30.pdf", ziel);
        Assert.Equal(ziel, PfadVon(id));

        // Und die Datei liegt wirklich dort.
        Assert.True(File.Exists(Path.Combine(root,
            "Versicherungen", "Allianz", "Beitragsrechnung_Allianz_2026-09-30.pdf")));
        Assert.False(File.Exists(Path.Combine(root, "Versicherungen", "Allianz", "Scannen0007.pdf")));
    }

    /// <summary>Der Ordner bleibt, wo er ist — umbenannt wird, nicht verschoben.</summary>
    [Fact]
    public async Task Der_Ordner_bleibt_unberuehrt()
    {
        var id = Beleg("Gesundheit/Praxis Dr. Test/2026/Scan_12.pdf");

        var ziel = await Dienst().RenameIfScannedAsync(id, "Arztrechnung", "Praxis Dr. Test", null);

        Assert.StartsWith("Gesundheit/Praxis Dr. Test/2026/", ziel);
        // Die Ablage entschärft den Namen: Leerzeichen und Punkte werden Unterstriche.
        Assert.Equal("Arztrechnung_Praxis_Dr._Test.pdf", Path.GetFileName(ziel));
    }

    [Theory]
    [InlineData("Scannen0002.pdf")]
    [InlineData("scan_0001.pdf")]
    [InlineData("IMG_20260930.jpg")]
    [InlineData("Bild 4.png")]
    [InlineData("Dokument.pdf")]
    [InlineData("datei-7.pdf")]
    public async Task Geraetenamen_werden_erkannt(string name)
    {
        var id = Beleg("Sonstiges/" + name);

        Assert.NotNull(await Dienst().RenameIfScannedAsync(id, "Rechnung", "Anbieter", null));
    }

    // ── Was unangetastet bleibt ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein gepflegter Name bleibt stehen.
    /// </summary>
    /// <remarks>
    /// Dieselbe Regel wie beim Umlegen: Was ein Mensch eingerichtet hat, fasst der Dienst nicht
    /// an. Ein überschriebener Name ist nicht zurückzuholen.
    /// </remarks>
    [Theory]
    [InlineData("Beitragsrechnung 2026.pdf")]
    [InlineData("20240102 Beitragsrechnung Rechtsschutz.pdf")]
    [InlineData("Kontoauszug_8009_2026-08-03.xml")]
    [InlineData("Scan der Police vom Notar.pdf")]
    public async Task Ein_gepflegter_Name_bleibt_stehen(string name)
    {
        var id = Beleg("Sonstiges/" + name);

        Assert.Null(await Dienst().RenameIfScannedAsync(id, "Rechnung", "Anbieter", null));
        Assert.Equal("Sonstiges/" + name, PfadVon(id));
    }

    /// <summary>Ohne Dokumentdatum steht kein Datum im Namen — keine erfundene Null.</summary>
    [Fact]
    public async Task Ohne_Datum_steht_kein_Datum_im_Namen()
    {
        var id = Beleg("Sonstiges/Scannen0003.pdf");

        var ziel = await Dienst().RenameIfScannedAsync(id, "Rechnung", "Globus", null);

        Assert.Equal("Sonstiges/Rechnung_Globus.pdf", ziel);
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
