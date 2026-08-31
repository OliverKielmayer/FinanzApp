namespace FinanzApp.Shared.Contracts;

/// <summary>Wie ein Beleg im Schirm gezeigt werden kann.</summary>
public enum DocumentPreviewKind
{
    /// <summary>Nicht anzeigbar — es bleibt beim Öffnen oder Herunterladen.</summary>
    None,

    /// <summary>Im Rahmen anzeigbar: PDF, Text, CSV, XML.</summary>
    Frame,

    /// <summary>Als Bild anzeigbar.</summary>
    Image,
}

/// <summary>
/// Welche Dateiarten die Vorschau zeigen kann.
/// </summary>
/// <remarks>
/// <para>Eigene Stelle und kein <c>switch</c> im Markup, weil die Regel eine Aussage ist: ein
/// Rahmen, der nichts zeigt, ist schlimmer als der Satz, dass diese Dateiart nicht angezeigt
/// werden kann. Beides muss aus derselben Entscheidung kommen.</para>
/// <para>HEIC steht bewusst nicht dabei: die Erweiterung ist erlaubt, aber nur ein Teil der
/// Browser stellt sie dar. Eine Vorschau, die bei jedem zweiten Benutzer leer bleibt, ist keine.
/// </para>
/// </remarks>
public static class DocumentPreview
{
    /// <summary>Was sich mit dieser Erweiterung anzeigen lässt.</summary>
    /// <param name="extension">Erweiterung mit Punkt, Groß- und Kleinschreibung beliebig.</param>
    /// <param name="fileName">
    /// Der Dateiname als Rückfall, wenn die Erweiterung nicht gepflegt ist.
    /// </param>
    /// <remarks>
    /// Der Rückfall ist kein Luxus: die Belege im Scaneingang wurden ohne Erweiterung angelegt,
    /// und ohne ihn hieße es bei einem sichtbaren <c>.pdf</c> „lässt sich nicht anzeigen“. Der
    /// Name trägt sie immer, das Feld nicht.
    /// </remarks>
    public static DocumentPreviewKind For(string? extension, string? fileName = null)
        => Endung(extension)
           ?? Endung(Path.GetExtension(fileName))
           ?? DocumentPreviewKind.None;

    private static DocumentPreviewKind? Endung(string? extension) =>
        extension?.Trim().ToLowerInvariant() switch
        {
            ".pdf" or ".txt" or ".xml" or ".csv" => DocumentPreviewKind.Frame,
            ".jpg" or ".jpeg" or ".png" or ".webp" => DocumentPreviewKind.Image,
            _ => null,
        };

    /// <summary>Die Adresse, unter der die Datei zum Anzeigen liegt.</summary>
    public static string FileUrl(int documentId) => $"/api/documents/{documentId}/file";

    /// <summary>Dieselbe Datei zum Speichern — mit Dateinamen im Kopf der Antwort.</summary>
    public static string DownloadUrl(int documentId) => $"/api/documents/{documentId}/file?download=true";
}
