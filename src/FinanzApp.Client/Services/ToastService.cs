namespace FinanzApp.Client.Services;

/// <summary>
/// Kurze Rückmeldung am unteren Rand. Eine neue Meldung ersetzt die laufende und setzt die
/// Anzeigedauer zurück.
/// </summary>
public sealed class ToastService : IDisposable
{
    /// <summary>Anzeigedauer laut Design-Handoff.</summary>
    public const int DurationMilliseconds = 2600;

    private CancellationTokenSource? dismissal;

    public string? Message { get; private set; }

    public event Action? Changed;

    public void Show(string message)
    {
        Message = message;
        Changed?.Invoke();

        dismissal?.Cancel();
        dismissal?.Dispose();
        dismissal = new CancellationTokenSource();

        _ = DismissAsync(dismissal.Token);
    }

    private async Task DismissAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DurationMilliseconds, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Message = null;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        dismissal?.Cancel();
        dismissal?.Dispose();
        dismissal = null;
    }
}
