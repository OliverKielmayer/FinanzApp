using System.Net;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Ordnerdienst;

/// <summary>
/// Was aus einem Übergabeversuch geworden ist.
/// </summary>
/// <remarks>
/// <para>Die Unterscheidung ist der Kern des ganzen Dienstes. „Fehlgeschlagen“ als einzige
/// Fehlermeldung wäre unbrauchbar: bei <see cref="Rejected"/> hilft kein zweiter Versuch, bei
/// <see cref="Deferred"/> hilft nur ein zweiter Versuch, und bei <see cref="Blocked"/> muss
/// entweder ein Mensch etwas einstellen oder ein Server zurückkommen.</para>
/// <para>Die Trennlinie liegt bei der Frage: <b>ist das ein Problem dieser Datei oder eins der
/// Verbindung?</b> Ohne sie legte ein Wartungsfenster von fünf Minuten den ganzen Eingang als
/// „fehlgeschlagen“ beiseite, obwohl an keiner einzigen Datei etwas fehlte.</para>
/// </remarks>
public enum HandoverStatus
{
    /// <summary>Angekommen. Die FinanzApp hat die Datei abgelegt.</summary>
    Handed,

    /// <summary>Dauerhaft abgelehnt — falsche Dateiart, zu groß, leer. Ein Fall für den Menschen.</summary>
    Rejected,

    /// <summary>An dieser Datei ist etwas vorübergehend schiefgegangen. Sie bleibt liegen.</summary>
    Deferred,

    /// <summary>
    /// Nicht die Datei, sondern der Weg dorthin: Server nicht erreichbar, Zugang oder Rechte
    /// stimmen nicht. Der Durchgang endet hier, und keine Datei verbraucht einen Versuch.
    /// </summary>
    Blocked,
}

/// <summary>Ergebnis einer Übergabe samt Klartext für das Protokoll.</summary>
public sealed record HandoverResult(HandoverStatus Status, string Message)
{
    /// <summary>
    /// Was die FinanzApp mit der Datei gemacht hat — gesetzt bei <see cref="HandoverStatus.Handed"/>.
    /// </summary>
    public ScanIntakeResultDto? Intake { get; init; }
}

/// <summary>
/// Wie ein Fehlschlag einzuordnen ist: Problem der Datei oder Problem der Verbindung?
/// </summary>
/// <remarks>
/// Eigen und nicht im <see cref="IntakeClient"/>, weil es die einzige Entscheidung des
/// Dienstes ist, die man ohne Server nachrechnen kann — und die einzige, deren Fehler man
/// erst Wochen später bemerkt: an einem Eingang, der stillsteht, oder an Dateien, die
/// beiseiteliegen, obwohl ihnen nichts fehlt.
/// </remarks>
public static class Handover
{
    /// <summary>
    /// Ordnet einen Übertragungsfehler zu: gar nicht erst hingekommen, oder unterwegs gestorben?
    /// </summary>
    /// <remarks>
    /// <para>Der Unterschied ist im Betrieb aufgefallen und nicht am Schreibtisch. Eine Datei
    /// oberhalb der Rumpfgrenze des Servers bekommt keine saubere Ablehnung: er bricht die
    /// Verbindung ab, <em>während</em> gesendet wird, und das kam als „keine Verbindung“ an.
    /// Damit beendete eine einzige zu große Datei jeden Durchgang, ohne je einen Versuch zu
    /// verbrauchen — der Eingang stand still, und im Protokoll stand eine Unwahrheit.</para>
    /// <para><see cref="HttpRequestException.HttpRequestError"/> trennt beides: die vier Fälle
    /// unten entstehen, bevor eine Anfrage überhaupt steht, und gelten für jede Datei gleich.
    /// Alles andere ist dieser Anfrage passiert und wird ihr auch zugerechnet.</para>
    /// </remarks>
    public static HandoverResult Failure(HttpRequestException ex)
        => ex.HttpRequestError is HttpRequestError.ConnectionError
                                  or HttpRequestError.NameResolutionError
                                  or HttpRequestError.SecureConnectionError
                                  or HttpRequestError.ProxyTunnelError
            ? new HandoverResult(HandoverStatus.Blocked, "Keine Verbindung: " + ex.Message)
            : new HandoverResult(
                HandoverStatus.Deferred, "Die Übertragung ist abgebrochen: " + ex.Message);

    /// <summary>
    /// Ordnet den Statuscode einem der Wege zu.
    /// </summary>
    /// <remarks>
    /// <para>Eine abgelehnte Datei ändert sich nie von selbst — sie wandert beiseite. Ein
    /// überlasteter Server erholt sich von selbst — die Datei bleibt liegen. Ein falscher Zugang
    /// und ein Server in Wartung sind beides keine Aussage über die Datei: dort endet der
    /// Durchgang.</para>
    /// <para>429 ist eine Bitte zu warten, keine Aussage über die Datei. 503 heißt: der Server
    /// ist gerade nicht da — die nächste Datei träfe es genauso.</para>
    /// </remarks>
    public static HandoverStatus StatusFor(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HandoverStatus.Blocked,
        HttpStatusCode.ServiceUnavailable => HandoverStatus.Blocked,
        HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => HandoverStatus.Deferred,
        _ when (int)code >= 500 => HandoverStatus.Deferred,
        _ when (int)code >= 400 => HandoverStatus.Rejected,
        _ => HandoverStatus.Deferred,
    };
}
