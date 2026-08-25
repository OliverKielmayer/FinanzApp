using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Liest eine Police oder einen Beleg und schlägt Feldwerte vor.
/// </summary>
/// <remarks>
/// <para>Der Handoff verlangt die Analyse hinter <b>einer</b> austauschbaren Schnittstelle und
/// keinen Anbieternamen im Fachcode. Deshalb steht hier nur, <em>was</em> herauskommt, nie
/// <em>wodurch</em>. Ob dahinter eine Texterkennung, ein Sprachmodell oder gar nichts sitzt,
/// merkt der Rest der Anwendung nicht.</para>
/// <para>Sie muss auch fehlen dürfen: dann liefert sie nichts, und die Maske ist dieselbe, nur
/// leer. Ein Anlege-Flow, der ohne Erkennung nicht funktioniert, wäre von ihr abhängig — genau
/// das soll er nicht sein.</para>
/// </remarks>
public interface IPolicyDocumentAnalyzer
{
    /// <summary>
    /// Analysiert den Inhalt. <paramref name="type"/> sagt, welche Felder überhaupt gesucht
    /// werden — eine Police trägt andere als eine Rechnung.
    /// </summary>
    Task<IReadOnlyList<ExtractedFieldDto>> AnalyseAsync(
        Stream content, string fileName, CreateObjectType type, CancellationToken ct = default);
}

/// <summary>
/// Erkennt nichts und sagt das auch. Der eingebaute Stand, solange keine Analyse angebunden ist.
/// </summary>
/// <remarks>
/// Bewusst kein Platzhalter, der etwas erfindet: erfundene Werte in einem Formular, das
/// Vermögenszahlen speist, wären schlimmer als ein leeres Formular.
/// </remarks>
public sealed class NoPolicyDocumentAnalyzer : IPolicyDocumentAnalyzer
{
    public Task<IReadOnlyList<ExtractedFieldDto>> AnalyseAsync(
        Stream content, string fileName, CreateObjectType type, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExtractedFieldDto>>([]);
}
