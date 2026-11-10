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

        var verbindung = FinTsDialog.Verbindung(account, pin);

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

        // Die Reihenfolge stammt aus dem README der Bibliothek und nicht aus ihrem
        // Beispielprogramm: anmelden, Verfahren setzen, TAN-Medium holen, fragen.
        //
        // Das Beispielprogramm ist kürzer — es baut den Klienten und fragt sofort. Danach ist
        // hier schon einmal umgebaut worden, und es war falsch: Das Beispielprogramm ist ein
        // Kommandozeilen-Demo, der README beschreibt den vollständigen Weg. Wer sich am
        // kürzeren ausrichtet, lässt zwei Schritte weg, die die Bibliothek selbst vorführt.
        var anmeldung = await kunde.Synchronization();
        if (!anmeldung.IsSuccess)
        {
            throw new BankAccessException(FinTsDialog.Meldung(anmeldung.Messages, "Die Anmeldung"));
        }

        log.LogInformation(
            "Konto {Konto} nach der Anmeldung: Verfahren {Abgeleitet}, Segmentversion "
                + "{Version}. Am Konto eingetragen: {Eingetragen}. Die Bank meldete: {Bank}",
            account.Id,
            string.IsNullOrEmpty(kunde.HIRMS) ? "keines" : kunde.HIRMS,
            kunde.HITANS,
            account.FinTsTanMethod,
            FinTsDialog.Alles(anmeldung.Messages));

        // Das TAN-Verfahren muss die Bibliothek wissen, bevor sie fragt — sonst wählt sie eines
        // aus der Liste der Bank, und das muss nicht das sein, das freigeschaltet ist.
        kunde.HIRMS = account.FinTsTanMethod!.Value.ToString();

        await FinTsDialog.TanMediumAsync(kunde, account.Id, log);

        var ergebnis = await kunde.Balance(new TANDialog(FinTsDialog.OhneTan));

        log.LogInformation(
            "Konto {Konto} hat gefragt mit Verfahren {Verfahren}, Segmentversion {Version}, "
                + "TAN-Medium {Medium}. Die Bank meldete: {Bank}",
            account.Id,
            kunde.HIRMS,
            kunde.HITANS,
            string.IsNullOrEmpty(kunde.HITAB) ? "keines" : kunde.HITAB,
            FinTsDialog.Alles(ergebnis.Messages));

        if (!ergebnis.IsSuccess || ergebnis.Data is null)
        {
            throw new BankAccessException(FinTsDialog.Meldung(ergebnis.Messages, "Der Abruf"));
        }

        return new BankBalanceReading(ergebnis.Data.Balance, clock.Now);
    }

    /// <summary>
    /// <summary>
    /// Was die Bank gesagt hat, in einem Satz.
    /// </summary>
    /// <remarks>
    /// Unverändert und mit Code: „Der Abruf ist fehlgeschlagen“ hilft niemandem, „9010 PIN
    /// gesperrt“ schon. Nur Fehler und Warnungen — die Hinweise der Bank sind Begrüssungen und
    /// Werbung und stünden hier im Weg.
    /// </remarks>
}
