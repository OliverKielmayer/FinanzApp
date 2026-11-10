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
    /// Fragt die Bank, welches TAN-Medium der Benutzer hat, und trägt es ein.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Schritt, den wir übersehen hatten.</b> Im Beispiel des README steht zwischen
    /// dem Setzen des Verfahrens und der Abfrage ein <c>InitTANMedium</c>. Was es tut, zeigt der
    /// README nicht — die Bibliothek bietet dafür <c>RequestTANMediumName</c> und das Feld
    /// <c>HITAB</c>. Bei TAN-Verfahren, die ein Medium kennen, geht dessen Bezeichnung in das
    /// Segment ein, mit dem gefragt wird.</para>
    /// <para><b>Es darf scheitern.</b> Nicht jede Bank und nicht jedes Verfahren kennt ein
    /// Medium; die Raiba des Inhabers antwortet mit „Auftragsart wird nicht unterstützt“. Dann
    /// geht der Abruf ohne weiter. Ein fehlgeschlagener Nebenschritt darf den Hauptweg nicht
    /// mitreißen.</para>
    /// <para><b>Nur bei genau einem Medium.</b> Kommen mehrere, wäre die Wahl eine Frage an den
    /// Benutzer und keine, die hier zu treffen ist. Dann bleibt das Feld leer und das Protokoll
    /// sagt, wie viele zur Wahl standen.</para>
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

            if (namen.Count > 1)
            {
                log.LogInformation(
                    "Konto {Konto}: Die Bank nennt {Anzahl} TAN-Medien. Welches gemeint ist, "
                        + "kann hier niemand entscheiden; das Feld bleibt leer.",
                    kontoId, namen.Count);
                return;
            }

            kunde.HITAB = namen[0];

            log.LogInformation("Konto {Konto}: TAN-Medium von der Bank übernommen.", kontoId);
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
    /// Wenn die Bank eine TAN verlangt.
    /// </summary>
    /// <remarks>
    /// <para>Dann endet der Abruf hier. Eine TAN einzugeben hiesse, den Benutzer mitten im
    /// Dialog zu fragen und die Verbindung so lange offen zu halten — das ist zu bauen, aber es
    /// ist nicht dieser Schritt.</para>
    /// <para>Saldo und Umsätze sind meist ohne TAN zu haben: Beide ändern nichts, und die
    /// meisten Banken verlangen eine Freigabe nur alle 90 Tage. Wer hier landet, muss sich
    /// einmal im Online-Banking anmelden; danach geht es wieder.</para>
    /// </remarks>
    public static Task<string> OhneTan(TANDialog dialog)
        => throw new BankAccessException(
            "Die Bank verlangt für diesen Abruf eine TAN. Die Anwendung kann keine TAN "
            + "entgegennehmen — sie fragt nur ab und ändert nichts. Melden Sie sich einmal im "
            + "Online-Banking an; danach lässt die Bank den Abruf in der Regel wieder ohne TAN "
            + "zu.");
}
