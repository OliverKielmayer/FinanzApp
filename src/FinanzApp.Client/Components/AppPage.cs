using FinanzApp.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace FinanzApp.Client.Components;

/// <summary>
/// Basis der Screens. Hängt die üblichen Dienste ein und zeichnet die Seite neu, wenn die
/// Beträge-Maske umgelegt wird — der Schalter sitzt im Kopf, wirken muss er überall.
/// </summary>
public abstract class AppPage : ComponentBase, IDisposable
{
    [Inject] protected FinanzAppApi Api { get; set; } = default!;

    [Inject] protected AppState State { get; set; } = default!;

    [Inject] protected ToastService Toasts { get; set; } = default!;

    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [Inject] protected ConnectionState Connection { get; set; } = default!;

    /// <summary>
    /// Läuft, solange die Seite ihre Daten <b>zum ersten Mal</b> holt.
    /// </summary>
    /// <remarks>
    /// Nur dann treten die Platzhalterzeilen an die Stelle des Inhalts. Bei jedem weiteren
    /// Abruf bleibt stehen, was da ist — der Handoff verlangt ausdrücklich, dass vorhandene
    /// Daten nie durch eine leere Seite ersetzt werden.
    /// </remarks>
    protected bool Loading { get; private set; } = true;

    /// <summary>Ob diese Seite schon einmal etwas geladen hat.</summary>
    protected bool HasLoadedOnce { get; private set; }

    /// <summary>Meldung, wenn der Abruf fehlgeschlagen ist.</summary>
    protected string? Error { get; private set; }

    /// <summary>Die Adresse, für die die Daten dieser Seite geholt wurden.</summary>
    private string? geladeneAdresse;

    protected override void OnInitialized()
    {
        State.Changed += OnAppStateChanged;

        // „Erneut versuchen“ im Verbindungsband trifft immer die Seite, die gerade steht.
        Connection.RetryRequested += ReloadAsync;

        Navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>Holt die Daten beim Aufbau.</summary>
    protected override Task OnInitializedAsync() => ReloadAsync();

    /// <summary>
    /// Lädt erneut, sobald die Adresse eine andere ist.
    /// </summary>
    /// <remarks>
    /// <para>Blazor behält die Seite stehen, wenn sich nur der Routenparameter ändert: von
    /// <c>/police/1</c> auf <c>/police/2</c> wird dieselbe Instanz weiterverwendet. Wer nur beim
    /// Aufbau lädt, zeigt danach den Namen des zweiten Vertrags über den Zahlen des
    /// ersten — nachgemessen, nicht vermutet.</para>
    /// <para><b>Versiegelt.</b> Das erste Laden hing schon einmal hier, und ein Baustein, der
    /// diese Methode überschrieb, ohne die Basis zu rufen, blieb auf „Wird geladen …“ stehen —
    /// die Dokumentvorschau. Wer auf geänderte Eigenschaften reagieren muss, überschreibt
    /// <see cref="OnParametersChangedAsync"/>; das Vergessen ist damit ein Übersetzungsfehler
    /// und keine leere Spalte.</para>
    /// <para>Verglichen wird die volle Adresse samt Abfrageteil, und nur ein Unterschied löst
    /// aus. Ein <see cref="ComponentBase.StateHasChanged"/> aus der Seite heraus setzt keine
    /// Parameter und kommt hier nicht an; eine Schleife entsteht deshalb nicht.</para>
    /// </remarks>
    protected sealed override async Task OnParametersSetAsync()
    {
        // Beim ersten Durchlauf hat OnInitializedAsync schon geladen: nur merken, nicht laden.
        if (geladeneAdresse is null)
        {
            geladeneAdresse = Navigation.Uri;
        }
        else
        {
            await NachAdresswechselAsync();
        }

        await OnParametersChangedAsync();
    }

    /// <summary>
    /// Der zweite Weg zum Nachladen: die Adresse hat sich geändert, die Parameter nicht.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Routen auf einer Komponente — <c>/vorsorge</c> und <c>/absicherung</c>, oder
    /// <c>/dokumente</c> und <c>/dokumente/5</c> — tragen keinen Routenparameter, der sich
    /// unterscheidet. Blazor sieht im Baum keinen Unterschied, setzt keine Parameter neu und ruft
    /// <see cref="OnParametersSetAsync"/> nicht: die Absicherungsliste zeigte die Vorsorgezahlen.
    /// Nur dieser Horcher kommt in beiden Fällen an.</para>
    /// <para>Über <see cref="ComponentBase.InvokeAsync(Func{Task})"/>, damit die Arbeit erst nach
    /// dem laufenden Zeichenlauf beginnt. Wer wegnavigiert, ist dann schon entsorgt — und holt
    /// keine Daten mehr für eine Seite, die niemand mehr sieht.</para>
    /// </remarks>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        => _ = InvokeAsync(NachAdresswechselAsync);

    private async Task NachAdresswechselAsync()
    {
        if (entsorgt || geladeneAdresse is null || geladeneAdresse == Navigation.Uri)
        {
            return;
        }

        geladeneAdresse = Navigation.Uri;

        if (ReloadOnNavigation)
        {
            await ReloadAsync();
        }
    }

    /// <summary>
    /// Ob eine geänderte Adresse die Seite neu laden soll.
    /// </summary>
    /// <remarks>
    /// Standard ist ja: bei einem Detailschirm gehört die Nummer in der Adresse zu den Daten.
    /// Wo die Adresse nur die zweite Spalte auswählt — Liste und Vorschau auf einer Seite —,
    /// wäre das ein Abruf ohne Ergebnis, und die Seite setzt es auf nein.
    /// </remarks>
    protected virtual bool ReloadOnNavigation => true;

    /// <summary>Haken für Bausteine, die auf eine geänderte Eigenschaft reagieren müssen.</summary>
    protected virtual Task OnParametersChangedAsync() => Task.CompletedTask;

    /// <summary>Holt die Daten der Seite. Wird beim Aufbau und beim erneuten Versuch gerufen.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Führt <see cref="LoadAsync"/> aus und hält Lade- und Fehlerzustand nach.</summary>
    protected async Task ReloadAsync()
    {
        Loading = !HasLoadedOnce;
        Error = null;
        StateHasChanged();

        try
        {
            await LoadAsync();

            HasLoadedOnce = true;
            Connection.ReportSuccess();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
            Connection.ReportFailure();
        }
        finally
        {
            Loading = false;
            StateHasChanged();
        }
    }

    /// <summary>Führt eine schreibende Aktion aus und meldet einen Fehlschlag als Toast.</summary>
    protected async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            Toasts.Show(ex.Message);
        }
    }

    private void OnAppStateChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        entsorgt = true;

        State.Changed -= OnAppStateChanged;
        Connection.RetryRequested -= ReloadAsync;
        Navigation.LocationChanged -= OnLocationChanged;
    }

    private bool entsorgt;
}
