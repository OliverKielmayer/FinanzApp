namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Der Weg zur FinanzApp, aus der Sicht des Durchgangs.
/// </summary>
/// <remarks>
/// <para>Eine Schnittstelle für genau eine Umsetzung — und trotzdem berechtigt: der Durchgang
/// entscheidet anhand der Antwort, ob eine Datei liegen bleibt, beiseitewandert oder der ganze
/// Durchgang endet. Diese Entscheidungen sind der Kern des Dienstes, und ohne eine einsetzbare
/// Gegenstelle ließen sie sich nur mit einem Server prüfen, der auf Kommando 401 sagt und danach
/// 200. So einen Server gibt es nicht.</para>
/// <para>Der <see cref="IntakeClient"/> selbst wird gegen einen eigenen Übertragungsweg geprüft
/// und nicht hierüber.</para>
/// </remarks>
public interface IIntakeClient
{
    /// <summary>Reicht eine Datei an die FinanzApp weiter.</summary>
    Task<HandoverResult> HandOverAsync(string path, CancellationToken ct);
}
