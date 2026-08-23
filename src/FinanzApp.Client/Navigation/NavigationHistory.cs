using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace FinanzApp.Client.Navigation;

/// <summary>
/// Merkt sich den zuletzt besuchten Screen, damit Detailseiten einen verlässlichen Zurück-Weg
/// haben. Der Browser-Verlauf allein reicht nicht: wer per Link direkt auf einer Detailseite
/// landet, würde damit die Anwendung verlassen.
/// </summary>
public sealed class NavigationHistory : IDisposable
{
    private readonly NavigationManager navigation;
    private string? previous;
    private string current;

    public NavigationHistory(NavigationManager navigation)
    {
        this.navigation = navigation;
        current = Relative(navigation.Uri);
        navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>Wohin ein Zurück führt. Ohne vorherigen Screen zum Dashboard.</summary>
    public string BackTarget => previous ?? "/";

    public void GoBack() => navigation.NavigateTo(BackTarget);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var next = Relative(e.Location);
        if (!string.Equals(next, current, StringComparison.OrdinalIgnoreCase))
        {
            previous = current;
            current = next;
        }
    }

    private string Relative(string uri) => "/" + navigation.ToBaseRelativePath(uri);

    public void Dispose() => navigation.LocationChanged -= OnLocationChanged;
}
