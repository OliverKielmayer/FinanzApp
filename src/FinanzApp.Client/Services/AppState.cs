using FinanzApp.Shared.Formatting;

namespace FinanzApp.Client.Services;

/// <summary>
/// Zustand, der über alle Screens hinweg gilt. Derzeit nur die Beträge-Maske: ein Schalter
/// im Kopf verdeckt alle Geldbeträge der Anwendung gleichzeitig. Prozentwerte bleiben sichtbar.
/// </summary>
public sealed class AppState
{
    public bool AmountsHidden { get; private set; }

    public event Action? Changed;

    public string ToggleLabel => AmountsHidden ? "Beträge zeigen" : "Beträge verbergen";

    public void ToggleAmounts()
    {
        AmountsHidden = !AmountsHidden;
        Changed?.Invoke();
    }

    /// <summary>Betrag mit zwei Nachkommastellen, maskiert wenn die Maske aktiv ist.</summary>
    public string Euro(decimal value, bool withPlusSign = false)
        => AmountsHidden ? GermanFormat.Masked : GermanFormat.Euro(value, withPlusSign);

    /// <summary>Betrag ohne Nachkommastellen, maskiert wenn die Maske aktiv ist.</summary>
    public string Euro0(decimal value, bool withPlusSign = false)
        => AmountsHidden ? GermanFormat.Masked : GermanFormat.EuroRounded(value, withPlusSign);
}
