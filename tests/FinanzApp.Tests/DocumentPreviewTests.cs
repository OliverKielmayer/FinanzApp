using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Was die Dokumentvorschau zeigen kann — und was sie stattdessen sagt.
/// </summary>
/// <remarks>
/// <para>Der Befund dahinter: die Vorschauspalte zeigte einen Satz über die Datei statt die
/// Datei. Der Knopf daneben lieferte sie mit <c>Content-Disposition: attachment</c> aus, also als
/// Download — auch im neuen Fenster kam nichts zu sehen.</para>
/// <para>Die Regel steht deshalb an einer Stelle und nicht im Markup: ein Rahmen, der nichts
/// zeigt, ist schlimmer als der Satz, dass diese Dateiart sich nicht anzeigen lässt.</para>
/// </remarks>
public sealed class DocumentPreviewTests
{
    [Theory]
    [InlineData(".pdf")]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData(".xml")]
    public void Im_Rahmen_zeigbar(string endung)
        => Assert.Equal(DocumentPreviewKind.Frame, DocumentPreview.For(endung));

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    public void Als_Bild_zeigbar(string endung)
        => Assert.Equal(DocumentPreviewKind.Image, DocumentPreview.For(endung));

    /// <summary>
    /// HEIC ist erlaubt, aber nicht vorführbar.
    /// </summary>
    /// <remarks>
    /// Die Erweiterung darf hochgeladen werden — nur stellt sie ein Teil der Browser nicht dar.
    /// Eine Vorschau, die bei jedem zweiten Benutzer leer bleibt, ist keine.
    /// </remarks>
    [Theory]
    [InlineData(".heic")]
    [InlineData(".docx")]
    [InlineData("")]
    [InlineData(null)]
    public void Nicht_zeigbar(string? endung)
        => Assert.Equal(DocumentPreviewKind.None, DocumentPreview.For(endung));

    /// <summary>Die Schreibweise der Erweiterung entscheidet nichts.</summary>
    /// <remarks>
    /// Hochgeladene Dateien heißen auch „Rechnung.PDF“. Ohne diese Zeile hinge die Vorschau an
    /// der Umschalttaste des Absenders.
    /// </remarks>
    [Theory]
    [InlineData(".PDF")]
    [InlineData(" .Pdf ")]
    public void Die_Schreibweise_ist_gleichgueltig(string endung)
        => Assert.Equal(DocumentPreviewKind.Frame, DocumentPreview.For(endung));

    /// <summary>
    /// Fehlt die Erweiterung, entscheidet der Dateiname.
    /// </summary>
    /// <remarks>
    /// Die Belege im Scaneingang wurden ohne Erweiterung angelegt. Ohne diesen Rückfall stünde
    /// bei einem sichtbaren <c>.pdf</c> „lässt sich hier nicht anzeigen“ — nachgemessen an den
    /// Beispieldaten, nicht vermutet.
    /// </remarks>
    [Fact]
    public void Ohne_Erweiterung_entscheidet_der_Dateiname()
    {
        Assert.Equal(DocumentPreviewKind.Frame, DocumentPreview.For(null, "Beitragsanpassung_2027.pdf"));
        Assert.Equal(DocumentPreviewKind.Image, DocumentPreview.For("", "Foto.PNG"));
        Assert.Equal(DocumentPreviewKind.None, DocumentPreview.For(null, "Notiz"));
    }

    /// <summary>Die gepflegte Erweiterung gewinnt gegen den Dateinamen.</summary>
    /// <remarks>
    /// Sie steht im Datensatz, weil jemand sie dort hingeschrieben hat — etwa beim Korrigieren
    /// eines Pfades. Der Name ist der Rückfall, nicht die Quelle.
    /// </remarks>
    [Fact]
    public void Die_gepflegte_Erweiterung_gewinnt()
        => Assert.Equal(DocumentPreviewKind.Image, DocumentPreview.For(".png", "Beleg.pdf"));

    /// <summary>
    /// Anzeigen und Herunterladen sind zwei Adressen.
    /// </summary>
    /// <remarks>
    /// Eine Adresse für beides ging nicht: mit Dateinamen im Kopf der Antwort lädt der Browser
    /// herunter statt zu zeigen. Der Download behält den Namen, das Anzeigen verzichtet darauf.
    /// </remarks>
    [Fact]
    public void Anzeigen_und_Herunterladen_haben_eigene_Adressen()
    {
        Assert.Equal("/api/documents/11/file", DocumentPreview.FileUrl(11));
        Assert.Equal("/api/documents/11/file?download=true", DocumentPreview.DownloadUrl(11));
    }
}
