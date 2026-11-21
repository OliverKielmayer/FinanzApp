using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Überweisungen aus der Anwendung heraus.
/// </summary>
/// <remarks>
/// <para><b>Was hier geprüft wird, ist alles, was sich prüfen lässt.</b> Der Auftrag selbst
/// geht an eine Bank, und die tut auf Kommando nichts — der Weg dorthin steckt deshalb hinter
/// <see cref="IBankTransferSource"/> und wird hier durch eine Attrappe ersetzt. Was davor
/// entschieden wird, ist der ganze Rest: die Prüfziffer, die Obergrenze, der zweite Klick.</para>
/// <para><b>Eine Überweisung holt niemand zurück.</b> Deshalb steht jede Prüfung vor dem
/// Bankaufruf, und die Zeile in der Tabelle entsteht vor ihm. Die letzten beiden Prüfungen
/// halten fest, was nach einem Abbruch dasteht — und dass das <em>nicht</em> „abgelehnt“
/// heißt.</para>
/// <para>Die IBANs sind erfunden und tragen richtige Prüfziffern; sie gehören keinem.</para>
/// </remarks>
public sealed class TransferTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 21);

    private readonly BankAccessOptions options = new()
    {
        ProductId = "1234567890ABCDEF12345678",
        ProductVersion = "1.0",
        TransferLimit = 5000m,
    };

    /// <summary>Erfundene IBANs mit aufgehender Prüfziffer.</summary>
    private const string Gut = "DE02120300000000202051";

    private const string Verdreht = "DE02120300000000202015";

    private readonly int konto;

    /// <summary>Was die Attrappe von der Bank behauptet.</summary>
    private sealed class Attrappe : IBankTransferSource
    {
        public bool Accepted { get; set; } = true;

        public Exception? Wirft { get; set; }

        public int Aufrufe { get; private set; }

        public decimal LetzterBetrag { get; private set; }

        public string? LetzteIban { get; private set; }

        public Task<TransferOutcome> SubmitAsync(
            Account account, string pin, string receiverName, string receiverIban,
            string? receiverBic, decimal amount, string purpose, CancellationToken ct = default)
        {
            Aufrufe++;
            LetzterBetrag = amount;
            LetzteIban = receiverIban;

            if (Wirft is { } fehler)
            {
                throw fehler;
            }

            return Task.FromResult(new TransferOutcome(
                Accepted, Accepted ? "0010 Auftrag entgegengenommen" : "9210 Auftrag abgelehnt"));
        }
    }

    private readonly Attrappe bank = new();

    public TransferTests()
    {
        using var context = database.Context();

        var account = new Account
        {
            Name = "Girokonto",
            ShortName = "Giro",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            Iban = "DE02500105170137075030",
            Blz = "50010517",
            Bic = "INGDDEFFXXX",
            FinTsUrl = "https://example.invalid/fints",
            FinTsUserId = "4711",
            FinTsTanMethod = 921,
        };

        context.Accounts.Add(account);
        context.SaveChanges();

        konto = account.Id;
    }

    private TransferService Dienst(FinanzAppDbContext context)
        => new(context, bank, options, clock);

    private Task<TransferDto> UeberweisenAsync(
        decimal betrag = 199.74m,
        string iban = Gut,
        string empfaenger = "Musterbetrieb GmbH",
        string zweck = "Rechnung 4711",
        string pin = "123456",
        string? bic = null)
    {
        var context = database.Context();

        return Dienst(context).SubmitAsync(new TransferRequest
        {
            AccountId = konto,
            Pin = pin,
            ReceiverName = empfaenger,
            ReceiverIban = iban,
            ReceiverBic = bic,
            Amount = betrag,
            Purpose = zweck,
        });
    }

    // ── Die Prüfziffer ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei vertauschte Ziffern kommen nicht durch.
    /// </summary>
    /// <remarks>
    /// Der häufigste Fehler beim Abtippen, und der teuerste: Die Bank nimmt eine formal
    /// richtige IBAN an und überweist an jemand anderen. Zurückholen geht nur mit dessen
    /// Einverständnis.
    /// </remarks>
    [Fact]
    public async Task Eine_verdrehte_IBAN_kommt_nicht_zur_Bank()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => UeberweisenAsync(iban: Verdreht));

        Assert.Contains("Prüfziffer", fehler.Message);
        Assert.Equal(0, bank.Aufrufe);
    }

    /// <summary>Leerzeichen in Vierergruppen sind keine falsche IBAN.</summary>
    /// <remarks>
    /// Auf dem Beleg steht sie so, und wer sie abtippt, tippt sie so ab. Eine Prüfung, die
    /// daran scheitert, prüft die Schreibweise statt der Nummer.
    /// </remarks>
    [Fact]
    public async Task Eine_IBAN_in_Vierergruppen_wird_angenommen()
    {
        await UeberweisenAsync(iban: "DE02 1203 0000 0000 2020 51");

        Assert.Equal(Gut, bank.LetzteIban);
    }

    [Fact]
    public async Task Eine_BIC_mit_neun_Zeichen_kommt_nicht_durch()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => UeberweisenAsync(bic: "INGDDEFFX"));

        Assert.Contains("BIC", fehler.Message);
        Assert.Equal(0, bank.Aufrufe);
    }

    /// <summary>Ohne BIC geht es — im SEPA-Raum findet die Bank das Institut über die IBAN.</summary>
    [Fact]
    public async Task Ohne_BIC_geht_es_auch()
    {
        var auftrag = await UeberweisenAsync(bic: "");

        Assert.Equal(TransferState.Accepted, auftrag.State);
    }

    // ── Die Obergrenze ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Über der Schranke geht nichts hinaus.
    /// </summary>
    /// <remarks>
    /// Sie fängt die Null zu viel ab. Ein Auftrag über das Zehnfache sieht auf dem Bildschirm
    /// aus wie jeder andere — und er ist weg, bevor jemand hinsieht.
    /// </remarks>
    [Fact]
    public async Task Ueber_der_Obergrenze_geht_nichts_hinaus()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => UeberweisenAsync(betrag: 5000.01m));

        Assert.Contains("Obergrenze", fehler.Message);
        Assert.Equal(0, bank.Aufrufe);
    }

    [Fact]
    public async Task Genau_auf_der_Obergrenze_geht_es_noch()
    {
        await UeberweisenAsync(betrag: 5000m);

        Assert.Equal(5000m, bank.LetzterBetrag);
    }

    [Fact]
    public async Task Null_und_weniger_ist_kein_Betrag()
    {
        await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync(betrag: 0m));
        await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync(betrag: -5m));

        Assert.Equal(0, bank.Aufrufe);
    }

    // ── Der zweite Klick ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derselbe Auftrag geht kein zweites Mal hinaus.
    /// </summary>
    /// <remarks>
    /// Der zweite Klick kommt innerhalb von Sekunden, und niemand sieht ihm an, dass der
    /// erste schon durch war. Bei einer Überweisung ist das kein Schönheitsfehler.
    /// </remarks>
    [Fact]
    public async Task Derselbe_Auftrag_geht_kein_zweites_Mal_hinaus()
    {
        await UeberweisenAsync();

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync());

        Assert.Contains("schon erteilt", fehler.Message);
        Assert.Equal(1, bank.Aufrufe);
    }

    /// <summary>Ein anderer Verwendungszweck ist ein anderer Auftrag.</summary>
    /// <remarks>
    /// Die zweite Rate an denselben Empfänger soll möglich bleiben. Unterscheidbar ist sie am
    /// Verwendungszweck — genau das sagt die Meldung der Schranke auch.
    /// </remarks>
    [Fact]
    public async Task Ein_anderer_Zweck_ist_ein_anderer_Auftrag()
    {
        await UeberweisenAsync(zweck: "Rechnung 4711");
        await UeberweisenAsync(zweck: "Rechnung 4712");

        Assert.Equal(2, bank.Aufrufe);
    }

    /// <summary>Ein abgelehnter Auftrag blockiert den zweiten Versuch nicht.</summary>
    [Fact]
    public async Task Nach_einer_Ablehnung_darf_es_noch_einmal()
    {
        bank.Accepted = false;
        await UeberweisenAsync();

        bank.Accepted = true;
        var zweiter = await UeberweisenAsync();

        Assert.Equal(TransferState.Accepted, zweiter.State);
        Assert.Equal(2, bank.Aufrufe);
    }

    // ── Was ohne Antwort dasteht ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reißt die Verbindung ab, heißt das nicht „abgelehnt“.
    /// </summary>
    /// <remarks>
    /// <b>Die wichtigste Prüfung hier.</b> Wer einen Abbruch als Ablehnung liest, erteilt den
    /// Auftrag ein zweites Mal — und beide kommen an. Ob der erste die Bank erreicht hat, weiß
    /// niemand; nachsehen muss ein Mensch im Kontoauszug.
    /// </remarks>
    [Fact]
    public async Task Ein_Abbruch_ist_keine_Ablehnung()
    {
        bank.Wirft = new BankAccessException("Die Verbindung wurde unterbrochen.");

        await Assert.ThrowsAsync<BankAccessException>(() => UeberweisenAsync());

        using var context = database.Context();
        var auftrag = context.BankTransfers.Single();

        Assert.Equal(TransferState.Unknown, auftrag.State);
        Assert.Contains("unterbrochen", auftrag.BankMessage);
    }

    /// <summary>Und er blockiert den zweiten Versuch — aus demselben Grund.</summary>
    [Fact]
    public async Task Nach_einem_Abbruch_haelt_die_Schranke()
    {
        bank.Wirft = new BankAccessException("Die Verbindung wurde unterbrochen.");
        await Assert.ThrowsAsync<BankAccessException>(() => UeberweisenAsync());

        bank.Wirft = null;

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync());

        Assert.Contains("schon erteilt", fehler.Message);
        Assert.Equal(1, bank.Aufrufe);
    }

    // ── Der Rest ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ohne_PIN_wird_nichts_beauftragt()
    {
        await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync(pin: "  "));

        Assert.Equal(0, bank.Aufrufe);
    }

    [Fact]
    public async Task Ohne_Verwendungszweck_wird_nichts_beauftragt()
    {
        await Assert.ThrowsAsync<RuleViolationException>(() => UeberweisenAsync(zweck: " "));

        Assert.Equal(0, bank.Aufrufe);
    }

    /// <summary>Ein zu langer Verwendungszweck wird abgewiesen, nicht abgeschnitten.</summary>
    /// <remarks>
    /// Abgeschnitten käme beim Empfänger eine halbe Rechnungsnummer an, und niemand hätte es
    /// gesagt.
    /// </remarks>
    [Fact]
    public async Task Ein_zu_langer_Zweck_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => UeberweisenAsync(zweck: new string('x', 141)));

        Assert.Contains("140", fehler.Message);
        Assert.Equal(0, bank.Aufrufe);
    }

    /// <summary>Die Zeile entsteht vor dem Bankaufruf und trägt danach die Antwort.</summary>
    [Fact]
    public async Task Der_Auftrag_wird_festgehalten()
    {
        await UeberweisenAsync();

        using var context = database.Context();
        var auftrag = context.BankTransfers.Single();

        Assert.Equal(TransferState.Accepted, auftrag.State);
        Assert.Equal(199.74m, auftrag.Amount);
        Assert.Equal(Gut, auftrag.ReceiverIban);
        Assert.Contains("entgegengenommen", auftrag.BankMessage);
        Assert.NotNull(auftrag.AnsweredAt);
    }

    public void Dispose() => database.Dispose();
}
