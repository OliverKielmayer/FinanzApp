using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Tests;

/// <summary>
/// Arbeit &amp; Beruf — v5-Handoff, Abschnitt 8.
/// </summary>
/// <remarks>
/// Der Handoff nennt zwei Regeln, gegen die der Prototyp verstoßen hat, und beide sind hier
/// festgenagelt: Beendetes zählt in keine Jahreslast, und die Zahlung ist die vorhandene
/// Buchung — es entsteht keine zweite.
/// </remarks>
public sealed class EmploymentTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 28);

    private EmploymentService Service() => new(database.Context(), clock);

    // ── Bauplan ────────────────────────────────────────────────────────────────────────────

    private int Stelle(
        string arbeitgeber, decimal brutto, decimal? netto = null,
        DateOnly? beginn = null, DateOnly? ende = null, bool aktiv = true)
    {
        using var context = database.Context();

        var stelle = new Employment
        {
            Employer = arbeitgeber,
            GrossMonthly = brutto,
            NetMonthly = netto,
            StartsOn = beginn ?? new DateOnly(2019, 3, 1),
            EndsOn = ende,
            IsActive = aktiv,
        };

        context.Employments.Add(stelle);
        context.SaveChanges();

        return stelle.Id;
    }

    private int Konto(string name = "Sparkasse Giro")
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = name, ShortName = name, BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 1),
        };

        context.Accounts.Add(konto);
        context.SaveChanges();

        return konto.Id;
    }

    private int Buchung(
        int kontoId, DateOnly tag, decimal betrag, string empfaenger = "Gehalt EWV",
        TransactionKind art = TransactionKind.Income)
    {
        using var context = database.Context();

        var buchung = new Transaction
        {
            AccountId = kontoId,
            BookingDate = tag,
            Payee = empfaenger,
            Amount = betrag,
            Kind = art,
            CreatedAt = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Local),
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();

        return buchung.Id;
    }

    private async Task<int> AbrechnungAsync(
        int stelleId, int monat, decimal brutto = 8400m, decimal? netto = 5240m,
        decimal? auszahlung = null)
    {
        var zeile = await Service().CreatePayslipAsync(new CreatePayslipRequest
        {
            EmploymentId = stelleId,
            Month = new DateOnly(2026, monat, 1),
            Gross = brutto,
            Net = netto,
            Payout = auszahlung,
        });

        return zeile.Id;
    }

    // ── Regel (b): Beendetes zählt nicht als laufende Last ─────────────────────────────────

    /// <summary>
    /// Die Jahressumme rechnet nur über laufende Verhältnisse.
    /// </summary>
    /// <remarks>
    /// Der Prototyp summierte beide zu „127.200 € Bruttogehalt pro Jahr“, während der Bereich
    /// selbst 77.760 € nannte — 49.440 € Unterschied für dieselbe Größe. Genau diese beiden
    /// Zahlen stehen hier, damit der Fall benannt bleibt.
    /// </remarks>
    [Fact]
    public async Task Die_Jahressumme_laesst_beendete_Verhaeltnisse_aus()
    {
        Stelle("EWV Kontrollsysteme", brutto: 6480m);
        Stelle("Rheinpark Klinikum", brutto: 4120m, ende: new DateOnly(2024, 8, 31), aktiv: false);

        var kopf = (await Service().GetAsync()).Head;

        Assert.Equal(77_760m, kopf.YearlyGross);
        Assert.NotEqual(127_200m, kopf.YearlyGross);
        Assert.Equal(1, kopf.ActiveCount);
        Assert.Equal(2, kopf.TotalCount);
    }

    [Fact]
    public async Task Die_beendete_Zeile_traegt_keine_Jahreszahl()
    {
        Stelle("EWV Kontrollsysteme", brutto: 6480m);
        Stelle("Rheinpark Klinikum", brutto: 4120m, ende: new DateOnly(2024, 8, 31), aktiv: false);

        var zeilen = (await Service().GetAsync()).Employments;

        Assert.Equal(6480m * 12m, zeilen.Single(z => z.IsActive).YearlyGross);
        Assert.Null(zeilen.Single(z => !z.IsActive).YearlyGross);
    }

    /// <summary>
    /// Ein abgelaufenes Enddatum beendet das Verhältnis, auch ohne dass jemand das Feld umlegt.
    /// </summary>
    /// <remarks>
    /// Sonst wäre die Regel nur so lange wahr, wie jemand täglich nachpflegt: am Tag nach dem
    /// Vertragsende stünde eine Jahreslast in der Summe, die es nicht mehr gibt.
    /// </remarks>
    [Fact]
    public async Task Ein_abgelaufenes_Enddatum_beendet_auch_ohne_Nachpflege()
    {
        Stelle("Gestern beendet", brutto: 3000m, ende: new DateOnly(2026, 8, 27), aktiv: true);

        var kopf = (await Service().GetAsync()).Head;

        Assert.Equal(0m, kopf.YearlyGross);
        Assert.Equal(0, kopf.ActiveCount);
        Assert.Equal(1, kopf.TotalCount);
    }

    /// <summary>Ein Ende in der Zukunft läuft noch — kündigen ist nicht beendet sein.</summary>
    [Fact]
    public async Task Ein_Ende_in_der_Zukunft_laeuft_noch()
    {
        Stelle("Läuft aus", brutto: 3000m, ende: new DateOnly(2026, 12, 31));

        Assert.Equal(1, (await Service().GetAsync()).Head.ActiveCount);
    }

    // ── Kopfzahlen ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_fehlendes_Netto_wird_geschaetzt_und_sagt_es()
    {
        Stelle("Ohne Netto", brutto: 5000m, netto: null);

        var arbeit = await Service().GetAsync();

        Assert.True(arbeit.Head.NetIsEstimated);
        Assert.True(arbeit.Employments.Single().NetIsEstimated);
        Assert.True(arbeit.Head.MonthlyNet is > 0m and < 5000m);
    }

    [Fact]
    public async Task Ein_erfasstes_Netto_gilt_unveraendert()
    {
        Stelle("Mit Netto", brutto: 8400m, netto: 5240m);

        var kopf = (await Service().GetAsync()).Head;

        Assert.False(kopf.NetIsEstimated);
        Assert.Equal(5240m, kopf.MonthlyNet);

        // (8400 − 5240) / 8400 = 37,6 %
        Assert.Equal(37.6m, kopf.DeductionRatePercent);
    }

    /// <summary>Ohne Brutto gibt es nichts zu teilen — und „0 %“ läse sich wie „keine Abgaben“.</summary>
    [Fact]
    public async Task Ohne_laufendes_Verhaeltnis_bleibt_die_Quote_leer()
    {
        Stelle("Beendet", brutto: 4000m, ende: new DateOnly(2020, 1, 31), aktiv: false);

        var kopf = (await Service().GetAsync()).Head;

        Assert.Null(kopf.DeductionRatePercent);
        Assert.Null(kopf.Employer);
    }

    // ── Abrechnungen ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Der_Auszahlungsbetrag_ist_ohne_Angabe_das_Netto()
    {
        var stelle = Stelle("EWV", brutto: 8400m);

        var zeile = await Service().CreatePayslipAsync(new CreatePayslipRequest
        {
            EmploymentId = stelle, Month = new DateOnly(2026, 7, 14), Gross = 8400m, Net = 5240m,
        });

        Assert.Equal(5240m, zeile.Payout);

        // Gespeichert wird der Monatserste, egal welcher Tag hereinkommt.
        Assert.Equal(new DateOnly(2026, 7, 1), zeile.Month);
    }

    /// <summary>Die Abgaben werden gerechnet, nicht gespeichert — sonst gäbe es sie zweimal.</summary>
    [Fact]
    public async Task Die_Abgaben_ergeben_sich_aus_Brutto_und_Netto()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 7);

        Assert.Equal(3160m, (await Service().GetAsync()).Payslips.Single().Deductions);
    }

    [Fact]
    public async Task Zwei_Abrechnungen_fuer_denselben_Monat_werden_abgewiesen()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 7);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => AbrechnungAsync(stelle, 7));

        Assert.Contains("07/2026", fehler.Message);
    }

    [Fact]
    public async Task Ein_Netto_ueber_dem_Brutto_wird_abgewiesen()
    {
        var stelle = Stelle("EWV", brutto: 8400m);

        await Assert.ThrowsAsync<RuleViolationException>(
            () => AbrechnungAsync(stelle, 7, brutto: 4000m, netto: 5240m));
    }

    // ── Regel (a): eine Größe, und sie kommt aus der Buchung ───────────────────────────────

    [Fact]
    public async Task Die_passende_Gutschrift_wird_als_bester_Treffer_vorgeschlagen()
    {
        var konto = Konto();
        var stelle = Stelle("EWV Kontrollsysteme", brutto: 8400m);
        var abrechnung = await AbrechnungAsync(stelle, 7);

        var richtig = Buchung(konto, new DateOnly(2026, 7, 1), 5240m);
        Buchung(konto, new DateOnly(2026, 7, 3), 5000m, "Erstattung Finanzamt");

        var treffer = await Service().GetPaymentCandidatesAsync(abrechnung);

        Assert.Equal(richtig, treffer[0].TransactionId);
        Assert.True(treffer[0].IsBestMatch);
        Assert.Contains("Betrag stimmt", treffer[0].Reason);
        Assert.Contains("im Abrechnungsmonat", treffer[0].Reason);
    }

    /// <summary>
    /// Was weiter als 15 % abweicht, ist kein Kandidat.
    /// </summary>
    /// <remarks>
    /// Der Prototyp knüpfte eine Abrechnung über 3.812 € Auszahlung an eine Buchung über
    /// 5.240 € — 37 % daneben. Diese Paarung hätte der eigene Matcher nie vorgeschlagen; hier
    /// wird sie auch nicht mehr angeboten.
    /// </remarks>
    [Fact]
    public async Task Eine_weit_abweichende_Gutschrift_ist_kein_Kandidat()
    {
        var konto = Konto();
        var stelle = Stelle("EWV", brutto: 6000m);
        var abrechnung = await AbrechnungAsync(stelle, 8, brutto: 6000m, netto: 3812m);

        Buchung(konto, new DateOnly(2026, 8, 1), 5240m);

        Assert.Empty(await Service().GetPaymentCandidatesAsync(abrechnung));
    }

    [Fact]
    public async Task Eine_Ausgabe_ist_kein_Kandidat()
    {
        var konto = Konto();
        var stelle = Stelle("EWV", brutto: 8400m);
        var abrechnung = await AbrechnungAsync(stelle, 7);

        Buchung(konto, new DateOnly(2026, 7, 1), -5240m, "Miete", TransactionKind.Expense);

        Assert.Empty(await Service().GetPaymentCandidatesAsync(abrechnung));
    }

    /// <summary>Die Grenze gilt auch von Hand — sonst wäre die Bestätigung eine Attrappe.</summary>
    [Fact]
    public async Task Auch_von_Hand_gilt_die_Fuenfzehnprozentgrenze()
    {
        var konto = Konto();
        var stelle = Stelle("EWV", brutto: 8400m);
        var abrechnung = await AbrechnungAsync(stelle, 7);
        var daneben = Buchung(konto, new DateOnly(2026, 7, 1), 3812m);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Service().LinkPaymentAsync(abrechnung, daneben));

        Assert.Contains("15 %", fehler.Message);
    }

    /// <summary>
    /// Die Zuordnung legt kein Geld an.
    /// </summary>
    /// <remarks>
    /// Eine zweite Buchung hieße, denselben Eingang doppelt zu zählen: einmal als Bankumsatz,
    /// einmal als Lohn. Das Dashboard nennte dann für denselben Monat 10.480 € Einnahmen.
    /// </remarks>
    [Fact]
    public async Task Die_Zuordnung_legt_keine_zweite_Buchung_an()
    {
        var konto = Konto();
        var stelle = Stelle("EWV", brutto: 8400m);
        var abrechnung = await AbrechnungAsync(stelle, 7);
        var buchung = Buchung(konto, new DateOnly(2026, 7, 1), 5240m);

        using (var context = database.Context())
        {
            Assert.Equal(1, await context.Transactions.CountAsync());
        }

        var zeile = await Service().LinkPaymentAsync(abrechnung, buchung);

        Assert.Equal(buchung, zeile.TransactionId);
        Assert.Equal(5240m, zeile.PaidAmount);

        using var danach = database.Context();
        Assert.Equal(1, await danach.Transactions.CountAsync());
        Assert.Equal(5240m, await danach.Transactions.SumAsync(t => t.Amount));
    }

    [Fact]
    public async Task Die_Zuordnung_laesst_sich_wieder_loesen_und_die_Buchung_bleibt()
    {
        var konto = Konto();
        var stelle = Stelle("EWV", brutto: 8400m);
        var abrechnung = await AbrechnungAsync(stelle, 7);
        var buchung = Buchung(konto, new DateOnly(2026, 7, 1), 5240m);

        await Service().LinkPaymentAsync(abrechnung, buchung);
        var zeile = await Service().DetachPaymentAsync(abrechnung);

        Assert.Null(zeile.TransactionId);

        using var context = database.Context();
        Assert.Equal(1, await context.Transactions.CountAsync());
    }

    // ── Löschen ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Das Löschen hält, was der Dialog verspricht: die Abrechnungen bleiben.
    /// </summary>
    /// <remarks>
    /// Der Löschtext sagt „Erfasste Lohnabrechnungen und ihre Dokumente bleiben erhalten“. Mit
    /// einem Pflicht-Fremdschlüssel wäre er entweder gelogen oder das Löschen scheiterte — die
    /// Abrechnung verliert darum nur ihren Verweis, wie eine Rechnung ihren Vertrag.
    /// </remarks>
    [Fact]
    public async Task Ein_geloeschtes_Verhaeltnis_laesst_seine_Abrechnungen_stehen()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 6);
        await AbrechnungAsync(stelle, 7);

        using (var context = database.Context())
        {
            context.Employments.Remove(await context.Employments.SingleAsync(e => e.Id == stelle));
            await context.SaveChangesAsync();
        }

        using var danach = database.Context();
        Assert.Equal(2, await danach.Payslips.CountAsync());
        Assert.True(await danach.Payslips.AllAsync(p => p.EmploymentId == null));

        // Und die Anzeige kommt ohne Arbeitgeber zurecht, statt zu stolpern.
        Assert.All((await Service().GetAsync()).Payslips, z => Assert.Null(z.Employer));
    }

    // ── Netto ist ein Eingabefeld (Abschnitt 15.5) ─────────────────────────────────────────

    /// <summary>
    /// Ohne eingetragenes Netto schätzt die Anzeige — und sagt, dass sie schätzt.
    /// </summary>
    /// <remarks>
    /// Ein Faktor, der niemandes Steuerklasse kennt, darf nicht unsichtbar in Auswertungen
    /// wirken. Er greift nur, wo nichts steht, und trägt überall sein Kennzeichen.
    /// </remarks>
    [Fact]
    public async Task Ohne_Netto_schaetzt_die_Anzeige_und_kennzeichnet_es()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 6, netto: null);

        var zeile = (await Service().GetAsync()).Payslips.Single();

        Assert.True(zeile.NetIsEstimated);
        Assert.Equal(4956m, zeile.Net);

        // Gespeichert ist nichts: sonst wäre die Schätzung später nicht mehr von einer
        // erfassten Zahl zu unterscheiden.
        using var context = database.Context();
        Assert.Null(context.Payslips.Single().Net);
    }

    [Fact]
    public async Task Ein_eingetragenes_Netto_bleibt_unangetastet()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 6, netto: 5240m);

        var zeile = (await Service().GetAsync()).Payslips.Single();

        Assert.False(zeile.NetIsEstimated);
        Assert.Equal(5240m, zeile.Net);
    }

    /// <summary>
    /// Ohne Netto und ohne Auszahlungsbetrag steht die Schätzung auch in der Auszahlung.
    /// </summary>
    /// <remarks>
    /// Sie ist die Vergleichsgröße der Zahlungszuordnung. Sie auf null zu lassen hieße, jede
    /// Buchung als Abweichung zu melden.
    /// </remarks>
    [Fact]
    public async Task Ohne_Netto_folgt_die_Auszahlung_der_Schaetzung()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 6, netto: null);

        Assert.Equal(4956m, (await Service().GetAsync()).Payslips.Single().Payout);
    }

    /// <summary>Derselbe Monat zweimal ist keine zweite Abrechnung.</summary>
    [Fact]
    public async Task Ein_doppelter_Monat_wird_abgewiesen()
    {
        var stelle = Stelle("EWV", brutto: 8400m);
        await AbrechnungAsync(stelle, 6);

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => AbrechnungAsync(stelle, 6));

        Assert.Contains("schon eine", fehler.Message);
    }

    public void Dispose() => database.Dispose();
}
