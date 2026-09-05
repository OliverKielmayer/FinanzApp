using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Anlege-Flows. Geprüft wird, was der Handoff ausdrücklich verlangt: die Validierung nennt
/// das fehlende Feld beim Namen, doppelte Anlage wird abgelehnt, und jeder Flow schreibt
/// wirklich — kein Formular, das nur so tut.
/// </summary>
public sealed class CreateFormTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private CreateFormService Service() => Service(database);

    private CreateFormService Service(TestDatabase db)
    {
        var context = db.Context();
        var paths = TestDatabase.PathService(root);
        var labels = new ObjectLabelService(context);
        var documents = new DocumentService(
            context, paths, labels, clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentService>.Instance);

        return new CreateFormService(context, clock, documents, new NoPolicyDocumentAnalyzer());
    }

    [Fact]
    public async Task Fehlendes_Pflichtfeld_wird_beim_Namen_genannt()
    {
        var result = await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.HouseholdContents),
            // Versicherer fehlt
            ["premium"] = "156",
        });

        Assert.False(result.Ok);
        Assert.Equal("provider", result.FieldKey);
        Assert.Equal("Versicherer fehlt", result.Message);
    }

    [Fact]
    public async Task Die_Meldung_nennt_das_erste_fehlende_Feld_der_Reihenfolge_nach()
    {
        // Art steht vor Versicherer im Formular, also wird sie zuerst bemängelt.
        var result = await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>());

        Assert.False(result.Ok);
        Assert.Equal("kind", result.FieldKey);
        Assert.Equal("Art fehlt", result.Message);
    }

    [Fact]
    public async Task Konto_wird_wirklich_angelegt_und_ist_danach_waehlbar()
    {
        var created = await Service().CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
        {
            ["kind"] = "checking",
            ["bank"] = "Volksbank",
            ["opening"] = "1.234,56",
            ["asOf"] = "2026-08-01",
        });

        Assert.True(created.Ok);

        using (var context = database.Context())
        {
            var account = Assert.Single(context.Accounts);
            Assert.Equal("Volksbank Giro", account.Name);
            Assert.Equal(1234.56m, account.OpeningBalance);
            Assert.Equal(new DateOnly(2026, 8, 1), account.BalanceAsOf);
        }

        // Und es steht sofort in der Vertragsanlage zur Auswahl — der Handoff verlangt genau das.
        var contractForm = await Service().GetFormAsync(CreateObjectType.Contract);
        var accountField = contractForm!.Fields.Single(f => f.Key == "account");
        Assert.Contains(accountField.Options!, o => o.Label == "Volksbank Giro");
    }

    [Fact]
    public async Task Zweites_Budget_auf_dieselbe_Kategorie_wird_abgelehnt()
    {
        int categoryId;
        using (var context = database.Context())
        {
            var category = new Category { Name = "Lebensmittel", Direction = CategoryDirection.Expense };
            context.Categories.Add(category);
            context.SaveChanges();
            categoryId = category.Id;
        }

        var values = new Dictionary<string, string?>
        {
            ["category"] = categoryId.ToString(),
            ["amount"] = "600",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Budget, values)).Ok);

        var second = await Service().CreateAsync(CreateObjectType.Budget, values);

        Assert.False(second.Ok);
        Assert.Equal("category", second.FieldKey);
        Assert.Equal("Budget für Lebensmittel besteht bereits", second.Message);
    }

    [Fact]
    public async Task Quartalsbudget_wird_auf_den_Monat_heruntergerechnet()
    {
        int categoryId;
        using (var context = database.Context())
        {
            var category = new Category { Name = "Kleidung", Direction = CategoryDirection.Expense };
            context.Categories.Add(category);
            context.SaveChanges();
            categoryId = category.Id;
        }

        await Service().CreateAsync(CreateObjectType.Budget, new Dictionary<string, string?>
        {
            ["category"] = categoryId.ToString(),
            ["amount"] = "900",
            ["period"] = nameof(PeriodScope.Quarter),
        });

        using var check = database.Context();
        var budget = Assert.Single(check.Budgets);
        Assert.Equal(300m, budget.PlannedPerMonth);
        Assert.Equal(PeriodScope.Quarter, budget.Period);
    }

    [Fact]
    public async Task Depot_im_Kontoformular_legt_kein_Konto_an()
    {
        // Der Handoff: „Depot" im Konto-Formular verweist auf den Depot-Flow.
        var form = await Service().GetFormAsync(CreateObjectType.Account);
        var depotOption = form!.Fields.Single(f => f.Key == "kind").Options!.Single(o => o.Value == "depot");

        Assert.Equal("/neu/depot", depotOption.RedirectTo);

        var result = await Service().CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
        {
            ["kind"] = "depot",
            ["bank"] = "finanzen.net ZERO",
            ["opening"] = "0",
            ["asOf"] = "2026-08-01",
        });

        Assert.False(result.Ok);
        using var check = database.Context();
        Assert.Empty(check.Accounts);
    }

    [Fact]
    public async Task Vorsorgevertrag_traegt_seinen_Wert_und_Stichtag()
    {
        var result = await Service().CreateAsync(CreateObjectType.Pension, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.Riester),
            ["provider"] = "Debeka",
            ["value"] = "11.930,40",
            ["asOf"] = "2025-12-31",
        });

        Assert.True(result.Ok);

        using var check = database.Context();
        var policy = Assert.Single(check.Policies);
        Assert.True(policy.IsCapitalForming);
        Assert.Equal(11930.40m, policy.AssetValue);
        Assert.Equal(new DateOnly(2025, 12, 31), policy.ValuationDate);
    }

    [Fact]
    public async Task Eine_Absicherung_bekommt_keinen_Wert_auch_nicht_versehentlich()
    {
        await Service().CreateAsync(CreateObjectType.Protection, new Dictionary<string, string?>
        {
            ["kind"] = nameof(PolicyKind.TermLife),
            ["provider"] = "Heidelberger Leben",
            ["premium"] = "42",
            ["interval"] = nameof(PremiumInterval.Monthly),

            // Diese beiden Felder gibt es im Absicherungs-Formular gar nicht.
            ["value"] = "250000",
            ["asOf"] = "2025-12-31",
        });

        using var check = database.Context();
        var policy = Assert.Single(check.Policies);
        Assert.False(policy.IsCapitalForming);
        Assert.Null(policy.AssetValue);
        Assert.Null(policy.CurrentValue);
    }

    [Fact]
    public async Task Betrag_wird_deutsch_und_englisch_gelesen()
    {
        foreach (var (input, expected) in new[] { ("1.234,56", 1234.56m), ("1234,56", 1234.56m), ("1234.56", 1234.56m) })
        {
            using var fresh = new TestDatabase();
            var service = Service(fresh);

            var result = await service.CreateAsync(CreateObjectType.Account, new Dictionary<string, string?>
            {
                ["kind"] = "checking",
                ["bank"] = "Testbank",
                ["opening"] = input,
                ["asOf"] = "2026-08-01",
            });

            Assert.True(result.Ok, input);
            using var check = fresh.Context();
            Assert.Equal(expected, check.Accounts.Single().OpeningBalance);
        }
    }

    [Fact]
    public async Task Jeder_Objekttyp_hat_ein_Formular_mit_Pflichtfeldern()
    {
        CreateObjectType[] types =
        [
            CreateObjectType.Account, CreateObjectType.Depot, CreateObjectType.Pension,
            CreateObjectType.Protection, CreateObjectType.Property, CreateObjectType.Contract,
            CreateObjectType.Budget, CreateObjectType.Vehicle,
        ];

        foreach (var type in types)
        {
            var form = await Service().GetFormAsync(type);

            Assert.NotNull(form);
            Assert.NotEmpty(form.Fields);
            Assert.Contains(form.Fields, f => f.Required);
            Assert.False(string.IsNullOrWhiteSpace(form.SubmitLabel), type.ToString());
        }
    }

    [Fact]
    public async Task Fahrzeug_verknuepft_die_Versicherung_statt_sie_zu_kopieren()
    {
        int policyId;
        using (var context = database.Context())
        {
            var policy = new Policy
            {
                Kind = PolicyKind.Vehicle,
                IsCapitalForming = false,
                Name = "Kfz WGV",
                Provider = "WGV",
                Premium = 618m,
                PremiumInterval = PremiumInterval.Yearly,
            };
            context.Policies.Add(policy);
            context.SaveChanges();
            policyId = policy.Id;
        }

        // Zur Auswahl steht nur, was ein Kfz-Vertrag ist — eine Hausratversicherung gehört
        // nicht ans Auto.
        var form = await Service().GetFormAsync(CreateObjectType.Vehicle);
        var field = form!.Fields.Single(f => f.Key == "policy");
        Assert.Contains(field.Options!, o => o.Value == policyId.ToString());

        var result = await Service().CreateAsync(CreateObjectType.Vehicle, new Dictionary<string, string?>
        {
            ["name"] = "VW Passat Variant",
            ["plate"] = "L-2905",
            ["policy"] = policyId.ToString(),
        });

        Assert.True(result.Ok);

        using var check = database.Context();
        var vehicle = Assert.Single(check.Vehicles);
        Assert.Equal(policyId, vehicle.PolicyId);

        // Der Vertrag bleibt genau einer — er wurde verwiesen, nicht kopiert.
        Assert.Single(check.Policies);
    }

    [Fact]
    public async Task Zweites_Fahrzeug_mit_demselben_Kennzeichen_wird_abgelehnt()
    {
        var values = new Dictionary<string, string?>
        {
            ["name"] = "VW Passat Variant",
            ["plate"] = "L-2905",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Vehicle, values)).Ok);

        var second = await Service().CreateAsync(CreateObjectType.Vehicle, values);

        Assert.False(second.Ok);
        Assert.Equal("plate", second.FieldKey);
    }

    [Fact]
    public async Task Ohne_Analyse_wird_die_Datei_trotzdem_abgelegt()
    {
        // Der Handoff: lauffähig auch wenn die Analyse fehlt — dann dieselbe Maske, leer.
        using var content = new MemoryStream("nur ein Beleg"u8.ToArray());

        var result = await Service().AnalyseAsync(CreateObjectType.Pension, content, "Police.pdf");

        Assert.False(result.HasContent);
        Assert.Empty(result.Fields);
        Assert.Equal("Police.pdf", result.FileName);
        Assert.False(string.IsNullOrWhiteSpace(result.RelativePath));
        Assert.Contains("keine Analyse", result.Note);

        // Das Dokument existiert, die Herkunftstabelle bleibt leer — es wurde ja nichts gelesen.
        using var check = database.Context();
        Assert.Single(check.Documents);
        Assert.Empty(check.DocumentExtractions);
    }

    [Fact]
    public async Task Der_Dateiname_liefert_keine_Metadaten()
    {
        // Metadaten kommen aus dem Inhalt, nie aus dem Namen — auch wenn der noch so sprechend ist.
        using var content = new MemoryStream("x"u8.ToArray());

        var result = await Service().AnalyseAsync(
            CreateObjectType.Protection, content, "Hausrat_HUK_156EUR_2027-12-31.pdf");

        Assert.Empty(result.Fields);
    }

    [Fact]
    public async Task Bestätigen_ohne_gelesene_Werte_vermerkt_nichts()
    {
        using var content = new MemoryStream("x"u8.ToArray());
        await Service().AnalyseAsync(CreateObjectType.Pension, content, "Police.pdf");

        using var check = database.Context();
        var documentId = check.Documents.Single().Id;

        Assert.Equal(0, await Service().ConfirmExtractionsAsync(documentId));
    }

    // ── Mehrere Verträge bei einem Anbieter ───────────────────────────────────────────────

    /// <summary>
    /// Zwei Versicherungen derselben Art bei demselben Anbieter lassen sich anlegen.
    /// </summary>
    /// <remarks>
    /// Der Normalfall: zwei Autos, zwei Wohnungen, zwei Lebensversicherungen bei derselben
    /// Gesellschaft. Vorher prüfte die Anwendung die <em>Bezeichnung</em> — und die ist beim
    /// Anlegen abgeleitet, bei einer Absicherung die Vertragsart. Die zweite Hausratpolice wurde
    /// deshalb abgewiesen, obwohl es ein anderer Vertrag ist.
    /// </remarks>
    [Fact]
    public async Task Zwei_Versicherungen_bei_einem_Anbieter_sind_erlaubt()
    {
        Dictionary<string, string?> Police(string nummer) => new()
        {
            ["kind"] = nameof(PolicyKind.HouseholdContents),
            ["provider"] = "HUK-Coburg",
            ["number"] = nummer,
            ["premium"] = "156",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Protection, Police("HR-1"))).Ok);

        var zweite = await Service().CreateAsync(CreateObjectType.Protection, Police("HR-2"));

        Assert.True(zweite.Ok);

        using var context = database.Context();
        Assert.Equal(2, context.Policies.Count(p => p.Provider == "HUK-Coburg"));
    }

    /// <summary>Auch zwei Vorsorgeverträge bei einer Gesellschaft gehen.</summary>
    /// <remarks>
    /// Dort ist die abgeleitete Bezeichnung der Anbieter selbst — sie kollidierte damit schon beim
    /// zweiten Vertrag desselben Hauses.
    /// </remarks>
    [Fact]
    public async Task Zwei_Vorsorgevertraege_bei_einer_Gesellschaft_sind_erlaubt()
    {
        Dictionary<string, string?> Vertrag(string nummer) => new()
        {
            ["kind"] = nameof(PolicyKind.CapitalLife),
            ["provider"] = "Heidelberger Leben",
            ["number"] = nummer,
            ["premium"] = "212",
            ["value"] = "10.000",
            ["asOf"] = "2026-06-30",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Pension, Vertrag("LV-1"))).Ok);
        Assert.True((await Service().CreateAsync(CreateObjectType.Pension, Vertrag("LV-2"))).Ok);

        using var context = database.Context();
        Assert.Equal(2, context.Policies.Count(p => p.Provider == "Heidelberger Leben"));
    }

    /// <summary>
    /// Dieselbe Nummer beim selben Anbieter ist eine Doppelung und wird abgewiesen.
    /// </summary>
    /// <remarks>
    /// Sie identifiziert den Vertrag. Die Meldung nennt das Nummernfeld, nicht den Anbieter —
    /// dort liegt die Ursache.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Versicherungsnummer_wird_abgewiesen()
    {
        Dictionary<string, string?> Police() => new()
        {
            ["kind"] = nameof(PolicyKind.Vehicle),
            ["provider"] = "Allianz",
            ["number"] = "KFZ-4711",
            ["premium"] = "78,40",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Protection, Police())).Ok);

        var zweite = await Service().CreateAsync(CreateObjectType.Protection, Police());

        Assert.False(zweite.Ok);
        Assert.Equal("number", zweite.FieldKey);
        Assert.Contains("KFZ-4711", zweite.Message);
    }

    /// <summary>
    /// Ohne Nummer wird nichts abgewiesen.
    /// </summary>
    /// <remarks>
    /// Dann trägt der Bestand keine Angabe, an der sich eine Doppelung erkennen ließe — und ein
    /// Verdacht ist kein Grund, den zweiten Vertrag zu verweigern.
    /// </remarks>
    [Fact]
    public async Task Ohne_Nummer_wird_nichts_abgewiesen()
    {
        Dictionary<string, string?> Police() => new()
        {
            ["kind"] = nameof(PolicyKind.Liability),
            ["provider"] = "Adam Riese",
            ["premium"] = "89",
        };

        Assert.True((await Service().CreateAsync(CreateObjectType.Protection, Police())).Ok);
        Assert.True((await Service().CreateAsync(CreateObjectType.Protection, Police())).Ok);

        using var context = database.Context();
        Assert.Equal(2, context.Policies.Count());
    }

    /// <summary>Auch beim Bearbeiten führt dieselbe Nummer nicht zu zwei gleichen Verträgen.</summary>
    [Fact]
    public async Task Beim_Bearbeiten_wird_dieselbe_Nummer_abgewiesen()
    {
        Dictionary<string, string?> Police(string nummer) => new()
        {
            ["kind"] = nameof(PolicyKind.HouseholdContents),
            ["provider"] = "HUK-Coburg",
            ["number"] = nummer,
            ["premium"] = "156",
        };

        var erste = await Service().CreateAsync(CreateObjectType.Protection, Police("HR-1"));
        var zweite = await Service().CreateAsync(CreateObjectType.Protection, Police("HR-2"));

        Assert.True(erste.Ok);
        Assert.True(zweite.Ok);

        var id = zweite.Id!.Value;

        var maske = await Service().GetFormAsync(CreateObjectType.Protection, id);
        var werte = new Dictionary<string, string?>(maske!.Values!) { ["number"] = "HR-1" };

        var ergebnis = await Service().UpdateAsync(CreateObjectType.Protection, id, werte);

        Assert.False(ergebnis.Ok);
        Assert.Equal("number", ergebnis.FieldKey);

        // Und der eigene Vertrag darf seine eigene Nummer behalten.
        Assert.True((await Service().UpdateAsync(
            CreateObjectType.Protection, id, maske.Values)).Ok);
    }

    // ── Vorbelegung von Zahlenfeldern ─────────────────────────────────────────────────────

    /// <summary>
    /// Eine Zahl mit Nachkommastelle kommt mit Punkt zurück in die Maske.
    /// </summary>
    /// <remarks>
    /// <para>Zahlenfelder rendern als <c>&lt;input type="number"&gt;</c>, und ein solches Feld
    /// verwirft „38,5“ als ungültig: es steht dann <em>leer</em> da, und das nächste Speichern
    /// löscht die gepflegte Angabe. Betroffen waren Arbeitszeit, Entfernung zur Arbeit,
    /// Eigentumsanteil und Wohnfläche.</para>
    /// <para>Derselbe Fehler wie bei den Wertbestandteilen einer Police (Handoff 20, §19.4):
    /// eine Vorbelegung, die das Feld nicht annimmt, ist ein stiller Datenverlust.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_Zahlenfeld_wird_mit_Punkt_vorbelegt()
    {
        int objekt;

        using (var context = database.Context())
        {
            var haus = new Property
            {
                Name = "Haus mit halben Metern",
                PurchaseDate = new DateOnly(2019, 4, 1),
                MarketValue = 400000m,
                LivingArea = 150.5m,
            };

            context.Properties.Add(haus);
            context.SaveChanges();
            objekt = haus.Id;
        }

        var maske = await Service().GetFormAsync(CreateObjectType.Property, objekt);

        Assert.Equal("150.5", maske!.Values!["area"]);

        // Und zurückgeschickt landet derselbe Wert wieder im Bestand — nicht 1505 und nicht null.
        var ergebnis = await Service().UpdateAsync(
            CreateObjectType.Property, objekt, maske.Values);

        Assert.True(ergebnis.Ok);

        using var pruefung = database.Context();
        Assert.Equal(150.5m, pruefung.Properties.Single(p => p.Id == objekt).LivingArea);
    }

    /// <summary>Ganze Zahlen bleiben ohne Nachkomma-Anhang.</summary>
    /// <remarks>
    /// „142“ und nicht „142.00“: das Feld soll aussehen wie eingegeben, sonst liest es sich wie
    /// eine Genauigkeit, die niemand gemeint hat.
    /// </remarks>
    [Fact]
    public async Task Eine_ganze_Zahl_bleibt_ganz()
    {
        int objekt;

        using (var context = database.Context())
        {
            var haus = new Property
            {
                Name = "Haus mit ganzen Metern",
                PurchaseDate = new DateOnly(2019, 4, 1),
                MarketValue = 400000m,
                LivingArea = 142m,
            };

            context.Properties.Add(haus);
            context.SaveChanges();
            objekt = haus.Id;
        }

        var maske = await Service().GetFormAsync(CreateObjectType.Property, objekt);

        Assert.Equal("142", maske!.Values!["area"]);
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
