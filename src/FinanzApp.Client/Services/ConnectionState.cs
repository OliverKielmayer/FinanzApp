namespace FinanzApp.Client.Services;

/// <summary>
/// Was die Anwendung über ihre Verbindung und das Alter des Gezeigten weiß.
/// </summary>
/// <remarks>
/// <para>v5-Handoff, Abschnitt 7. Der Kern ist nicht der Verbindungszustand, sondern der
/// <see cref="LastSuccessAt"/>: „Die Frage ist nie ‚bin ich offline‘, sondern ‚wie alt ist,
/// was ich sehe‘.“ Ein Offline-Hinweis ohne Zeitangabe ist wertlos.</para>
/// <para>Der Zustand liegt hier und nicht in den Seiten, weil das Band über allen Screens steht
/// und den Zeitpunkt über einen Bereichswechsel hinweg behalten muss. Im Browser lebt Scoped so
/// lange wie die Anwendung.</para>
/// </remarks>
public sealed class ConnectionState(TimeProvider time)
{
    /// <summary>Der Browser meldet keine Netzverbindung.</summary>
    public bool IsOffline { get; private set; }

    /// <summary>
    /// Wann zuletzt etwas erfolgreich geladen wurde. <c>null</c>, solange nie etwas ankam.
    /// </summary>
    public DateTimeOffset? LastSuccessAt { get; private set; }

    /// <summary>Der letzte Abruf ist fehlgeschlagen.</summary>
    public bool SyncFailed { get; private set; }

    /// <summary>Ein angestoßener Abgleich läuft gerade.</summary>
    public bool Busy { get; private set; }

    /// <summary>Etwas am Zustand hat sich geändert — Band und Seiten zeichnen neu.</summary>
    public event Action? Changed;

    /// <summary>
    /// Das Band bittet die aktuelle Seite, noch einmal zu laden.
    /// </summary>
    /// <remarks>
    /// Es kennt die Seite nicht und soll sie nicht kennen. Die Seite meldet sich an, solange sie
    /// steht — damit trifft „Erneut versuchen“ immer das, was der Benutzer gerade ansieht.
    /// </remarks>
    public event Func<Task>? RetryRequested;

    public void ReportSuccess()
    {
        LastSuccessAt = time.GetLocalNow();
        SyncFailed = false;
        Busy = false;
        Raise();
    }

    public void ReportFailure()
    {
        SyncFailed = true;
        Busy = false;
        Raise();
    }

    public void SetOnline(bool online)
    {
        if (IsOffline == !online)
        {
            return;
        }

        IsOffline = !online;

        // Wieder online heißt noch nicht: wieder aktuell. Was gezeigt wird, ist so alt wie der
        // letzte erfolgreiche Abruf, und das bleibt so, bis einer gelingt.
        Raise();
    }

    /// <summary>Stößt den Abruf der aktuellen Seite an.</summary>
    public async Task RetryAsync()
    {
        if (Busy || RetryRequested is null)
        {
            return;
        }

        Busy = true;
        Raise();

        await RetryRequested.Invoke();
    }

    private void Raise() => Changed?.Invoke();
}
