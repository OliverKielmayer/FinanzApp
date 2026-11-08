using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanzApp.Api.Application;

/// <summary>
/// Wie ein Beleg heißt, wenn man ihn einem Menschen hinstellt.
/// </summary>
/// <remarks>
/// <para><b>Der Titel ist nicht immer der Name.</b> Ein Scanner nennt jede Seite
/// „Scannen0003“, und dieser Titel landet unverändert am Beleg. Der <em>Dateiname</em> dagegen
/// wird beim Einliefern und beim Zuordnen von der Anwendung gebaut und weiß die Art, das Objekt
/// und das Datum: <c>Arztrechnung_DIAK_Klinikum_2026-04-22.pdf</c>. Steht der Beleg dann in
/// einer Liste, liest dort „Zu erledigen: Scannen0003“ — und niemand kann sagen, worum es geht,
/// ohne das Papier zu öffnen.</para>
/// <para><b>Gelesen, nicht geschrieben.</b> Der gespeicherte Titel bleibt, wie der Scanner ihn
/// vergeben hat. Ihn zu überschreiben hieße, eine Angabe des Benutzers wegzuwerfen, die er
/// später vielleicht selbst pflegt — und <see cref="Data.Entities.Document.HasGeneratedName"/>
/// spricht über den Dateinamen, nicht über den Titel. Hier wird nur hergeleitet, was angezeigt
/// wird.</para>
/// <para><b>Das Datum gehört dazu.</b> Zwei Arztrechnungen desselben Hauses unterscheiden sich
/// im Dateinamen allein durch ihr Datum. Ohne es stünden in der Vorgangsliste zwei Zeilen, die
/// Zeichen für Zeichen gleich heißen.</para>
/// </remarks>
public static class BelegName
{
    /// <summary>
    /// Namen, die ein Gerät vergibt und kein Mensch.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Formen, beide an echten Scans gemessen: <c>Scannen0003</c> — Wort und
    /// laufende Nummer — und <c>Scan_20260820_0001</c> — Wort, Datum, laufende Nummer. Die
    /// zweite braucht eine eigene Gruppe, weil zwischen den Ziffernblöcken wieder ein Trenner
    /// steht.</para>
    /// <para>Bewusst verankert mit <c>^</c> und <c>$</c>: „Scan der Police“ ist ein Titel, den
    /// jemand geschrieben hat, und bleibt stehen.</para>
    /// </remarks>
    public static readonly Regex Geraetename =
        new(@"^(scannen|scan|bild|image|img|dokument|document|doc|pdf|datei|file)([ _-]*\d+)*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Ein angehängtes Datum im Dateinamen, so wie die Ablage es schreibt.</summary>
    private static readonly Regex NameMitDatum =
        new(@"^(?<stamm>.*?)[ _-]*(?<jahr>\d{4})-(?<monat>\d{2})-(?<tag>\d{2})$",
            RegexOptions.CultureInvariant);

    private static readonly Regex Mehrfach = new(@"\s{2,}", RegexOptions.CultureInvariant);

    /// <summary>Ob dieser Titel von einem Gerät stammt und nichts aussagt.</summary>
    public static bool IstGeraetename(string? titel)
        => titel is { Length: > 0 } && Geraetename.IsMatch(titel.Trim());

    /// <summary>
    /// Der Name, unter dem dieser Beleg einem Menschen vorgestellt wird.
    /// </summary>
    /// <param name="titel">Der gespeicherte Titel.</param>
    /// <param name="dateiname">Der Dateiname, mit Erweiterung.</param>
    /// <param name="dateinameErzeugt">
    /// Ob der Dateiname von der Anwendung stammt. Ist er es nicht, trägt er denselben
    /// Gerätenamen wie der Titel, und der Umweg gewänne nichts.
    /// </param>
    /// <param name="belegdatum">Fällt die Ablage ohne Datum aus, hilft das Datum am Beleg.</param>
    public static string Fuer(
        string? titel, string? dateiname, bool dateinameErzeugt, DateOnly? belegdatum)
    {
        var gepflegt = titel?.Trim() ?? string.Empty;

        if (!dateinameErzeugt || !IstGeraetename(gepflegt))
        {
            return gepflegt;
        }

        var stamm = Path.GetFileNameWithoutExtension(dateiname ?? string.Empty).Trim();
        if (stamm.Length == 0 || IstGeraetename(stamm))
        {
            return gepflegt;
        }

        var datum = belegdatum;
        var treffer = NameMitDatum.Match(stamm);

        if (treffer.Success
            && int.TryParse(treffer.Groups["jahr"].Value, out var jahr)
            && int.TryParse(treffer.Groups["monat"].Value, out var monat)
            && int.TryParse(treffer.Groups["tag"].Value, out var tag)
            && monat is >= 1 and <= 12 && tag is >= 1 and <= 31)
        {
            // Das Datum aus dem Namen schlägt das am Beleg: Es ist dasselbe, und wo es das
            // einmal nicht ist, steht der Name näher an dem, was der Benutzer sieht.
            try
            {
                datum = new DateOnly(jahr, monat, tag);
                stamm = treffer.Groups["stamm"].Value;
            }
            catch (ArgumentOutOfRangeException)
            {
                // 2026-02-31 gibt es nicht. Dann bleibt der Stamm, wie er ist.
            }
        }

        // Nur der Unterstrich wird zum Leerzeichen. Der Bindestrich bleibt: Die Ablage trennt
        // ihre Felder mit Unterstrichen, und was dazwischen einen Bindestrich trägt, trug ihn
        // schon vorher — „Buche-Regalbrett“ ist ein Wort und keine zwei.
        var lesbar = Mehrfach.Replace(stamm.Replace('_', ' '), " ").Trim();

        if (lesbar.Length == 0)
        {
            return gepflegt;
        }

        return datum is { } wann
            ? $"{lesbar} · {wann.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : lesbar;
    }
}
