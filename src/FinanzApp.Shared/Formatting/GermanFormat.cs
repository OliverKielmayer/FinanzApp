using System.Globalization;

namespace FinanzApp.Shared.Formatting;

/// <summary>
/// Deutsche Darstellung von Geldbeträgen, Prozentwerten und Daten.
/// </summary>
/// <remarks>
/// Die Zahlenformate sind hier explizit definiert statt über <c>CultureInfo("de-DE")</c> aufgelöst.
/// Damit liefert der WebAssembly-Client dieselbe Ausgabe wie der Server, unabhängig davon, welche
/// ICU-Daten der Browser lädt oder welche Sprache im Betriebssystem eingestellt ist.
/// </remarks>
public static class GermanFormat
{
    /// <summary>Typografisches Minus (U+2212), nicht der Bindestrich.</summary>
    public const string Minus = "\u2212";

    /// <summary>Geschütztes Leerzeichen zwischen Zahl und Einheit.</summary>
    public const string Nbsp = "\u00a0";

    /// <summary>Platzhalter, wenn die Beträge-Maske aktiv ist.</summary>
    public const string Masked = "••••••";

    private static readonly NumberFormatInfo Numbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberNegativePattern = 1,
    };

    private static readonly string[] MonthsShort =
        ["JAN", "FEB", "MRZ", "APR", "MAI", "JUN", "JUL", "AUG", "SEP", "OKT", "NOV", "DEZ"];

    /// <summary>Betrag mit zwei Nachkommastellen: <c>1.234,56 €</c>.</summary>
    public static string Euro(decimal value, bool withPlusSign = false)
        => Sign(value, withPlusSign) + Math.Abs(value).ToString("N2", Numbers) + Nbsp + "€";

    /// <summary>Betrag ohne Nachkommastellen: <c>1.234 €</c>.</summary>
    public static string EuroRounded(decimal value, bool withPlusSign = false)
        => Sign(value, withPlusSign) + Math.Abs(value).ToString("N0", Numbers) + Nbsp + "€";

    /// <summary>Reine Zahl mit zwei Nachkommastellen, ohne Währung: <c>1.234,56</c>.</summary>
    public static string Decimal2(decimal value) => value.ToString("N2", Numbers);

    /// <summary>Stückzahl: ganze Zahlen ohne Nachkommastellen, Bruchteile mit so vielen wie nötig.</summary>
    public static string Quantity(decimal value)
        => value == Math.Truncate(value)
            ? value.ToString("N0", Numbers)
            : value.ToString("#,##0.####", Numbers);

    /// <summary>Prozentwert: <c>31 %</c> bzw. <c>+11,4 %</c>.</summary>
    public static string Percent(decimal value, int decimals = 0, bool withPlusSign = false)
        => Sign(value, withPlusSign) + Math.Abs(value).ToString("N" + decimals, Numbers) + Nbsp + "%";

    /// <summary>Datum <c>22.08.2026</c>.</summary>
    public static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Kurzdatum <c>22.08</c> für die Buchungsliste.</summary>
    public static string DateShort(DateOnly value) => value.ToString("dd.MM", CultureInfo.InvariantCulture);

    /// <summary>Zeitstempel <c>22.08.2026, 17:35</c> für Kursstände.</summary>
    public static string DateTime(DateTime value) => value.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Monat <c>09/2026</c> für den Tilgungsplan.</summary>
    public static string MonthYear(DateOnly value) => value.ToString("MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Achsenbeschriftung <c>AUG 26</c>.</summary>
    public static string MonthAxis(DateOnly value)
        => MonthsShort[value.Month - 1] + " " + (value.Year % 100).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>Voller Monatsname für Überschriften wie „Budgets August“.</summary>
    public static string MonthName(int month) => month switch
    {
        1 => "Januar", 2 => "Februar", 3 => "März", 4 => "April",
        5 => "Mai", 6 => "Juni", 7 => "Juli", 8 => "August",
        9 => "September", 10 => "Oktober", 11 => "November", 12 => "Dezember",
        _ => throw new ArgumentOutOfRangeException(nameof(month)),
    };

    private static string Sign(decimal value, bool withPlusSign)
        => value < 0 ? Minus : withPlusSign ? "+" : string.Empty;
}
