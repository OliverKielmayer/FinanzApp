using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Umsätze bei der Bank holen — und erkennen, was davon längst gebucht ist.
/// </summary>
/// <remarks>
/// <para><b>Nur die Steckdose ist nachgemacht.</b> Die Attrappe gibt denselben camt-Text aus, den
/// eine Bank schickt, und ab da läuft die echte Kette: derselbe Leser, dieselbe Vorschau,
/// dieselbe Duplikatprüfung wie bei einer hochgeladenen Datei. Ein echter Dialog bräuchte eine
/// echte PIN und eine Gegenstelle — was die Anwendung selbst entscheidet, steht hier vollständig
/// auf dem Prüfstand.</para>
/// <para><b>Was die Attrappe mitzählt, ist der halbe Test.</b> Bei unvollständigem Zugang,
/// fehlender PIN oder unsinnigem Zeitraum darf sie gar nicht erst gerufen werden: Ein Abruf, der
/// erst bei der Bank scheitert, kostet einen Fehlversuch auf der PIN, und drei davon sperren den
/// Zugang.</para>
/// <para>Die Zahlen stammen aus der Beispieldatei und sind erfunden.</para>
/// </remarks>
public sealed class BankStatementTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 9);
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 5000 });

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Die IBAN der Beispieldatei. Daran erkennt die Vorschau das Zielkonto.</summary>
    private const string Iban = "DE44672500200034889102";

    private int KontoId { get; }

    public BankStatementTests()
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = "Sparkasse Giro",
            ShortName = "Sparkasse",
            BankName = "Sparkasse",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 11, 1),
            Iban = Iban,
            Blz = "67250020",
            Bic = "SOLADES1HDB",
            FinTsUrl = "https://banking.example.test/fints30",
            FinTsUserId = "mustermann1",
            // Dieselbe erfundene Nummer wie im Prüfgerüst der Saldenabfrage. Welches Verfahren
            // am echten Konto eingetragen ist, gehört nicht in einen öffentlichen Quelltext —
            // der Test hängt an der Nummer ohnehin nicht.
            FinTsTanMethod = 921,
        };

        context.Accounts.Add(konto);
        context.SaveChanges();
        KontoId = konto.Id;
    }

    public void Dispose()
    {
        cache.Dispose();
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── Hilfsmittel ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Bank, die einen Auszug schickt — und mitschreibt, wonach sie gefragt wurde.
    /// </summary>
    /// <remarks>
    /// Sie liefert den camt-Text der Beispieldatei durch denselben Leser, den auch der echte
    /// Abruf benutzt. Damit prüft der Test die Kette ab dem Punkt, an dem die Bibliothek ihre
    /// Dateien hinlegt — und nicht nur einen von Hand gebauten Auszug.
    /// </remarks>
    private sealed class Attrappe : IBankStatementSource
    {
        public int Fragen { get; private set; }

        public string? GesehenePin { get; private set; }

        public DateOnly Von { get; private set; }

        public DateOnly Bis { get; private set; }

        public string? GesehenesKonto { get; private set; }

        public async Task<ParsedStatement> FetchAsync(
            Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Fragen++;
            GesehenePin = pin;
            GesehenesKonto = account.Iban;
            Von = from;
            Bis = to;

            await using var inhalt = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(Beispiel()));

            return await new CamtStatementParser()
                .ParseAsync(inhalt, $"Abruf {from:dd.MM.yyyy}.xml", ct);
        }
    }

    /// <summary>Eine Bank, die den Abruf abweist.</summary>
    private sealed class AbweisendeAttrappe(string grund) : IBankStatementSource
    {
        public Task<ParsedStatement> FetchAsync(
            Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct = default)
            => throw new BankAccessException(grund);
    }

    /// <summary>Eine Bank, die für den Zeitraum nichts zu melden hat.</summary>
    private sealed class LeereAttrappe : IBankStatementSource
    {
        public Task<ParsedStatement> FetchAsync(
            Account account, string pin, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Task.FromResult(new ParsedStatement
            {
                FileName = $"Abruf {from:dd.MM.yyyy}–{to:dd.MM.yyyy}",
                Format = "CAMT052",
                BankName = account.BankName,
                Iban = account.Iban,
                Records = [],
            });
    }

    private static string Beispiel()
        => File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Beispiele", "camt052-sparkasse.xml"));

    private static BankAccessOptions Produkt() => new() { ProductId = "ABC123DEF456GHI789JKL0123" };

    private ImportService Imports(FinanzAppDbContext context)
        => new(context, clock, new CamtStatementParser(), cache,
            TestDatabase.PathService(root), TestDatabase.SignedIn(null));

    private BankStatementService Dienst(IBankStatementSource quelle, FinanzAppDbContext context)
        => new(context, Produkt(), quelle, Imports(context), clock);

    private async Task<ImportPreviewDto> HoleAsync(
        IBankStatementSource quelle, string pin = "1234",
        DateOnly? von = null, DateOnly? bis = null)
    {
        using var context = database.Context();
        return await Dienst(quelle, context).FetchAsync(KontoId, pin, von, bis);
    }

    private async Task<ImportCommitResultDto> UebernehmeAsync(
        ImportPreviewDto vorschau, IEnumerable<int> saetze)
    {
        using var context = database.Context();

        return await Imports(context).CommitAsync(new ImportCommitRequest
        {
            PreviewId = vorschau.Id,
            AccountId = KontoId,
            Indexes = [.. saetze],
        });
    }

    // ── Der Abruf ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Abruf endet in einer Vorschau und bucht nichts.
    /// </summary>
    /// <remarks>
    /// Das ist die Schwelle, die auch für eine hochgeladene Datei gilt: Aus Sätzen werden
    /// Buchungen, und daran hängen Kategorie, Zielkonto und die Frage, ob ein Satz schon im
    /// Bestand steht. Das entscheidet ein Mensch an der Vorschau und kein Dienst — erst recht
    /// keiner, der die Zeilen gerade erst von der Bank geholt hat.
    /// </remarks>
    [Fact]
    public async Task Der_Abruf_endet_in_einer_Vorschau_und_bucht_nichts()
    {
        var vorschau = await HoleAsync(new Attrappe());

        // Sieben Sätze aus sechs Einträgen: Einer ist eine Sammelbuchung mit zwei Einzelumsätzen.
        Assert.Equal(7, vorschau.RecordCount);

        using var context = database.Context();
        Assert.Empty(context.Transactions);
    }

    /// <summary>
    /// Ein zweiter Abruf erkennt die schon gebuchten Sätze.
    /// </summary>
    /// <remarks>
    /// <para>Der Grund, warum es den Abruf gibt: Wer zweimal im Monat holt, bekommt beim zweiten
    /// Mal grösstenteils dasselbe. Stünde jede Zeile wieder als neu da, müsste man sie von Hand
    /// gegen den Bestand halten — und ein übersehener Haken bucht eine Miete doppelt.</para>
    /// <para>Die Erkennung ist dieselbe wie beim Datei-Import und keine zweite: gleiche
    /// Importreferenz heisst vorhanden. Die Referenz kommt aus der Bankreferenz des Satzes, und
    /// die schickt die Bank bei jedem Abruf gleich mit. Einer der Sätze im Beispiel trägt gar
    /// keine — den erkennt der Fingerabdruck aus Tag, Empfänger und Betrag wieder. Das ist der
    /// Fall, an dem sich der Abruf entscheidet: ohne ihn stünde eine Zeile ohne Bankreferenz
    /// bei jedem Mal wieder als neu da.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_zweiter_Abruf_erkennt_die_schon_gebuchten_Saetze()
    {
        var bank = new Attrappe();

        var erster = await HoleAsync(bank);

        // Beim Namen genannt und nicht nur im Verhältnis zueinander: „null neue“ stimmte auch
        // dann, wenn gar nichts ankam. Fünf der sieben Sätze sind buchbar; die beiden anderen
        // sind nur vorgemerkt und nennen einen unleserlichen Betrag.
        Assert.Equal(5, erster.NewCount);
        Assert.Equal(2, erster.ErrorCount);
        Assert.Equal(0, erster.ExistingCount);

        await UebernehmeAsync(erster, erster.Rows.Select(r => r.Index));

        var zweiter = await HoleAsync(bank);

        Assert.Equal(7, zweiter.RecordCount);
        Assert.Equal(0, zweiter.NewCount);
        Assert.Equal(5, zweiter.ExistingCount);

        // Und keiner steht angehakt da. Das ist die zweite Hälfte: Erkennen allein nützt
        // nichts, wenn der Vorschlag trotzdem alles zur Übernahme anbietet. Wer jetzt auf
        // „Übernehmen“ drückt, bucht nichts.
        Assert.DoesNotContain(zweiter.Rows, r => r.PreSelected);
    }

    /// <summary>
    /// Nur das Neue bleibt übrig, wenn ein Teil schon gebucht ist.
    /// </summary>
    /// <remarks>
    /// Der Normalfall im Betrieb: Seit dem letzten Abruf sind ein paar Zeilen dazugekommen. Die
    /// alten dürfen nicht noch einmal als neu gelten, die neuen nicht als vorhanden — sonst
    /// buchte man entweder doppelt oder gar nicht.
    /// </remarks>
    [Fact]
    public async Task Nur_das_Neue_bleibt_uebrig()
    {
        var bank = new Attrappe();

        var erster = await HoleAsync(bank);
        var zweiDavon = erster.Rows
            .Where(r => r.State == ImportRowState.New)
            .Take(2)
            .Select(r => r.Index)
            .ToList();

        Assert.Equal(2, zweiDavon.Count);

        await UebernehmeAsync(erster, zweiDavon);

        var zweiter = await HoleAsync(bank);

        // Zwei von fünf gebucht: Die zwei stehen beim nächsten Mal als vorhanden da, die
        // restlichen drei weiter als neu — und nur diese drei sind angehakt.
        Assert.Equal(2, zweiter.ExistingCount);
        Assert.Equal(3, zweiter.NewCount);
        Assert.Equal(3, zweiter.Rows.Count(r => r.PreSelected));
    }

    /// <summary>Der Abruf landet auf dem Konto, dessen IBAN im Auszug steht.</summary>
    [Fact]
    public async Task Die_Vorschau_schlaegt_das_richtige_Konto_vor()
    {
        var vorschau = await HoleAsync(new Attrappe());

        Assert.Equal(KontoId, vorschau.SuggestedAccountId);
    }

    /// <summary>
    /// Ein Zeitraum ohne Umsätze ist kein Fehler.
    /// </summary>
    /// <remarks>
    /// Wer über Weihnachten nichts gebucht hat, bekommt einen leeren Auszug und keine
    /// Fehlermeldung. Die Vorschau sagt dann schlicht: nichts Neues.
    /// </remarks>
    [Fact]
    public async Task Ein_Zeitraum_ohne_Umsaetze_ist_kein_Fehler()
    {
        var vorschau = await HoleAsync(new LeereAttrappe());

        Assert.Equal(0, vorschau.RecordCount);
        Assert.Equal(0, vorschau.NewCount);
    }

    // ── Der Zeitraum ──────────────────────────────────────────────────────────────────────

    /// <summary>Ohne Angabe fragt der Abruf neunzig Tage zurück bis heute.</summary>
    [Fact]
    public async Task Ohne_Angabe_gilt_das_Standardfenster()
    {
        var bank = new Attrappe();
        await HoleAsync(bank);

        Assert.Equal(new DateOnly(2026, 11, 9), bank.Bis);
        Assert.Equal(
            new DateOnly(2026, 11, 9).AddDays(-BankStatementService.DefaultDays), bank.Von);
    }

    /// <summary>Ein genannter Zeitraum geht unverändert an die Bank.</summary>
    [Fact]
    public async Task Ein_genannter_Zeitraum_geht_durch()
    {
        var bank = new Attrappe();
        await HoleAsync(bank, von: new DateOnly(2026, 8, 1), bis: new DateOnly(2026, 8, 31));

        Assert.Equal(new DateOnly(2026, 8, 1), bank.Von);
        Assert.Equal(new DateOnly(2026, 8, 31), bank.Bis);
    }

    /// <summary>Ein Zeitraum, der nach seinem Ende beginnt, kommt nicht bis zur Bank.</summary>
    [Fact]
    public async Task Ein_verkehrter_Zeitraum_wird_abgewiesen()
    {
        var bank = new Attrappe();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => HoleAsync(bank, von: new DateOnly(2026, 9, 1), bis: new DateOnly(2026, 8, 1)));

        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>
    /// Ein Zeitraum in der Zukunft kommt nicht bis zur Bank.
    /// </summary>
    /// <remarks>
    /// Die Bank antwortete darauf mit leeren Händen, und niemand wüsste, warum. Ein Vertipper im
    /// Jahr sähe dann aus wie ein Konto ohne Umsätze.
    /// </remarks>
    [Fact]
    public async Task Ein_Zeitraum_in_der_Zukunft_wird_abgewiesen()
    {
        var bank = new Attrappe();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => HoleAsync(bank, von: new DateOnly(2027, 1, 1), bis: new DateOnly(2027, 1, 31)));

        Assert.Equal(0, bank.Fragen);
    }

    // ── Wann gar nicht erst gefragt wird ──────────────────────────────────────────────────

    /// <summary>Ohne PIN wird die Bank nicht gefragt.</summary>
    [Fact]
    public async Task Ohne_PIN_wird_die_Bank_nicht_gefragt()
    {
        var bank = new Attrappe();

        await Assert.ThrowsAsync<RuleViolationException>(() => HoleAsync(bank, pin: "   "));

        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>
    /// Ein unvollständiger Zugang wird vor der PIN erkannt.
    /// </summary>
    /// <remarks>
    /// Wer die Zugangsadresse noch gar nicht eingetragen hat, soll nicht erst nach einer PIN
    /// gefragt werden, um dann zu erfahren, dass es an etwas anderem lag.
    /// </remarks>
    [Fact]
    public async Task Ein_unvollstaendiger_Zugang_wird_vor_der_PIN_erkannt()
    {
        using (var vorbereiten = database.Context())
        {
            var konto = vorbereiten.Accounts.Single(a => a.Id == KontoId);
            konto.FinTsUrl = null;
            vorbereiten.SaveChanges();
        }

        var bank = new Attrappe();

        await Assert.ThrowsAsync<RuleViolationException>(() => HoleAsync(bank));

        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>Ein Konto, das es nicht gibt, wird nicht abgerufen.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Konto_wird_abgewiesen()
    {
        using var context = database.Context();
        var bank = new Attrappe();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(bank, context).FetchAsync(4711, "1234"));

        Assert.Equal(0, bank.Fragen);
    }

    /// <summary>
    /// Eine Zurückweisung der Bank kommt unverändert an.
    /// </summary>
    /// <remarks>
    /// Was die Bank sagt, weiss nur sie. „Der Abruf ist fehlgeschlagen“ ohne ihren Wortlaut hätte
    /// uns einen ganzen Abend gekostet — die Meldung nannte am Ende genau die Stelle.
    /// </remarks>
    [Fact]
    public async Task Eine_Zurueckweisung_der_Bank_kommt_unveraendert_an()
    {
        var fehler = await Assert.ThrowsAsync<BankAccessException>(
            () => HoleAsync(new AbweisendeAttrappe("9010 Nachricht nicht bearbeitet.")));

        Assert.Contains("9010", fehler.Message, StringComparison.Ordinal);
    }

    // ── Die PIN ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die PIN geht an die Bank und sonst nirgendwohin.
    /// </summary>
    /// <remarks>
    /// Sie kommt mit der Anfrage, lebt für den Dialog und ist danach weg. Landete sie am Konto,
    /// stünde sie in der Datenbank — und der Abruf im Hintergrund wäre nur noch eine Frage der
    /// Bequemlichkeit.
    /// </remarks>
    [Fact]
    public async Task Die_PIN_geht_an_die_Bank_und_sonst_nirgendwohin()
    {
        var bank = new Attrappe();
        await HoleAsync(bank, pin: "4321");

        Assert.Equal("4321", bank.GesehenePin);
        Assert.Equal(Iban, bank.GesehenesKonto);
    }

    // ── Was dem Zugang fehlt ──────────────────────────────────────────────────────────────

    /// <summary>Die Mängelliste ist dieselbe wie beim Saldo — ein Zugang, eine Antwort.</summary>
    [Fact]
    public async Task Die_Maengelliste_kommt_ohne_Abruf()
    {
        using var context = database.Context();
        var bank = new Attrappe();

        var probleme = await Dienst(bank, context).ProblemsAsync(KontoId);

        Assert.Empty(probleme);
        Assert.Equal(0, bank.Fragen);
    }

    // ── Was im Ablageordner liegt ─────────────────────────────────────────────────────────

    /// <summary>
    /// Nicht alles im Ablageordner kommt von der Bank.
    /// </summary>
    /// <remarks>
    /// <para><b>Dieser Test fehlte, und deshalb scheiterte der erste echte Abruf.</b> Die
    /// Attrappen oben setzen beim fertigen Auszug an und überspringen damit genau den Schritt,
    /// der hier geprüft wird: das Einsammeln der Dateien, die die FinTS-Bibliothek hinterlässt.
    /// Sie legt dort neben den camt-Dokumenten ihre Zwischenspeicher ab — Bankparameter und
    /// Benutzerparameter, beides FinTS-Segmente in Textform. Die liefen in den camt-Leser, und
    /// der ganze Abruf ging an einer Datei verloren, die die Bank nie geschickt hat.</para>
    /// <para>Der Auszug liegt in einem Unterordner, weil die Bibliothek ihn dort ablegt.</para>
    /// </remarks>
    [Fact]
    public async Task Die_Zwischenspeicher_der_Bibliothek_sind_kein_Auszug()
    {
        var ordner = Ablage();

        LegeAuszug(ordner, "unterordner/4711.camt052", Beispiel());
        LegeDatei(ordner, "280_88888888_pruefung.upd", Segmenttext);
        LegeDatei(ordner, "280_88888888.bpd", Segmenttext);

        var auszug = await Quelle().LesenAsync(
            ordner, Konto(), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 9));

        // Dieselben sieben Sätze wie aus der hochgeladenen Datei — und keiner davon ist die
        // Meldung, dass etwas kein XML war.
        Assert.Equal(7, auszug.Records.Count);
        Assert.Equal(Iban, auszug.Iban);
        Assert.DoesNotContain(
            auszug.Records, r => r.Problem is not null && r.Problem.Contains("XML"));
    }

    /// <summary>
    /// Liegen nur Zwischenspeicher da, hat die Bank nichts gemeldet.
    /// </summary>
    /// <remarks>
    /// Ein Zeitraum ohne Umsätze ist ein gültiges Ergebnis und kein Fehler. Vorher warf dieser
    /// Fall eine Formatmeldung — über eine Datei, nach der niemand gefragt hatte.
    /// </remarks>
    [Fact]
    public async Task Ein_Ordner_ohne_Auszug_meldet_keine_Umsaetze()
    {
        var ordner = Ablage();

        LegeDatei(ordner, "280_88888888_pruefung.upd", Segmenttext);

        var auszug = await Quelle().LesenAsync(
            ordner, Konto(), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 9));

        Assert.Empty(auszug.Records);
        Assert.Equal(Iban, auszug.Iban);
    }

    /// <summary>
    /// Ein unlesbares Dokument nimmt die anderen nicht mit.
    /// </summary>
    /// <remarks>
    /// Die Bank schickt pro Zeitraum mehrere Dokumente. Wäre eines davon kaputt und risse den
    /// Abruf ab, fehlten die Umsätze der anderen — und warum, stünde nirgends. Stattdessen steht
    /// es als Zeile mit Grund in der Vorschau, so wie eine kaputte Datei in einem Auszugsarchiv.
    /// </remarks>
    [Fact]
    public async Task Ein_unlesbares_Dokument_nimmt_die_anderen_nicht_mit()
    {
        var ordner = Ablage();

        LegeAuszug(ordner, "1000.camt052", Beispiel());
        LegeAuszug(ordner, "2000.camt052", "<Document>abgeschnitten");

        var auszug = await Quelle().LesenAsync(
            ordner, Konto(), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 9));

        Assert.Equal(8, auszug.Records.Count);
        Assert.Single(auszug.Records, r => r.Payee == "Dokument 2 des Abrufs");
        Assert.Single(auszug.Records, r => r.Problem is not null && r.Problem.Contains("XML"));

        // Die sieben aus dem heilen Dokument sind vollständig da.
        Assert.Equal(7, auszug.Records.Count(r => r.Payee != "Dokument 2 des Abrufs"));
    }

    /// <summary>
    /// Ist kein einziges Dokument zu lesen, ist das kein leerer Zeitraum.
    /// </summary>
    /// <remarks>
    /// „Keine Umsätze“ wäre hier eine falsche Auskunft: Die Bank hat etwas geschickt, es war nur
    /// nicht zu lesen. Wer das verwechselt, hält einen Monat für leer, in dem gebucht wurde.
    /// </remarks>
    [Fact]
    public async Task Sind_alle_Dokumente_unlesbar_scheitert_der_Abruf()
    {
        var ordner = Ablage();

        LegeAuszug(ordner, "1000.camt052", "<Document>abgeschnitten");

        var fehler = await Assert.ThrowsAsync<StatementFormatException>(
            () => Quelle().LesenAsync(
                ordner, Konto(), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 9)));

        Assert.Contains("keines", fehler.Message, StringComparison.Ordinal);
    }

    // ── Hilfsmittel für den Ablageordner ──────────────────────────────────────────────────

    /// <summary>
    /// Ein erfundenes FinTS-Segment, wie es in den Zwischenspeichern steht.
    /// </summary>
    /// <remarks>
    /// Kein XML — darauf kommt es an. Bankleitzahl und Kennung sind ausgedacht; was am echten
    /// Zugang steht, gehört nicht in einen öffentlichen Quelltext.
    /// </remarks>
    private const string Segmenttext =
        "HIUPD:5:6:4+0000000000::280:88888888+DE00888888880000000000+pruefung+1+EUR'";

    private DirectoryInfo Ablage()
        => Directory.CreateDirectory(Path.Combine(root, "abruf", Guid.NewGuid().ToString("N")));

    private static void LegeAuszug(DirectoryInfo ordner, string pfad, string inhalt)
        => LegeDatei(ordner, pfad, inhalt);

    private static void LegeDatei(DirectoryInfo ordner, string pfad, string inhalt)
    {
        var ziel = new FileInfo(Path.Combine(ordner.FullName, pfad));
        ziel.Directory!.Create();
        File.WriteAllText(ziel.FullName, inhalt);
    }

    private static FinTsStatementSource Quelle()
        => new(Produkt(), new CamtStatementParser(),
            NullLogger<FinTsStatementSource>.Instance, NullLoggerFactory.Instance);

    private Account Konto()
    {
        using var context = database.Context();
        return context.Accounts.Single(a => a.Id == KontoId);
    }
}
