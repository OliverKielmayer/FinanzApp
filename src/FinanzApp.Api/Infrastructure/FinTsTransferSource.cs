using libfintx.Globals;
using FinanzApp.Api.Data.Entities;
using libfintx.FinTS;
using Microsoft.Extensions.Logging;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Was die Bank auf einen Überweisungsauftrag geantwortet hat.</summary>
/// <param name="Accepted">Ob sie ihn angenommen hat.</param>
/// <param name="Message">Ihre Meldung im Klartext — auch bei Erfolg, sie sagt oft mehr.</param>
public readonly record struct TransferOutcome(bool Accepted, string Message);

/// <summary>
/// Der Weg, auf dem ein Überweisungsauftrag zur Bank kommt.
/// </summary>
/// <remarks>
/// Eine Schnittstelle für eine Umsetzung, und aus demselben Grund wie bei
/// <see cref="IBankStatementSource"/>: Was <em>vor</em> dem Auftrag entschieden wird — ob die
/// IBAN stimmt, ob der Betrag unter der Schranke liegt, ob derselbe Auftrag gerade schon
/// einmal hinausging — ließe sich sonst nur mit einer echten Bank prüfen, die auf Kommando
/// das Richtige tut. So eine Bank gibt es nicht.
/// </remarks>
public interface IBankTransferSource
{
    /// <summary>Beauftragt eine Überweisung und wartet auf die Antwort der Bank.</summary>
    Task<TransferOutcome> SubmitAsync(
        Account account,
        string pin,
        string receiverName,
        string receiverIban,
        string? receiverBic,
        decimal amount,
        string purpose,
        CancellationToken ct = default);
}

