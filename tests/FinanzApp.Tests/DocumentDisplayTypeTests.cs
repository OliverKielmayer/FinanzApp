using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Unter welchem Typ eine Datei zum <em>Ansehen</em> hinausgeht.
/// </summary>
/// <remarks>
/// <para><b>Woher die Frage kommt:</b> Eine CSV ging als <c>text/csv</c> in den Vorschaurahmen,
/// und darauf öffnete der Browser einen Speichern-Dialog statt die Datei zu zeigen. Der Rahmen
/// blieb leer, und ein Fenster ging auf, das niemand gerufen hatte.</para>
/// <para><b>Und warum die Zeichensatzangabe dazugehört:</b> Ohne sie liest der Browser
/// <c>text/plain</c> als UTF-8. Eine Datei in Windows-1252 wird dann zu einer Reihe von
/// Ersatzzeichen — lesbar ist das so wenig wie der Speichern-Dialog.</para>
/// <para>Der Download bleibt unberührt: Dort ist eine Tabellenkalkulation das Ziel, und die
/// will <c>text/csv</c>.</para>
/// <para>Die Inhalte hier sind erfunden.</para>
/// </remarks>
public sealed class DocumentDisplayTypeTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 14);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    /// <summary>Legt eine Datei mit genau diesen Bytes ab und trägt sie ein.</summary>
    private int Ablegen(string dateiname, byte[] inhalt)
    {
        var absolut = Path.Combine(root, dateiname);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(absolut, inhalt);

        using var context = database.Context();

        var dokument = new Document
        {
            Title = Path.GetFileNameWithoutExtension(dateiname),
            RelativePath = dateiname,
            FileName = dateiname,
            Extension = Path.GetExtension(dateiname),
            Area = DocumentArea.Insurance,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        return dokument.Id;
    }

    private async Task<string> TypAsync(int id, bool anzeigen)
    {
        using var context = database.Context();
        var datei = await Documents(context).OpenAsync(id, forDisplay: anzeigen);

        Assert.NotNull(datei);
        await datei.Value.Content.DisposeAsync();

        return datei.Value.ContentType;
    }

    // ── Die CSV ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zum Ansehen geht eine CSV als Text hinaus — sonst zeigt der Browser sie nicht.
    /// </summary>
    [Fact]
    public async Task Zum_Ansehen_ist_eine_CSV_Text()
    {
        var id = Ablegen("uebersicht.csv", Encoding.UTF8.GetBytes("Spalte;Wert\nMiete;1\n"));

        var typ = await TypAsync(id, anzeigen: true);

        Assert.StartsWith("text/plain", typ, StringComparison.Ordinal);
        Assert.DoesNotContain("text/csv", typ, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Zum Herunterladen bleibt sie eine CSV.</b> Dort öffnet die Tabellenkalkulation sie,
    /// und die geht nach dem Typ.
    /// </summary>
    [Fact]
    public async Task Zum_Herunterladen_bleibt_sie_eine_CSV()
    {
        var id = Ablegen("uebersicht.csv", Encoding.UTF8.GetBytes("Spalte;Wert\n"));

        Assert.Equal("text/csv", await TypAsync(id, anzeigen: false));
    }

    // ── Der Zeichensatz ───────────────────────────────────────────────────────────────────

    /// <summary>UTF-8 wird als UTF-8 angesagt.</summary>
    [Fact]
    public async Task Eine_Datei_in_UTF8_wird_als_UTF8_angesagt()
    {
        var id = Ablegen(
            "utf8.csv", Encoding.UTF8.GetBytes("Bezeichnung;Wert\nGrundstück;1 €\n"));

        Assert.Contains("charset=utf-8", await TypAsync(id, anzeigen: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Und Windows-1252 als Windows-1252.</b> Ohne diese Unterscheidung stünde in der
    /// Vorschau „Verm?gen in ?" statt „Vermögen in €“.
    /// </summary>
    /// <remarks>
    /// Die Bytes sind von Hand gesetzt und keine Umwandlung: <c>0xF6</c> ist das ö,
    /// <c>0xDF</c> das ß, <c>0x80</c> das Eurozeichen. Als UTF-8 gelesen ergibt keines davon
    /// eine gültige Folge — genau daran wird es erkannt.
    /// </remarks>
    [Fact]
    public async Task Eine_Datei_in_Windows1252_wird_als_Windows1252_angesagt()
    {
        byte[] inhalt =
        [
            .. "Bezeichnung;Gr"u8.ToArray(), 0xF6, 0xDF, .. "e in "u8.ToArray(), 0x80,
            .. "\r\nKiste;1\r\n"u8.ToArray(),
        ];

        var id = Ablegen("ansi.csv", inhalt);

        Assert.Contains("charset=windows-1252", await TypAsync(id, anzeigen: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Ein Umlaut jenseits der ersten Leseprobe entscheidet nicht mehr.</b>
    /// </summary>
    /// <remarks>
    /// Geprüft werden 64 KB vom Anfang. Diese Grenze ist eine Entscheidung und keine
    /// Nachlässigkeit: Eine ganze Datei zu lesen, nur um ihren Typ anzusagen, kostet bei jedem
    /// Blick in die Vorschau — und eine Datei, deren erste 64 KB sauber als UTF-8 aufgehen, ist
    /// praktisch nie in einer anderen Kodierung geschrieben. Der Test hält diese Annahme fest,
    /// damit sie beim nächsten Umbau nicht unbemerkt verschwindet.
    /// </remarks>
    [Fact]
    public async Task Jenseits_der_Leseprobe_entscheidet_nichts_mehr()
    {
        byte[] inhalt =
        [
            .. Encoding.ASCII.GetBytes(new string('x', 70_000)),
            0xF6,
        ];

        var id = Ablegen("spaet.csv", inhalt);

        Assert.Contains("charset=utf-8", await TypAsync(id, anzeigen: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Ein Zeichen, das genau auf dem Schnitt liegt, kippt die Kodierung nicht.</b>
    /// </summary>
    /// <remarks>
    /// <para>Die Leseprobe endet nach 64 KB, und ein Umlaut besteht in UTF-8 aus zwei Bytes.
    /// Liegt das erste davon auf dem letzten Platz des Puffers, sieht der Decoder eine
    /// angefangene Folge ohne Fortsetzung. Zählte das als Fehler, entschiede die zufällige
    /// Stelle des Schnitts über den Zeichensatz: dieselbe Datei, ein Zeichen früher
    /// geschrieben, käme als Windows-1252 heraus.</para>
    /// <para>Gebaut ist der Fall von Hand, weil er sich nicht zufällig einstellt:
    /// 65.535 Zeichen <c>x</c>, dann <c>0xC3 0xA4</c> — das ä. Das <c>0xC3</c> ist das letzte
    /// Byte der Leseprobe, das <c>0xA4</c> das erste dahinter.</para>
    /// <para>Diese Prüfung fehlte zuerst: Eine Eichung mit <c>flush: true</c> an der
    /// entscheidenden Stelle ging durch, ohne dass ein einziger Test ansprang.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_Zeichen_auf_dem_Schnitt_kippt_nichts()
    {
        byte[] inhalt =
        [
            .. Encoding.ASCII.GetBytes(new string('x', 64 * 1024 - 1)),
            0xC3, 0xA4,
            .. Encoding.ASCII.GetBytes(new string('y', 100)),
        ];

        // Die Annahme hinter dem Fall — stimmt sie nicht, prüft der Test etwas anderes.
        Assert.Equal(0xC3, inhalt[64 * 1024 - 1]);

        var id = Ablegen("schnitt.csv", inhalt);

        Assert.Contains("charset=utf-8", await TypAsync(id, anzeigen: true),
            StringComparison.Ordinal);
    }

    // ── Was unberührt bleibt ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein PDF wird nicht angefasst — der Browser zeigt es von sich aus.
    /// </summary>
    [Fact]
    public async Task Ein_PDF_bleibt_ein_PDF()
    {
        var id = Ablegen("beleg.pdf", "%PDF-1.4"u8.ToArray());

        Assert.Equal("application/pdf", await TypAsync(id, anzeigen: true));
        Assert.Equal("application/pdf", await TypAsync(id, anzeigen: false));
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
