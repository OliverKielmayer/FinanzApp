using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Liest die Eckdaten aus einem gescannten Beleg.
/// </summary>
/// <remarks>
/// Bewusst eine Schnittstelle mit einem einzigen Aufruf: welcher Anbieter die Texterkennung
/// leistet — lokale Bibliothek, Dienst, gar keine — darf den Fachcode nicht erreichen. Der
/// PKV-Flow funktioniert ohne Erkennung vollständig; die Maske ist dann nur leer.
/// </remarks>
public interface IBillTextExtractor
{
    Task<ExtractedBillDto> ExtractAsync(Stream content, string fileName, CancellationToken ct = default);
}

/// <summary>
/// Erkennt nichts und sagt das auch. Bleibt als Rückfall, wo kein Lesen gewollt ist.
/// </summary>
public sealed class NoBillTextExtractor : IBillTextExtractor
{
    private static readonly ExtractedBillDto Empty = new() { HasContent = false };

    public Task<ExtractedBillDto> ExtractAsync(
        Stream content, string fileName, CancellationToken ct = default)
        => Task.FromResult(Empty);
}

/// <summary>
/// Liest den Beleg und dann seine Eckdaten.
/// </summary>
/// <remarks>
/// <para>Zwei Schritte, die nichts voneinander wissen: Der <see cref="IPdfTextReader"/> macht
/// aus einer Datei Text — Textebene, Bild, Erkennung, einerlei. <see cref="BillTextReader"/>
/// macht aus Text Werte und kennt keine Dateien.</para>
/// <para>Liegt keine Texterkennung an und ist der Beleg ein Bild, kommt kein Text und damit
/// auch kein Wert. Die Maske bleibt leer und fragt — genau wie vorher.</para>
/// </remarks>
public sealed class ReadingBillTextExtractor(IPdfTextReader reader) : IBillTextExtractor
{
    public Task<ExtractedBillDto> ExtractAsync(
        Stream content, string fileName, CancellationToken ct = default)
        => Task.FromResult(BillTextReader.Read(reader.Read(content).PlainText));
}
