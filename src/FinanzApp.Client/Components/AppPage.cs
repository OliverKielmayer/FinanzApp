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

    /// <summary>Läuft, solange die Seite ihre Daten holt.</summary>
    protected bool Loading { get; private set; } = true;

    /// <summary>Meldung, wenn der Abruf fehlgeschlagen ist.</summary>
    protected string? Error { get; private set; }

    protected override void OnInitialized() => State.Changed += OnAppStateChanged;

    protected override Task OnInitializedAsync() => ReloadAsync();

    /// <summary>Holt die Daten der Seite. Wird beim Aufbau und beim erneuten Versuch gerufen.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Führt <see cref="LoadAsync"/> aus und hält Lade- und Fehlerzustand nach.</summary>
    protected async Task ReloadAsync()
    {
        Loading = true;
        Error = null;
        StateHasChanged();

        try
        {
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
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

    public virtual void Dispose() => State.Changed -= OnAppStateChanged;
}
