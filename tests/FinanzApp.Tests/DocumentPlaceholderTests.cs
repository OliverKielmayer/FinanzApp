using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace FinanzApp.Tests;

/// <summary>
/// Die Platzhalterdateien der Beispieldaten sind das, was ihr Name behauptet.
/// </summary>
/// <remarks>
/// <para>Vorher schrieb der Seed Text unter PDF-Namen. Solange die Vorschau nur einen Satz über
/// die Datei zeigte, fiel das nicht auf; sobald sie die Datei selbst zeigt, bekommt der Browser
/// <c>application/pdf</c> mit einem Inhalt, der keines ist, und stellt einen Ladefehler dar.</para>
/// <para>Geprüft wird mit demselben Leser, den der Belegweg benutzt — nicht gegen die Bytes,
/// sondern gegen die Frage „lässt sich das öffnen und lesen“.</para>
/// </remarks>
public sealed class DocumentPlaceholderTests : IDisposable
{
    private readonly TestDatabase database = new();

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private async Task SeedAsync()
    {
        using var context = database.Context();
        await SeedData.EnsureSeededAsync(
            context, new PasswordHasher<User>(), TestDatabase.PathService(root));
    }

    /// <summary>Jede abgelegte Datei mit PDF-Namen ist ein lesbares PDF.</summary>
    [Fact]
    public async Task Die_Platzhalter_sind_lesbare_PDFs()
    {
        await SeedAsync();

        var dateien = Directory.GetFiles(root, "*.pdf", SearchOption.AllDirectories);

        Assert.NotEmpty(dateien);

        var leser = new PdfPigTextReader();

        foreach (var datei in dateien)
        {
            using var strom = File.OpenRead(datei);
            var inhalt = leser.Read(strom);

            // Der Leser meldet auch das Scheitern als Zustand: „ließ sich nicht als PDF lesen“.
            Assert.True(inhalt.PageCount > 0, datei + ": " + inhalt.Note);
            Assert.Contains("FinanzApp", string.Join(' ', inhalt.Lines.SelectMany(l => l.Cells)));
        }
    }

    /// <summary>
    /// Der Titel steht auf dem Blatt.
    /// </summary>
    /// <remarks>
    /// Sonst wären alle Platzhalter dasselbe Blatt, und in der Vorschau ließe sich nicht sehen,
    /// welches Dokument gerade offen ist.
    /// </remarks>
    [Fact]
    public async Task Der_Platzhalter_traegt_seinen_Titel()
    {
        await SeedAsync();

        var lohn = Directory.GetFiles(root, "Lohn_05_2026.pdf", SearchOption.AllDirectories).Single();

        using var strom = File.OpenRead(lohn);
        var text = string.Join(' ', new PdfPigTextReader().Read(strom).Lines.SelectMany(l => l.Cells));

        Assert.Contains("Lohnabrechnung 05/2026", text);
    }

    /// <summary>Was kein PDF ist, bleibt Text — der Platzhalter lügt in beide Richtungen nicht.</summary>
    [Fact]
    public async Task Andere_Dateiarten_bleiben_Text()
    {
        await SeedAsync();

        var andere = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(d => !d.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var datei in andere)
        {
            Assert.StartsWith("FinanzApp", await File.ReadAllTextAsync(datei));
        }
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
