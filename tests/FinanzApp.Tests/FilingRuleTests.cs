using System.Text;
using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Gelernte Ablageregeln: wohin Post von einem Absender gehört.
/// </summary>
/// <remarks>
/// <para>Die App schlägt die <em>Art</em> eines Schreibens längst vor — aus Schlagwörtern, mit
/// Schwelle. Wozu es gehört, schlug bisher nichts vor, und genau das musste bei jedem Beleg von
/// Hand gesucht werden. Das Material lag dabei schon da: Jede frühere Zuordnung ist ein
/// Beispiel.</para>
/// <para>Was hier geprüft wird, ist vor allem die Zurückhaltung. Eine Regel, die zu breit
/// greift, legt einen Beleg still an ein fremdes Objekt — und dort sucht ihn niemand. Deshalb
/// der ganze Absender als Muster und nicht sein erstes Wort, deshalb kein Lernen aus einer
/// halben Angabe, und deshalb ein Vorschlag statt einer Ablage.</para>
/// </remarks>
public sealed class FilingRuleTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 6);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private ScanInboxService Eingang(FinanzAppDbContext context)
        => new(context, Documents(context), clock);

    private int Objekt(string name = "Haus Musterweg 3")
    {
        using var context = database.Context();
        var haus = new Property { Name = name, MarketValue = 400_000m };
        context.Properties.Add(haus);
        context.SaveChanges();
        return haus.Id;
    }

    private int Dokumentart(string name = "Gebührenbescheid", DocumentArea bereich = DocumentArea.Housing)
    {
        using var context = database.Context();
        var art = new DocumentType { Name = name, Area = bereich };
        context.DocumentTypes.Add(art);
        context.SaveChanges();
        return art.Id;
    }

    /// <summary>Legt einen Beleg ab und meldet ihn im Eingang an.</summary>
    private async Task<int> Einliefern(string dateiname, string? absender)
    {
        int dokumentId;
        using (var context = database.Context())
        {
            var pfad = Path.Combine(root, "Sonstiges", dateiname);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            await File.WriteAllTextAsync(pfad, "Beleg", Encoding.UTF8);

            var dokument = new Document
            {
                Title = Path.GetFileNameWithoutExtension(dateiname),
                RelativePath = $"Sonstiges/{dateiname}",
                FileName = dateiname,
                Extension = ".pdf",
                DocumentDate = new DateOnly(2026, 11, 6),
            };

            context.Documents.Add(dokument);
            context.SaveChanges();
            dokumentId = dokument.Id;
        }

        using var ctx = database.Context();
        return await Eingang(ctx).AddAsync(dokumentId, absender, pageCount: 1, recognised: false);
    }

    private async Task<bool> Zuordnen(int eingangId, int artId, LinkTargetType? art, int? ziel)
    {
        using var context = database.Context();
        return await Eingang(context).AssignAsync(eingangId, new AssignScanInboxRequest
        {
            DocumentTypeId = artId,
            TargetType = art,
            TargetId = ziel,
        });
    }

    private async Task<ScanInboxItemDto?> Eintrag(int eingangId)
    {
        using var context = database.Context();
        var liste = await Eingang(context).GetAsync();
        return liste.Items.FirstOrDefault(i => i.Id == eingangId);
    }

    // ── Lernen ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_Zuordnung_wird_zur_Regel()
    {
        var haus = Objekt();
        var art = Dokumentart();
        var erster = await Einliefern("bescheid1.pdf", "Landkreis Musterkreis Abfallwirtschaftsamt");

        Assert.True(await Zuordnen(erster, art, LinkTargetType.Property, haus));

        using var check = database.Context();
        var regel = Assert.Single(check.FilingRules);
        Assert.Equal(LinkTargetType.Property, regel.TargetType);
        Assert.Equal(haus, regel.TargetId);
        Assert.Equal(clock.Now, regel.LearnedAt);
    }

    /// <summary>
    /// Der nächste Beleg desselben Absenders bringt das Objekt mit.
    /// </summary>
    /// <remarks>
    /// Der eigentliche Zweck: Beim zweiten Gebührenbescheid soll niemand dasselbe Haus noch
    /// einmal suchen müssen.
    /// </remarks>
    [Fact]
    public async Task Der_naechste_Beleg_desselben_Absenders_schlaegt_das_Objekt_vor()
    {
        var haus = Objekt();
        var art = Dokumentart();

        await Zuordnen(
            await Einliefern("bescheid1.pdf", "Landkreis Musterkreis Abfallwirtschaftsamt"),
            art, LinkTargetType.Property, haus);

        var zweiter = await Einliefern("bescheid2.pdf", "Landkreis Musterkreis Abfallwirtschaftsamt");

        var vorschlag = (await Eintrag(zweiter))?.SuggestedTarget;

        Assert.NotNull(vorschlag);
        Assert.Equal(LinkTargetType.Property, vorschlag.TargetType);
        Assert.Equal(haus, vorschlag.TargetId);
        Assert.Equal("Haus Musterweg 3", vorschlag.Label);
        Assert.Equal("Immobilie", vorschlag.TargetLabel);
        Assert.Equal(new DateOnly(2026, 11, 6), vorschlag.LearnedOn);
    }

    /// <summary>
    /// Eine zweite Zuordnung an ein anderes Objekt berichtigt die Regel <em>nicht</em> — sie
    /// hebt sie auf.
    /// </summary>
    /// <remarks>
    /// <para>Hier stand zuerst das Gegenteil: Die zweite Zuordnung sollte die erste
    /// überschreiben, damit sich eine falsche Regel selbst heilt. An einer echten Ablage fiel
    /// auf, dass sich die beiden Fälle nicht unterscheiden lassen. „Ich habe mich vertan" und
    /// „dieser Absender bedient mehrere Objekte" sehen in den Daten gleich aus, und bei einem
    /// Versicherer mit sieben Verträgen unter einem Briefkopf ist durchgehend der zweite Fall
    /// gemeint.</para>
    /// <para>Also die vorsichtige Lesart: Wer zweimal verschieden zuordnet, hat gezeigt, dass
    /// der Absender nichts über das Objekt verrät. Das kostet im Verwechslungsfall eine
    /// Handsuche; die andere Lesart kostet im Regelfall einen still falsch abgelegten Beleg.</para>
    /// </remarks>
    [Fact]
    public async Task Eine_zweite_Zuordnung_an_ein_anderes_Objekt_hebt_die_Regel_auf()
    {
        var erstes = Objekt("Haus Musterweg 3");
        var zweites = Objekt("Haus Beispielallee 7");
        var art = Dokumentart();

        await Zuordnen(await Einliefern("a.pdf", "Stadtwerke Musterstadt"), art,
            LinkTargetType.Property, erstes);
        await Zuordnen(await Einliefern("b.pdf", "Stadtwerke Musterstadt"), art,
            LinkTargetType.Property, zweites);

        using var check = database.Context();
        Assert.Empty(check.FilingRules);
    }

    // ── Zurückhaltung ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ohne gewähltes Objekt wird nichts gelernt.
    /// </summary>
    /// <remarks>
    /// „Keines“ ist keine Regel. Wer nur die Art gesetzt hat, hat über das Objekt nichts gesagt,
    /// und beim nächsten Beleg soll wieder gefragt werden.
    /// </remarks>
    [Fact]
    public async Task Ohne_Objekt_entsteht_keine_Regel()
    {
        var art = Dokumentart();

        Assert.True(await Zuordnen(
            await Einliefern("ohne.pdf", "Stadtwerke Musterstadt"), art, null, null));

        using var check = database.Context();
        Assert.Empty(check.FilingRules);
    }

    /// <summary>
    /// Ohne Absender wird nichts gelernt.
    /// </summary>
    /// <remarks>
    /// Ein Muster aus einer leeren Zeile träfe jeden Beleg, der ebenfalls keinen Absender trägt —
    /// und das sind die unsichersten von allen.
    /// </remarks>
    [Fact]
    public async Task Ohne_Absender_entsteht_keine_Regel()
    {
        var haus = Objekt();
        var art = Dokumentart();

        Assert.True(await Zuordnen(
            await Einliefern("anonym.pdf", absender: null), art, LinkTargetType.Property, haus));

        using var check = database.Context();
        Assert.Empty(check.FilingRules);
    }

    /// <summary>
    /// Das Muster ist der ganze Absender, nicht sein erstes Wort.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zur Kategorisierung von Buchungen, und der Grund dafür: „Landkreis“
    /// träfe jeden Landkreis. Eine falsche Kategorie steht in einer Auswertung und fällt auf;
    /// ein falsch abgelegter Beleg hängt still am fremden Objekt.
    /// </remarks>
    [Fact]
    public async Task Ein_anderer_Absender_mit_gleichem_ersten_Wort_erbt_nichts()
    {
        var haus = Objekt();
        var art = Dokumentart();

        await Zuordnen(await Einliefern("a.pdf", "Landkreis Musterkreis"), art,
            LinkTargetType.Property, haus);

        var fremd = await Einliefern("b.pdf", "Landkreis Beispielkreis");

        Assert.Null((await Eintrag(fremd))?.SuggestedTarget);
    }

    /// <summary>Ein längerer Absender derselben Stelle erbt dagegen schon.</summary>
    [Fact]
    public async Task Derselbe_Absender_mit_Zusatz_erbt()
    {
        var haus = Objekt();
        var art = Dokumentart();

        await Zuordnen(await Einliefern("a.pdf", "Landkreis Musterkreis"), art,
            LinkTargetType.Property, haus);

        var laenger = await Einliefern("b.pdf", "Landkreis Musterkreis Abfallwirtschaftsamt");

        Assert.Equal(haus, (await Eintrag(laenger))?.SuggestedTarget?.TargetId);
    }

    /// <summary>
    /// Zeigt die Regel auf ein gelöschtes Objekt, entfällt der Vorschlag still.
    /// </summary>
    /// <remarks>
    /// Ein Vorschlag mit dem Namen „Nicht mehr vorhanden“ wäre schlechter als keiner — er
    /// verlangte eine Entscheidung über etwas, das es nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Ein_geloeschtes_Ziel_schlaegt_nichts_mehr_vor()
    {
        var haus = Objekt();
        var art = Dokumentart();

        await Zuordnen(await Einliefern("a.pdf", "Stadtwerke Musterstadt"), art,
            LinkTargetType.Property, haus);

        using (var context = database.Context())
        {
            context.Properties.Remove(context.Properties.Single(p => p.Id == haus));
            context.SaveChanges();
        }

        var weiterer = await Einliefern("b.pdf", "Stadtwerke Musterstadt");

        Assert.Null((await Eintrag(weiterer))?.SuggestedTarget);
    }

    // ── Ein Absender, viele Objekte ────────────────────────────────────────────────────────
    //
    // Der Fall, an dem die Regel ohne Schutz Schaden anrichtete. In einer echten Ablage lagen
    // 82 Statusreports desselben Versicherers, verteilt auf sieben Verträge: Unterschieden
    // werden sie an der Vertragsnummer im Text, nicht am Briefkopf. Eine Regel „dieser
    // Absender gehört an diesen Vertrag" wäre siebenmal von acht falsch gewesen — und zwar
    // still, mit einem vorgewählten Objekt, das plausibel aussieht.

    private int Vertrag(string name)
    {
        using var context = database.Context();
        var police = new Policy
        {
            Name = name,
            Kind = PolicyKind.CapitalLife,
            Provider = "Musterleben",
            Premium = 100m,
            PremiumInterval = PremiumInterval.Monthly,
        };

        context.Policies.Add(police);
        context.SaveChanges();
        return police.Id;
    }

    [Fact]
    public async Task Ein_Absender_mit_mehreren_Objekten_erzeugt_keine_Regel()
    {
        var erster = Vertrag("Musterleben 01");
        var zweiter = Vertrag("Musterleben 02");
        var art = Dokumentart("Statusreport", DocumentArea.Insurance);

        await Zuordnen(await Einliefern("s1.pdf", "Musterleben AG"), art,
            LinkTargetType.Policy, erster);
        await Zuordnen(await Einliefern("s2.pdf", "Musterleben AG"), art,
            LinkTargetType.Policy, zweiter);

        using var check = database.Context();
        Assert.Empty(check.FilingRules);
    }

    /// <summary>
    /// Eine Regel, die sich als uneindeutig erweist, wird gelöscht.
    /// </summary>
    /// <remarks>
    /// Der erste Beleg darf eine Regel anlegen — da weiß die Ablage es nicht besser. Der zweite
    /// widerlegt sie. Sie stehen zu lassen hieße, einen Vorschlag weiterzugeben, von dem die
    /// Ablage bereits weiß, dass er nicht trägt.
    /// </remarks>
    [Fact]
    public async Task Die_widerlegte_Regel_verschwindet_wieder()
    {
        var erster = Vertrag("Musterleben 01");
        var zweiter = Vertrag("Musterleben 02");
        var art = Dokumentart("Statusreport", DocumentArea.Insurance);

        await Zuordnen(await Einliefern("s1.pdf", "Musterleben AG"), art,
            LinkTargetType.Policy, erster);

        using (var nachErstem = database.Context())
        {
            Assert.Single(nachErstem.FilingRules);
        }

        await Zuordnen(await Einliefern("s2.pdf", "Musterleben AG"), art,
            LinkTargetType.Policy, zweiter);

        using var check = database.Context();
        Assert.Empty(check.FilingRules);
    }

    /// <summary>
    /// Derselbe Absender an dasselbe Objekt bleibt eindeutig.
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zum Schutz: Er darf nicht jede Wiederholung für einen Widerspruch halten.
    /// Der zweite Gebührenbescheid desselben Amtes an dasselbe Haus ist genau der Fall, für den
    /// die Regel gebaut ist.
    /// </remarks>
    [Fact]
    public async Task Wiederholung_an_dasselbe_Objekt_ist_kein_Widerspruch()
    {
        var haus = Objekt();
        var art = Dokumentart();

        await Zuordnen(await Einliefern("b1.pdf", "Landkreis Musterkreis"), art,
            LinkTargetType.Property, haus);
        await Zuordnen(await Einliefern("b2.pdf", "Landkreis Musterkreis"), art,
            LinkTargetType.Property, haus);

        using var check = database.Context();
        var regel = Assert.Single(check.FilingRules);
        Assert.Equal(haus, regel.TargetId);
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
