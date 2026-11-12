using FinanzApp.Api.Data.Entities;
using libfintx.FinTS;
using libfintx.FinTS.Camt;
using libfintx.Globals;

// Eindeutig benannt: Die Bibliothek bringt einen gleichnamigen Typ mit, und welcher gemeint ist,
// soll nicht von der Reihenfolge der using-Zeilen abhängen.
using ImportRecord = FinanzApp.Api.Data.ImportRecord;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Holt die Umsätze eines Kontos bei der Bank.
/// </summary>
/// <remarks>
/// <para>Getrennt vom Saldo, weil es eine andere Frage ist: Der Saldo ist eine Zahl, die
/// Umsätze sind ein Auszug. Was beide brauchen, steht in <see cref="FinTsDialog"/>.</para>
/// <para><b>Nicht geprüft und nicht prüfbar</b>, wie der Saldo — ein Dialog mit einer echten
/// Bank braucht eine echte PIN. Prüfbar ist, was davor und dahinter liegt: die Mängelliste und
/// das Zusammenlegen der Dokumente.</para>
/// </remarks>
public interface IBankStatementSource
{
    /// <summary>Die Umsätze eines Zeitraums, als Auszug wie aus einer Datei.</summary>
    Task<ParsedStatement> FetchAsync(
        Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// Der Umsatzabruf über FinTS.
/// </summary>
/// <remarks>
/// <para><b>Der Kniff: Die Bank liefert camt, und camt lesen wir schon.</b> Die Bibliothek
/// bringt eigene Umsatzobjekte mit, aber deren Felder sind nicht dieselben, aus denen unser
/// Leser seine Importreferenz bildet — ihnen fehlen <c>AcctSvcrRef</c> und <c>TxId</c>, also
/// gerade die beiden, auf die er zuerst schaut. Eine Abbildung daraus ergäbe andere Referenzen
/// als beim Datei-Import. Folge: Alles sähe neu aus, und ein Dauerauftrag fiele zu einer
/// einzigen Buchung zusammen.</para>
/// <para>Deshalb geht das camt-XML der Bank durch <see cref="IStatementParser"/> — denselben
/// Leser, dieselben Referenzen. Wer einen Auszug früher als Datei eingelesen hat, bekommt
/// dieselben Sätze wiedererkannt.</para>
/// <para><b>Über eine Datei, weil die Bibliothek es nicht anders hergibt.</b> Sie schreibt das
/// XML in einen Unterordner von <c>FinTsGlobals.ProgramBaseDir</c> und gibt den Pfad nicht
/// zurück — im Namen steht eine GUID, er ist also nicht zu erraten. Deshalb zeigt dieser Abruf
/// das Basisverzeichnis auf einen eigenen, leeren Ordner und liest hinterher, was darin
/// aufgetaucht ist. Der Ordner wird danach gelöscht; er trägt Kontodaten.</para>
/// </remarks>
public sealed class FinTsStatementSource(
    BankAccessOptions options,
    IStatementParser parser,
    ILogger<FinTsStatementSource> log,
    ILoggerFactory loggerFactory)
    : IBankStatementSource
{
    /// <summary>
    /// Welche camt-Fassung angefragt wird.
    /// </summary>
    /// <remarks>
    /// 052 ist der Umsatzbericht — das, was eine Bank für einen Zeitraum hergibt, auch noch
    /// nicht abgeschlossene Tage. 053 ist der abgeschlossene Auszug und reicht oft weniger weit
    /// zurück. Gelesen werden beide vom selben Leser, geholt wird die Fassung, die mehr
    /// liefert.
    /// </remarks>
    private const CamtVersion Fassung = CamtVersion.Camt052;

    /// <summary>
    /// Ein Abruf nach dem anderen.
    /// </summary>
    /// <remarks>
    /// <para><b>Nicht aus Vorsicht, sondern weil das Ablageverzeichnis der Bibliothek ein
    /// statisches Feld ist.</b> Zwei Abrufe gleichzeitig überschrieben einander
    /// <c>FinTsGlobals.ProgramBaseDir</c> — und dann läse der eine Abruf die camt-Dateien des
    /// anderen. Das sind Umsätze eines fremden Kontos in der Vorschau des eigenen; in einem
    /// Haushalt mit mehreren Konten ist das kein Randfall.</para>
    /// <para>Der Preis ist gering: Ein Abruf dauert Sekunden und steht am Ende eines Klicks.
    /// Zwei Menschen, die im selben Moment auf „Umsätze holen“ drücken, warten nacheinander —
    /// gemerkt hat das noch keiner.</para>
    /// </remarks>
    private readonly SemaphoreSlim einerNachDemAnderen = new(1, 1);

    public async Task<ParsedStatement> FetchAsync(
        Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        try
        {
            return await AbrufenAsync(account, pin, from, to, ct);
        }
        catch (BankAccessException ex)
        {
            log.LogWarning(
                "Der Umsatzabruf für Konto {Konto} wurde abgelehnt: {Meldung}",
                account.Id, ex.Message);

            throw;
        }
        catch (StatementFormatException ex)
        {
            // Die Bank hat geantwortet, aber das Gelieferte war nicht zu lesen. Die allgemeine
            // Beschreibung unten passte hier nicht: Sie spricht von der Verbindung und der PIN,
            // und beides war in Ordnung. Die Meldung des Lesers nennt als Einzige, woran es lag.
            log.LogWarning(
                "Der Umsatzabruf für Konto {Konto} brachte einen unlesbaren Auszug: {Meldung}",
                account.Id, ex.Message);

            throw new BankAccessException(
                "Die Bank hat geantwortet, aber der Auszug ließ sich nicht lesen: " + ex.Message,
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var meldung = BankAccessFailure.Describe(ex, account.FinTsUrl);

            log.LogWarning(
                "Der Umsatzabruf für Konto {Konto} ist abgebrochen: {Arten}. {Meldung}{Zeile}{Stapel}",
                account.Id, BankAccessFailure.TypeChain(ex), meldung, Environment.NewLine,
                ex.StackTrace);

            throw new BankAccessException(meldung, ex);
        }
    }

    private async Task<ParsedStatement> AbrufenAsync(
        Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct)
    {
        FinTsGlobals.ProductId = options.ProductId;
        FinTsGlobals.Version = options.ProductVersion;

        await einerNachDemAnderen.WaitAsync(ct);

        // Das try beginnt unmittelbar nach dem Warten: Scheiterte das Anlegen des Ordners davor,
        // bliebe der Wächter für immer geschlossen, und kein Konto liesse sich mehr abrufen.
        var vorher = FinTsGlobals.ProgramBaseDir;
        DirectoryInfo? ablage = null;

        try
        {
            // Ein eigener, leerer Ordner je Abruf. Was die Bibliothek hineinschreibt, gehört
            // diesem Abruf — ohne das müsste man raten, welche der Dateien von heute ist.
            ablage = Directory.CreateTempSubdirectory("finanzapp-camt-");
            FinTsGlobals.ProgramBaseDir = ablage.FullName;

            var kunde = new FinTsClient(
                FinTsDialog.Verbindung(account, pin), loggerFactory: loggerFactory);

            log.LogInformation(
                "Umsatzabruf für Konto {Konto} bei BLZ {Blz}, {Von} bis {Bis}.",
                account.Id, account.Blz, from, to);

            var anmeldung = await kunde.Synchronization();

            // Vor der Prüfung protokolliert, nicht danach: Scheitert die Anmeldung, ist genau
            // das die Zeile, die man braucht. Welche TAN-Verfahren die Bank anbietet, sagt sie
            // hier — und ob das am Konto eingetragene darunter ist. Die Bibliothek ersetzt PIN
            // und Benutzerkennung durch XXXXXX, bevor sie etwas abgibt; geprüft in
            // BankDialogLoggingTests.
            //
            // Diese Auskunft stand bis zum 11.11.2026 nur in der Saldenabfrage. Mit ihr war
            // sie weg, und ein gescheiterter Sparkassen-Abruf meldete nur noch „9050 Die
            // Nachricht enthält Fehler“ — ohne zu sagen, woran.
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

            // Verlangt die Bank eine Freigabe, schickt sie die Anfrage in ihre eigene App und
            // die Bibliothek fragt nach, bis dort getippt wurde. Eingetippt wird hier nichts.
            var warten = FinTsDialog.Freigabe(account.Id, log, ct, out var abbruch);
            using var frist = abbruch;

            var ergebnis = await kunde.Transactions_camt(
                warten,
                Fassung,
                from.ToDateTime(TimeOnly.MinValue),
                to.ToDateTime(TimeOnly.MinValue),
                saveCamtFile: true);

            log.LogInformation(
                "Konto {Konto} hat Umsätze erfragt. Die Bank meldete: {Bank}",
                account.Id, FinTsDialog.Alles(ergebnis.Messages));

            if (!ergebnis.IsSuccess)
            {
                throw new BankAccessException(
                    FinTsDialog.Meldung(ergebnis.Messages, "Der Umsatzabruf"));
            }

            return await LesenAsync(ablage, account, from, to, ct);
        }
        finally
        {
            // Der Ordner trägt Kontonummer, Empfänger und Beträge. Er hat seinen Zweck erfüllt,
            // sobald gelesen ist, und ein Abruf, der scheitert, lässt erst recht nichts liegen.
            if (ablage is not null)
            {
                Aufraeumen(ablage);
            }

            // Und das statische Feld zurück auf seinen alten Wert: Es stünde sonst auf einem
            // gerade gelöschten Ordner, und der nächste, der die Bibliothek benutzt, bekäme
            // einen Pfad ins Leere.
            FinTsGlobals.ProgramBaseDir = vorher;

            einerNachDemAnderen.Release();
        }
    }

    /// <summary>
    /// Liest, was die Bibliothek abgelegt hat, und legt es zu einem Auszug zusammen.
    /// </summary>
    /// <remarks>
    /// <para>Die Bank antwortet pro Zeitraum mit mehreren camt-Dokumenten, und jedes wird eine
    /// eigene Datei. Zusammengelegt wird mit derselben Stelle, die ein Auszugsarchiv
    /// zusammenlegt — eine Vorschau, eine Übernahme, ein Schlusssaldo vom jüngsten Dokument.
    /// </para>
    /// <para>Liegt nichts da, ist das kein Fehler: Ein Zeitraum ohne Umsätze ist ein gültiges
    /// Ergebnis. Dann kommt ein Auszug ohne Sätze zurück, und die Vorschau sagt „nichts
    /// Neues“.</para>
    /// <para><c>internal</c> und nicht <c>private</c>: Davor steht ein Dialog mit einer echten
    /// Bank, also ist dieser Schritt über den Abruf nicht erreichbar. Geprüft wird er mit einem
    /// Ordner, der aussieht wie der, den die Bibliothek hinterlässt.</para>
    /// </remarks>
    internal async Task<ParsedStatement> LesenAsync(
        DirectoryInfo ablage, Account account, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var alle = ablage.Exists
            ? ablage.GetFiles("*", SearchOption.AllDirectories).OrderBy(f => f.Name).ToList()
            : [];

        var dateien = alle
            .Where(f => Auszugsendungen.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var name = $"Abruf {from:dd.MM.yyyy}–{to:dd.MM.yyyy}";

        log.LogInformation(
            "Konto {Konto}: {Anzahl} camt-Dokument(e) von der Bank erhalten, {Fremde} weitere "
            + "Datei(en) im Ablageordner gehören der Bibliothek.",
            account.Id, dateien.Count, alle.Count - dateien.Count);

        if (dateien.Count == 0)
        {
            if (alle.Count > 0)
            {
                // Dann stimmt die Annahme über die Endungen nicht mehr, und „keine Umsätze“ wäre
                // eine falsche Auskunft. Gemeldet werden nur die Endungen, nicht die Namen: In
                // denen stehen Bankleitzahl und Benutzerkennung.
                log.LogWarning(
                    "Konto {Konto}: Im Ablageordner liegen {Anzahl} Datei(en), aber keine mit "
                    + "einer bekannten Auszugsendung. Gefunden wurden: {Endungen}.",
                    account.Id, alle.Count,
                    string.Join(", ", alle.Select(f => f.Extension).Distinct()));
            }

            return new ParsedStatement
            {
                FileName = name,
                Format = Fassung.ToString().ToUpperInvariant(),
                BankName = account.BankName,
                Iban = account.Iban,
                Records = [],
            };
        }

        var auszuege = new List<ParsedStatement>();
        var saetze = new List<ImportRecord>();
        var nummer = 0;

        foreach (var datei in dateien)
        {
            nummer++;

            try
            {
                await using var inhalt = datei.OpenRead();

                // Mit .xml, damit der Leser sich zuständig fühlt: Die Bibliothek benennt ihre
                // Dateien .camt052, und das ist keine Endung, die ein Auszugsleser kennt.
                var gelesen = await parser.ParseAsync(inhalt, datei.Name + ".xml", ct);

                auszuege.Add(gelesen);
                saetze.AddRange(gelesen.Records);
            }
            catch (StatementFormatException ex)
            {
                // Wie beim Auszugsarchiv: Ein unlesbares Dokument steht als Zeile mit Grund in
                // der Vorschau, statt die Sätze der anderen Dokumente mitzunehmen.
                log.LogWarning(
                    "Konto {Konto}: Dokument {Nummer} von {Anzahl} ließ sich nicht lesen. {Grund}",
                    account.Id, nummer, dateien.Count, ex.Message);

                // Aus Zeitraum und Platz in der Reihe, nicht aus dem Dateinamen: Der trägt eine
                // GUID der Bibliothek, die bei jedem Abruf eine andere ist.
                saetze.Add(new ImportRecord(
                    Reference: $"ABRUF:{from:yyyyMMdd}-{to:yyyyMMdd}#{nummer}",
                    BookingDate: null,
                    Payee: $"Dokument {nummer} des Abrufs",
                    Amount: null,
                    Problem: ex.Message));
            }
        }

        if (auszuege.Count == 0)
        {
            throw new StatementFormatException(
                $"Die Bank hat {dateien.Count} Dokument(e) geschickt, aber keines davon ließ sich "
                + "lesen. Woran es lag, steht im Protokoll der Anwendung.");
        }

        return ZipStatementReader.Merge(name, auszuege, saetze);
    }

    /// <summary>
    /// Welche Dateien im Ablageordner ein Auszug sind.
    /// </summary>
    /// <remarks>
    /// <para><b>Nicht alles dort kommt von der Bank.</b> Die Bibliothek legt daneben ihre
    /// Zwischenspeicher ab — die Bankparameter (<c>.bpd</c>) und die Benutzerparameter
    /// (<c>.upd</c>), beides FinTS-Segmente in Textform. Wer sie dem camt-Leser gibt, bekommt
    /// „kein gültiges XML“ zurück und verliert den ganzen Abruf an einer Datei, die die Bank nie
    /// geschickt hat. Genau das ist beim ersten Abruf gegen eine echte Bank passiert.</para>
    /// <para>Die Endungen stammen aus der Bibliothek selbst: Sie schreibt camt052- und
    /// camt053-Dokumente unter eben diesen Namen. <c>.xml</c> und <c>.camt</c> stehen daneben,
    /// weil eine künftige Fassung sie so benennen könnte — sie kosten nichts.</para>
    /// </remarks>
    private static readonly string[] Auszugsendungen = [".camt052", ".camt053", ".camt", ".xml"];

    private void Aufraeumen(DirectoryInfo ablage)
    {
        try
        {
            if (ablage.Exists)
            {
                ablage.Delete(recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Liegenbleiben ist ärgerlich, aber kein Grund, den Abruf scheitern zu lassen. Wer
            // es wissen will, findet den Pfad hier.
            log.LogWarning(
                "Der Ordner {Ordner} ließ sich nicht löschen ({Art}). Er enthält Kontodaten.",
                ablage.FullName, ex.GetType().Name);
        }
    }
}
