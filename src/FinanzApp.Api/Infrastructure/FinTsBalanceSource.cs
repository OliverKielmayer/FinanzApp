using FinanzApp.Api.Data.Entities;
using libfintx.FinTS;
using libfintx.FinTS.Data;
using libfintx.Globals;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Der Abruf über FinTS, dem Verfahren der deutschen Banken.
/// </summary>
/// <remarks>
/// <para><b>Dieser Teil ist nicht geprüft und kann es nicht sein.</b> Ein Dialog mit einer echten
/// Bank braucht eine echte PIN, eine echte Zugangsadresse und eine Gegenstelle, die antwortet.
/// Nachgestellt wäre er nur ein Gerüst, das beweist, dass das Gerüst steht. Was sich prüfen
/// lässt, liegt deshalb davor und dahinter — in <see cref="BankAccessReadiness"/> und
/// <see cref="Application.BankBalanceService"/>, beide ohne Bank.</para>
/// <para><b>Die PIN steht in keiner Meldung.</b> Auch nicht in einer Fehlermeldung, auch nicht
/// im Protokoll. Was von der Bank zurückkommt, wird unverändert durchgereicht; was die
/// Anwendung hinschickt, bleibt hier.</para>
/// <para><b>Der Dialog selbst darf ins Protokoll</b>, und zwar genau auf einem Weg: Die
/// Bibliothek bekommt eine <see cref="ILoggerFactory"/> und setzt an die Stelle von PIN und
/// Benutzerkennung <c>XXXXXX</c>, bevor sie eine Zeile abgibt. Nachgemessen, nicht geglaubt —
/// siehe die Tests zum Bankdialog. Ohne diesen Weg bliebe ein Abbruch beim Zerlegen der
/// Bankantwort unaufklärbar: Man sähe, <em>dass</em> die Antwort nicht lesbar war, nie
/// <em>was</em> die Bank geschickt hat.</para>
/// <para><b>Gelesen wird, nicht geschrieben.</b> Von allem, was FinTS kann — Überweisung,
/// Lastschrift, Dauerauftrag — wird genau ein Geschäftsvorfall verwendet: der Saldo. Eine
/// Anwendung, die den Haushalt aufschreibt, braucht keinen Zugriff, der ihn verändert.</para>
/// </remarks>
public sealed class FinTsBalanceSource(
    BankAccessOptions options,
    IClock clock,
    ILogger<FinTsBalanceSource> log,
    ILoggerFactory loggerFactory)
    : IBankBalanceSource
{
    public async Task<BankBalanceReading> FetchAsync(
        Account account, string pin, CancellationToken ct = default)
    {
        // Alles, was von hier an schiefgeht, verlässt diese Klasse als BankAccessException —
        // auch das, was die Bibliothek selbst wirft. Sonst bliebe von einer Bank ohne Antwort
        // ein Serverfehler übrig, und am Bildschirm stünde „Der Server hat die Anfrage
        // abgelehnt“: ein Satz, der nichts sagt und auf die PIN zeigt, wo die Leitung schuld
        // ist. Abbruch durch den Aufrufer ist kein Bankfehler und bleibt durch.
        //
        // Zwei Fänge, weil zwei verschiedene Dinge zu tun sind: Eine Zurückweisung der Bank
        // trägt schon die richtige Meldung und wird nur protokolliert; alles andere muss erst
        // übersetzt werden, und sein Text darf dabei nicht mitkommen.
        try
        {
            return await AbrufenAsync(account, pin, ct);
        }
        catch (BankAccessException ex)
        {
            // Diese Meldung ist die eigene: Sie entsteht aus den Rückmeldungen, die die Bank
            // geschickt hat, und steht ohnehin am Bildschirm. Sie vom Protokoll auszunehmen
            // war die Regel für fremde Texte, auf einen eigenen übertragen — und sie kostete
            // genau die Auskunft, die den Fall erklärt: Weist die Bank den Abruf zurück, stand
            // der Grund nur dort, wo gerade jemand hinsah, und nirgends zum Nachlesen.
            log.LogWarning(
                "Der Bankabruf für Konto {Konto} wurde abgelehnt: {Meldung}",
                account.Id, ex.Message);

            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Die Ausnahme kommt nicht als Objekt ins Protokoll: Ihr Text ist aus demselben
            // Grund nicht zu trauen wie der Nachrichtenstrom der Bibliothek — darin kann die
            // PIN stehen. Protokolliert wird, was die Anwendung daraus gemacht hat, dazu die
            // Kette der Ausnahmearten und der Stapel.
            var meldung = BankAccessFailure.Describe(ex, account.FinTsUrl);

            log.LogWarning(
                "Der Bankabruf für Konto {Konto} ist abgebrochen: {Arten}. {Meldung}{Zeile}{Stapel}",
                account.Id, BankAccessFailure.TypeChain(ex), meldung, Environment.NewLine,
                ex.StackTrace);

            throw new BankAccessException(meldung, ex);
        }
    }

    private async Task<BankBalanceReading> AbrufenAsync(
        Account account, string pin, CancellationToken ct)
    {
        // Die Registrierungsnummer ist statisch und gilt für den ganzen Prozess: Die Bibliothek
        // trägt sie in jede Dialogeröffnung ein. Vor jedem Abruf gesetzt, damit sie auch dann
        // stimmt, wenn die Konfiguration sich geändert hat — sie kostet nichts.
        FinTsGlobals.ProductId = options.ProductId;
        FinTsGlobals.Version = options.ProductVersion;

        var verbindung = Verbindung(account, pin);

        // Das Protokoll der Bibliothek läuft durch das der Anwendung. Ob etwas davon ankommt,
        // entscheidet allein die Stufe für den Bereich „libfintx“ in appsettings.json — normal
        // schweigt er, zur Fehlersuche sagt er, was die Bank geschickt hat. Der Bereich steht
        // nur dort und nicht zusätzlich als Konstante hier: Zwei Orte, die denselben Namen
        // nennen sollen, laufen auseinander, und bemerkt würde es an einem stillen Protokoll.
        //
        // Das ist der einzige Weg zu dieser Auskunft, der die PIN nicht preisgibt: Die
        // Bibliothek setzt an ihrer Stelle XXXXXX, bevor sie die Zeile abgibt. Die Ausnahme
        // selbst ins Protokoll zu schreiben bliebe verboten — ihr Text ist ungeprüft.
        var kunde = new FinTsClient(verbindung, loggerFactory: loggerFactory);

        log.LogInformation(
            "Bankabruf für Konto {Konto} bei BLZ {Blz}.", account.Id, verbindung.Blz);

        // Direkt fragen, ohne vorher anzumelden — so führt es das Beispielprogramm der
        // Bibliothek vor, und so ist es gemeint: Balance() baut die Verbindung selbst auf und
        // synchronisiert dabei.
        //
        // Hier stand einmal ein eigener Aufruf von Synchronization() davor, dazu das Setzen
        // des TAN-Verfahrens von Hand. Beides sah nach Sorgfalt aus und war das Gegenteil: Das
        // Protokoll zeigte zwei Synchronisierungen und zwei verschiedene System-Kennungen. Die
        // zweite entwertet die erste, und der Auftrag ging mit einem Zustand hinaus, den die
        // Bank nicht erwartete — sie antwortete „Ungültige Auftragsnachricht: Unbekannter
        // Aufbau“.
        //
        // Das Verfahren wählt die Bibliothek seitdem selbst aus dem, was die Bank anbietet.
        // Das ist nicht schlechter: Die Bank nennt in ihrer Antwort die für diesen Benutzer
        // zugelassenen Verfahren, und eines davon muss es sein. Das Feld am Konto bleibt
        // trotzdem gebraucht — ohne es meldet die Mängelliste nichts, und wer den Zugang
        // einrichtet, bekäme keinen Hinweis.
        var ergebnis = await kunde.Balance(new TANDialog(OhneTan));

        log.LogInformation(
            "Konto {Konto}: Verfahren {Verfahren}, Segmentversion {Version}. Am Konto "
                + "eingetragen: {Eingetragen}. Die Bank meldete: {Bank}",
            account.Id,
            string.IsNullOrEmpty(kunde.HIRMS) ? "keines" : kunde.HIRMS,
            kunde.HITANS,
            account.FinTsTanMethod,
            Alles(ergebnis.Messages));

        if (!ergebnis.IsSuccess || ergebnis.Data is null)
        {
            throw new BankAccessException(Meldung(ergebnis.Messages, "Der Abruf"));
        }

        return new BankBalanceReading(ergebnis.Data.Balance, clock.Now);
    }

    /// <summary>
    /// Was die Bibliothek der Bank über Konto und Zugang sagt.
    /// </summary>
    /// <remarks>
    /// Alles daraus steht am Konto; nichts wird erfunden. Die Kontonummer ist die eine
    /// Ausnahme, und auch sie wird nicht geraten, sondern aus der deutschen IBAN gelesen, in der
    /// sie wörtlich enthalten ist.
    /// </remarks>
    private static ConnectionDetails Verbindung(Account account, string pin) => new()
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
    private static string? KontonummerAusIban(string? iban)
    {
        var sauber = iban?.Replace(" ", "");
        return sauber is { Length: 22 } && sauber.StartsWith("DE", StringComparison.Ordinal)
            ? sauber[12..].TrimStart('0')
            : null;
    }

    /// <summary>
    /// Was die Bank gesagt hat, in einem Satz.
    /// </summary>
    /// <remarks>
    /// Unverändert und mit Code: „Der Abruf ist fehlgeschlagen“ hilft niemandem, „9010 PIN
    /// gesperrt“ schon. Nur Fehler und Warnungen — die Hinweise der Bank sind Begrüssungen und
    /// Werbung und stünden hier im Weg.
    /// </remarks>
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
    private static string Alles(IEnumerable<HBCIBankMessage> meldungen)
    {
        var alle = meldungen.Select(m => $"{m.Code} {m.Message}".Trim()).ToList();

        return alle.Count > 0 ? string.Join("; ", alle) : "nichts";
    }

    private static string Meldung(IEnumerable<HBCIBankMessage> meldungen, string was)
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
    /// Wenn die Bank eine TAN verlangt.
    /// </summary>
    /// <remarks>
    /// <para>Dann endet der Abruf hier. Eine TAN einzugeben hiesse, den Benutzer mitten im
    /// Dialog zu fragen und die Verbindung so lange offen zu halten — das ist zu bauen, aber es
    /// ist nicht dieser Schritt.</para>
    /// <para>Der Saldo ist meist ohne TAN zu haben: Er ändert nichts, und die meisten Banken
    /// verlangen eine Freigabe nur alle 90 Tage. Wer hier landet, muss sich einmal im
    /// Online-Banking anmelden; danach geht es wieder.</para>
    /// </remarks>
    private static Task<string> OhneTan(TANDialog dialog)
        => throw new BankAccessException(
            "Die Bank verlangt für diesen Abruf eine TAN. Die Anwendung kann keine TAN "
            + "entgegennehmen — sie fragt nur nach dem Stand und ändert nichts. Melden Sie sich "
            + "einmal im Online-Banking an; danach lässt die Bank den Abruf in der Regel wieder "
            + "ohne TAN zu.");
}
