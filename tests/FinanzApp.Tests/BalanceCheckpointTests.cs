using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Abgleich zwischen dem gerechneten Saldo und dem tatsächlichen.
/// </summary>
/// <remarks>
/// <para><b>Hier läuft keine Bank mit.</b> Ein Kontrollpunkt ist eine Zahl mit einem Stichtag,
/// und woher sie kommt, ist dem Saldo gleichgültig — von Hand getippt wirkt sie genauso wie aus
/// einer Saldenabfrage übernommen. Genau das ist der Punkt: Konten ohne Bankzugang sollen
/// abgleichbar sein, und wenn der Abruf klemmt, bleibt die Tastatur.</para>
/// <para>Die Zahlen sind klein und ausgedacht und sagen über den Bestand des Benutzers
/// nichts.</para>
/// </remarks>
public sealed class BalanceCheckpointTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 10);

    private int KontoId { get; }

    public BalanceCheckpointTests()
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = "Girokonto",
            ShortName = "Giro",
            BankName = "Musterbank",
            Kind = AccountKind.Checking,

            // Kein Bankzugang: Dieses Konto kann gar nicht abrufen. Es ist der Fall, für den
            // der Weg von Hand da sein muss.
            OpeningBalance = 100m,
            BalanceAsOf = new DateOnly(2026, 1, 1),
        };

        context.Accounts.Add(konto);
        context.SaveChanges();
        KontoId = konto.Id;
    }

    public void Dispose() => database.Dispose();

    // ── Hilfsmittel ───────────────────────────────────────────────────────────────────────

    private void Buche(DateOnly tag, decimal betrag)
    {
        using var context = database.Context();

        context.Transactions.Add(new Transaction
        {
            AccountId = KontoId,
            BookingDate = tag,
            Payee = "Prüfung",
            Amount = betrag,
            Kind = betrag < 0 ? TransactionKind.Expense : TransactionKind.Income,
            CreatedAt = clock.Now,
        });

        context.SaveChanges();
    }

    private async Task<decimal> SaldoAsync()
    {
        using var context = database.Context();
        var salden = await new AccountService(context).GetBalancesAsync();
        return salden[KontoId];
    }

    private async Task<BalanceCheckpointDto> SetzeAsync(
        DateOnly stichtag, decimal stand,
        BalanceCheckpointSource herkunft = BalanceCheckpointSource.Manual)
    {
        using var context = database.Context();

        return await Dienst(context).AddAsync(KontoId, new BalanceCheckpointRequest
        {
            AsOf = stichtag,
            Balance = stand,
            Source = herkunft,
        });
    }

    private BalanceCheckpointService Dienst(FinanzAppDbContext context)
        => new(context, new AccountService(context), clock);

    // ── Die Rechnung ──────────────────────────────────────────────────────────────────────

    /// <summary>Ohne Kontrollpunkt bleibt alles, wie es war.</summary>
    /// <remarks>
    /// Die Neuerung darf an Konten, die niemand abgleicht, nichts ändern. Das sind die meisten.
    /// </remarks>
    [Fact]
    public async Task Ohne_Kontrollpunkt_gilt_der_Anfangsbestand()
    {
        Buche(new DateOnly(2026, 3, 1), -30m);
        Buche(new DateOnly(2026, 4, 1), 50m);

        Assert.Equal(120m, await SaldoAsync());
    }

    /// <summary>
    /// Mit Kontrollpunkt rechnet der Saldo von dort an weiter.
    /// </summary>
    /// <remarks>
    /// Der Fall, um den es geht: Vor dem Stichtag fehlt etwas — hier eine Buchung, die es nie in
    /// die Anwendung geschafft hat. Nach dem Abgleich stimmt der Stand trotzdem, und zwar ohne
    /// dass jemand die fehlende Buchung finden musste.
    /// </remarks>
    [Fact]
    public async Task Mit_Kontrollpunkt_zaehlt_nur_was_danach_gebucht_wurde()
    {
        Buche(new DateOnly(2026, 3, 1), -30m);

        // Gerechnet wären 70. Tatsächlich waren es 55 — irgendwo fehlen 15.
        await SetzeAsync(new DateOnly(2026, 6, 30), 55m);

        Buche(new DateOnly(2026, 7, 5), 20m);

        Assert.Equal(75m, await SaldoAsync());
    }

    /// <summary>
    /// Eine Buchung vom Stichtag selbst steckt schon im festgestellten Stand.
    /// </summary>
    /// <remarks>
    /// Der Fehler, der hier lauert, ist eine Vorzeichenstelle: Wer <c>&gt;=</c> statt <c>&gt;</c>
    /// schreibt, zählt den Tag des Abgleichs doppelt. Das fällt an einem Konto mit einer Buchung
    /// pro Tag niemandem auf — und es ist jedes Mal falsch.
    /// </remarks>
    [Fact]
    public async Task Eine_Buchung_vom_Stichtag_wird_nicht_doppelt_gezaehlt()
    {
        Buche(new DateOnly(2026, 6, 30), -40m);

        await SetzeAsync(new DateOnly(2026, 6, 30), 60m);

        Assert.Equal(60m, await SaldoAsync());
    }

    /// <summary>Der jüngste Punkt gilt, der ältere wird übergangen.</summary>
    [Fact]
    public async Task Der_juengste_Kontrollpunkt_gewinnt()
    {
        await SetzeAsync(new DateOnly(2026, 6, 30), 55m);
        await SetzeAsync(new DateOnly(2026, 9, 30), 200m);

        Buche(new DateOnly(2026, 10, 1), -20m);

        // Und zwar auch dann, wenn vor dem jüngsten Punkt noch etwas gebucht wird: Das liegt
        // davor und steckt in seiner Zahl.
        Buche(new DateOnly(2026, 8, 15), -999m);

        Assert.Equal(180m, await SaldoAsync());
    }

    /// <summary>
    /// Zwei Punkte auf denselben Stichtag: der zuletzt angelegte gilt.
    /// </summary>
    /// <remarks>
    /// So korrigiert man einen Vertipper, ohne erst löschen zu müssen. Ohne die Regel entschiede
    /// die Reihenfolge der Datenbank, und die ist keine Zusage.
    /// </remarks>
    [Fact]
    public async Task Bei_gleichem_Stichtag_gilt_der_zuletzt_angelegte()
    {
        await SetzeAsync(new DateOnly(2026, 6, 30), 55m);
        await SetzeAsync(new DateOnly(2026, 6, 30), 65m);

        Assert.Equal(65m, await SaldoAsync());
    }

    // ── Was festgehalten wird ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Differenz wird gegen den Stand <em>desselben Tages</em> gebildet.
    /// </summary>
    /// <remarks>
    /// Nicht gegen den heutigen: Zwischen Stichtag und heute liegen Buchungen, und eine
    /// Differenz aus zwei verschiedenen Zeitpunkten beschriebe nichts. Hier liegen zwischen
    /// beiden Tagen vierzig Euro, die den Befund sonst verfälschten.
    /// </remarks>
    [Fact]
    public async Task Die_Differenz_gilt_dem_Stichtag_und_nicht_heute()
    {
        Buche(new DateOnly(2026, 3, 1), -30m);     // davor: gerechnet 70
        Buche(new DateOnly(2026, 8, 1), -40m);     // danach, zählt nicht mit

        var punkt = await SetzeAsync(new DateOnly(2026, 6, 30), 55m);

        Assert.Equal(70m, punkt.LedgerBalance);
        Assert.Equal(-15m, punkt.Difference);
        Assert.False(punkt.WasAligned);
    }

    /// <summary>Stimmen beide Zahlen, ist die Differenz null und nichts war auszugleichen.</summary>
    [Fact]
    public async Task Ohne_Abweichung_ist_der_Punkt_nur_eine_Bestaetigung()
    {
        Buche(new DateOnly(2026, 3, 1), -30m);

        var punkt = await SetzeAsync(new DateOnly(2026, 6, 30), 70m);

        Assert.True(punkt.WasAligned);
        Assert.Equal(0m, punkt.Difference);
        Assert.Equal(70m, await SaldoAsync());
    }

    /// <summary>Die Herkunft steht am Punkt und wird nicht erfunden.</summary>
    [Fact]
    public async Task Die_Herkunft_wird_vermerkt()
    {
        var vonHand = await SetzeAsync(new DateOnly(2026, 6, 30), 55m);
        var ausAbruf = await SetzeAsync(
            new DateOnly(2026, 7, 31), 60m, BalanceCheckpointSource.BankFetch);

        Assert.Equal(BalanceCheckpointSource.Manual, vonHand.Source);
        Assert.Equal(BalanceCheckpointSource.BankFetch, ausAbruf.Source);
    }

    /// <summary>
    /// „Stand vom“ am Konto wandert mit — aber nur beim jüngsten Punkt.
    /// </summary>
    /// <remarks>
    /// Wer eine alte Lücke nachträgt, hat nicht zuletzt abgeglichen. Stünde danach das ältere
    /// Datum am Konto, läse sich der Bildschirm wie ein Rückschritt.
    /// </remarks>
    [Fact]
    public async Task Der_Kontostand_vom_wandert_nur_vorwaerts()
    {
        await SetzeAsync(new DateOnly(2026, 9, 30), 200m);
        await SetzeAsync(new DateOnly(2026, 6, 30), 55m);

        using var context = database.Context();
        var konto = context.Accounts.Single(a => a.Id == KontoId);

        Assert.Equal(new DateOnly(2026, 9, 30), konto.BalanceAsOf);
    }

    // ── Was abgewiesen wird ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Stichtag in der Zukunft wird abgewiesen.
    /// </summary>
    /// <remarks>
    /// Er wäre keine Feststellung, sondern eine Vorhersage — und er hielte jede Buchung aus dem
    /// Saldo heraus, bis das Datum erreicht ist. Das fiele frühestens in Wochen auf.
    /// </remarks>
    [Fact]
    public async Task Ein_Stichtag_in_der_Zukunft_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => SetzeAsync(new DateOnly(2026, 11, 11), 55m));

        Assert.Contains("Zukunft", fehler.Message, StringComparison.Ordinal);

        using var context = database.Context();
        Assert.Empty(context.BalanceCheckpoints);
    }

    /// <summary>Heute ist kein Stichtag in der Zukunft.</summary>
    [Fact]
    public async Task Heute_geht_als_Stichtag()
    {
        var punkt = await SetzeAsync(new DateOnly(2026, 11, 10), 55m);

        Assert.Equal(new DateOnly(2026, 11, 10), punkt.AsOf);
    }

    /// <summary>Ein unbekanntes Konto bekommt keinen Punkt.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Konto_wird_abgewiesen()
    {
        using var context = database.Context();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(context).AddAsync(4711, new BalanceCheckpointRequest
            {
                AsOf = new DateOnly(2026, 6, 30),
                Balance = 55m,
            }));
    }

    // ── Zurücknehmen ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein gelöschter Punkt gibt den alten Saldo zurück.
    /// </summary>
    /// <remarks>
    /// Ein vertippter Betrag verschiebt sonst alles, was die Anwendung über dieses Konto sagt —
    /// und ein zweiter Punkt heilt ihn nicht, wenn der Vertipper auf dem jüngeren Stichtag steht.
    /// </remarks>
    [Fact]
    public async Task Ein_geloeschter_Punkt_gibt_den_alten_Saldo_zurueck()
    {
        Buche(new DateOnly(2026, 3, 1), -30m);

        var punkt = await SetzeAsync(new DateOnly(2026, 6, 30), 5555m);
        Assert.Equal(5555m, await SaldoAsync());

        using (var context = database.Context())
        {
            await Dienst(context).RemoveAsync(KontoId, punkt.Id);
        }

        Assert.Equal(70m, await SaldoAsync());
    }

    /// <summary>
    /// Nach dem Zurücknehmen zeigt „Stand vom“ auf den vorigen Abgleich.
    /// </summary>
    /// <remarks>
    /// Bliebe das Datum stehen, behauptete das Konto einen Abgleich, den es nicht mehr gibt —
    /// und die Gesundheitsprüfung hielte es für frisch.
    /// </remarks>
    [Fact]
    public async Task Das_Zuruecknehmen_gibt_den_vorigen_Stichtag_zurueck()
    {
        await SetzeAsync(new DateOnly(2026, 5, 31), 80m);
        var spaeter = await SetzeAsync(new DateOnly(2026, 6, 30), 90m);

        using (var context = database.Context())
        {
            Assert.Equal(
                new DateOnly(2026, 6, 30),
                context.Accounts.Single(a => a.Id == KontoId).BalanceAsOf);

            await Dienst(context).RemoveAsync(KontoId, spaeter.Id);
        }

        using (var context = database.Context())
        {
            Assert.Equal(
                new DateOnly(2026, 5, 31),
                context.Accounts.Single(a => a.Id == KontoId).BalanceAsOf);
        }
    }

    /// <summary>
    /// Ein Datum, das jemand anders gesetzt hat, bleibt beim Zurücknehmen stehen.
    /// </summary>
    /// <remarks>
    /// Am Kontoformular lässt sich „Stand vom“ eintragen. Wer das nach einem Abgleich tut, hat
    /// damit das letzte Wort — ein gelöschter Kontrollpunkt darf seine Angabe nicht umwerfen.
    /// </remarks>
    [Fact]
    public async Task Ein_fremd_gesetzter_Stichtag_bleibt_stehen()
    {
        // Zwei Punkte, damit nach dem Löschen einer übrig bleibt: Sonst bestünde die Prüfung
        // auch ohne die Schutzbedingung, weil es gar nichts gäbe, worauf zurückzufallen wäre.
        await SetzeAsync(new DateOnly(2026, 5, 31), 80m);
        var spaeter = await SetzeAsync(new DateOnly(2026, 6, 30), 90m);

        using (var context = database.Context())
        {
            context.Accounts.Single(a => a.Id == KontoId).BalanceAsOf = new DateOnly(2026, 7, 15);
            context.SaveChanges();
        }

        using (var context = database.Context())
        {
            await Dienst(context).RemoveAsync(KontoId, spaeter.Id);
        }

        using (var context = database.Context())
        {
            Assert.Equal(
                new DateOnly(2026, 7, 15),
                context.Accounts.Single(a => a.Id == KontoId).BalanceAsOf);
        }
    }

    /// <summary>Ein Punkt eines fremden Kontos lässt sich hier nicht löschen.</summary>
    [Fact]
    public async Task Ein_fremder_Punkt_wird_nicht_geloescht()
    {
        var punkt = await SetzeAsync(new DateOnly(2026, 6, 30), 55m);

        using var context = database.Context();

        await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst(context).RemoveAsync(4711, punkt.Id));

        Assert.Single(context.BalanceCheckpoints);
    }
}
