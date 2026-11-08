using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using FinanzApp.Api.Data.Entities;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Was eine Bank über den Stand eines Kontos sagt.</summary>
/// <param name="Balance">Der gemeldete Saldo.</param>
/// <param name="RetrievedAt">Wann gefragt wurde.</param>
public readonly record struct BankBalanceReading(decimal Balance, DateTime RetrievedAt);

/// <summary>
/// Der Abruf bei der Bank.
/// </summary>
/// <remarks>
/// <para>Hinter einer Schnittstelle, weil sich dahinter nichts prüfen lässt: Ein Dialog mit
/// einer echten Bank braucht eine echte PIN und lässt sich nicht nachstellen. Alles davor und
/// danach — ob die Angaben vollständig sind, wie die Differenz entsteht, was bei einem Fehler
/// geschieht — ist damit prüfbar, und nur der Dialog selbst bleibt es nicht.</para>
/// <para>Dieselbe Bauform wie beim Kursabruf: Die Anwendung hängt an der Schnittstelle, nicht
/// an der Bibliothek dahinter.</para>
/// </remarks>
public interface IBankBalanceSource
{
    Task<BankBalanceReading> FetchAsync(Account account, string pin, CancellationToken ct = default);
}

/// <summary>
/// Wenn der Abruf nicht geht — und warum.
/// </summary>
/// <remarks>
/// Eigene Ausnahme, damit die Meldung der Bank unverändert bis zur Oberfläche kommt. „Abruf
/// fehlgeschlagen“ hilft niemandem; „PIN gesperrt nach drei Fehlversuchen“ schon, und das weiss
/// nur die Bank.
/// </remarks>
public sealed class BankAccessException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Was zu sagen ist, wenn der Dialog abbricht, bevor die Bank geantwortet hat.
/// </summary>
/// <remarks>
/// <para>Eine Bank, die nicht antwortet, ist kein Sonderfall: kein Netz, Wartungsfenster,
/// ein Tippfehler in der FinTS-Adresse. Ohne diese Übersetzung bliebe davon eine Ausnahme der
/// Bibliothek übrig, die niemandem sagt, was zu tun ist.</para>
/// <para><b>Hier und nicht in <see cref="FinTsBalanceSource"/>, weil sich genau das prüfen
/// lässt.</b> Der Satz im Fehlerfall ist das Einzige, was dann bleibt.</para>
/// <para><b>Aus der Ausnahme wird nur genommen, was das Netz sagt.</b> Die Meldungen von
/// <see cref="SocketException"/> und seinesgleichen beschreiben die Leitung — Rechnername,
/// Port, Zeitüberschreitung — und tragen nie den Nachrichtenstrom und damit nie die PIN. Was
/// die Bibliothek selbst in eine Ausnahme schreibt, bleibt draussen: Dort stünde im Zweifel
/// die Nachricht, die gerade verschickt wurde.</para>
/// </remarks>
public static class BankAccessFailure
{
    /// <summary>Der Satz, der im Fehlerfall am Konto steht.</summary>
    /// <param name="fehler">Was beim Dialog herauskam.</param>
    /// <param name="url">Die FinTS-Adresse am Konto, soweit eine da ist.</param>
    public static string Describe(Exception fehler, string? url)
    {
        // Das abgelehnte TAN-Verfahren zuerst: Es ist die einzige Zurückweisung, aus der sich
        // sagen lässt, was zu tun ist — und sie kommt als dieselbe Ausnahmeart wie ein
        // Verbindungsabbruch.
        if (TanVerfahren(fehler) is { } verfahren)
        {
            return verfahren;
        }

        if (Netzgrund(fehler) is not { } grund)
        {
            // Kein Netzfehler und nichts Erkanntes. Dann ist weder gesagt, dass die Bank
            // unerreichbar war, noch dass sie geantwortet hat — also wird beides nicht
            // behauptet. Genau das stand hier einmal falsch: „bevor die Bank geantwortet hat“
            // galt auch dann, wenn sie geantwortet und den Dialog zurückgewiesen hatte, und
            // schickte die Suche in die falsche Richtung.
            return $"Der Abruf ist abgebrochen ({fehler.GetType().Name}). Das ist kein Fehler "
                   + "der PIN. Ob und was die Bank geantwortet hat, steht im Protokoll der "
                   + "Anwendung — den Dialog selbst zeigt es, sobald die Stufe für „libfintx“ "
                   + "in appsettings.json angehoben ist.";
        }

        var wo = string.IsNullOrWhiteSpace(url) ? "Die Bank" : $"Die Bank unter „{url.Trim()}“";

        return $"{wo} war nicht zu erreichen: {grund}. Das ist kein Fehler der PIN, sondern "
               + "der Verbindung — nachzusehen ist die FinTS-Adresse am Konto und ob dieser "
               + "Rechner überhaupt ins Netz kommt.";
    }

