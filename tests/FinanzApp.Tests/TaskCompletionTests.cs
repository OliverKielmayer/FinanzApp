using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Einen Vorgang von Hand abhaken.
/// </summary>
/// <remarks>
/// <para>Der Vorgänge-Tab hatte einen Filter „Erledigt“ und keinen Weg, etwas dorthin zu
/// bringen. Für die meisten Vorgänge war das richtig gedacht: Sie spiegeln den Stand eines
/// Objekts und schließen sich dort, wo dieser Stand sich ändert — eine Rechnung wird an der
/// Rechnung bezahlt.</para>
/// <para>Ein Schreiben hat dieses Objekt nicht. Die Forderung steht auf dem Papier und sonst
/// nirgends, und ob jemand sie erfüllt hat, weiß nur er selbst. Ein bezahlter Bescheid stand
/// deshalb noch wochenlang offen da, bis sein Fenster ablief.</para>
/// <para><b>Abhaken ist endgültig</b> — ein erledigter Vorgang wird nicht wiederbelebt. Genau
/// deshalb steht der Haken nur dort, wo kein Objekt den Stand trägt.</para>
/// </remarks>
public sealed class TaskCompletionTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 7);

    // ── Wer abgehakt werden darf ───────────────────────────────────────────────────────────

    /// <summary>Was kein Objekt hinter sich hat, kann nur ein Mensch schließen.</summary>
    [Theory]
    [InlineData(TaskSource.DocumentAction)]
    [InlineData(TaskSource.Manual)]
    public void Ein_Vorgang_ohne_eigenes_Objekt_darf_abgehakt_werden(TaskSource quelle)
        => Assert.True(Vorgang(quelle).CanComplete);

    /// <summary>
    /// Was den Stand eines Objekts spiegelt, wird am Objekt erledigt.
    /// </summary>
    /// <remarks>
    /// Eine offene Rechnung hier abzuhaken hieße: Die Rechnung bleibt offen, und der einzige
    /// Hinweis darauf ist weg. Dasselbe gilt für gelesene Werte, die niemand übernommen hat —
    /// sie werden im Prüfschritt übernommen, und dort verschwindet der Vorgang von selbst.
    /// </remarks>
    [Theory]
    [InlineData(TaskSource.InvoiceDue)]
    [InlineData(TaskSource.ContractNotice)]
    [InlineData(TaskSource.MedicalBillOpen)]
    [InlineData(TaskSource.ReimbursementOverdue)]
    [InlineData(TaskSource.DocumentExpiry)]
    [InlineData(TaskSource.DocumentValuesOpen)]
    public void Ein_gespiegelter_Vorgang_darf_nicht_abgehakt_werden(TaskSource quelle)
        => Assert.False(Vorgang(quelle).CanComplete);

    /// <summary>Ein erledigter Vorgang bietet den Haken nicht noch einmal an.</summary>
    [Fact]
    public void Ein_erledigter_Vorgang_bietet_kein_zweites_Abhaken()
        => Assert.False(Vorgang(TaskSource.DocumentAction, TaskState.Done).CanComplete);

    // ── Was das Abhaken bewirkt ────────────────────────────────────────────────────────────

    /// <summary>
    /// Abgehakt bleibt abgehakt, obwohl das Schreiben weiter etwas verlangt.
    /// </summary>
    /// <remarks>
    /// Der Beleg ändert sich durch den Haken nicht: Seine Zeile steht weiter da, seine Frist
    /// liegt weiter im Fenster, und die Ableitung läuft vor jedem Lesen erneut. Ohne die
    /// Sperre gegen das Wiederbeleben stünde der Vorgang beim nächsten Blick wieder offen —
    /// und der Haken wäre eine Geste ohne Wirkung.
    /// </remarks>
    [Fact]
    public async Task Ein_abgehakter_Vorgang_steht_nicht_wieder_auf()
    {
        Beleg("Steuerbescheid Musterstadt", new DateOnly(2026, 11, 27),
            "spätestens am 27.11.2026 4.100,00");

        var vorgang = await EinzigerOffenerAsync();
        Assert.True(vorgang.CanComplete);

        Assert.True(await AbhakenAsync(vorgang.Id));

        Assert.Empty(await OffeneAsync());

        var erledigt = Assert.Single(await MitZustandAsync(TaskState.Done));
        Assert.Equal(vorgang.Id, erledigt.Id);
    }

    /// <summary>
    /// Der Beleg behält, was er verlangt.
    /// </summary>
    /// <remarks>
    /// Abgehakt ist der Vorgang, nicht die Forderung. Wer den Beleg in einem Jahr öffnet, soll
    /// den Satz noch finden — er steht auf dem Papier, und daran ändert ein Haken nichts.
    /// </remarks>
    [Fact]
    public async Task Das_Abhaken_loescht_die_Forderung_am_Beleg_nicht()
    {
        var belegId = Beleg("Steuerbescheid Musterstadt", new DateOnly(2026, 11, 27),
            "spätestens am 27.11.2026 4.100,00");

        await AbhakenAsync((await EinzigerOffenerAsync()).Id);

        using var context = database.Context();
        var beleg = context.Documents.Single(d => d.Id == belegId);

        Assert.Equal("spätestens am 27.11.2026 4.100,00", beleg.ActionLine);
        Assert.Equal(new DateOnly(2026, 11, 27), beleg.DueDate);
    }

    /// <summary>Der Zeitpunkt wird vermerkt, damit die Liste der Erledigten eine Ordnung hat.</summary>
    [Fact]
    public async Task Das_Abhaken_vermerkt_wann()
    {
        Beleg("Steuerbescheid Musterstadt", new DateOnly(2026, 11, 27),
            "spätestens am 27.11.2026 4.100,00");

        var id = (await EinzigerOffenerAsync()).Id;
        await AbhakenAsync(id);

        using var context = database.Context();
        Assert.Equal(clock.Now, context.TaskItems.Single(t => t.Id == id).CompletedAt);
    }

    /// <summary>Einen Vorgang, den es nicht gibt, kann niemand abhaken.</summary>
    [Fact]
    public async Task Ein_unbekannter_Vorgang_laesst_sich_nicht_abhaken()
        => Assert.False(await AbhakenAsync(4711));

    // ── Aufbau ─────────────────────────────────────────────────────────────────────────────

    private static TaskItemDto Vorgang(TaskSource quelle, TaskState zustand = TaskState.Open)
        => new()
        {
            Id = 1,
            Title = "Vorgang",
            State = zustand,
            Source = quelle,
            DaysOverdue = 0,
        };

    private int Beleg(string titel, DateOnly frist, string zeile)
    {
        using var context = database.Context();

        var dokument = new Document
        {
            Title = titel,
            RelativePath = $"Wohnen/{Guid.NewGuid():N}.pdf",
            FileName = $"{titel}.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Housing,
            DocumentDate = new DateOnly(2026, 11, 2),
            DueDate = frist,
            ActionLine = zeile,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();
        return dokument.Id;
    }

    private async Task<bool> AbhakenAsync(int id)
    {
        using var context = database.Context();
        return await new TaskService(context, clock).SetStateAsync(id, TaskState.Done);
    }

    private async Task<IReadOnlyList<TaskItemDto>> MitZustandAsync(TaskState zustand)
    {
        using var context = database.Context();
        var liste = await new TaskService(context, clock).GetListAsync(zustand);

        return [.. liste.Items.Where(i => i.Source == TaskSource.DocumentAction)];
    }

    private Task<IReadOnlyList<TaskItemDto>> OffeneAsync() => MitZustandAsync(TaskState.Open);

    private async Task<TaskItemDto> EinzigerOffenerAsync() => Assert.Single(await OffeneAsync());

    public void Dispose() => database.Dispose();
}
