using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Woher ein Beleg kam — und wer diesen Satz formuliert.
/// </summary>
/// <remarks>
/// <para>Der Eingang setzte vor jede mitgelieferte Herkunft denselben Vorspann: „Aus dem
/// überwachten Ordner übernommen: …“. Von drei Aufrufern stimmte er bei <em>einem</em>. Aus dem
/// Postfach wurde „Aus dem überwachten Ordner übernommen: Postfach: ‚Betreff‘ von …“, und aus
/// einer von Hand gewählten Datei „… übernommen: Erfassen“.</para>
/// <para>Der Satz gehört zum Aufrufer, weil nur er weiß, woher die Bytes kommen. Die Anwendung
/// legt ab, was ihr gesagt wird — und das halten diese Prüfungen fest.</para>
/// </remarks>
public sealed class ScanOriginTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 21);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private sealed class StubReader(PdfContent content) : IPdfTextReader
    {
        public PdfContent Read(Stream stream) => content;
    }

    /// <summary>Ein Blatt ohne Kennzeichen — die Art ist hier nicht die Frage.</summary>
    private static PdfContent Blatt() => new()
    {
        PageCount = 1,
        Lines = [new PdfLine(1, ["Guten Tag, anbei die Unterlagen."])],
        TextIsInvisible = false,
        ImageCount = 0,
    };

    private ScanIntakeService Dienst(FinanzAppDbContext context)
    {
        var paths = TestDatabase.PathService(root);

        var documents = new DocumentService(
            context, paths, new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        var scans = new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new StubReader(Blatt()), clock);

        return new ScanIntakeService(
            context, scans, documents, new ScanInboxService(context, documents, clock),
            new AssetSheetParser(), new CamtStatementParser(),
            TestDatabase.NoActions(context, clock), clock);
    }

    private int eingeliefert;

    /// <summary>Liefert ein Blatt ein und gibt zurück, was als Herkunft am Beleg steht.</summary>
    private async Task<string?> HerkunftAsync(string? herkunft)
    {
        using var context = database.Context();

        // Jede Datei mit eigenem Inhalt: Der Eingang weist eine zweite mit demselben Abdruck ab,
        // und dann stünde die Herkunft der ersten da.
        var datei = $"%PDF-1.4 Blatt {++eingeliefert}";

        var ergebnis = await Dienst(context).TakeInAsync(
            new MemoryStream(Encoding.UTF8.GetBytes(datei)),
            $"Schreiben{eingeliefert}.pdf",
            herkunft);

        using var nachher = database.Context();

        return nachher.Documents.First(d => d.Id == ergebnis.DocumentId).Description;
    }

    /// <summary>
    /// Was aus dem Postfach kommt, sagt das auch so.
    /// </summary>
    /// <remarks>
    /// Der Fall, an dem es aufgefallen ist: Eine Beitragsrechnung kam als Mailanhang herein und
    /// trug danach am Beleg, sie stamme aus einem überwachten Ordner.
    /// </remarks>
    [Fact]
    public async Task Eine_Mail_kommt_nicht_aus_einem_Ordner()
    {
        const string herkunft = "Postfach: „Ihre Rechnung“ von absender@example.org (12.11.2026)";

        Assert.Equal(herkunft, await HerkunftAsync(herkunft));
    }

    /// <summary>Und was aus dem Ordner kommt, behält seinen Satz unverändert.</summary>
    [Fact]
    public async Task Der_Ordner_behaelt_seinen_Satz()
    {
        const string herkunft = @"Aus dem überwachten Ordner: C:\Ablage\Eingang\Schreiben.pdf";

        Assert.Equal(herkunft, await HerkunftAsync(herkunft));
    }

    /// <summary>Ohne Herkunft bleibt das Feld leer — erfunden wird keine.</summary>
    [Fact]
    public async Task Ohne_Angabe_bleibt_die_Herkunft_leer()
    {
        Assert.True(string.IsNullOrEmpty(await HerkunftAsync(null)));
    }

    /// <summary>
    /// Ein überlanger Satz wird gekürzt, und man sieht es ihm an.
    /// </summary>
    /// <remarks>
    /// Die Grenze ist zweihundert Zeichen. Sie gilt jetzt dem ganzen Satz statt nur seinem Ende
    /// — vorher kam der Vorspann danach noch dazu, und das Feld wurde länger als erlaubt.
    /// </remarks>
    [Fact]
    public async Task Eine_lange_Herkunft_wird_gekuerzt()
    {
        var lang = "Postfach: " + new string('x', 400);
        var gespeichert = await HerkunftAsync(lang);

        Assert.NotNull(gespeichert);
        Assert.Equal(201, gespeichert.Length);
        Assert.EndsWith("…", gespeichert, StringComparison.Ordinal);
        Assert.StartsWith("Postfach: ", gespeichert, StringComparison.Ordinal);
    }

    /// <summary>
    /// Zeilenumbrüche aus einem Betreff zerreißen die Beschreibung nicht.
    /// </summary>
    /// <remarks>
    /// Ein Mailbetreff kommt von außen und darf alles enthalten. Steuerzeichen fallen deshalb
    /// weg, bevor der Satz am Beleg landet.
    /// </remarks>
    [Fact]
    public async Task Steuerzeichen_fallen_weg()
    {
        var gespeichert = await HerkunftAsync("Postfach: „Zeile\neins\tund zwei“ von a@b.de");

        Assert.NotNull(gespeichert);
        Assert.DoesNotContain('\n', gespeichert);
        Assert.DoesNotContain('\t', gespeichert);
    }

    public void Dispose() => database.Dispose();
}
