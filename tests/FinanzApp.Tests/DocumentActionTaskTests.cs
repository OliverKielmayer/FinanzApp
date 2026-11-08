using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Schreiben, das eine Frist setzt, wird ein Vorgang.
/// </summary>
/// <remarks>
/// <para>Die Frage, die keine Ablageregel beantwortet: <em>Verlangt dieses Schreiben eine
/// Handlung, und bis wann?</em> Für einen Teil der Post steht die Antwort im Klartext da —
/// „zur Zahlung fällig“, „zahlbar bis“ —, und bisher las die Anwendung den Satz, zog ihren
/// Betrag heraus und warf die Frist weg.</para>
/// <para>Der Vorgang trägt die Zeile mit, aus der die Frist stammt. Ein Termin ohne seinen
/// Satz ist eine Behauptung; mit ihm sieht jeder in zwei Sekunden nach, ob richtig gelesen
/// wurde, ohne den Beleg zu öffnen.</para>
/// </remarks>
public sealed class DocumentActionTaskTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 6);

    // ── Was auf dem Tisch landet ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Eine_Frist_vom_Blatt_wird_ein_Vorgang()
    {
        var belegId = Beleg("Gebührenbescheid Musterkreis", new DateOnly(2026, 11, 20),
            "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.");

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal(TaskSource.DocumentAction, vorgang.Source);
        Assert.Equal(LinkTargetType.Document, vorgang.SourceType);
        Assert.Equal(belegId, vorgang.SourceId);
        Assert.Equal(new DateOnly(2026, 11, 20), vorgang.DueOn);
        Assert.Contains("Gebührenbescheid Musterkreis", vorgang.Title);
    }

    /// <summary>
    /// Der Vorgang nennt die Zeile, aus der die Frist gelesen wurde.
    /// </summary>
    /// <remarks>
    /// Ein Vorschlag ohne Begründung wird entweder blind befolgt oder misstrauisch ignoriert,
    /// und beides ist schlechter als ein Satz, der sagt, woher er kommt.
    /// </remarks>
    [Fact]
    public async Task Der_Vorgang_nennt_die_Zeile_aus_der_die_Frist_stammt()
    {
        Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20),
            "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.");

        var vorgang = await EinzigerVorgangAsync();

        Assert.Contains("20.11.2026", vorgang.Detail);
        Assert.Contains("zur Zahlung fällig", vorgang.Detail);
    }

    [Fact]
    public async Task Ein_Beleg_ohne_Frist_wird_kein_Vorgang()
    {
        Beleg("Kontoauszug", frist: null, quelle: null);

        Assert.Empty(await VorgaengeAsync());
    }

    // ── Das Fenster ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Frist in einem halben Jahr steht heute noch nicht auf dem Tisch.
    /// </summary>
    /// <remarks>
    /// Sie gibt jetzt nichts zu tun. Stünde sie da, wäre der Vorgänge-Tab eine Liste von
    /// Dingen, die man zur Kenntnis nimmt statt erledigt — und genau dadurch hört man auf,
    /// hinzusehen.
    /// </remarks>
    [Fact]
    public async Task Eine_weit_entfernte_Frist_steht_noch_nicht_auf_dem_Tisch()
    {
        Beleg("Vorauszahlung", new DateOnly(2027, 5, 1), "zahlbar bis 01.05.2027");

        Assert.Empty(await VorgaengeAsync());
    }

    /// <summary>Eine überfällige Frist bleibt einen Monat liegen und ist als überfällig zu sehen.</summary>
    [Fact]
    public async Task Eine_frisch_verstrichene_Frist_bleibt_liegen_und_gilt_als_ueberfaellig()
    {
        Beleg("Handwerkerrechnung", new DateOnly(2026, 10, 25), "fällig am 25.10.2026");

        var vorgang = await EinzigerVorgangAsync();

        Assert.True(vorgang.IsOverdue);
        Assert.Equal(12, vorgang.DaysOverdue);
    }

    /// <summary>
    /// Eine lange abgelaufene Frist wird kein Vorgang.
    /// </summary>
    /// <remarks>
    /// Anders als bei einer erfassten Rechnung weiß die Anwendung hier nicht, ob bezahlt wurde
    /// — es gibt keinen Status, nur den Satz auf dem Papier. Ohne untere Grenze stünde der
    /// Vorgänge-Tab voller Fristen aus abgelegten Jahren: Archäologie statt Arbeit.
    /// </remarks>
    [Fact]
    public async Task Eine_lange_abgelaufene_Frist_wird_kein_Vorgang()
    {
        Beleg("Alte Rechnung", new DateOnly(2026, 3, 1), "fällig am 01.03.2026");

        Assert.Empty(await VorgaengeAsync());
    }

    // ── Haltbarkeit ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Zweimal_nachsehen_legt_den_Vorgang_nicht_zweimal_an()
    {
        Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20), "fällig am 20.11.2026");

        await VorgaengeAsync();

        Assert.Single(await VorgaengeAsync());
    }

    /// <summary>Wird die Forderung am Beleg entfernt, erledigt sich der Vorgang.</summary>
    [Fact]
    public async Task Ohne_Forderung_am_Beleg_erledigt_sich_der_Vorgang()
    {
        var belegId = Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20), "fällig am 20.11.2026");

        await EinzigerVorgangAsync();

        using (var context = database.Context())
        {
            var beleg = context.Documents.Single(d => d.Id == belegId);
            beleg.DueDate = null;
            beleg.ActionLine = null;
            context.SaveChanges();
        }

        var danach = await VorgaengeAsync();

        Assert.Single(danach);
        Assert.Equal(TaskState.Done, danach[0].State);
    }

    /// <summary>Ein gelöschter Beleg lässt keinen Vorgang zurück, der ins Leere führt.</summary>
    [Fact]
    public async Task Ein_geloeschter_Beleg_laesst_keinen_offenen_Vorgang_zurueck()
    {
        var belegId = Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20), "fällig am 20.11.2026");

        await EinzigerVorgangAsync();

        using (var context = database.Context())
        {
            context.Documents.RemoveRange(context.Documents.Where(d => d.Id == belegId));
            context.SaveChanges();
        }

        var danach = await VorgaengeAsync();

        Assert.Single(danach);
        Assert.Equal(TaskState.Done, danach[0].State);
    }

    // ── Das Lesen vom Blatt ────────────────────────────────────────────────────────────────

    /// <summary>Die Frist wird aus dem Text gelesen, samt ihrer Zeile.</summary>
    [Fact]
    public void Die_Frist_wird_mitsamt_ihrer_Zeile_vom_Blatt_gelesen()
    {
        var dokument = new Document
        {
            Title = "Bescheid",
            RelativePath = "Wohnen/Bescheid.pdf",
            FileName = "Bescheid.pdf",
            ContentText = "Landkreis Musterkreis\nGesamtbetrag 63,40\n"
                          + "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.",
            DocumentDate = new DateOnly(2026, 11, 6),
            CreatedAt = clock.Now,
        };

        var ergebnis = PaperFacts.Apply(dokument, clock.Today, dateMarker: clock.Today);

        Assert.True(ergebnis.Demanded);
        Assert.Equal(new DateOnly(2026, 11, 20), dokument.DueDate);
        Assert.Contains("zur Zahlung fällig", dokument.ActionLine);
    }

    /// <summary>
    /// Eine eingetragene Forderung wird nicht überschrieben.
    /// </summary>
    /// <remarks>
    /// Sie stammt von einem Menschen, aus einem früheren Lesen oder vom Sprachmodell — und ein
    /// zweites Lesen weiß es nicht besser. Die Schranke hängt am Satz und nicht am Termin:
    /// Eine Forderung ohne Datum ist auch eine, und ohne diese Schranke würde sie beim
    /// nächsten Lauf von einer gefundenen Zahlungsfrist verdrängt.
    /// </remarks>
    [Fact]
    public void Eine_eingetragene_Forderung_bleibt_stehen()
    {
        var dokument = new Document
        {
            Title = "Bescheid",
            RelativePath = "Wohnen/Bescheid.pdf",
            FileName = "Bescheid.pdf",
            ContentText = "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.",
            ActionLine = "Bitte stellen Sie Ihren Einzug um.",
            DocumentDate = new DateOnly(2026, 11, 6),
            CreatedAt = clock.Now,
        };

        var ergebnis = PaperFacts.Apply(dokument, clock.Today, dateMarker: clock.Today);

        Assert.False(ergebnis.Demanded);
        Assert.Equal("Bitte stellen Sie Ihren Einzug um.", dokument.ActionLine);
        Assert.Null(dokument.DueDate);
    }

    // ── Eine Forderung ohne Termin ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Schreiben kann etwas verlangen, ohne ein Datum zu nennen.
    /// </summary>
    /// <remarks>
    /// „Bitte stellen Sie Ihren Einzug um“ ist eine Forderung und keine Frist. Der Vorgang
    /// entsteht trotzdem, nur ohne Dringlichkeit — eine erfundene Frist machte daraus eine,
    /// die das Papier nicht hergibt, und der Vorgang stünde eines Tages als „überfällig“ da,
    /// ohne dass jemand etwas versäumt hätte.
    /// </remarks>
    [Fact]
    public async Task Eine_Forderung_ohne_Termin_wird_ein_Vorgang_ohne_Frist()
    {
        Beleg("Umstellungsschreiben", frist: null,
            quelle: "Bitte stellen Sie Ihren Lastschrifteinzug auf das neue Konto um.");

        var vorgang = await EinzigerVorgangAsync();

        Assert.Null(vorgang.DueOn);
        Assert.False(vorgang.IsOverdue);
        Assert.Contains("Zu erledigen", vorgang.Title);
        Assert.Contains("Lastschrifteinzug", vorgang.Detail);
    }

    /// <summary>
    /// Eine Forderung ohne Termin fällt aus keinem Fenster.
    /// </summary>
    /// <remarks>
    /// Das Fenster gibt es, weil eine abgelaufene Frist irgendwann Archäologie wird. Ohne
    /// Termin läuft nichts ab: Der Vorgang bleibt, bis ihn jemand erledigt, egal wie alt der
    /// Beleg ist.
    /// </remarks>
    [Fact]
    public async Task Eine_Forderung_ohne_Termin_bleibt_auch_an_einem_alten_Beleg_stehen()
    {
        Beleg("Alter Brief", frist: null, quelle: "Bitte senden Sie uns die Erklärung zurück.",
            datum: new DateOnly(2019, 3, 1));

        Assert.Single(await VorgaengeAsync());
    }

    /// <summary>
    /// Stammt der Satz vom Sprachmodell, sagt der Vorgang das.
    /// </summary>
    /// <remarks>
    /// Beide Wege liefern dieselbe Art Beleg — eine geprüfte Zeile. Verschieden ist, warum sie
    /// als Forderung gilt: die Leseregel weiß es an einem Wort, das Modell an seinem Urteil.
    /// Das zweite ist schwächer, und wer den Vorgang liest, soll das wissen.
    /// </remarks>
    [Fact]
    public async Task Ein_Vorgang_aus_dem_Sprachmodell_nennt_seine_Herkunft()
    {
        Beleg("Umstellungsschreiben", frist: null,
            quelle: "Bitte stellen Sie Ihren Lastschrifteinzug um.", vomModell: true);

        var vorgang = await EinzigerVorgangAsync();

        Assert.Contains("Sprachmodell", vorgang.Detail);
    }

    /// <summary>Was die Leseregel fand, trägt den Hinweis nicht.</summary>
    [Fact]
    public async Task Ein_Vorgang_aus_der_Leseregel_nennt_kein_Sprachmodell()
    {
        Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20), "fällig am 20.11.2026");

        Assert.DoesNotContain("Sprachmodell", (await EinzigerVorgangAsync()).Detail);
    }

    // ── Wie der Vorgang heißt ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ein Vorgang heißt nicht nach dem Gerät, das den Beleg eingescannt hat.
    /// </summary>
    /// <remarks>
    /// Der Scanner zählt seine Seiten durch; der Dateiname dagegen wird von der Ablage gebaut
    /// und kennt Art, Objekt und Datum. „Zu erledigen: Scannen0003“ zwang dazu, den Beleg zu
    /// öffnen, nur um zu sehen, worum es überhaupt geht.
    /// </remarks>
    [Fact]
    public async Task Ein_Scannertitel_wird_zum_Namen_aus_der_Ablage()
    {
        Beleg("Scannen0003", frist: null, "Bitte überweisen Sie den Rechnungsbetrag.",
            datum: new DateOnly(2026, 4, 3),
            dateiname: "Arztrechnung_Musterklinik_Nordstadt_2026-04-03.pdf", nameErzeugt: true);

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal("Zu erledigen: Arztrechnung Musterklinik Nordstadt · 03.04.2026", vorgang.Title);
        Assert.DoesNotContain("Scannen", vorgang.Title);
    }

    /// <summary>Ein Titel, den ein Mensch gewählt hat, bleibt im Vorgang stehen.</summary>
    [Fact]
    public async Task Ein_gepflegter_Titel_bleibt_im_Vorgang()
    {
        Beleg("Gebührenbescheid Musterkreis", new DateOnly(2026, 11, 20),
            "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.",
            dateiname: "Gebuehrenbescheid_Musterkreis_2026-11-20.pdf", nameErzeugt: true);

        Assert.Equal("Frist: Gebührenbescheid Musterkreis", (await EinzigerVorgangAsync()).Title);
    }

    // ── Eine Frist, die niemand abgelesen hat ──────────────────────────────────────────────

    /// <summary>
    /// Eine gerechnete Frist sagt, dass sie gerechnet ist.
    /// </summary>
    /// <remarks>
    /// Sie steht so nicht auf dem Papier: Dort steht eine Spanne. Wer den Tag dort nachschlägt,
    /// findet ihn nicht — und würde die Anwendung für falsch halten, statt den Zusatz zu lesen.
    /// </remarks>
    [Fact]
    public async Task Eine_gerechnete_Frist_sagt_es_dazu()
    {
        Beleg("Arztrechnung Musterklinik", new DateOnly(2026, 11, 10),
            "Bitte überweisen Sie den Rechnungsbetrag innerhalb von 7 Tagen.",
            datum: new DateOnly(2026, 11, 2));

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal(new DateOnly(2026, 11, 10), vorgang.DueOn);
        Assert.Contains("vom Belegdatum errechnet", vorgang.Detail);
    }

    /// <summary>Ein abgelesener Tag braucht den Zusatz nicht.</summary>
    [Fact]
    public async Task Eine_abgelesene_Frist_sagt_nichts_dazu()
    {
        Beleg("Gebührenbescheid", new DateOnly(2026, 11, 20),
            "Dieser Betrag ist am 20.11.2026 zur Zahlung fällig.");

        Assert.DoesNotContain("errechnet", (await EinzigerVorgangAsync()).Detail);
    }

    /// <summary>Ohne Frist gibt es nichts zu erklären.</summary>
    [Fact]
    public async Task Ohne_Frist_steht_kein_Zusatz_da()
    {
        Beleg("Betreiberwechsel", frist: null, "Bitte stellen Sie Ihren Einzug um.");

        Assert.DoesNotContain("errechnet", (await EinzigerVorgangAsync()).Detail);
    }

    /// <summary>
    /// Eine gerechnete Frist schließt keinen Vorgang, auch wenn sie längst verstrichen ist.
    /// </summary>
    /// <remarks>
    /// <para><b>Am echten Bestand gefunden, nicht am Reißbrett.</b> Beim Probelauf gegen eine
    /// Kopie trugen zwei Arztrechnungen vom Frühjahr ihre Spanne nach — und verschwanden
    /// dadurch aus der Liste: Die gerechnete Frist lag ein halbes Jahr zurück, weit außerhalb
    /// des Fensters, und der Vorgang wurde geschlossen. Vorher stand er ohne Frist da und
    /// blieb sichtbar. Das Nachtragen hätte ihn gekostet, und erledigt bleibt erledigt.</para>
    /// <para>Das Fenster entscheidet über Termine, die der Absender gesetzt hat. Eine Spanne,
    /// die wir selbst in einen Tag umgerechnet haben, ist unsere Rechnung — sie darf anzeigen,
    /// aber nicht aufräumen.</para>
    /// </remarks>
    [Fact]
    public async Task Eine_laengst_verstrichene_gerechnete_Frist_schliesst_nicht()
    {
        Beleg("Arztrechnung Musterklinik", new DateOnly(2026, 4, 17),
            "Bitte überweisen Sie den Rechnungsbetrag innerhalb von 14 Tagen.",
            datum: new DateOnly(2026, 4, 3));

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal(TaskState.Open, vorgang.State);
        Assert.Equal(new DateOnly(2026, 4, 17), vorgang.DueOn);
        Assert.True(vorgang.IsOverdue);
    }

    /// <summary>
    /// Ein Termin, den der Absender gesetzt hat, räumt weiterhin auf.
    /// </summary>
    /// <remarks>
    /// Die Ausnahme gilt nur für Gerechnetes. Ein Bescheid, dessen genannter Tag ein halbes
    /// Jahr zurückliegt, ist erledigt oder vergessen — in beiden Fällen gehört er nicht mehr
    /// auf den Tisch, und daran ändert sich nichts.
    /// </remarks>
    [Fact]
    public async Task Eine_genannte_verstrichene_Frist_schliesst_weiterhin()
    {
        Beleg("Gebührenbescheid Musterkreis", new DateOnly(2026, 4, 17),
            "Dieser Betrag ist am 17.04.2026 zur Zahlung fällig.",
            datum: new DateOnly(2026, 4, 3));

        Assert.Empty(await VorgaengeAsync());
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private int Beleg(
        string titel, DateOnly? frist, string? quelle,
        DateOnly? datum = null, bool vomModell = false,
        string? dateiname = null, bool nameErzeugt = false)
    {
        using var context = database.Context();

        var dokument = new Document
        {
            Title = titel,
            HasGeneratedName = nameErzeugt,
            RelativePath = $"Wohnen/{Guid.NewGuid():N}.pdf",
            FileName = dateiname ?? $"{titel}.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Housing,
            DocumentDate = datum ?? new DateOnly(2026, 10, 1),
            DueDate = frist,
            ActionLine = quelle,
            ActionFromModel = vomModell,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private async Task<IReadOnlyList<TaskItemDto>> VorgaengeAsync()
    {
        using var context = database.Context();
        var liste = await new TaskService(context, clock).GetListAsync();

        return [.. liste.Items.Where(i => i.Source == TaskSource.DocumentAction)];
    }

    private async Task<TaskItemDto> EinzigerVorgangAsync()
        => Assert.Single(await VorgaengeAsync());

    public void Dispose() => database.Dispose();
}
