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

/// <summary>Friert „heute“ auf einen konfigurierten Tag ein. Nur für die Beispieldaten:
/// mit <c>Demo:Today</c> in der Konfiguration bleiben Monatssummen und Budgets stimmig,
/// egal wann die Anwendung gestartet wird.</summary>
public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; } = now;
}