/// <summary>
/// Die Überweisung über FinTS.
/// </summary>
/// <remarks>
/// <para><b>Derselbe Ablauf wie der Umsatzabruf</b>, Segment für Segment: anmelden,
/// TAN-Verfahren setzen, TAN-Medium erfragen, Auftrag senden, auf die Freigabe warten. Was
/// sich unterscheidet, ist der eine Aufruf in der Mitte — und dass hier Geld bewegt wird.</para>
/// <para><b>Freigegeben wird in der App der Bank.</b> Hier wird keine TAN entgegengenommen,
/// siehe <see cref="FinTsDialog.Freigabe"/>. Verlangt die Bank eine eingetippte TAN statt der
/// Freigabe in ihrer App, scheitert der Auftrag mit ihrer eigenen Meldung — und das ist die
/// ehrliche Antwort darauf, dass es dafür keine Maske gibt.</para>
/// <para><b>Nicht prüfbar ohne Bank.</b> Wie der ganze Dialog. Was sich davor prüfen lässt,
/// steht im Dienst und in <see cref="IbanCheck"/>.</para>
/// </remarks>
public sealed class FinTsTransferSource(
    BankAccessOptions options,
    ILoggerFactory loggerFactory,
    ILogger<FinTsTransferSource> log) : IBankTransferSource
{
    public async Task<TransferOutcome> SubmitAsync(
        Account account,
        string pin,
        string receiverName,
        string receiverIban,
        string? receiverBic,
        decimal amount,
        string purpose,
        CancellationToken ct = default)
    {
        FinTsGlobals.ProductId = options.ProductId;
        FinTsGlobals.Version = options.ProductVersion;

        // Ein eigener, leerer Ordner je Auftrag — und er verschwindet danach wieder. Ohne
        // diese Zeilen schreibt die Bibliothek ihre Bankparameter (BPD) und Benutzerdaten
        // (UPD) ins Benutzerprofil und lässt sie dort liegen: Bankleitzahl, Benutzerkennung,
        // Kontodaten. Der Umsatzabruf macht es seit jeher so; dieser Dienst tat es nicht, und
        // das ist am 21.11.2026 aufgefallen, als die Dateien noch dalagen.
        var vorher = FinTsGlobals.ProgramBaseDir;
        DirectoryInfo? ablage = null;

        try
        {
            ablage = Directory.CreateTempSubdirectory("finanzapp-auftrag-");
            FinTsGlobals.ProgramBaseDir = ablage.FullName;

            return await BeauftragenAsync(
                account, pin, receiverName, receiverIban, receiverBic, amount, purpose, ct);
        }
        finally
        {
            if (ablage is not null)
            {
                Aufraeumen(ablage);
            }

            FinTsGlobals.ProgramBaseDir = vorher;
        }
    }

    /// <summary>Der Dialog selbst, innerhalb der eigenen Ablage.</summary>
    private async Task<TransferOutcome> BeauftragenAsync(
        Account account,
        string pin,
        string receiverName,
        string receiverIban,
        string? receiverBic,
        decimal amount,
        string purpose,
        CancellationToken ct)
    {
        var kunde = new FinTsClient(
            FinTsDialog.Verbindung(account, pin), loggerFactory: loggerFactory);

        // Der Betrag steht im Protokoll, der Empfänger gekürzt: Wer später sucht, warum ein
        // Auftrag scheiterte, braucht beides. Die PIN ersetzt die Bibliothek selbst durch
        // XXXXXX, bevor sie etwas abgibt — geprüft in BankDialogLoggingTests.
        log.LogInformation(
            "Überweisung von Konto {Konto} bei BLZ {Blz}: {Betrag} an {Empfänger}.",
            account.Id, account.Blz, amount, Gekuerzt(receiverName));

        var anmeldung = await kunde.Synchronization();

        log.LogInformation(
            "Konto {Konto} nach der Anmeldung: Verfahren {Abgeleitet}, Segmentversion "
                + "{Version}. Am Konto eingetragen: {Eingetragen}. Die Bank meldete: {Bank}",
            account.Id,
            string.IsNullOrEmpty(kunde.HIRMS) ? "keines" : kunde.HIRMS,
            kunde.HITANS,
            account.FinTsTanMethod,
            FinTsDialog.Alles(anmeldung.Messages));

        if (!anmeldung.IsSuccess)
        {
            throw new BankAccessException(
                FinTsDialog.Meldung(anmeldung.Messages, "Die Anmeldung"));
        }

        kunde.HIRMS = account.FinTsTanMethod!.Value.ToString();
        await FinTsDialog.TanMediumAsync(kunde, account.Id, log);

        var warten = FinTsDialog.Freigabe(account.Id, log, ct, out var abbruch);
        using var frist = abbruch;

        var ergebnis = await kunde.Transfer(
            warten,
            receiverName,
            IbanCheck.Normalise(receiverIban),
            IbanCheck.Normalise(receiverBic),
            amount,
            purpose,
            kunde.HIRMS);

        var meldung = FinTsDialog.Alles(ergebnis.Messages);

        log.LogInformation(
            "Konto {Konto}: Die Bank hat den Auftrag {Ergebnis}. Sie meldete: {Bank}",
            account.Id, ergebnis.IsSuccess ? "angenommen" : "abgelehnt", meldung);

        return new TransferOutcome(ergebnis.IsSuccess, meldung);
    }

    /// <summary>Der Empfänger im Protokoll: so viel, dass man ihn wiedererkennt.</summary>
    private static string Gekuerzt(string name)
        => name.Length <= 24 ? name : name[..24] + "…";

    /// <summary>
    /// Räumt den Ordner weg, den die Bibliothek beschrieben hat.
    /// </summary>
    /// <remarks>
    /// Ein Fehler beim Aufräumen darf den Auftrag nicht nachträglich scheitern lassen — er ist
    /// zu diesem Zeitpunkt längst bei der Bank. Gemeldet wird er, mehr nicht.
    /// </remarks>
    private void Aufraeumen(DirectoryInfo ablage)
    {
        try
        {
            ablage.Delete(recursive: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(
                "Der Ordner {Ordner} liess sich nicht aufräumen: {Grund}. Darin stehen "
                    + "Bankleitzahl und Benutzerkennung.",
                ablage.FullName, ex.Message);
        }
    }
}
