using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Vertragsstände aus einer Vermögensübersicht — der Weg von der Datei bis in den Verlauf.
/// </summary>
/// <remarks>
/// <para>Der Anlass sind zwei Dateien mit demselben Inhalt in verschiedener Gestalt: die
/// Monatsübersicht des Maklers und eine über zwanzig Jahre von Hand geführte
/// Tabellenkalkulation. Die zweite ist in das Format der ersten umgewandelt worden, damit es
/// <b>eine</b> Strecke gibt. Geprüft wird deshalb beides an derselben Stelle: acht Zeilen und
/// zweihundert.</para>
/// <para>Der wichtigste Test ist der ganze Bogen — einliefern verändert keine Vermögenszahl,
/// bestätigen verändert genau die, die in der Datei steht. Und derselbe Monat ein zweites Mal
/// eingelesen schlägt nichts mehr vor.</para>
/// </remarks>
public sealed class PolicyValueImportTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Die Kopfzeile, wie sie in der Datei von MLP steht.</summary>
    private const string Header =
        "\"Kategorie\";\"Vertragsart\";\"Vertragsnummer\";\"IBAN\";\"Gesellschaft\";"
        + "\"Vertragsinhaber\";\"Betreuungsstatus\";\"Bezeichnung\";\"Rate mtl. Basis\";"
        + "\"Vermögen in €\";\"Verbindlichkeit in €\";\"Stand\";\"Hinweis\"";

    /// <summary>
    /// Ein Ausschnitt der echten Übersicht: zwei Policen, ein Konto, ein Depot.
    /// </summary>
    /// <remarks>
    /// Die drei Zeilen ohne Vorsorgevertrag gehören dazu und nicht weg. Die Datei führt Konten
    /// und Depots mit, und ein Import, der sie stillschweigend schluckte, ließe niemanden
    /// nachsehen, wo sein Girokonto geblieben ist.
    /// </remarks>
    private static readonly string[] Sheet =
    [
        Header,
        "\"Konten & Kreditkarten\";\"Girokonto\";\"?\";\"\";\"Raiffeisenbank Grüntal\";"
        + "\"Oliver Kielmayer\";\"manuell erfasst\";\"\";\"\";\"3.150,00\";\"\";\"01.11.2023\";\"\"",
        "\"Depots\";\"Wertpapierdepot\";\"1009944110\";\"\";\"FFB\";\"Oliver Kielmayer\";"
        + "\"manuell erfasst\";\"\";\"250,00\";\"27.310,00\";\"\";\"30.09.2023\";\"\"",
        "\"Lebens- und Rentenversicherungen\";\"Lebensversicherung\";\"77001122-01\";\"\";"
        + "\"Heidelberger Lebensversicherung AG\";\"Oliver Kielmayer\";\"\";\"\";\"198,40\";"
        + "\"24.318,60\";\"\";\"31.07.2026\";\"\"",
        "\"Lebens- und Rentenversicherungen\";\"Rentenversicherung\";\"R-77001122-04\";\"\";"
        + "\"Heidelberger Lebensversicherung AG\";\"Oliver Kielmayer\";\"\";\"\";\"74,20\";"
        + "\"15.902,80\";\"\";\"31.07.2026\";\"\"",
    ];

    public void Dispose()
    {
        database.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── Der Leser ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Datei kommt in Windows-1252 — so wird sie auch gelesen.
    /// </summary>
    /// <remarks>
    /// Als UTF-8 gelesen wird aus „Grüntal“ ein Wort mit Ersatzzeichen, und die Nummer daneben
    /// stimmt trotzdem: der Fehler fiele erst im Namen eines angelegten Vertrags auf.
    /// </remarks>
    [Fact]
    public async Task Leser_versteht_Windows1252_und_deutsche_Zahlen()
    {
        var zeilen = await new AssetSheetParser()
            .ParseAsync(Stream(Encoding.Latin1), "uebersicht.csv");

        Assert.Equal(4, zeilen.Count);

        var police = zeilen.Single(z => z.ContractNumber == "77001122-01");
        Assert.Equal(24318.60m, police.Value);
        Assert.Equal(198.40m, police.MonthlyRate);
        Assert.Equal(new DateOnly(2026, 7, 31), police.AsOf);
        Assert.Equal("Heidelberger Lebensversicherung AG", police.Company);
        Assert.Null(police.Problem);

        // Das Konto trägt „?“ als Nummer. Durchgereicht träfe dieser Platzhalter beim nächsten
        // Import eine andere Zeile mit demselben Zeichen.
        var konto = zeilen.Single(z => z.Category.StartsWith("Konten"));
        Assert.Equal("Raiffeisenbank Grüntal", konto.Company);
        Assert.Empty(konto.ContractNumber);
        Assert.Equal("ohne Vertragsnummer", konto.Problem);
    }

    /// <summary>Dieselbe Datei als UTF-8 mit Signatur — die umgewandelte Historie.</summary>
    [Fact]
    public async Task Leser_versteht_auch_UTF8_mit_Signatur()
    {
        var zeilen = await new AssetSheetParser()
            .ParseAsync(Stream(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)), "historie.csv");

        Assert.Equal("Raiffeisenbank Grüntal", zeilen.Single(z => z.Category.StartsWith("Konten")).Company);
        Assert.Equal(24318.60m, zeilen.Single(z => z.ContractNumber == "77001122-01").Value);
    }

    /// <summary>Eine Orderdatei ist auch semikolongetrennt — und trotzdem keine Übersicht.</summary>
    [Fact]
    public void Kopfzeile_entscheidet_und_nicht_der_Dateiname()
    {
        Assert.True(AssetSheetParser.LooksLikeSheet(Stream(Encoding.Latin1)));

        var order = new MemoryStream(Encoding.UTF8.GetBytes(
            "ISIN;Name;Status;Ausführung Datum;Ausführung Kurs\r\nIE00B4L5Y983;Core;ausgeführt;"));

        Assert.False(AssetSheetParser.LooksLikeSheet(order));
    }

    // ── Vorschau ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Was die Datei anbietet, steht neben dem, was im Bestand steht.
    /// </summary>
    /// <remarks>
    /// Der bekannte Vertrag wird getroffen, der unbekannte zum Anlegen vorgeschlagen, Konto und
    /// Depot stehen mit Grund unter „nicht übernommen“. Keine der dreizehn Zeilen verschwindet.
    /// </remarks>
    [Fact]
    public async Task Vorschau_trifft_bekannte_Vertraege_und_benennt_die_uebrigen()
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "Heidelberger Leben 01",
            Provider = "Heidelberger Leben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            PolicyNumber = "77001122-01",
        });

        context.SaveChanges();

        var dokument = await FileAsync(context, Sheet);
        var vorschau = await Service(context).PreviewAsync(dokument);

        Assert.Equal(4, vorschau.LineCount);
        Assert.Equal(2, vorschau.Contracts.Count);
        Assert.Equal(2, vorschau.Skipped.Count);
        Assert.Null(vorschau.Blocker);

        var getroffen = vorschau.Contracts.Single(c => c.ContractNumber == "77001122-01");
        Assert.Equal(PolicyValueAction.Match, getroffen.Action);
        Assert.NotNull(getroffen.PolicyId);
        Assert.Equal(1, getroffen.NewCount);
        Assert.True(getroffen.PreSelected);

        var anzulegen = vorschau.Contracts.Single(c => c.ContractNumber == "R-77001122-04");
        Assert.Equal(PolicyValueAction.Create, anzulegen.Action);
        Assert.Equal(PolicyKind.Pension, anzulegen.SuggestedKind);
        Assert.Equal("Heidelberger Lebensversicherung AG R-77001122-04", anzulegen.SuggestedName);

        // Das Depot hat eine Nummer und einen Wert und ist trotzdem kein Vorsorgevertrag. Der
        // Grund steht dabei, samt dem Weg dorthin.
        var depot = vorschau.Skipped.Single(s => s.Label.Contains("1009944110"));
        Assert.Contains("Depots", depot.Reason);
    }

    // ── Übernahme ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der ganze Bogen: anlegen, Stände schreiben, Wert am Vertrag führen.
    /// </summary>
    [Fact]
    public async Task Uebernahme_legt_an_und_schreibt_den_Stand_ins_Vermoegen()
    {
        using var context = database.Context();

        var dokument = await FileAsync(context, Sheet);
        var dienst = Service(context);
        var vorschau = await dienst.PreviewAsync(dokument);

        var ergebnis = await dienst.ConfirmAsync(Alle(vorschau));

        Assert.Equal(2, ergebnis.PolicyCount);
        Assert.Equal(2, ergebnis.CreatedCount);
        Assert.Equal(2, ergebnis.NewCount);
        Assert.Equal(0, ergebnis.ChangedCount);

        var vertrag = await context.Policies
            .Include(p => p.Reports)
            .SingleAsync(p => p.PolicyNumber == "77001122-01");

        Assert.Equal(PolicyKind.CapitalLife, vertrag.Kind);
        Assert.True(vertrag.IsCapitalForming);

        // Beitrag und Zahlweise kommen aus der Datei — aber nur beim Anlegen.
        Assert.Equal(198.40m, vertrag.Premium);
        Assert.Equal(PremiumInterval.Monthly, vertrag.PremiumInterval);

        // Der erreichte Wert ist der des neuesten Berichts, und der Stichtag wandert mit.
        Assert.Equal(24318.60m, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2026, 7, 31), vertrag.ValuationDate);

        var bericht = Assert.Single(vertrag.Reports);
        Assert.Equal(PolicyValueImportService.Source, bericht.Source);
        Assert.Equal(dokument, bericht.DocumentId);
    }

    /// <summary>
    /// Dieselbe Datei ein zweites Mal schlägt nichts mehr vor.
    /// </summary>
    /// <remarks>
    /// Der Ordnerdienst liefert monatlich ein; wer im Zweifel zweimal einliest, soll keinen
    /// zweiten Punkt neben den ersten legen. Ein Stichtag, ein Stand.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Datei_zweimal_aendert_beim_zweiten_Mal_nichts()
    {
        using var context = database.Context();

        var dokument = await FileAsync(context, Sheet);
        var dienst = Service(context);

        await dienst.ConfirmAsync(Alle(await dienst.PreviewAsync(dokument)));

        var zweite = await dienst.PreviewAsync(dokument);

        Assert.Equal(0, zweite.NewCount);
        Assert.Equal(0, zweite.ChangedCount);
        Assert.Equal(2, zweite.UnchangedCount);
        Assert.All(zweite.Contracts, c => Assert.False(c.PreSelected));
        Assert.All(zweite.Contracts, c => Assert.Equal(PolicyValueAction.Match, c.Action));

        // Und wer trotzdem übernimmt, verdoppelt nichts.
        await dienst.ConfirmAsync(Alle(zweite));
        Assert.Equal(2, await context.PolicyReports.CountAsync());
    }

    /// <summary>
    /// Ein berichtigter Wert zum bekannten Stichtag überschreibt, statt zu verdoppeln.
    /// </summary>
    [Fact]
    public async Task Berichtigter_Wert_ersetzt_den_Stand_desselben_Stichtags()
    {
        using var context = database.Context();
        var dienst = Service(context);

        await dienst.ConfirmAsync(Alle(await dienst.PreviewAsync(await FileAsync(context, Sheet))));

        var berichtigt = Sheet
            .Select(z => z.Replace("\"24.318,60\"", "\"24.900,00\""))
            .ToArray();

        var zweites = await FileAsync(context, berichtigt, "berichtigt.csv");
        var vorschau = await dienst.PreviewAsync(zweites);

        var betroffen = vorschau.Contracts.Single(c => c.ContractNumber == "77001122-01");
        Assert.Equal(1, betroffen.ChangedCount);
        Assert.Equal(24318.60m, Assert.Single(betroffen.Stands).Previous);

        var ergebnis = await dienst.ConfirmAsync(Alle(vorschau));
        Assert.Equal(1, ergebnis.ChangedCount);
        Assert.Equal(0, ergebnis.NewCount);

        var vertrag = await context.Policies
            .Include(p => p.Reports)
            .SingleAsync(p => p.PolicyNumber == "77001122-01");

        Assert.Single(vertrag.Reports);
        Assert.Equal(24900m, vertrag.CurrentValue);
    }

    /// <summary>
    /// Ein nachgetragener älterer Stand ergänzt die Reihe, ohne den Kopfwert zu senken.
    /// </summary>
    /// <remarks>
    /// Genau der Fall der umgewandelten Historie: sie kommt <em>nach</em> der Monatsdatei herein
    /// und trägt zwanzig Jahre nach. Setzte sie den erreichten Wert auf ihren letzten Satz,
    /// stünde am Vertrag hinterher eine Zahl von 2015.
    /// </remarks>
    [Fact]
    public async Task Nachgetragene_Historie_ergaenzt_die_Reihe_und_senkt_den_Wert_nicht()
    {
        using var context = database.Context();
        var dienst = Service(context);

        await dienst.ConfirmAsync(Alle(await dienst.PreviewAsync(await FileAsync(context, Sheet))));

        string[] historie =
        [
            Header,
            Zeile("77001122-01", "Lebensversicherung", "20.415,30", "31.12.2023"),
            Zeile("77001122-01", "Lebensversicherung", "21.006,45", "31.12.2024"),
        ];

        var dokument = await FileAsync(context, historie, "historie.csv");
        var vorschau = await dienst.PreviewAsync(dokument);

        Assert.Equal(2, vorschau.NewCount);
        await dienst.ConfirmAsync(Alle(vorschau));

        var vertrag = await context.Policies
            .Include(p => p.Reports)
            .SingleAsync(p => p.PolicyNumber == "77001122-01");

        Assert.Equal(3, vertrag.Reports.Count);
        Assert.Equal(24318.60m, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2026, 7, 31), vertrag.ValuationDate);
    }

    /// <summary>
    /// Zweihundert Stände sind derselbe Fall wie acht.
    /// </summary>
    /// <remarks>
    /// Die umgewandelte Historie trägt für sieben Verträge zweiundzwanzig Jahre. Geprüft wird,
    /// dass daraus eine Reihe je Vertrag wird und nicht eine gemeinsame — und dass der jüngste
    /// Satz oben landet.
    /// </remarks>
    [Fact]
    public async Task Historie_wird_je_Vertrag_zu_einer_eigenen_Reihe()
    {
        using var context = database.Context();

        var zeilen = new List<string> { Header };
        var erwartet = 0m;

        for (var jahr = 2005; jahr <= 2026; jahr++)
        {
            for (var monat = 1; monat <= 12; monat++)
            {
                if (jahr == 2026 && monat > 7)
                {
                    continue;
                }

                var tag = DateTime.DaysInMonth(jahr, monat);
                erwartet = 10000m + ((jahr - 2005) * 12) + monat;

                zeilen.Add(Zeile("77001122-01", "Lebensversicherung",
                    erwartet.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-DE")),
                    $"{tag:00}.{monat:00}.{jahr}"));

                zeilen.Add(Zeile("77001122-03", "Rentenversicherung", "5.000,00",
                    $"{tag:00}.{monat:00}.{jahr}"));
            }
        }

        var dokument = await FileAsync(context, [.. zeilen], "historie.csv");
        var dienst = Service(context);
        var vorschau = await dienst.PreviewAsync(dokument);

        Assert.Equal(2, vorschau.Contracts.Count);
        Assert.Equal(new DateOnly(2005, 1, 31), vorschau.From);
        Assert.Equal(new DateOnly(2026, 7, 31), vorschau.To);

        await dienst.ConfirmAsync(Alle(vorschau));

        var stände = zeilen.Count - 1;
        Assert.Equal(stände, await context.PolicyReports.CountAsync());

        var vertrag = await context.Policies.SingleAsync(p => p.PolicyNumber == "77001122-01");
        Assert.Equal(erwartet, vertrag.CurrentValue);
        Assert.Equal(new DateOnly(2026, 7, 31), vertrag.ValuationDate);

        // Der zweite Vertrag führt seine eigene Reihe mit seinem eigenen Wert.
        var zweiter = await context.Policies.SingleAsync(p => p.PolicyNumber == "77001122-03");
        Assert.Equal(5000m, zweiter.CurrentValue);
    }

    /// <summary>
    /// Derselbe Vertrag zweimal zum selben Tag: die spätere Zeile gilt.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Schritt liefe die Übernahme in den eindeutigen Schlüssel über Vertrag und
    /// Stichtag — und zwar erst beim Speichern, mit einer Meldung, die niemandem sagt, welche
    /// Zeile gemeint ist.
    /// </remarks>
    [Fact]
    public async Task Zwei_Zeilen_zum_selben_Stichtag_ergeben_einen_Stand()
    {
        using var context = database.Context();

        string[] doppelt =
        [
            Header,
            Zeile("77001122-01", "Lebensversicherung", "24.000,00", "31.07.2026"),
            Zeile("77001122-01", "Lebensversicherung", "24.318,60", "31.07.2026"),
        ];

        var dienst = Service(context);
        var vorschau = await dienst.PreviewAsync(await FileAsync(context, doppelt));

        var vertrag = Assert.Single(vorschau.Contracts);
        Assert.Single(vertrag.Stands);
        Assert.Equal(24318.60m, vertrag.LatestValue);

        await dienst.ConfirmAsync(Alle(vorschau));

        Assert.Equal(1, await context.PolicyReports.CountAsync());
        Assert.Equal(24318.60m,
            (await context.Policies.SingleAsync(p => p.PolicyNumber == "77001122-01")).CurrentValue);
    }

    /// <summary>
    /// Der Beitrag wird nachgetragen, wo keiner steht — und nie überschrieben.
    /// </summary>
    /// <remarks>
    /// <para>Der Fall kommt aus den echten Dateien: die umgewandelte Historie nennt keinen
    /// Beitrag, die Monatsdatei nennt ihn auf den Cent. Welche der beiden zuerst hereinkommt,
    /// darf am Ergebnis nichts ändern — sonst hinge der Beitrag eines Vertrags an der
    /// Reihenfolge zweier Dateien.</para>
    /// <para>Umgekehrt schreibt eine monatlich unbeaufsichtigt hereinkommende Datei keinen
    /// vorhandenen Beitrag um. Das wäre eine Änderung, um die niemand gebeten hat.</para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Beitrag_wird_ergaenzt_aber_nie_ueberschrieben(bool historieZuerst)
    {
        using var context = database.Context();
        var dienst = Service(context);

        // Die Historie: Stände ohne Beitrag. Die Monatsdatei: ein Stand mit Beitrag.
        string[] historie = [Header, OhneRate("77001122-01", "21.006,45", "31.12.2024")];
        string[] monat = [Header, Zeile("77001122-01", "Lebensversicherung", "24.318,60", "31.07.2026")];

        var reihenfolge = historieZuerst ? new[] { historie, monat } : [monat, historie];
        var nummer = 0;

        foreach (var datei in reihenfolge)
        {
            var dokument = await FileAsync(context, datei, $"datei{++nummer}.csv");
            var vorschau = await dienst.PreviewAsync(dokument);

            // Ein nachzutragender Beitrag hält die Zeile angehakt, auch wenn ihre Stände schon
            // dastehen. Ohne das bliebe nach „erst die Historie, dann die Monatsdatei“ ein
            // Beitrag von null stehen: die Zeile wäre abgewählt, weil sich am Wert nichts ändert.
            Assert.All(vorschau.Contracts, c => Assert.True(c.PreSelected));

            await dienst.ConfirmAsync(Alle(vorschau));
        }

        var vertrag = await context.Policies.SingleAsync(p => p.PolicyNumber == "77001122-01");

        Assert.Equal(198.40m, vertrag.Premium);
        Assert.Equal(PremiumInterval.Monthly, vertrag.PremiumInterval);
        Assert.Equal(24318.60m, vertrag.CurrentValue);

        // Und eine spätere Datei mit anderem Beitrag lässt ihn stehen: eine Dynamik wird am
        // Vertrag eingetragen, nicht vom Import.
        string[] erhoeht =
        [
            Header,
            Zeile("77001122-01", "Lebensversicherung", "24.900,00", "31.08.2026")
                .Replace("\"198,40\"", "\"205,00\""),
        ];

        var letztes = await FileAsync(context, erhoeht, "erhoeht.csv");
        await dienst.ConfirmAsync(Alle(await dienst.PreviewAsync(letztes)));

        await context.Entry(vertrag).ReloadAsync();
        Assert.Equal(198.40m, vertrag.Premium);
        Assert.Equal(24900m, vertrag.CurrentValue);
    }

    /// <summary>
    /// Eine Absicherung nimmt keinen Wert an, auch wenn ihre Nummer passt.
    /// </summary>
    /// <remarks>
    /// Ein Risikoleben-Vertrag zahlt im Todesfall; er ist kein Guthaben und darf in keiner
    /// Vermögenssumme auftauchen. Die Vorschau sagt das, statt die Zeile stillschweigend
    /// mitzunehmen.
    /// </remarks>
    [Fact]
    public async Task Absicherung_bekommt_keinen_erreichten_Wert()
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "Risikoleben",
            Provider = "Heidelberger Leben",
            Kind = PolicyKind.TermLife,
            IsCapitalForming = false,
            PolicyNumber = "77001122-01",
        });

        context.SaveChanges();

        string[] eine = [Header, Zeile("77001122-01", "Lebensversicherung", "24.318,60", "31.07.2026")];
        var vorschau = await Service(context).PreviewAsync(await FileAsync(context, eine));

        Assert.Empty(vorschau.Contracts);
        Assert.Contains("Absicherung", Assert.Single(vorschau.Skipped).Reason);
        Assert.NotNull(vorschau.Blocker);
    }

    /// <summary>
    /// Vertragsnummern werden ohne Leerzeichen verglichen, aber nicht ohne Bindestrich.
    /// </summary>
    /// <remarks>
    /// „80 511623094“ steht im Vertrag vielleicht ohne Leerzeichen. Der Bindestrich dagegen
    /// trennt bei „77001122-01“ sieben Verträge voneinander — ohne ihn fielen sie zusammen, und
    /// sieben Verläufe würden einer.
    /// </remarks>
    [Fact]
    public async Task Nummernvergleich_uebergeht_Leerzeichen_aber_nicht_den_Bindestrich()
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "RuV Rente",
            Provider = "RuV",
            Kind = PolicyKind.Pension,
            IsCapitalForming = true,
            PolicyNumber = "80511623094",
        });

        context.SaveChanges();

        string[] zeilen =
        [
            Header,
            Zeile("80 511623094", "Rentenversicherung", "18.740,00", "31.12.2025"),
            Zeile("77001122-01", "Lebensversicherung", "24.318,60", "31.07.2026"),
            Zeile("77001122-02", "Lebensversicherung", "51.320,00", "31.07.2026"),
        ];

        var vorschau = await Service(context).PreviewAsync(await FileAsync(context, zeilen));

        Assert.Equal(PolicyValueAction.Match,
            vorschau.Contracts.Single(c => c.ContractNumber == "80 511623094").Action);

        // Die beiden Heidelberger unterscheiden sich nur hinter dem Bindestrich und bleiben zwei.
        Assert.Equal(2, vorschau.Contracts.Count(c => c.ContractNumber.StartsWith("77001122-")));
    }

    // ── Einlieferung ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Der Ordnerdienst legt die Übersicht ab und übernimmt nichts.
    /// </summary>
    /// <remarks>
    /// Die Regel des unbeaufsichtigten Wegs, hier an der Datei, die sie am meisten braucht: sie
    /// trägt für jeden Vertrag eine Zahl. Ließe der Dienst sie durch, stünden acht
    /// Vermögenswerte in der Anwendung, die nie jemand gesehen hat.
    /// </remarks>
    [Fact]
    public async Task Einlieferung_legt_ab_und_veraendert_keine_Vermoegenszahl()
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = "Heidelberger Leben 01",
            Provider = "Heidelberger Leben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            PolicyNumber = "77001122-01",
        });

        context.SaveChanges();

        var ergebnis = await Intake(context).TakeInAsync(
            Stream(Encoding.Latin1), "MLP_Vermögensübersicht_2026-08-23.csv",
            source: @"C:\Scans\Eingang\MLP.csv");

        Assert.Equal(ScanIntakeOutcome.Waiting, ergebnis.Outcome);
        Assert.Equal(AssetSheetKind.Key, ergebnis.KindKey);
        Assert.Equal(DocumentArea.Insurance, ergebnis.Area);
        // Drei Zeilen sind lesbar — auch das Depot. Was davon ein Vertragsstand wird, weiß der
        // Dienst nicht und behauptet es darum auch nicht.
        Assert.Contains("3 mit Vertragsnummer, Wert und Stichtag", ergebnis.Summary);

        // Nichts ist übernommen: kein Bericht, kein Wert, kein Stichtag.
        Assert.Equal(0, await context.PolicyReports.CountAsync());
        Assert.Null((await context.Policies.SingleAsync()).CurrentValue);

        // Aber die Datei liegt, und der Eingang führt den Weg zum Prüfschritt.
        var eingang = await new ScanInboxService(
            context, Documents(context), clock).GetAsync();

        var zeile = Assert.Single(eingang.Items);
        Assert.False(zeile.Recognised);
        Assert.Equal(AssetSheetKind.Href(ergebnis.DocumentId), zeile.ValuesHref);

        // Und von dort führt der Prüfschritt zu genau den Ständen, die auf dem Papier stehen.
        var dienst = Service(context);
        var vorschau = await dienst.PreviewAsync(ergebnis.DocumentId);
        Assert.Equal(2, vorschau.Contracts.Count);
        Assert.Contains("Eingang", vorschau.Source);

        // Nach der Bestätigung ist die Datei erledigt. Bliebe sie stehen, behauptete der Eingang
        // weiter „wartet auf Prüfung“ über Werte, die längst in den Verträgen stehen.
        await dienst.ConfirmAsync(Alle(vorschau));

        Assert.Empty((await new ScanInboxService(context, Documents(context), clock).GetAsync()).Items);
        Assert.Equal(24318.60m,
            (await context.Policies.SingleAsync(p => p.PolicyNumber == "77001122-01")).CurrentValue);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle angebotenen Verträge zuschalten — die Vorgabe des Schirms.</summary>
    private static ConfirmPolicyValuesRequest Alle(PolicyValueImportDto vorschau)
        => new()
        {
            DocumentId = vorschau.DocumentId,
            Contracts =
            [
                .. vorschau.Contracts.Select(c => new PolicyValueChoice(
                    c.ContractNumber, c.PolicyId, c.Action == PolicyValueAction.Create)),
            ],
        };

    private static MemoryStream Stream(Encoding encoding)
        => new(encoding.GetPreamble().Concat(
            encoding.GetBytes(string.Join("\r\n", Sheet))).ToArray());

    /// <summary>Eine Zeile der Übersicht, wie die Datei sie schreibt.</summary>
    private static string Zeile(string nummer, string art, string wert, string stand)
        => $"\"Lebens- und Rentenversicherungen\";\"{art}\";\"{nummer}\";\"\";"
           + $"\"Heidelberger Lebensversicherung AG\";\"Oliver Kielmayer\";\"\";\"\";\"198,40\";"
           + $"\"{wert}\";\"\";\"{stand}\";\"\"";

    /// <summary>Dieselbe Zeile ohne Beitrag — so sieht die umgewandelte Historie aus.</summary>
    private static string OhneRate(string nummer, string wert, string stand)
        => Zeile(nummer, "Lebensversicherung", wert, stand).Replace("\"198,40\"", "\"\"");

    /// <summary>Legt die Datei ab und gibt ihre Dokumentnummer zurück.</summary>
    private async Task<int> FileAsync(
        FinanzAppDbContext context, string[] zeilen, string fileName = "uebersicht.csv")
    {
        using var inhalt = new MemoryStream(
            Encoding.Latin1.GetBytes(string.Join("\r\n", zeilen)));

        var ablage = await Documents(context).UploadAsync(
            inhalt, fileName, DocumentArea.Insurance,
            title: AssetSheetKind.Label, documentTypeId: null, documentDate: null);

        var dokument = await context.Documents.FirstAsync(d => d.Id == ablage.DocumentId);
        dokument.ScanKind = AssetSheetKind.Key;
        await context.SaveChangesAsync();

        return ablage.DocumentId;
    }

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            NullLogger<DocumentService>.Instance);

    private PolicyValueImportService Service(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new AssetSheetParser(), clock);

    private ScanIntakeService Intake(FinanzAppDbContext context)
    {
        var documents = Documents(context);
        var paths = TestDatabase.PathService(root);

        var scans = new DocumentScanService(
            context, documents, paths, new DepotStatementService(context),
            new ThrowingReader(), clock);

        return new ScanIntakeService(
            context, scans, documents, new ScanInboxService(context, documents, clock),
            new AssetSheetParser(), new CamtStatementParser(), clock);
    }

    /// <summary>
    /// Ein Leser, der jede Datei ablehnt.
    /// </summary>
    /// <remarks>
    /// Er ist die eigentliche Prüfung dieses Tests: käme die Übersicht doch bei der
    /// Belegerkennung an, flöge sie hier auf, statt still ein leeres Ergebnis zu liefern.
    /// </remarks>
    private sealed class ThrowingReader : IPdfTextReader
    {
        public PdfContent Read(Stream stream)
            => throw new InvalidOperationException("Die Übersicht ist bei der PDF-Analyse gelandet.");
    }
}