    /// <summary>Das zurückgewiesene Verfahren und die, die gingen.</summary>
    private static readonly Regex Abgelehnt =
        new(@"Tan-Mode\s+(\d{3})\s+detected",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Erlaubt =
        new(@"allowed modes:\s*(\d{3}(?:\s*,\s*\d{3})*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Sagt, wenn die Bank das TAN-Verfahren am Konto nicht anbietet.
    /// </summary>
    /// <remarks>
    /// <para><b>Der einzige Satz, der aus einem fremden Text entsteht — und doch keinen
    /// durchreicht.</b> Genommen werden allein die Ziffernfolgen, die zwei enge Muster
    /// hergeben: die abgelehnte Nummer und die erlaubten. Alles andere am Text bleibt
    /// draussen; daraus wird hier ein eigener Satz gebaut. Ein Verfahren hat drei Ziffern,
    /// eine PIN steht in keinem dieser Muster.</para>
    /// <para><b>Warum es diesen Umweg überhaupt gibt.</b> Die Bibliothek wirft die
    /// Zurückweisung als <see cref="InvalidOperationException"/> — dieselbe Art, die auch ein
    /// Verbindungsabbruch trägt. Ohne diesen Blick stünde am Bildschirm, der Abruf sei
    /// gescheitert, und die Zahl, die ihn flottmacht, läge im Protokoll und würde übersehen.
    /// Am echten Konto des Inhabers am 08.11.2026 war es genau dieser Fall.</para>
    /// <para>Gesucht wird über die ganze Kette: Die Bibliothek packt die eigentliche Meldung
    /// in eine innere Ausnahme.</para>
    /// </remarks>
    private static string? TanVerfahren(Exception fehler)
    {
        for (var e = fehler; e is not null; e = e.InnerException)
        {
            if (e.Message is not { Length: > 0 } text || Erlaubt.Match(text) is not { Success: true } gut)
            {
                continue;
            }

            var moeglich = string.Join(", ", gut.Groups[1].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

            var genannt = Abgelehnt.Match(text) is { Success: true } schlecht
                ? $"Das TAN-Verfahren {schlecht.Groups[1].Value} am Konto wird von dieser Bank "
                  + "nicht angeboten."
                : "Das TAN-Verfahren am Konto wird von dieser Bank nicht angeboten.";

            return $"{genannt} Erlaubt {(moeglich.Contains(',') ? "sind" : "ist")}: {moeglich}. "
                   + "Die Nummer wird am Konto eingetragen; das ist kein Fehler der PIN.";
        }

        return null;
    }

    /// <summary>
    /// Die Kette der Ausnahmearten, von aussen nach innen — und nichts sonst.
    /// </summary>
    /// <remarks>
    /// Fürs Protokoll gedacht, wo sonst die Ausnahme selbst stünde. Ihre Texte bleiben
    /// draussen: Das ist derselbe Grund, aus dem der Nachrichtenstrom der Bibliothek nicht
    /// protokolliert wird. Die Arten und der Stapel reichen, um die Stelle zu finden.
    /// </remarks>
    public static string TypeChain(Exception fehler)
    {
        var arten = new List<string>();

        for (var e = fehler; e is not null; e = e.InnerException)
        {
            arten.Add(e.GetType().Name);
        }

        return string.Join(" <- ", arten);
    }

    /// <summary>Die äusserste Ursache, die vom Netz spricht.</summary>
    /// <remarks>
    /// Von aussen nach innen, nicht umgekehrt: Die äussere Netzausnahme nennt in der Regel
    /// schon Rechnernamen und Port, die innerste nur noch den Fehlercode.
    /// </remarks>
    private static string? Netzgrund(Exception fehler)
    {
        for (var e = fehler; e is not null; e = e.InnerException)
        {
            if (e is SocketException or HttpRequestException or WebException)
            {
                return e.Message.Trim().TrimEnd('.');
            }
        }

        return null;
    }
}
