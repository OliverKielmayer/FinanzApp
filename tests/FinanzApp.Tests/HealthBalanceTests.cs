using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die PKV-Bilanz — v5-Handoff, Abschnitt 12.
/// </summary>
/// <remarks>
/// Zwei Regeln tragen den Bericht, und beide sind hier festgenagelt: Eigenanteile sind die
/// Gesundheitsausgabe und erstattete Beträge nicht, und Anspruch ist nicht Auszahlung. Die
/// zweite ist die heiklere — „ausgezahlt“ und „erwartet“ unter demselben Wort zu führen
/// behauptet Zahlungen, die nicht stattgefunden haben.
/// </remarks>
public sealed class HealthBalanceTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 28);

    private HealthBalanceService Service() => new(database.Context(), clock);

    private void Bill(
        string provider, decimal brutto, decimal eigen, decimal erwartet,
        decimal? erstattet = null, MedicalBillStatus status = MedicalBillStatus.Submitted,
        string datum = "2026-03-15", string? eingereicht = null, string? gezahlt = null)
    {
        using var context = database.Context();

        context.MedicalBills.Add(new MedicalBill
        {
            Provider = provider,
            BillDate = DateOnly.Parse(datum),
            GrossAmount = brutto,
            OwnShare = eigen,
            ExpectedReimbursement = erwartet,
            ActualReimbursement = erstattet,
            Status = status,
            SubmittedAt = eingereicht is null ? null : DateTime.Parse(eingereicht),
            PaidAt = gezahlt is null ? null : DateTime.Parse(gezahlt),
            CreatedAt = new DateTime(2026, 3, 15, 8, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    private void Krankenversicherung(decimal beitrag, PremiumInterval takt)
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "Krankenversicherung", Provider = "Debeka",
            Kind = PolicyKind.Health, IsCapitalForming = false,
            Premium = beitrag, PremiumInterval = takt,
        });

        context.SaveChanges();
    }

    // ── Anspruch ist nicht Auszahlung ──────────────────────────────────────────────────────

    /// <summary>
    /// Der Balken hat drei Teile, und keiner davon deckt einen anderen mit ab.
    /// </summary>
    /// <remarks>
    /// „Ausgezahlt“ meint Geld, das eingegangen ist. Ein Anspruch, auf den noch gewartet wird,
    /// gehört in ein eigenes Segment — sonst behauptet der Balken Zahlungen, die es nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Ausgezahlt_und_erwartet_bleiben_getrennt()
    {
        // 1.000 € Rechnung: 200 € Eigenanteil, 800 € Anspruch, davon 300 € schon da.
        Bill("Dr. Meyer", 1_000m, 200m, 800m, erstattet: 300m);

        var bilanz = await Service().GetAsync();

        Assert.Equal(300m, bilanz.Split.Paid);
        Assert.Equal(500m, bilanz.Split.Expected);
        Assert.Equal(200m, bilanz.Split.OwnShare);
        Assert.Equal(1_000m, bilanz.Split.Total);

        Assert.Equal(800m, bilanz.Claim);
        Assert.Equal(37.5m, bilanz.PaidSharePercent);
    }

    /// <summary>
    /// Ein abgeschlossener Vorgang hat keinen offenen Anspruch mehr.
    /// </summary>
    /// <remarks>
    /// Was dort nicht gezahlt wurde, ist kein Erwarten, sondern eine Absage — und trägt der
    /// Haushalt. Es zählt darum zum Eigenanteil, nicht zu „erwartet“.
    /// </remarks>
    [Fact]
    public async Task Was_abgelehnt_wurde_zaehlt_zum_Eigenanteil_und_nicht_zum_Erwarten()
    {
        Bill("Dr. Meyer", 1_000m, 200m, 800m,
            erstattet: 600m, status: MedicalBillStatus.Completed);

        var bilanz = await Service().GetAsync();

        Assert.Equal(600m, bilanz.Split.Paid);
        Assert.Equal(0m, bilanz.Split.Expected);
        Assert.Equal(400m, bilanz.Split.OwnShare);
        Assert.Equal(1_000m, bilanz.Split.Total);
    }

    /// <summary>Die drei Teile ergeben immer die Rechnungssumme.</summary>
    [Fact]
    public async Task Die_drei_Teile_ergeben_die_Rechnungssumme()
    {
        Bill("A", 1_000m, 200m, 800m, erstattet: 300m);
        Bill("B", 500m, 500m, 0m, status: MedicalBillStatus.Recorded);
        Bill("C", 300m, 0m, 300m, erstattet: 300m, status: MedicalBillStatus.Completed);

        var bilanz = await Service().GetAsync();

        Assert.Equal(1_800m, bilanz.Split.Total);
        Assert.Equal(bilanz.Split.Paid + bilanz.Split.Expected + bilanz.Split.OwnShare,
            bilanz.Split.Total);
    }

    /// <summary>Ohne Anspruch gibt es keine Quote — nicht null Prozent.</summary>
    [Fact]
    public async Task Ohne_Anspruch_bleibt_die_Quote_leer()
    {
        Bill("Selbstzahler", 200m, 200m, 0m, status: MedicalBillStatus.Recorded);

        Assert.Null((await Service().GetAsync()).PaidSharePercent);
    }

    // ── Bearbeitungsdauer ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Dauer wird gerechnet, nicht gesetzt.
    /// </summary>
    /// <remarks>
    /// Aus Einreich- und Zahldatum. Derselbe Wert entscheidet darüber, welcher offene Vorgang
    /// „über dem Schnitt“ wartet — zwei Grundlagen für dieselbe Aussage wären zwei Wahrheiten.
    /// </remarks>
    [Fact]
    public async Task Die_Bearbeitungsdauer_kommt_aus_Einreich_und_Zahldatum()
    {
        Bill("A", 1_000m, 0m, 1_000m, erstattet: 1_000m, status: MedicalBillStatus.Completed,
            eingereicht: "2026-03-01", gezahlt: "2026-03-11");
        Bill("B", 1_000m, 0m, 1_000m, erstattet: 1_000m, status: MedicalBillStatus.Completed,
            eingereicht: "2026-04-01", gezahlt: "2026-04-21");

        Assert.Equal(15m, (await Service().GetAsync()).AverageDays);
    }

    /// <summary>
    /// „Abgeschlossen“ und „offen“ zählen dieselbe Menge, nur andersherum.
    /// </summary>
    /// <remarks>
    /// Beim ersten Bau war „abgeschlossen“ eine eigene Liste von Zuständen. Derselbe Vorgang
    /// galt damit oben als erledigt und stand unten mit 124 € offen — der Fehlertyp, den
    /// Abschnitt 13 des Handoffs als Lehre aus acht Prüfrunden festhält.
    /// </remarks>
    [Fact]
    public async Task Abgeschlossen_und_offen_widersprechen_sich_nicht()
    {
        Bill("Voll erstattet", 420m, 0m, 420m, erstattet: 420m,
            status: MedicalBillStatus.Completed, eingereicht: "2026-02-09", gezahlt: "2026-02-20");

        Bill("Weniger als beantragt", 980m, 150m, 830m, erstattet: 706m,
            status: MedicalBillStatus.Completed, eingereicht: "2026-04-27", gezahlt: "2026-05-16");

        Bill("Wartet noch", 500m, 0m, 500m, eingereicht: "2026-08-01");

        var bilanz = await Service().GetAsync();

        Assert.Equal(3, bilanz.BillCount);
        Assert.Equal(2, bilanz.CompletedCount);

        // Genau der eine wartende Vorgang steht offen — nicht drei, nicht zwei.
        Assert.Single(bilanz.OpenBills);
        Assert.Equal("Wartet noch", bilanz.OpenBills[0].Provider);
        Assert.Equal(bilanz.Split.Expected, bilanz.OpenBills.Sum(o => o.Expected));
    }

    [Fact]
    public async Task Ohne_abgeschlossenen_Vorgang_gibt_es_keinen_Schnitt()
    {
        Bill("A", 1_000m, 0m, 1_000m, eingereicht: "2026-08-01");

        Assert.Null((await Service().GetAsync()).AverageDays);
    }

    /// <summary>
    /// „Über dem Schnitt“ misst am eigenen Durchschnitt.
    /// </summary>
    /// <remarks>
    /// Nicht an einer erfundenen Frist: was lange ist, weiß nur diese Versicherung, und sie
    /// sagt es durch ihre abgeschlossenen Fälle.
    /// </remarks>
    [Fact]
    public async Task Ein_Vorgang_ueber_dem_eigenen_Schnitt_wird_hervorgehoben()
    {
        Bill("Schnell", 1_000m, 0m, 1_000m, erstattet: 1_000m,
            status: MedicalBillStatus.Completed, eingereicht: "2026-03-01", gezahlt: "2026-03-11");

        Bill("Wartet lang", 500m, 0m, 500m, eingereicht: "2026-08-01");
        Bill("Wartet kurz", 500m, 0m, 500m, eingereicht: "2026-08-25");

        var bilanz = await Service().GetAsync();

        Assert.Equal(10m, bilanz.AverageDays);
        Assert.True(bilanz.OpenBills.Single(o => o.Provider == "Wartet lang").AboveAverage);
        Assert.False(bilanz.OpenBills.Single(o => o.Provider == "Wartet kurz").AboveAverage);
    }

    /// <summary>Nicht eingereicht heißt: es wartet niemand auf die Versicherung.</summary>
    [Fact]
    public async Task Ein_nicht_eingereichter_Vorgang_wartet_auf_niemanden()
    {
        Bill("Liegt herum", 500m, 0m, 500m, status: MedicalBillStatus.Recorded);

        var offen = (await Service().GetAsync()).OpenBills.Single();

        Assert.Null(offen.SubmittedOn);
        Assert.Null(offen.WaitingDays);
        Assert.False(offen.AboveAverage);
    }

    // ── Beitrag und Steuerbrücke ───────────────────────────────────────────────────────────

    /// <summary>
    /// Der Beitrag steht getrennt von den Behandlungskosten.
    /// </summary>
    /// <remarks>
    /// Er ist Absicherung. In eine Summe mit den Eigenanteilen geworfen wäre er beides und
    /// keines — und die Ausgabenseite der Gesundheit plötzlich um einen Jahresbeitrag höher.
    /// </remarks>
    [Fact]
    public async Task Der_Jahresbeitrag_zaehlt_nicht_zum_Eigenanteil()
    {
        Bill("Dr. Meyer", 1_000m, 200m, 800m, erstattet: 800m,
            status: MedicalBillStatus.Completed);
        Krankenversicherung(742m, PremiumInterval.Monthly);

        var bilanz = await Service().GetAsync();

        Assert.Equal(200m, bilanz.Split.OwnShare);
        Assert.Equal(8_904m, bilanz.YearlyPremium);

        // Die Steuerbrücke nennt beide zusammen — aber erst dort, und benannt.
        Assert.Equal(9_104m, bilanz.Deductible);
    }

    [Fact]
    public async Task Ein_Vierteljahresbeitrag_wird_auf_das_Jahr_gerechnet()
    {
        Krankenversicherung(300m, PremiumInterval.Quarterly);

        Assert.Equal(1_200m, (await Service().GetAsync()).YearlyPremium);
    }

    // ── Jahr und Erbringer ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Zeitraum ist das Kalenderjahr, bestimmt vom Rechnungsdatum.
    /// </summary>
    /// <remarks>
    /// Nicht vom Zahlungseingang: die Behandlung fand damals statt, und steuerlich hängt der
    /// Eigenanteil an ihr — nicht daran, wann die Versicherung sich Zeit ließ.
    /// </remarks>
    [Fact]
    public async Task Das_Rechnungsdatum_bestimmt_das_Jahr()
    {
        Bill("A", 1_000m, 1_000m, 0m, datum: "2025-12-20", status: MedicalBillStatus.Completed,
            eingereicht: "2025-12-21", gezahlt: "2026-01-15");
        Bill("B", 500m, 500m, 0m, datum: "2026-01-05", status: MedicalBillStatus.Completed);

        var y2025 = await Service().GetAsync(2025);
        var y2026 = await Service().GetAsync(2026);

        Assert.Equal(1_000m, y2025.Split.Total);
        Assert.Equal(500m, y2026.Split.Total);

        // Der Jahresfilter zeigt beide Jahre samt „Alle“.
        Assert.Equal(3, y2026.Years.Count);
        Assert.Equal(1_500m, y2026.Years.Single(j => j.Year is null).Total);
    }

    [Fact]
    public async Task Je_Leistungserbringer_wird_derselbe_Dreiklang_gerechnet()
    {
        Bill("Dr. Meyer", 1_000m, 200m, 800m, erstattet: 300m);
        Bill("Dr. Meyer", 500m, 100m, 400m, erstattet: 400m, status: MedicalBillStatus.Completed);
        Bill("Zahnarzt Weber", 300m, 300m, 0m, status: MedicalBillStatus.Recorded);

        var erbringer = (await Service().GetAsync()).Providers;

        var meyer = erbringer.Single(p => p.Provider == "Dr. Meyer");
        Assert.Equal(2, meyer.BillCount);
        Assert.Equal(1_500m, meyer.Split.Total);
        Assert.Equal(700m, meyer.Split.Paid);
        Assert.Equal(500m, meyer.Split.Expected);
        Assert.Equal(300m, meyer.Split.OwnShare);

        // Der grösste zuerst — die Frage ist, wo das Geld hingeht.
        Assert.Equal("Dr. Meyer", erbringer[0].Provider);
    }

    public void Dispose() => database.Dispose();
}
