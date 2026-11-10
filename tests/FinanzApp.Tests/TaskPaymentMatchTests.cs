using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Vorgang aus einem Schreiben findet die Buchung, die ihn bezahlt hat.
/// </summary>
/// <remarks>
/// <para>Eine erfasste Rechnung weiss, ob sie bezahlt ist. Ein Bescheid weiss nichts — da steht
/// ein Satz auf Papier, und ob ihm jemand nachgekommen ist, stand bisher nirgends. Der Vorgang
/// blieb offen, bis ein Mensch ihn abhakte, und das Geld war längst weg.</para>
/// <para><b>Die Form dieser Prüfungen ist der echten Ablage abgesehen, die Zahlen sind
/// ausgedacht.</b> Zwei Bescheide an zwei Personen, derselbe Betrag, am selben Tag an denselben
/// Empfänger überwiesen — das kommt wirklich vor, und genau daran zerbricht ein Abgleich, der
/// auf Betrag, Tag und Empfänger schaut: Er hat je zwei Kandidaten und ordnet sie womöglich
/// über Kreuz zu. Auffallen würde es nie, denn die Summe stimmt.</para>
/// </remarks>
public sealed class TaskPaymentMatchTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 6);

    private const decimal Betrag = 3650m;

    /// <summary>Derselbe Betrag, wie ihn ein deutsches Schreiben schreibt.</summary>
    /// <remarks>
    /// Ausgeschrieben und nicht formatiert: Eine Zahlenformatierung richtet sich nach der Kultur
    /// des Rechners, auf dem die Prüfung läuft, und dann stünde im Papier „3,650.00“. Der Leser
    /// käme damit zurecht, aber geprüft werden soll die Form, die wirklich vorkommt.
    /// </remarks>
    private const string BetragAufPapier = "3.650,00";

    private static readonly DateOnly Frist = new(2026, 11, 20);
    private static readonly DateOnly Zahltag = new(2026, 10, 28);

    private int KontoId { get; }

    public TaskPaymentMatchTests()
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = "Girokonto",
            ShortName = "Giro",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            OpeningBalance = 0m,
            BalanceAsOf = new DateOnly(2026, 1, 1),
        };

        context.Accounts.Add(konto);
        context.SaveChanges();
        KontoId = konto.Id;
    }

    public void Dispose() => database.Dispose();

    // ── Der Fall, um den es geht ──────────────────────────────────────────────────────────

    /// <summary>
    /// Zwei gleiche Forderungen, zwei gleiche Zahlungen — und jede findet ihre eigene.
    /// </summary>
    /// <remarks>
    /// Die Probe aufs Ganze. Betrag, Tag und Empfänger sind bei beiden identisch; was sie
    /// trennt, ist allein das Aktenzeichen im Verwendungszweck, das auch im Beleg steht. Ohne
    /// diese Spur müsste der Abgleich raten, und raten hiesse: in der Hälfte der Fälle falsch,
    /// ohne dass es jemals auffiele.
    /// </remarks>
    [Fact]
    public async Task Zwei_gleiche_Forderungen_finden_jede_ihre_eigene_Zahlung()
    {
        var belegA = Beleg("Bescheid A", "12345/67890");
        var belegB = Beleg("Bescheid B", "12346/67891");

        var zahlungA = Buchung("Steuernummer 12345/67890");
        var zahlungB = Buchung("Steuernummer 12346/67891");

        await ListeLesenAsync();

        Assert.Equal(zahlungA, await BuchungAmBelegAsync(belegA));
        Assert.Equal(zahlungB, await BuchungAmBelegAsync(belegB));

        foreach (var vorgang in await VorgaengeAsync())
        {
            Assert.Equal(TaskState.Done, vorgang.State);
        }
    }

    /// <summary>Der geschlossene Vorgang sagt, woran er es festgemacht hat.</summary>
    /// <remarks>
    /// Ein Vorgang, der von selbst zuging und niemandem sagen kann warum, ist schlimmer als
    /// einer, der offen blieb: Beim offenen sieht man wenigstens, dass etwas fehlt.
    /// </remarks>
    [Fact]
    public async Task Der_geschlossene_Vorgang_nennt_seinen_Grund()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Steuernummer 12345/67890");

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.Equal(TaskState.Done, vorgang.State);
        Assert.Contains("bezahlt", vorgang.Detail);
        Assert.Contains("28.10.2026", vorgang.Detail);
        Assert.Contains("12345/67890", vorgang.Detail);
    }

    /// <summary>
    /// Eine Zahlung vor dem Belegdatum zählt genauso.
    /// </summary>
    /// <remarks>
    /// Bezahlt wird, wenn das Geld bereit ist; abgelegt wird, wenn jemand Zeit hat. In der
    /// echten Ablage liegen zwischen beidem Tage in der falschen Richtung — ein Fenster „vom
    /// Belegdatum bis zur Frist“ fände solche Zahlungen nie.
    /// </remarks>
    [Fact]
    public async Task Eine_Zahlung_vor_dem_Belegdatum_zaehlt_auch()
    {
        var beleg = Beleg("Bescheid", "12345/67890", datum: new DateOnly(2026, 10, 20));
        var zahlung = Buchung("Steuernummer 12345/67890", tag: new DateOnly(2026, 9, 30));

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.Equal(TaskState.Done, vorgang.State);
        Assert.Equal(zahlung, await BuchungAmBelegAsync(beleg));
    }

    /// <summary>
    /// Ein früher Beleg mit später Frist lässt weiter zurücksuchen.
    /// </summary>
    /// <remarks>
    /// <para>Ein Bescheid vom Frühsommer, zahlbar erst im Spätherbst, bezahlt im Juli: Gerechnet
    /// von der Frist liegt diese Zahlung ausserhalb jeder vernünftigen Spanne, gerechnet vom
    /// Papier mitten darin. Deshalb zählt für den Anfang des Fensters das frühere der beiden
    /// Daten.</para>
    /// <para>Die Prüfung eine Ebene höher — Zahlung vor dem Belegdatum — kommt dafür nicht auf:
    /// Dort liegen Beleg und Frist nah beieinander, und schon die Spanne allein fängt die
    /// Zahlung. Erst wenn beide Daten weit auseinanderliegen, trägt die Regel überhaupt etwas
    /// bei.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_frueher_Beleg_laesst_weiter_zurueck_suchen()
    {
        var beleg = Beleg("Bescheid", "12345/67890", datum: new DateOnly(2026, 6, 15));
        var zahlung = Buchung("Steuernummer 12345/67890", tag: new DateOnly(2026, 7, 1));

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.Equal(TaskState.Done, vorgang.State);
        Assert.Equal(zahlung, await BuchungAmBelegAsync(beleg));
    }

    // ── Wo die Anwendung nicht entscheidet ────────────────────────────────────────────────

    /// <summary>
    /// Ohne Spur vom Papier zur Zahlung wird nichts geschlossen, sondern vorgeschlagen.
    /// </summary>
    /// <remarks>
    /// Der Betrag allein kann jede andere Zahlung derselben Höhe auch. Eine falsch geschlossene
    /// Forderung sieht genauso aus wie eine richtig geschlossene — deshalb entscheidet hier ein
    /// Mensch, und die Anwendung legt ihm nur den Fund hin.
    /// </remarks>
    [Fact]
    public async Task Ohne_Beweis_wird_vorgeschlagen_und_nicht_geschlossen()
    {
        Beleg("Bescheid", "12345/67890");
        var zahlung = Buchung("Ueberweisung");

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);

        var vorschlag = Assert.Single(vorgang.PaymentSuggestions);
        Assert.Equal(zahlung, vorschlag.TransactionId);
        Assert.Null(vorschlag.Evidence);
    }

    /// <summary>Passen zwei Zahlungen und keine trägt eine Nummer, bleibt der Vorgang offen.</summary>
    [Fact]
    public async Task Zwei_namenlose_Treffer_schliessen_nichts()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Ueberweisung");
        Buchung("Dauerauftrag", tag: new DateOnly(2026, 10, 27));

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);
        Assert.Equal(2, vorgang.PaymentSuggestions.Count);
    }

    /// <summary>
    /// Der Betrag im Verwendungszweck ist kein Beweis.
    /// </summary>
    /// <remarks>
    /// Er steht natürlich im Beleg — er hat den Treffer ja erst hergestellt. Zählte er auch als
    /// Bestätigung, bestätigte sich der Abgleich selbst, und jede Zahlung der richtigen Höhe
    /// schlösse den Vorgang.
    /// </remarks>
    [Fact]
    public async Task Der_Betrag_im_Verwendungszweck_beweist_nichts()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Rechnungsbetrag 3650.00 beglichen");

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);
        Assert.Null(Assert.Single(vorgang.PaymentSuggestions).Evidence);
    }

    [Fact]
    public async Task Ein_anderer_Betrag_passt_nicht()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Steuernummer 12345/67890", betrag: Betrag + 0.01m);

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);
        Assert.Empty(vorgang.PaymentSuggestions);
    }

    [Fact]
    public async Task Eine_Zahlung_lange_vor_der_Frist_passt_nicht()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Steuernummer 12345/67890", tag: new DateOnly(2026, 5, 4));

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);
        Assert.Empty(vorgang.PaymentSuggestions);
    }

    /// <summary>
    /// Eine Forderung ohne Betrag wird gar nicht erst abgeglichen.
    /// </summary>
    /// <remarks>
    /// „Bitte stellen Sie Ihren Einzug um“ verlangt etwas und nennt keine Summe. Es gibt nichts,
    /// wogegen zu vergleichen wäre, und das bleibt so.
    /// </remarks>
    [Fact]
    public async Task Eine_Forderung_ohne_Betrag_wird_nicht_abgeglichen()
    {
        Beleg("Schreiben", "12345/67890", zeile: "Bitte stellen Sie Ihren Einzug bis zum 20.11.2026 um.");
        Buchung("Steuernummer 12345/67890");

        var vorgang = Assert.Single(await ListeLesenAsync());

        Assert.NotEqual(TaskState.Done, vorgang.State);
        Assert.Empty(vorgang.PaymentSuggestions);
    }

    // ── Von Hand ──────────────────────────────────────────────────────────────────────────

    /// <summary>Ein Vorschlag lässt sich annehmen.</summary>
    [Fact]
    public async Task Ein_Vorschlag_laesst_sich_annehmen()
    {
        var beleg = Beleg("Bescheid", "12345/67890");
        var zahlung = Buchung("Ueberweisung");

        var vorgang = Assert.Single(await ListeLesenAsync());

        using (var context = database.Context())
        {
            Assert.True(await new TaskService(context, clock).AssignPaymentAsync(vorgang.Id, zahlung));
        }

        Assert.Equal(zahlung, await BuchungAmBelegAsync(beleg));
        Assert.Equal(TaskState.Done, Assert.Single(await VorgaengeAsync()).State);
    }

    /// <summary>
    /// Eine Buchung, die gar nicht in Frage kommt, lässt sich nicht zuordnen.
    /// </summary>
    /// <remarks>
    /// Wer auf eine Schaltfläche drückt, hat die Liste gesehen und nicht den Betrag
    /// nachgerechnet. Die Prüfung gehört deshalb dorthin, wo geschrieben wird.
    /// </remarks>
    [Fact]
    public async Task Eine_fremde_Buchung_laesst_sich_nicht_zuordnen()
    {
        Beleg("Bescheid", "12345/67890");
        Buchung("Ueberweisung");
        var fremde = Buchung("Miete", betrag: 950m, tag: new DateOnly(2026, 10, 2));

        var vorgang = Assert.Single(await ListeLesenAsync());

        using var context = database.Context();

        Assert.False(await new TaskService(context, clock).AssignPaymentAsync(vorgang.Id, fremde));
        Assert.NotEqual(TaskState.Done, context.TaskItems.Single(t => t.Id == vorgang.Id).State);
    }

    // ── Hilfsmittel ───────────────────────────────────────────────────────────────────────

    private int Beleg(string titel, string aktenzeichen, DateOnly? datum = null, string? zeile = null)
    {
        using var context = database.Context();

        var beleg = new Document
        {
            Title = titel,
            RelativePath = $"Wohnen/{Guid.NewGuid():N}.pdf",
            FileName = $"{titel}.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Housing,
            DocumentDate = datum ?? new DateOnly(2026, 11, 1),
            DueDate = Frist,
            ActionLine = zeile
                         ?? $"Bitte überweisen Sie spätestens am 20.11.2026 {BetragAufPapier} EUR.",
            ContentText = $"Aktenzeichen {aktenzeichen}\nBetrag {BetragAufPapier} EUR"
                          + "\nZahlbar bis 20.11.2026",
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(beleg);
        context.SaveChanges();
        return beleg.Id;
    }

    private int Buchung(string zweck, decimal? betrag = null, DateOnly? tag = null)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            AccountId = KontoId,
            BookingDate = tag ?? Zahltag,
            Payee = "Musteramt",
            Purpose = zweck,
            Amount = -(betrag ?? Betrag),
            Kind = TransactionKind.Expense,
            CreatedAt = clock.Now,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();
        return buchung.Id;
    }

    /// <summary>Die Liste lesen — dabei läuft der Abgleich.</summary>
    private async Task<IReadOnlyList<TaskItemDto>> ListeLesenAsync()
    {
        using var context = database.Context();
        var liste = await new TaskService(context, clock).GetListAsync();

        return [.. liste.Items.Where(i => i.Source == TaskSource.DocumentAction)];
    }

    private async Task<IReadOnlyList<TaskItem>> VorgaengeAsync()
    {
        using var context = database.Context();

        return await context.TaskItems
            .Where(t => t.Source == TaskSource.DocumentAction)
            .ToListAsync();
    }

    /// <summary>Die Buchung, an der dieser Beleg hängt. 0 heisst: keine.</summary>
    private async Task<int> BuchungAmBelegAsync(int belegId)
    {
        using var context = database.Context();

        var links = await context.DocumentLinks
            .Where(l => l.DocumentId == belegId && l.TargetType == LinkTargetType.Transaction)
            .Select(l => l.TargetId)
            .ToListAsync();

        return links.Count == 1 ? links[0] : 0;
    }
}
