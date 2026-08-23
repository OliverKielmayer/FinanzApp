using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Pfadauflösung relativ ↔ absolut, fehlende Datei und der Ausbruchsversuch aus dem
/// Dokumentordner. Der gespeicherte Pfad ist eine Eingabe wie jede andere.
/// </summary>
public sealed class DocumentPathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Relativer_Pfad_wird_unter_der_Wurzel_aufgeloest()
    {
        var paths = TestDatabase.PathService(root);

        var resolved = paths.Resolve("Versicherungen/Hausrat/Schein.pdf");

        Assert.NotNull(resolved);
        Assert.StartsWith(paths.Root, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Schein.pdf", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../geheim.txt")]
    [InlineData("Wohnen/../../geheim.txt")]
    [InlineData("Wohnen/../../../Windows/win.ini")]
    public void Ausbruch_aus_dem_Dokumentordner_wird_abgewiesen(string attempt)
    {
        var paths = TestDatabase.PathService(root);

        Assert.Null(paths.Resolve(attempt));
    }

    [Fact]
    public void Absoluter_Pfad_wird_abgewiesen()
    {
        var paths = TestDatabase.PathService(root);

        Assert.Null(paths.Resolve(Path.Combine(Path.GetTempPath(), "fremd.pdf")));
    }

    [Fact]
    public void Fehlende_Datei_meldet_sich_als_nicht_vorhanden()
    {
        var paths = TestDatabase.PathService(root);

        Assert.False(paths.Exists("Arbeit/Lohn/2026/Lohn_07_2026.pdf"));
    }

    [Fact]
    public async Task Abgelegte_Datei_liegt_im_Bereichsordner_und_wird_gefunden()
    {
        var paths = TestDatabase.PathService(root);
        using var content = new MemoryStream("Inhalt"u8.ToArray());

        var relative = await paths.StoreAsync(content, DocumentArea.Insurance, "Police 2026.pdf");

        Assert.StartsWith("Versicherungen/", relative, StringComparison.Ordinal);
        Assert.True(paths.Exists(relative));
    }

    [Fact]
    public async Task Gleicher_Dateiname_ueberschreibt_nicht()
    {
        var paths = TestDatabase.PathService(root);

        using var first = new MemoryStream("eins"u8.ToArray());
        var a = await paths.StoreAsync(first, DocumentArea.Health, "Rechnung.pdf");

        using var second = new MemoryStream("zwei"u8.ToArray());
        var b = await paths.StoreAsync(second, DocumentArea.Health, "Rechnung.pdf");

        Assert.NotEqual(a, b);
        Assert.True(paths.Exists(a));
        Assert.True(paths.Exists(b));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("Lohn Juli.pdf", "Lohn_Juli.pdf")]
    [InlineData("Rückkauf ößü.pdf", "Rueckkauf_oessue.pdf")]
    public void Dateiname_wird_entschaerft(string input, string expected)
        => Assert.Equal(expected, FinanzApp.Api.Infrastructure.DocumentPathService.Sanitize(input));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
