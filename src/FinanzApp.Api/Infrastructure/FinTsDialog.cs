using FinanzApp.Api.Data.Entities;
using libfintx.FinTS;
using libfintx.FinTS.Data;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Was jeder Dialog mit einer Bank braucht, unabhängig davon, wonach gefragt wird.
/// </summary>
/// <remarks>
/// <para>Hier steht, was Saldo und Umsätze teilen: wie der Zugang gebaut wird, wie aus den
/// Rückmeldungen der Bank ein Satz wird, und was geschieht, wenn sie eine TAN verlangt.</para>
/// <para>Herausgezogen, als der zweite Abruf dazukam. Beide Wege zweimal zu pflegen hieße, sie
/// irgendwann nur noch einmal zu pflegen — und dann unterscheidet sich der Umsatzabruf vom
/// Saldo in Kleinigkeiten, die niemand beabsichtigt hat.</para>
/// </remarks>
internal static class FinTsDialog
{
    /// <summary>
    /// Was die Bibliothek der Bank über Konto und Zugang sagt.
    /// </summary>
    /// <remarks>
    /// Alles daraus steht am Konto; nichts wird erfunden. Die Kontonummer ist die eine
    /// Ausnahme, und auch sie wird nicht geraten, sondern aus der deutschen IBAN gelesen, in der
    /// sie wörtlich enthalten ist.
    /// </remarks>
    public static ConnectionDetails Verbindung(Account account, string pin) => new()
    {
        Blz = int.Parse(account.Blz!),
        Url = account.FinTsUrl,
        UserId = account.FinTsUserId,
        Pin = pin,
        Iban = account.Iban,

        // Die Bankleitzahl spricht die Bank an, die BIC gehört zum Konto: Eine Saldenabfrage
        // nach FinTS 3.0 benennt es als Kontoverbindung international, und das ist IBAN und
        // BIC. Fehlt sie, sendet die Bibliothek „DE…:“ mit leerem zweiten Feld, und die Bank
        // antwortet mit einem fehlenden Mussfeld — ohne zu sagen, welchem.
        //
        // Und sie muss die richtige Länge haben: Acht oder elf Zeichen, nie zwölf. Ein zwölftes
        // sprengt das Feld, und die Bank antwortet mit „Unbekannter Aufbau“ — ein Satz, der von
        // der BIC kein Wort sagt. Geprüft wird das in BankAccessReadiness, bevor es hierher
        // kommt.
        Bic = account.Bic,
        Account = KontonummerAusIban(account.Iban),
        FinTSVersion = FinTsVersion.v300,
    };

    /// <summary>
    /// Die Kontonummer aus einer deutschen IBAN.
    /// </summary>
    /// <remarks>
    /// Eine deutsche IBAN ist aufgebaut aus DE, zwei Prüfziffern, acht Stellen Bankleitzahl und
    /// zehn Stellen Kontonummer — die Nummer steht also wörtlich darin und wird nicht abgeleitet.
    /// Bei jeder anderen IBAN gilt das nicht, und dann bleibt das Feld leer: Die Bank findet das
    /// Konto auch über die IBAN selbst.
    /// </remarks>
    public static string? KontonummerAusIban(string? iban)
    {
        var sauber = iban?.Replace(" ", "");
        return sauber is { Length: 22 } && sauber.StartsWith("DE", StringComparison.Ordinal)
            ? sauber[12..].TrimStart('0')
            : null;
    }

    /// <summary>
    /// Alle Rückmeldungen der Bank, auch die, die kein Fehler sind.
    /// </summary>
    /// <remarks>
    /// <para>Für die Fehlersuche, nicht für den Bildschirm: Gerade die Hinweise tragen, was
    /// weiterhilft — die Liste der zugelassenen TAN-Verfahren etwa kommt als Hinweis und nicht
    /// als Fehler. <see cref="Meldung"/> wirft sie weg, und das ist dort richtig.</para>
    /// <para>Diese Texte stammen von der Bank und nicht aus dem Nachrichtenstrom: Codes und
    /// Sätze, die sie selbst formuliert hat. Eine PIN steht darin nicht.</para>
    /// </remarks>
    public static string Alles(IEnumerable<HBCIBankMessage> meldungen)
    {
        var alle = meldungen.Select(m => $"{m.Code} {m.Message}".Trim()).ToList();

        return alle.Count > 0 ? string.Join("; ", alle) : "nichts";
    }

