using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Ein Beleg, dessen gelesene Werte niemand übernommen hat, wird ein Vorgang.
/// </summary>
/// <remarks>
/// <para>Der Scaneingang zeigt so einen Beleg, solange er dort steht. Weggeräumt verschwindet er
/// daraus, und danach zeigte nichts mehr auf ihn: die Zahl steht am Beleg, aber nur wer ihn
/// ohnehin öffnet, sieht sie. Genau so sind dreizehn Statusreporte eines Vertrags
/// durchgerutscht, und am Vertrag stand hinterher eine Null.</para>
/// <para>Die Vorgänge sind die Stelle, an der Unerledigtes einen holt, statt darauf zu warten,
/// dass jemand nachsieht.</para>
/// </remarks>
public sealed class OpenValueTaskTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 10, 27);

    [Fact]
    public async Task Ein_Beleg_mit_offenen_Werten_steht_in_den_Vorgaengen()
    {
        var belegId = Beleg("Statusreport Lebensversicherung", offen: 3, uebernommen: 0);

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal(TaskSource.DocumentValuesOpen, vorgang.Source);
        Assert.Equal(LinkTargetType.Document, vorgang.SourceType);
        Assert.Equal(belegId, vorgang.SourceId);
        Assert.Equal(TaskState.Open, vorgang.State);
        Assert.Contains("Statusreport Lebensversicherung", vorgang.Title);
        Assert.Contains("3 gelesene Angaben", vorgang.Detail);
    }

    /// <summary>
    /// Ohne Termin — hier läuft nichts ab, es bleibt nur etwas liegen.
    /// </summary>
    /// <remarks>
    /// Ein erfundenes Fälligkeitsdatum machte daraus eine Dringlichkeit, die das Papier nicht
    /// hergibt, und der Vorgang stünde eines Tages als „überfällig“ da, ohne dass jemand eine
    /// Frist versäumt hätte.
    /// </remarks>
    [Fact]
    public async Task Der_Vorgang_erfindet_keine_Frist()
    {
        Beleg("Statusreport", offen: 2, uebernommen: 0);

        var vorgang = await EinzigerVorgangAsync();

        Assert.Null(vorgang.DueOn);
        Assert.False(vorgang.IsOverdue);
    }

    [Fact]
    public async Task Eine_einzelne_Angabe_wird_im_Singular_genannt()
    {
        Beleg("Statusreport", offen: 1, uebernommen: 4);

        var vorgang = await EinzigerVorgangAsync();

        Assert.Equal("1 gelesene Angabe steht noch nicht im Objekt", vorgang.Detail);
    }

    [Fact]
    public async Task Ein_vollstaendig_uebernommener_Beleg_wird_kein_Vorgang()
    {
        Beleg("Statusreport", offen: 0, uebernommen: 7);

        Assert.Empty(await VorgaengeAsync());
    }

    /// <summary>
    /// Ein Beleg ohne Leseabdruck wird kein Vorgang.
    /// </summary>
    /// <remarks>
    /// Was nie gelesen wurde, hat auch nichts Offenes. Sonst stünde jeder abgelegte Zettel —
    /// Zulagenantrag, Bezugsrechtsänderung, der eingescannte Brief der Versicherung — als
    /// Aufgabe da, und die Liste wäre nach einer Woche unbrauchbar.
    /// </remarks>
    [Fact]
    public async Task Ein_Beleg_ohne_gelesene_Werte_wird_kein_Vorgang()
    {
        Beleg("Zulagenantrag", offen: 0, uebernommen: 0);

        Assert.Empty(await VorgaengeAsync());
    }

    /// <summary>
    /// Sind die Werte übernommen, ist der Vorgang erledigt.
    /// </summary>
    [Fact]
    public async Task Die_Uebernahme_erledigt_den_Vorgang()
    {
        var belegId = Beleg("Statusreport", offen: 5, uebernommen: 0);

        await EinzigerVorgangAsync();

        using (var context = database.Context())
        {
            foreach (var wert in context.DocumentExtractions.Where(x => x.DocumentId == belegId))
            {
                wert.Confirmed = true;
            }

            await context.SaveChangesAsync();
        }

        var danach = await VorgaengeAsync();

        Assert.Single(danach);
        Assert.Equal(TaskState.Done, danach[0].State);
    }

    /// <summary>
    /// Ein gelöschter Beleg lässt keinen Vorgang zurück, der ins Leere führt.
    /// </summary>
    /// <remarks>
    /// Geschlossen wird deshalb über die vorhandenen Vorgänge und nicht über die Belege: in
    /// einer Schleife über Belege käme ein gelöschter nicht mehr vor, und sein Vorgang bliebe
    /// für immer offen.
    /// </remarks>
    [Fact]
    public async Task Ein_geloeschter_Beleg_laesst_keinen_offenen_Vorgang_zurueck()
    {
        var belegId = Beleg("Statusreport", offen: 3, uebernommen: 0);

        await EinzigerVorgangAsync();

        using (var context = database.Context())
        {
            context.Documents.RemoveRange(context.Documents.Where(d => d.Id == belegId));
            await context.SaveChangesAsync();
        }

        var danach = await VorgaengeAsync();

        Assert.Single(danach);
        Assert.Equal(TaskState.Done, danach[0].State);
    }

    /// <summary>
    /// Zwei Belege sind zwei Vorgänge, und jeder nennt seinen eigenen.
    /// </summary>
    [Fact]
    public async Task Jeder_Beleg_bekommt_seinen_eigenen_Vorgang()
    {
        var eins = Beleg("Statusreport 2024", offen: 2, uebernommen: 0);
        var zwei = Beleg("Statusreport 2025", offen: 4, uebernommen: 0);

        var vorgaenge = await VorgaengeAsync();

        Assert.Equal(2, vorgaenge.Count);
        Assert.Equal([eins, zwei], vorgaenge.Select(v => v.SourceId).Order().ToList());
    }

    /// <summary>
    /// Zweimal lesen legt den Vorgang nicht zweimal an.
    /// </summary>
    /// <remarks>
    /// Die Ableitung läuft vor jedem Lesen der Liste. Ohne diesen Test fiele erst im Betrieb
    /// auf, dass der Vorgänge-Tab bei jedem Öffnen eine Zeile mehr trägt.
    /// </remarks>
    [Fact]
    public async Task Zweimal_nachsehen_legt_den_Vorgang_nicht_zweimal_an()
    {
        Beleg("Statusreport", offen: 3, uebernommen: 0);

        await VorgaengeAsync();
        var zweites = await VorgaengeAsync();

        Assert.Single(zweites);
    }

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Ein abgelegter Beleg mit so vielen offenen und übernommenen Werten.</summary>
    private int Beleg(string titel, int offen, int uebernommen)
    {
        using var context = database.Context();

        var dokument = new Document
        {
            Title = titel,
            RelativePath = $"Versicherungen/{titel}_{Guid.NewGuid():N}.pdf",
            FileName = $"{titel}.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Insurance,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        for (var i = 0; i < offen + uebernommen; i++)
        {
            context.DocumentExtractions.Add(new DocumentExtraction
            {
                DocumentId = dokument.Id,
                FieldKey = $"feld{i}",
                Label = $"Feld {i}",
                Value = $"{i}00,00",
                Confirmed = i >= offen,
                CreatedAt = clock.Now,
            });
        }

        context.SaveChanges();
        return dokument.Id;
    }

    private async Task<IReadOnlyList<TaskItemDto>> VorgaengeAsync()
    {
        using var context = database.Context();
        var liste = await new TaskService(context, clock).GetListAsync();

        return [.. liste.Items.Where(i => i.Source == TaskSource.DocumentValuesOpen)];
    }

    private async Task<TaskItemDto> EinzigerVorgangAsync()
        => Assert.Single(await VorgaengeAsync());

    public void Dispose() => database.Dispose();
}
