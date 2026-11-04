namespace FinanzApp.Client.Services;

/// <summary>
/// Was die Einblendung über den Ausgang sagt.
/// </summary>
/// <remarks>
/// <para><b>Die Voreinstellung behauptet nichts.</b> Lange trug jede Einblendung den Stempel
/// „Gespeichert“, auch die über einen fehlgeschlagenen Bankabruf — eine Zusage, die daneben
/// stand und das Gegenteil dessen sagte, was gerade passiert war. Wer eine neue Stelle
/// schreibt und nichts angibt, bekommt deshalb keinen Stempel: Ein fehlender sagt zu wenig,
/// ein falscher das Falsche.</para>
/// <para><see cref="Saved"/> ist die Zusage, dass es in der Datenbank steht — auch bei einer
/// Löschung, denn auch die ist geschrieben.</para>
/// </remarks>
public enum ToastKind
{
    /// <summary>Etwas ist passiert, aber nichts wurde geschrieben. Ohne Stempel.</summary>
    Notice,

    /// <summary>Es steht in der Datenbank.</summary>
    Saved,

    /// <summary>Es hat nicht geklappt. Ohne Stempel, und für Vorleseprogramme dringend.</summary>
    Problem,
}

/// <summary>
/// Kurze Rückmeldung am unteren Rand. Eine neue Meldung ersetzt die laufende und setzt die
/// Anzeigedauer zurück.
/// </summary>
/// <remarks>
/// <b>Für einen Satz, nach dem jemand etwas tun soll, ist sie der falsche Ort.</b> Nach
/// zweieinhalb Sekunden ist sie weg, und wer gerade woanders hinsieht, hat sie nie gelesen.
/// Was gebraucht wird, während man handelt, gehört dorthin, wo gehandelt wird.
/// </remarks>
public sealed class ToastService : IDisposable
{
    /// <summary>Anzeigedauer laut Design-Handoff.</summary>
    public const int DurationMilliseconds = 2600;

    private CancellationTokenSource? dismissal;

    public string? Message { get; private set; }

    public ToastKind Kind { get; private set; }

    public event Action? Changed;

    public void Show(string message, ToastKind kind = ToastKind.Notice)
    {
        Message = message;
        Kind = kind;
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
