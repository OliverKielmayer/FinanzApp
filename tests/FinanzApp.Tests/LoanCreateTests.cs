using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Darlehen anlegen, ändern, löschen.
/// </summary>
/// <remarks>
/// <para>Der Anlass ist ein Kreis: Wer unter „Konto“ die Art „Darlehen“ wählte, landete im
/// Darlehensbereich. Der hatte ohne erfasstes Darlehen nichts zu zeigen und verwies auf die
/// Übersicht der Arten — wo wieder nur „Konto“ stand. Ein Darlehen war schlicht nicht
/// anzulegen, obwohl Immobilie, Vermögen und Objektbericht es überall verlinkten.</para>
/// <para>Darum prüfen die ersten beiden Tests nicht die Maske, sondern den <em>Weg</em>: dass
/// jede Tür, die „Darlehen“ verspricht, auch in die Maske führt.</para>
/// </remarks>
public sealed class LoanCreateTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 31);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private CreateFormService Service()
    {
        var context = database.Context();
        var documents = new DocumentService(
            context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

        return new CreateFormService(context, clock, documents, new NoPolicyDocumentAnalyzer());
    }

    private static Dictionary<string, string?> Werte(
        string name = "Immobiliendarlehen",
        string lender = "Sparkasse",
        string debt = "148.300,00",
        string rate = "1,84",
        string installment = "1.180,00",
        string payment = "2026-12-01")
        => new()
        {
            ["name"] = name,
            ["lender"] = lender,
            ["debt"] = debt,
            ["rate"] = rate,
            ["installment"] = installment,
            ["payment"] = payment,
        };

    // ── Der Weg in die Maske ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Wahl „Darlehen“ im Konto-Formular führt in die Darlehensmaske.
    /// </summary>
    /// <remarks>
    /// Vorher zeigte sie auf <c>/darlehen</c> — den Anzeigebereich. Ohne erfasstes Darlehen hat
    /// der nichts zu zeigen, und von dort ging es zurück auf die Typenübersicht: der Kreis.
    /// </remarks>
    [Fact]
    public async Task Die_Wahl_Darlehen_im_Kontoformular_fuehrt_in_die_Darlehensmaske()
    {
        var konto = await Service().GetFormAsync(CreateObjectType.Account);

        var art = konto!.Fields.Single(f => f.Key == "kind");
        var darlehen = art.Options!.Single(o => o.Value == "loan");

        Assert.Equal("/neu/loan", darlehen.RedirectTo);
    }

    [Fact]
    public async Task Die_Darlehensmaske_gibt_es_wirklich()
    {
        var form = await Service().GetFormAsync(CreateObjectType.Loan);

        Assert.NotNull(form);
        Assert.Equal("Darlehen anlegen", form.Title);
        Assert.Equal(
            ["name", "lender", "debt", "rate", "installment", "payment"],
            form.Fields.Select(f => f.Key));
    }

    /// <summary>
    /// Die nächste Zahlung ist auf den Monatsersten nach heute vorbelegt.
    /// </summary>
    /// <remarks>
    /// Der heutige Tag wäre falsch: Eine Rate, die heute fällig wäre, ist in aller Regel schon
    /// gezahlt, und der Tilgungsplan begänne mit einem Monat, der vorbei ist.
    /// </remarks>
    [Fact]
    public async Task Die_naechste_Zahlung_ist_auf_den_kommenden_Monatsersten_vorbelegt()
    {
        var form = await Service().GetFormAsync(CreateObjectType.Loan);

        Assert.Equal("2026-11-01", form!.Fields.Single(f => f.Key == "payment").DefaultValue);
    }

    // ── Anlegen ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_Darlehen_wird_wirklich_angelegt()
    {
        var result = await Service().CreateAsync(CreateObjectType.Loan, Werte());

        Assert.True(result.Ok, result.Message);

        using var check = database.Context();
        var loan = Assert.Single(check.Loans);
        Assert.Equal("Immobiliendarlehen", loan.Name);
        Assert.Equal("Sparkasse", loan.Lender);
        Assert.Equal(148_300m, loan.RemainingDebt);
        Assert.Equal(1.84m, loan.InterestRatePercent);
        Assert.Equal(1_180m, loan.Installment);
        Assert.Equal(new DateOnly(2026, 12, 1), loan.NextPaymentDate);
    }

    /// <summary>
    /// Danach steht das Darlehen dort, wo es verlinkt wird.
    /// </summary>
    /// <remarks>
    /// Die Immobilienmaske führt eine Auswahl „Bestehendes Darlehen“. Bis hierher konnte sie
    /// nur leer sein — es gab keinen Weg, eines anzulegen.
    /// </remarks>
    [Fact]
    public async Task Es_steht_danach_in_der_Immobilienmaske_zur_Auswahl()
    {
        await Service().CreateAsync(CreateObjectType.Loan, Werte());

        var objekt = await Service().GetFormAsync(CreateObjectType.Property);
        var auswahl = objekt!.Fields.Single(f => f.Key == "loan");

        var option = Assert.Single(auswahl.Options!);
        Assert.Equal("Immobiliendarlehen", option.Label);
        Assert.Equal("Sparkasse", option.Hint);
    }

    /// <summary>
    /// Der Sprung nach dem Anlegen nennt die Kennung.
    /// </summary>
    /// <remarks>
    /// Ohne sie zeigte der Bereich immer das <em>erste</em> Darlehen — wer ein zweites anlegte,
    /// sähe nach dem Speichern das falsche und hielte seine Eingabe für verloren.
    /// </remarks>
    [Fact]
    public async Task Nach_dem_Anlegen_wird_genau_dieses_Darlehen_gezeigt()
    {
        await Service().CreateAsync(CreateObjectType.Loan, Werte());
        var zweites = await Service().CreateAsync(
            CreateObjectType.Loan, Werte(name: "Modernisierung", debt: "20.000,00", installment: "250,00"));

        Assert.True(zweites.Ok, zweites.Message);
        Assert.Equal($"/darlehen?id={zweites.Id}", zweites.Route);
    }

    /// <summary>Aus den erfassten Werten rechnet derselbe Plan, den die Seite zeigt.</summary>
    [Fact]
    public async Task Aus_den_erfassten_Werten_entsteht_ein_Tilgungsplan()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());

        using var context = database.Context();
        var darlehen = await new LoanService(context).GetAsync(angelegt.Id!.Value);

        var erste = darlehen!.Schedule[0];

        // 148.300,00 € × 1,84 % ÷ 12 = 227,39 €. Der Rest der Rate tilgt.
        Assert.Equal(227.39m, erste.Interest);
        Assert.Equal(952.61m, erste.Principal);
        Assert.Equal(147_347.39m, erste.RemainingDebt);
    }

    [Fact]
    public async Task Ein_fehlendes_Pflichtfeld_wird_beim_Namen_genannt()
    {
        var werte = Werte();
        werte["lender"] = null;

        var result = await Service().CreateAsync(CreateObjectType.Loan, werte);

        Assert.False(result.Ok);
        Assert.Equal("lender", result.FieldKey);
        Assert.Equal("Darlehensgeber fehlt", result.Message);
    }

    [Fact]
    public async Task Zweimal_derselbe_Name_wird_abgelehnt()
    {
        await Service().CreateAsync(CreateObjectType.Loan, Werte());

        var zweites = await Service().CreateAsync(CreateObjectType.Loan, Werte(lender: "Volksbank"));

        Assert.False(zweites.Ok);
        Assert.Equal("name", zweites.FieldKey);
        Assert.Contains("besteht bereits", zweites.Message);

        using var check = database.Context();
        Assert.Single(check.Loans);
    }

    // ── Was die Rechnung nicht trägt ───────────────────────────────────────────────────────

    /// <summary>
    /// Eine Rate unter dem Monatszins wird abgewiesen — und die Meldung nennt den Zins.
    /// </summary>
    /// <remarks>
    /// Sie tilgt nichts, sie lässt die Schuld wachsen. Der Tilgungsplan rechnete das brav vor,
    /// und die steigenden Zahlen sähen aus wie eine Aussage über das Darlehen. Sie wären die
    /// Folge eines Vertippers — etwa der Jahresrate im Feld für die Monatsrate.
    /// </remarks>
    [Fact]
    public async Task Eine_Rate_unter_dem_Monatszins_wird_abgelehnt()
    {
        var result = await Service().CreateAsync(
            CreateObjectType.Loan, Werte(installment: "200,00"));

        Assert.False(result.Ok);
        Assert.Equal("installment", result.FieldKey);
        Assert.Contains("227,39", result.Message);

        using var check = database.Context();
        Assert.Empty(check.Loans);
    }

    /// <summary>
    /// Eine Rate genau in Höhe des Zinses geht durch: das ist ein endfälliges Darlehen.
    /// </summary>
    /// <remarks>
    /// Der Plan zeigt dann Tilgung null und eine gleichbleibende Restschuld. Das ist keine
    /// Fehlfunktion, sondern die Wahrheit über diesen Vertrag.
    /// </remarks>
    [Fact]
    public async Task Ein_endfaelliges_Darlehen_wird_angenommen()
    {
        var result = await Service().CreateAsync(
            CreateObjectType.Loan, Werte(installment: "227,39"));

        Assert.True(result.Ok, result.Message);

        using var context = database.Context();
        var plan = await new LoanService(context).GetAsync(result.Id!.Value);

        Assert.Equal(0m, plan!.Schedule[0].Principal);
        Assert.Equal(148_300m, plan.Schedule[0].RemainingDebt);
    }

    [Theory]
    [InlineData("debt", "0,00", "debt")]
    [InlineData("installment", "0,00", "installment")]
    [InlineData("rate", "-1", "rate")]
    [InlineData("debt", "keine Zahl", "debt")]
    [InlineData("payment", "irgendwann", "payment")]
    public async Task Was_keine_Rechnung_traegt_wird_abgewiesen(string feld, string eingabe, string gemeldet)
    {
        var werte = Werte();
        werte[feld] = eingabe;

        var result = await Service().CreateAsync(CreateObjectType.Loan, werte);

        Assert.False(result.Ok);
        Assert.Equal(gemeldet, result.FieldKey);

        using var check = database.Context();
        Assert.Empty(check.Loans);
    }

    // ── Ändern ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Werte kommen in die Maske zurück — der Zins mit Punkt, nicht mit Komma.
    /// </summary>
    /// <remarks>
    /// Das Zinsfeld rendert als <c>&lt;input type="number"&gt;</c>, und ein solches Feld verwirft
    /// „1,84“ als ungültig. Das Formular käme leer heraus, und das nächste Speichern verlöre den
    /// gepflegten Zins — derselbe Fehler, der schon einmal Wertbestandteile einer Police
    /// gekostet hat.
    /// </remarks>
    [Fact]
    public async Task Die_Maske_bekommt_den_Zins_im_Eingabeformat_zurueck()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());

        var form = await Service().GetFormAsync(CreateObjectType.Loan, angelegt.Id);

        Assert.Equal("Darlehen bearbeiten", form!.Title);
        Assert.Equal("Immobiliendarlehen", form.Values["name"]);
        Assert.Equal("1.84", form.Values["rate"]);
        Assert.Equal("148300,00", form.Values["debt"]);
        Assert.Equal("2026-12-01", form.Values["payment"]);
    }

    [Fact]
    public async Task Oeffnen_und_Speichern_veraendert_nichts()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());
        var form = await Service().GetFormAsync(CreateObjectType.Loan, angelegt.Id);

        var gespeichert = await Service().UpdateAsync(
            CreateObjectType.Loan, angelegt.Id!.Value, new Dictionary<string, string?>(form!.Values));

        Assert.True(gespeichert.Ok, gespeichert.Message);

        using var check = database.Context();
        var loan = Assert.Single(check.Loans);
        Assert.Equal(148_300m, loan.RemainingDebt);
        Assert.Equal(1.84m, loan.InterestRatePercent);
        Assert.Equal(1_180m, loan.Installment);
    }

    [Fact]
    public async Task Eine_neue_Restschuld_wird_uebernommen()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());

        var result = await Service().UpdateAsync(
            CreateObjectType.Loan, angelegt.Id!.Value, Werte(debt: "140.000,00"));

        Assert.True(result.Ok, result.Message);
        Assert.Equal($"/darlehen?id={angelegt.Id}", result.Route);

        using var check = database.Context();
        Assert.Equal(140_000m, check.Loans.Single().RemainingDebt);
    }

    // ── Löschen ────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Löschfolge zählt die Objekte, statt sie zu behaupten.</summary>
    [Fact]
    public async Task Die_Loeschfolge_zaehlt_die_verknuepften_Objekte()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());
        Haus("Haus Birkenstetter Straße", angelegt.Id);

        var form = await Service().GetFormAsync(CreateObjectType.Loan, angelegt.Id);

        Assert.Equal("Darlehen löschen", form!.DeleteImpact!.Title);
        Assert.Contains("1 Immobilie verliert die Verknüpfung, bleibt aber bestehen.",
            form.DeleteImpact.Consequence);
    }

    /// <summary>Und sie sagt es im richtigen Numerus — zwei Objekte verlieren, eines verliert.</summary>
    [Fact]
    public async Task Bei_zwei_Objekten_steht_die_Folge_im_Plural()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());
        Haus("Haus Birkenstetter Straße", angelegt.Id);
        Haus("Wohnung Bahnhofstraße", angelegt.Id);

        var form = await Service().GetFormAsync(CreateObjectType.Loan, angelegt.Id);

        Assert.Contains("2 Immobilien verlieren die Verknüpfung, bleiben aber bestehen.",
            form!.DeleteImpact!.Consequence);
    }

    [Fact]
    public async Task Ohne_Objekt_sagt_die_Folge_auch_das()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());

        var form = await Service().GetFormAsync(CreateObjectType.Loan, angelegt.Id);

        Assert.Contains("Es hängt keine Immobilie daran.", form!.DeleteImpact!.Consequence);
    }

    /// <summary>
    /// Das Löschen nimmt die Immobilie nicht mit.
    /// </summary>
    /// <remarks>
    /// Ein abbezahltes Darlehen verschwindet, das Haus steht weiter. Die Verknüpfung wird auf
    /// null gesetzt — stünde dort eine fremde Kennung weiter, zeigte der Objektbericht Zins und
    /// Tilgung eines Darlehens, das es nicht mehr gibt.
    /// </remarks>
    [Fact]
    public async Task Geloescht_wird_das_Darlehen_und_nicht_das_Haus()
    {
        var angelegt = await Service().CreateAsync(CreateObjectType.Loan, Werte());
        Haus("Haus Birkenstetter Straße", angelegt.Id);

        var result = await Service().DeleteAsync(CreateObjectType.Loan, angelegt.Id!.Value);

        Assert.True(result.Ok);
        Assert.Equal("/darlehen", result.Route);

        using var check = database.Context();
        Assert.Empty(check.Loans);

        var haus = Assert.Single(check.Properties);
        Assert.Null(haus.LoanId);
    }

    private void Haus(string name, int? loanId)
    {
        using var context = database.Context();
        context.Properties.Add(new Property
        {
            Name = name,
            Kind = PropertyKind.House,
            PurchaseDate = new DateOnly(2019, 4, 1),
            MarketValue = 420_000m,
            LoanId = loanId,
        });

        context.SaveChanges();
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
