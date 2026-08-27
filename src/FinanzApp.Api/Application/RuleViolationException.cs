namespace FinanzApp.Api.Application;

/// <summary>
/// Eine Eingabe verstößt gegen eine fachliche Regel. Die Meldung geht an den Benutzer.
/// </summary>
/// <remarks>
/// Nicht <see cref="ArgumentException"/>: die hängt an ihre Meldung den Parameternamen an
/// („… (Parameter 'name')“). Das ist eine Auskunft für den Aufrufer, nicht für den Menschen davor
/// — und genau so stand es zwischenzeitlich in der Oberfläche. Ein doppelter Kategoriename ist
/// auch kein Programmierfehler, sondern eine Entscheidung, die der Fachcode zurückweist.
/// </remarks>
public sealed class RuleViolationException(string message) : Exception(message);
