using FinanzApp.Client.Services;
using Microsoft.AspNetCore.Components;

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
    }

    /// <summary>
    /// Holt die Daten beim Aufbau und erneut, sobald die Adresse eine andere ist.
    /// </summary>
    /// <remarks>
    /// <para>Blazor behält die Seite stehen, wenn sich nur der Routenparameter ändert: von
    /// <c>/police/1</c> auf <c>/police/2</c> wird dieselbe Instanz weiterverwendet. Wer nur beim
    /// Aufbau lädt, zeigt danach den Namen des zweiten Vertrags über den Zahlen des
    /// ersten — nachgemessen, nicht vermutet.</para>
    /// <para>Verglichen wird die volle Adresse samt Abfrageteil, und nur ein Unterschied löst
    /// aus. Ein <see cref="ComponentBase.StateHasChanged"/> aus der Seite heraus setzt keine
    /// Parameter und kommt hier nicht an; eine Schleife entsteht deshalb nicht.</para>
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (geladeneAdresse == Navigation.Uri)
        {
            return;
        }

        geladeneAdresse = Navigation.Uri;
        await ReloadAsync();
    }

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
        State.Changed -= OnAppStateChanged;
        Connection.RetryRequested -= ReloadAsync;
    }
}
