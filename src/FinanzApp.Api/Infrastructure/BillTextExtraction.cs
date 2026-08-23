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
/// Erkennt nichts und sagt das auch. Der eingebaute Stand, solange keine Texterkennung
/// angebunden ist.
/// </summary>
public sealed class NoBillTextExtractor : IBillTextExtractor
{
    private static readonly ExtractedBillDto Empty = new() { HasContent = false };

    public Task<ExtractedBillDto> ExtractAsync(
        Stream content, string fileName, CancellationToken ct = default)
        => Task.FromResult(Empty);
}
