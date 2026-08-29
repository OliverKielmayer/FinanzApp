using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Steuerjahr-Bericht — v5-Handoff, Abschnitt 15.
/// </summary>
/// <remarks>
/// <para>Der wichtigste Test hier ist der auf die <b>zwei getrennten Kennzeichen</b>. Solange
/// „ohne Beleg“ und „geschätzt“ dasselbe Feld waren, stand die Entfernungspauschale — aus dem
/// Arbeitsvertrag gerechnet und damit sehr wohl belegt — im Topf der fehlenden Belege und machte
/// dort den größten Betrag aus. Für den Empfänger des Blattes ist das der entscheidende
/// Unterschied: einen fehlenden Beleg reicht man nach, eine Schätzung muss man nachrechnen.</para>
/// <para>Der zweitwichtigste prüft, dass Krankheitskosten und PKV-Bilanz dieselbe Zahl nennen.
/// Zwei Rechnungen für eine Aussage laufen irgendwann auseinander.</para>
/// </remarks>
public sealed class TaxYearTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 29);

    private TaxYearService Service()
    {
        var context = database.Context();
        return new TaxYearService(context, new HealthBalanceService(context, clock), clock);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private int Policy(
        string name, PolicyKind art, decimal beitrag,
        PremiumInterval takt = PremiumInterval.Monthly,
        DateOnly? von = null, DateOnly? bis = null)
    {
        using var context = database.Context();
        var vertrag = new Policy
        {
            Name = name,
            Provider = name,
            Kind = art,
            Premium = beitrag,
            PremiumInterval = takt,
            StartsOn = von,
            EndsOn = bis,
        };

        context.Policies.Add(vertrag);
        context.SaveChanges();
        return vertrag.Id;
    }

    private void Bill(DateOnly datum, decimal brutto, decimal erstattet, decimal offen = 0m)
    {
        using var context = database.Context();
        context.MedicalBills.Add(new MedicalBill
        {
            Provider = "Dr. Muster",
            BillDate = datum,
            GrossAmount = brutto,
            OwnShare = 0m,
            ExpectedReimbursement = erstattet + offen,
            ActualReimbursement = erstattet,
            Status = offen > 0m ? MedicalBillStatus.Submitted : MedicalBillStatus.Completed,
            CreatedAt = new DateTime(datum.Year, datum.Month, datum.Day, 9, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    private int Category(string name, TaxCategory art)
    {
        using var context = database.Context();
        var kategorie = new Category
        {
            Name = name, Direction = CategoryDirection.Expense, TaxCategory = art,
        };

        context.Categories.Add(kategorie);
        context.SaveChanges();
        return kategorie.Id;
    }

    private int Account()
    {
        using var context = database.Context();
        var konto = new Account
        {
            Name = "Giro", ShortName = "Giro", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 1, 1),
        };

        context.Accounts.Add(konto);
        context.SaveChanges();
        return konto.Id;
    }

    private void Booking(int konto, int kategorie, DateOnly tag, string empfaenger, decimal betrag)
    {
        using var context = database.Context();
        context.Transactions.Add(new Transaction
        {
            BookingDate = tag,
            Payee = empfaenger,
            Kind = TransactionKind.Expense,
            Amount = -betrag,
            AccountId = konto,
            CategoryId = kategorie,
            CreatedAt = new DateTime(tag.Year, tag.Month, tag.Day, 9, 0, 0, DateTimeKind.Local),
        });

        context.SaveChanges();
    }

    private void Employment(decimal km, int tage, DateOnly? bis = null)
    {
        using var context = database.Context();
        context.Employments.Add(new Employment
        {
            Employer = "Musterfirma",
            Kind = EmploymentKind.Permanent,
            StartsOn = new DateOnly(2019, 1, 1),
            EndsOn = bis,
            GrossMonthly = 5000m,
            CommuteKilometres = km,
            WorkDaysPerYear = tage,
        });

        context.SaveChanges();
    }

    private static TaxPositionDto Find(TaxYearDto bericht, TaxSectionKind abschnitt)
        => bericht.Sections.Single(s => s.Kind == abschnitt).Positions[0];

    // ── Die zwei Kennzeichen ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Belegt und geschätzt sind zwei verschiedene Dinge.
    /// </summary>
    /// <remarks>
    /// Der teuerste Fehler dieser Runde. Die Entfernungspauschale ist aus Angaben im
    /// Arbeitsverhältnis gerechnet — sie ist belegt und muss trotzdem nachgerechnet werden. Wer
    /// beides in ein Feld legt, schickt den Nutzer nach Belegen suchen, die er längst hat.
    /// </remarks>
    [Fact]
    public async Task Geschaetzt_heisst_nicht_ohne_Beleg()
    {
        Employment(km: 38m, tage: 214);

        var bericht = await Service().GetAsync(2026);
        var fahrten = Find(bericht, TaxSectionKind.Werbungskosten);

        Assert.True(fahrten.Estimated);
        Assert.False(fahrten.DocumentMissing);

        Assert.Equal(0m, bericht.MissingAmount);
        Assert.Equal(0, bericht.MissingCount);
        Assert.Equal(fahrten.Amount, bericht.EstimatedAmount);
        Assert.Equal(1, bericht.EstimatedCount);

        // Belegt heißt hier wirklich belegt: die Quote steht bei hundert Prozent.
        Assert.Equal(100m, bericht.DocumentedPercent);
    }

    /// <summary>
    /// Ein fehlender Beleg wiegt schwerer als eine Schätzung.
    /// </summary>
    /// <remarks>
    /// Beide Marken nebeneinander machten aus einer Zeile eine Fußnote. Wer beides hat, muss
    /// zuerst den Beleg besorgen — nachrechnen kann er danach.
    /// </remarks>
    [Fact]
    public void Fehlender_Beleg_schlaegt_Schaetzung()
    {
        var beides = new TaxPositionDto
        {
            Section = TaxSectionKind.Werbungskosten,
            Label = "Etwas",
            Amount = 100m,
            Evidence = "Rechnung 2026",
            DocumentMissing = true,
            Estimated = true,
        };

        Assert.Equal(TaxMarkTone.Missing, beides.Mark.Tone);
        Assert.Equal("⚠ fehlt: Rechnung 2026", beides.Mark.Text);
    }

    /// <summary>
    /// Der Belegtext benennt die Sache, nicht die Aussage.
    /// </summary>
    /// <remarks>
    /// „Anbieterbescheinigung“, nicht „Bescheinigung fehlt“. Sonst setzt die Marke daraus
    /// „⚠ fehlt: Bescheinigung fehlt“ zusammen — und genau das stand eine Runde lang auf dem
    /// Blatt.
    /// </remarks>
    [Fact]
    public async Task Der_Belegtext_ist_ein_Substantiv_keine_Aussage()
    {
        Policy("Riester Muster", PolicyKind.Riester, 100m);

        var bericht = await Service().GetAsync(2026);
        var riester = Find(bericht, TaxSectionKind.Vorsorge);

        Assert.Equal("Anbieterbescheinigung", riester.Evidence);
        Assert.Equal("⚠ fehlt: Anbieterbescheinigung", riester.Mark.Text);

        Assert.DoesNotContain("fehlt: ", riester.Evidence);
        Assert.DoesNotContain("fehlt fehlt", riester.Mark.Text);
    }

    /// <summary>
    /// Der fehlerfreie Abschnitt heißt „belegt, kein Schätzwert“.
    /// </summary>
    /// <remarks>
    /// Nicht „belegt und gerechnet“: „gerechnet“ heißt in der Marke <em>geschätzt</em>, und ein
    /// Wort darf nicht zweierlei bedeuten.
    /// </remarks>
    [Fact]
    public async Task Ein_sauberer_Abschnitt_nennt_beide_Kennzeichen_als_erfuellt()
    {
        Bill(new DateOnly(2026, 3, 1), brutto: 500m, erstattet: 400m);

        var bericht = await Service().GetAsync(2026);
        var krank = bericht.Sections.Single(s => s.Kind == TaxSectionKind.Krankheit);

        Assert.Equal("1 Position · belegt, kein Schätzwert", krank.Meta);
        Assert.False(krank.NeedsAttention);
    }

    [Fact]
    public async Task Ein_Abschnitt_mit_Maengeln_nennt_beide_getrennt()
    {
        Policy("Riester Muster", PolicyKind.Riester, 100m);
        Employment(km: 10m, tage: 200);

        var bericht = await Service().GetAsync(2026);

        Assert.Equal("1 Position · 1 ohne Beleg", Meta(bericht, TaxSectionKind.Vorsorge));
        Assert.Equal("1 Position · 1 geschätzt", Meta(bericht, TaxSectionKind.Werbungskosten));
    }

    private static string Meta(TaxYearDto bericht, TaxSectionKind art)
        => bericht.Sections.Single(s => s.Kind == art).Meta;

    /// <summary>
    /// Die Belegquote rechnet über die Beträge, nicht über die Positionen.
    /// </summary>
    /// <remarks>
    /// Sonst stünde „belegt 100 %“ neben „1 Position ohne Beleg“ — zwei Größen in einer
    /// Kennzahl, dieselbe Regelverletzung wie beim Vermögensmodell.
    /// </remarks>
    [Fact]
    public async Task Die_Belegquote_rechnet_ueber_Betraege()
    {
        // 1.200 € belegt, 120 € ohne Beleg: über die Positionen wären das 50 %, über die
        // Beträge sind es 91 %.
        Bill(new DateOnly(2026, 3, 1), brutto: 1200m, erstattet: 0m);
        Policy("Riester Muster", PolicyKind.Riester, 10m);

        var bericht = await Service().GetAsync(2026);

        Assert.Equal(120m, bericht.MissingAmount);
        Assert.Equal(1320m, bericht.Total);
        Assert.Equal(91m, bericht.DocumentedPercent);
    }

    // ── Eine Menge, eine Quelle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Krankheitskosten und PKV-Bilanz nennen dieselbe Zahl.
    /// </summary>
    /// <remarks>
    /// Nicht ungefähr dieselbe: dieselbe. Der Bericht fragt den Bilanzdienst, statt die
    /// Eigenanteile ein zweites Mal zu rechnen — im Prototyp standen 9.620 € gegen 12.798 € für
    /// dieselbe Aussage, weil eine Rechnung doppelt zählte.
    /// </remarks>
    [Fact]
    public async Task Krankheitskosten_kommen_aus_der_PKV_Bilanz()
    {
        Bill(new DateOnly(2026, 2, 1), brutto: 800m, erstattet: 600m);
        Bill(new DateOnly(2026, 5, 1), brutto: 1200m, erstattet: 700m, offen: 200m);

        var bilanz = await new HealthBalanceService(database.Context(), clock).GetAsync(2026);
        var bericht = await Service().GetAsync(2026);

        var krank = Find(bericht, TaxSectionKind.Krankheit);

        Assert.Equal(bilanz.Split.OwnShare, krank.Amount);
        Assert.Contains("aus der PKV-Bilanz", krank.Evidence);
        Assert.Contains("2 Rechnungen", krank.Evidence);
    }

    /// <summary>Ohne Eigenanteil gibt es keinen Abschnitt.</summary>
    /// <remarks>
    /// Ein leerer Abschnitt mit Einschränkungstext behauptete eine Prüfung, die niemand
    /// angestellt hat.
    /// </remarks>
    [Fact]
    public async Task Ohne_Eigenanteil_faellt_der_Abschnitt_weg()
    {
        Bill(new DateOnly(2026, 2, 1), brutto: 500m, erstattet: 500m);

        var bericht = await Service().GetAsync(2026);

        Assert.DoesNotContain(bericht.Sections, s => s.Kind == TaxSectionKind.Krankheit);
    }

    // ── Erwartet, noch ohne Betrag ─────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Position über 0 € ist keine Steuerposition.
    /// </summary>
    /// <remarks>
    /// Sie zählt in keine Summe und in keinen Zähler. Ein Posten, der noch keinen Betrag hat,
    /// als „Position ohne Beleg“ zu führen machte aus einer offenen Erwartung einen Mangel.
    /// </remarks>
    [Fact]
    public async Task Eine_Position_ueber_null_Euro_zaehlt_nirgends()
    {
        var konto = Account();
        var handwerk = Category("Handwerker", TaxCategory.Handwerkerleistung);

        Booking(konto, handwerk, new DateOnly(2026, 3, 1), "Heizung Grau", 0m);
        Booking(konto, handwerk, new DateOnly(2026, 4, 1), "Schornsteinfeger", 96m);

        var bericht = await Service().GetAsync(2026);
        var abschnitt = bericht.Sections.Single(s => s.Kind == TaxSectionKind.Handwerker);

        Assert.Single(abschnitt.Positions);
        Assert.Equal(96m, bericht.Total);
        Assert.Equal(1, bericht.PositionCount);
    }

    // ── Vorsorgeaufwendungen ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Beiträge zählen anteilig auf das Jahr.
    /// </summary>
    /// <remarks>
    /// Ein Vertrag, der im Juli beginnt, kostet in diesem Jahr sechs Monate. Den vollen
    /// Jahresbeitrag anzusetzen wäre bequem und falsch.
    /// </remarks>
    [Fact]
    public async Task Ein_Vertrag_zaehlt_nur_die_Monate_die_er_laeuft()
    {
        Policy("BU Muster", PolicyKind.DisabilityInsurance, 100m,
            von: new DateOnly(2026, 7, 1));

        var bericht = await Service().GetAsync(2026);

        Assert.Equal(600m, Find(bericht, TaxSectionKind.Vorsorge).Amount);
    }

    [Fact]
    public async Task Ein_beendeter_Vertrag_zaehlt_im_Folgejahr_nicht_mehr()
    {
        Policy("BU Muster", PolicyKind.DisabilityInsurance, 100m,
            von: new DateOnly(2019, 1, 1), bis: new DateOnly(2025, 6, 30));

        Bill(new DateOnly(2026, 1, 1), brutto: 100m, erstattet: 0m);

        var bericht = await Service().GetAsync(2026);

        Assert.DoesNotContain(bericht.Sections, s => s.Kind == TaxSectionKind.Vorsorge);
    }

    /// <summary>Nur Vertragsarten, deren Beiträge überhaupt in Frage kommen.</summary>
    [Fact]
    public async Task Eine_Hausratversicherung_ist_keine_Vorsorge()
    {
        Policy("Hausrat Muster", PolicyKind.HouseholdContents, 20m);
        Policy("BU Muster", PolicyKind.DisabilityInsurance, 100m);

        var bericht = await Service().GetAsync(2026);
        var vorsorge = bericht.Sections.Single(s => s.Kind == TaxSectionKind.Vorsorge);

        Assert.Single(vorsorge.Positions);
        Assert.Equal(1200m, vorsorge.Total);
    }

    // ── Entfernungspauschale ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Pauschale ist gestaffelt.
    /// </summary>
    /// <remarks>
    /// Die ersten zwanzig Kilometer zu 0,30 €, jeder weitere zu 0,38 €. Bei 38 km sind das
    /// 6,00 € + 6,84 € = 12,84 € am Tag. Flach gerechnet läge man bei langen Wegen deutlich
    /// daneben.
    /// </remarks>
    [Fact]
    public async Task Die_Entfernungspauschale_rechnet_gestaffelt()
    {
        Employment(km: 38m, tage: 214);

        var bericht = await Service().GetAsync(2026);

        Assert.Equal(2747.76m, Find(bericht, TaxSectionKind.Werbungskosten).Amount);
    }

    /// <summary>
    /// Ohne Entfernung oder ohne Arbeitstage entsteht die Position nicht.
    /// </summary>
    /// <remarks>
    /// Eine Pauschale aus geratener Entfernung wäre keine Schätzung mehr, sondern eine
    /// Erfindung.
    /// </remarks>
    [Fact]
    public async Task Ohne_Entfernung_gibt_es_keine_Pauschale()
    {
        using (var context = database.Context())
        {
            context.Employments.Add(new Employment
            {
                Employer = "Musterfirma",
                Kind = EmploymentKind.Permanent,
                StartsOn = new DateOnly(2019, 1, 1),
                GrossMonthly = 5000m,
                WorkDaysPerYear = 214,
            });

            context.SaveChanges();
        }

        Bill(new DateOnly(2026, 1, 1), brutto: 100m, erstattet: 0m);

        var bericht = await Service().GetAsync(2026);

        Assert.DoesNotContain(bericht.Sections, s => s.Kind == TaxSectionKind.Werbungskosten);
    }

    // ── Jahre ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Jahreswechsler zeigt nur Jahre, für die es etwas gibt.
    /// </summary>
    /// <remarks>
    /// Eine feste Liste ließe auf ein Jahr springen, in dem nichts steht — und behauptete damit
    /// Daten, die es nicht gibt. Das laufende Jahr ist immer dabei, es füllt sich noch.
    /// </remarks>
    [Fact]
    public async Task Die_Jahresliste_kommt_aus_den_Daten()
    {
        Bill(new DateOnly(2024, 5, 1), brutto: 100m, erstattet: 0m);
        Bill(new DateOnly(2025, 5, 1), brutto: 100m, erstattet: 0m);

        var bericht = await Service().GetAsync();

        Assert.Equal([2026, 2025, 2024], bericht.Years);
    }

    [Fact]
    public async Task Ohne_Jahresangabe_gilt_das_laufende()
    {
        Bill(new DateOnly(2024, 5, 1), brutto: 100m, erstattet: 0m);

        var bericht = await Service().GetAsync();

        Assert.Equal(2026, bericht.Year);
    }

    // ── Was bewusst fehlt ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Block „Nicht enthalten“ ist immer da.
    /// </summary>
    /// <remarks>
    /// Ohne ihn hält der Leser die Liste für vollständig und sucht später nach Posten, die nie
    /// darin sein sollten.
    /// </remarks>
    [Fact]
    public async Task Der_Bericht_sagt_auch_was_er_weglaesst()
    {
        var bericht = await Service().GetAsync(2026);

        Assert.NotEmpty(bericht.Excluded);
        Assert.All(bericht.Excluded, a => Assert.NotEmpty(a.Reason));
    }

    public void Dispose() => database.Dispose();
}
