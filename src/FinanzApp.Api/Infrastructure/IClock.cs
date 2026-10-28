namespace FinanzApp.Api.Infrastructure;

/// <summary>Liefert die aktuelle Zeit. Als Abhängigkeit statt <c>DateTime.Now</c>, damit
/// Auswertungen testbar bleiben und die Demodaten an einem festen Stichtag hängen können.</summary>
public interface IClock
{
    DateTime Now { get; }

    DateOnly Today => DateOnly.FromDateTime(Now);
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

/// <summary>Friert „heute“ auf einen konfigurierten Tag ein.</summary>
/// <remarks>
/// <para>Nur zum Vorführen der Beispieldaten: mit <c>Demo:Today</c> bleiben deren Monatssummen
/// und Budgets stimmig, egal wann die Anwendung gestartet wird. Ab Werk ist der Wert
/// <b>leer</b>.</para>
/// <para>Denn die Uhr gilt der ganzen Anwendung und nicht nur der Demo. Ein gesetzter Stichtag
/// stempelt auch jeden echten Beleg mit diesem Tag — und wo das Papier kein Dokumentdatum nennt,
/// steht er dort als Datum des Schreibens. Drei Statusreporte aus drei Jahren trugen so denselben
/// 23.08.2026.</para>
/// </remarks>
public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; } = now;
}