    /// <summary>
    /// Was die Bank gesagt hat, in einem Satz.
    /// </summary>
    /// <remarks>
    /// Unverändert und mit Code: „Der Abruf ist fehlgeschlagen“ hilft niemandem, „9010 PIN
    /// gesperrt“ schon. Nur Fehler und Warnungen — die Hinweise der Bank sind Begrüssungen und
    /// Werbung und stünden hier im Weg.
    /// </remarks>
    public static string Meldung(IEnumerable<HBCIBankMessage> meldungen, string was)
    {
        var wichtig = meldungen
            .Where(m => m.IsError || m.IsWarning)
            .Select(m => $"{m.Code} {m.Message}".Trim())
            .ToList();

        return wichtig.Count > 0
            ? $"{was} bei der Bank ist fehlgeschlagen: {string.Join("; ", wichtig)}"
            : $"{was} bei der Bank ist fehlgeschlagen, ohne dass sie einen Grund genannt hat.";
    }

    /// <summary>
    /// Fragt die Bank, welche TAN-Medien der Benutzer hat, und schreibt sie ins Protokoll.
    /// </summary>
    /// <remarks>
    /// <para><b>Gefragt wird, gesetzt wird nicht.</b> Die Bibliothek bietet dafür das Feld
    /// <c>HITAB</c>, und dort stand die Bezeichnung auch eine Zeit lang. Am <b>12.11.2026</b>
    /// an zwei echten Banken gemessen, was das bewirkt:</para>
    /// <list type="bullet">
    /// <item>Leer: Die Bibliothek sendet <c>HKTAN</c> mit <b>zwei</b> Datenelementen — dem
    /// TAN-Prozess und der Segmentkennung. Beide Häuser nehmen das an.</item>
    /// <item>Gesetzt: Die Bezeichnung landet an der <b>elften</b> Stelle desselben Segments,
    /// und die Sparkasse weist es zurück — „9110 Falsche Segmentzusammenstellung: HKTAN“.</item>
    /// </list>
    /// <para>Der Name stammte dabei von der Bank selbst: Sie nannte genau <em>ein</em> Medium,
    /// und es hieß „Alle Geräte“ — also kein Gerät, sondern deren Gesamtheit. Bei der
    /// anderen Bank ist die Frage gar nicht beantwortbar („Auftragsart wird nicht
    /// unterstützt“); dort blieb das Feld immer leer, und <b>nur dieser Weg ist je
    /// durchgelaufen</b>.</para>
    /// <para><b>Es darf scheitern.</b> Ein fehlgeschlagener Nebenschritt darf den Hauptweg
    /// nicht mitreißen — der Abruf läuft ohne weiter.</para>
    /// <para>Die Auskunft bleibt im Protokoll, weil sie die Grundlage wäre, die Medien
    /// später einmal zur Auswahl zu stellen. Dann aber an der richtigen Stelle im Segment.</para>
    /// </remarks>
    public static async Task TanMediumAsync(FinTsClient kunde, int kontoId, ILogger log)
    {
        try
        {
            var medien = await kunde.RequestTANMediumName();

            if (!medien.IsSuccess || medien.Data is not { Count: > 0 } namen)
            {
                log.LogInformation(
                    "Konto {Konto}: Die Bank nennt kein TAN-Medium. {Bank}",
                    kontoId, Alles(medien.Messages));
                return;
            }

            // Gefragt wird, gesetzt wird nicht — und das ist am 12.11.2026 an einer echten
            // Bank gemessen worden:
            //
            // Steht das Feld leer, sendet die Bibliothek ein kurzes HKTAN mit zwei
            // Datenelementen („4“ und die Segmentkennung), und genau das nehmen beide
            // Häuser an. Gesetzt, schiebt sie den Namen an die elfte Stelle des Segments,
            // und die Sparkasse weist es zurück: „9110 Falsche Segmentzusammenstellung:
            // HKTAN“. Der Name kam dabei von der Bank selbst — sie nannte genau ein Medium,
            // und das hiess „Alle Geräte“, also gar kein Gerät, sondern deren Gesamtheit.
            //
            // Bei der anderen Bank ist die Frage gar nicht erst beantwortbar (HKTAB wird
            // nicht unterstützt), das Feld blieb dort immer leer — und nur dieser Weg ist
            // je durchgelaufen.
            //
            // Die Auskunft bleibt trotzdem im Protokoll: Sie sagt, welche Medien es gibt,
            // und das ist die Grundlage, falls jemand sie später zur Auswahl stellen will.
            log.LogInformation(
                "Konto {Konto}: Die Bank nennt {Anzahl} TAN-Medium/Medien: {Namen}. Gesetzt "
                    + "wird keins — ein benanntes Medium sprengt das HKTAN-Segment.",
                kontoId, namen.Count, string.Join(", ", namen));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                "Konto {Konto}: Das TAN-Medium ließ sich nicht holen ({Art}). Der Abruf läuft "
                    + "ohne weiter.",
                kontoId, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Wie lange auf die Freigabe in der App der Bank gewartet wird.
    /// </summary>
    /// <remarks>
    /// Lang genug, um das Telefon aus der Tasche zu holen, und kurz genug, dass eine
    /// vergessene Anfrage die Leitung nicht den Abend lang offen hält.
    /// </remarks>
    public static readonly TimeSpan Freigabefrist = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Der Dialog für eine Freigabe, die <b>nicht</b> über eine eingetippte TAN läuft.
    /// </summary>
    /// <remarks>
    /// <para><b>Entkoppeltes Verfahren.</b> Verlangt die Bank eine starke Authentifizierung,
    /// antwortet sie mit <c>3955 Auftrag empfangen - Bitte Auftrag in Ihrer App freigeben</c>
    /// und schickt die Anfrage in <em>ihre eigene</em> App. Dort wird getippt. Die Bibliothek
    /// fragt derweil selbst nach, ob es schon so weit ist — dafür ist
    /// <see cref="TANDialog.IsDecoupled"/> da.</para>
    /// <para><b>Hier wird keine TAN entgegengenommen</b>, und das bleibt so. Der Rückruf
    /// liefert deshalb eine leere Zeichenkette: Er wird im entkoppelten Verfahren nicht zum
    /// Eintippen gebraucht, und eine Maske dafür gibt es nicht. Das Geheimnis bleibt zwischen
    /// dem Inhaber und seiner Bank.</para>
    /// <para><b>Mit Frist.</b> Wer die Anfrage übersieht, soll nicht eine offene Verbindung
    /// hinterlassen. Nach <see cref="Freigabefrist"/> bricht die Statusabfrage ab — und auch,
    /// wenn der Aufrufer abbricht, etwa weil die Seite geschlossen wurde.</para>
    /// <para><b>Nicht prüfbar ohne Bank.</b> Wie der ganze Dialog: Was sich davor und danach
    /// prüfen lässt, steht in <see cref="BankAccessReadiness"/> und den Diensten.</para>
    /// </remarks>
    public static TANDialog Freigabe(
        int kontoId, ILogger log, CancellationToken ct, out IDisposable abbruch)
    {
        var dialog = new TANDialog(
            _ =>
            {
                log.LogInformation(
                    "Konto {Konto}: Die Bank wartet auf die Freigabe in ihrer App. Bis zu "
                        + "{Frist} wird nachgefragt.",
                    kontoId, Freigabefrist);

                return Task.FromResult(string.Empty);
            },
            erteilt =>
            {
                log.LogInformation(
                    "Konto {Konto}: Die Statusabfrage endete — freigegeben: {Erteilt}.",
                    kontoId, erteilt);

                return Task.CompletedTask;
            })
        {
            IsDecoupled = true,
        };

        // Zwei Gründe zum Aufhören: die Frist und der Abbruch durch den Aufrufer. Die
        // Registrierung geht an den Aufrufer zurück, damit sie mit dem Abruf endet und nicht
        // am Token hängen bleibt.
        var frist = new CancellationTokenSource(Freigabefrist);
        var beide = CancellationTokenSource.CreateLinkedTokenSource(ct, frist.Token);

        var registrierung = beide.Token.Register(() => dialog.IsCancelWaitForApproval = true);

        abbruch = new Aufraeumen(registrierung, beide, frist);
        return dialog;
    }

    /// <summary>Gibt beim Ende des Abrufs zusammen frei, was zur Frist gehört.</summary>
    private sealed class Aufraeumen(
        CancellationTokenRegistration registrierung,
        CancellationTokenSource beide,
        CancellationTokenSource frist) : IDisposable
    {
        public void Dispose()
        {
            registrierung.Dispose();
            beide.Dispose();
            frist.Dispose();
        }
    }
}
