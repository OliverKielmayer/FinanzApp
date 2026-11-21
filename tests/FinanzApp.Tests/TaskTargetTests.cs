using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Ein von Hand angelegter Vorgang, der auf etwas zeigt.
/// </summary>
/// <remarks>
/// <para><b>Der Fall, aus dem es entstand:</b> Jede Mail aus dem überwachten Postfach wurde ein
/// Vorgang, ihre Anhänge wurden Belege — und nichts verband beides. Wer die Zeile antippte,
/// bekam „Zu dieser Aufgabe gibt es keinen eigenen Screen“, während der Anhang zwei Klicks
/// weiter in der Ablage lag.</para>
/// <para><b>Die Schleife, die dabei zu vermeiden war:</b> Kommt dieselbe Mail ein zweites Mal,
/// erkennt der Eingang den Anhang am Abdruck wieder und gibt <em>dieselbe</em> Belegnummer
/// zurück. Ein zweiter Vorgang darauf verstößt gegen den Index; das Anlegen scheiterte, die
/// Mail bliebe liegen, und beim nächsten Durchgang begänne dasselbe von vorn.</para>
/// </remarks>
public sealed class TaskTargetTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 21);

    private TaskService Dienst(FinanzAppDbContext context) => new(context, clock);

    private async Task<TaskItemDto> AnlegenAsync(
        string titel, LinkTargetType? art = null, int? ziel = null)
    {
        using var context = database.Context();

        return await Dienst(context).CreateAsync(new CreateTaskRequest
        {
            Title = titel,
            Detail = "Mail von absender@example.org vom 19.11.2026",
            SourceType = art,
            SourceId = ziel,
        });
    }

    /// <summary>Das Ziel kommt mit und steht danach am Vorgang.</summary>
    [Fact]
    public async Task Ein_Vorgang_merkt_sich_seinen_Beleg()
    {
        var vorgang = await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);

        Assert.Equal(LinkTargetType.Document, vorgang.SourceType);
        Assert.Equal(306, vorgang.SourceId);
    }

    /// <summary>Ohne Ziel bleibt er ohne — erfunden wird keines.</summary>
    /// <remarks>
    /// Eine Mail ohne Anhang hat nichts, worauf sie zeigen könnte. Ein Platzhalter führte ins
    /// Leere und wäre schlimmer als gar kein Ziel.
    /// </remarks>
    [Fact]
    public async Task Ohne_Ziel_bleibt_er_ohne()
    {
        var vorgang = await AnlegenAsync("Automatische Antwort");

        Assert.Null(vorgang.SourceType);
        Assert.Null(vorgang.SourceId);
    }

    /// <summary>
    /// Zweimal dasselbe Ziel ergibt <b>einen</b> Vorgang.
    /// </summary>
    /// <remarks>
    /// Die Prüfung, die die Schleife verhindert. Ohne sie scheiterte der zweite Versuch am
    /// Index — und der Dienst legte die Mail jedes Mal aufs Neue zurück.
    /// </remarks>
    [Fact]
    public async Task Derselbe_Beleg_bekommt_keinen_zweiten_Vorgang()
    {
        var erster = await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);
        var zweiter = await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);

        Assert.Equal(erster.Id, zweiter.Id);

        using var context = database.Context();

        Assert.Equal(1, context.TaskItems.Count(t => t.SourceId == 306));
    }

    /// <summary>Der zurückgegebene ist der vorhandene — mit seinem Titel, nicht dem neuen.</summary>
    /// <remarks>
    /// Wer den Vorgang inzwischen umbenannt hat, soll das nicht dadurch verlieren, dass
    /// dieselbe Mail noch einmal hereinkommt.
    /// </remarks>
    [Fact]
    public async Task Der_vorhandene_behaelt_seinen_Titel()
    {
        await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);

        using (var context = database.Context())
        {
            await Dienst(context).RenameAsync(
                context.TaskItems.Single(t => t.SourceId == 306).Id,
                "Pellets 19.11.: Rechnung abwarten");
        }

        var noch = await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);

        Assert.Equal("Pellets 19.11.: Rechnung abwarten", noch.Title);
    }

    /// <summary>Zwei verschiedene Belege ergeben zwei Vorgänge.</summary>
    [Fact]
    public async Task Verschiedene_Belege_ergeben_verschiedene_Vorgaenge()
    {
        var erster = await AnlegenAsync("Rechnung pellets", LinkTargetType.Document, 306);
        var zweiter = await AnlegenAsync("SFR-Übertragung", LinkTargetType.Document, 307);

        Assert.NotEqual(erster.Id, zweiter.Id);
    }

    /// <summary>
    /// Zwei Mails ohne Anhang ergeben zwei Vorgänge.
    /// </summary>
    /// <remarks>
    /// Die Schranke greift nur über das Ziel. Ohne Ziel gibt es nichts zusammenzuführen — zwei
    /// Mails sind zwei Vorgänge, auch wenn sie gleich heißen.
    /// </remarks>
    [Fact]
    public async Task Zwei_ohne_Ziel_bleiben_zwei()
    {
        var erster = await AnlegenAsync("Automatische Antwort");
        var zweiter = await AnlegenAsync("Automatische Antwort");

        Assert.NotEqual(erster.Id, zweiter.Id);
    }

    public void Dispose() => database.Dispose();
}
