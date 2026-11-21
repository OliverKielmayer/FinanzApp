using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Einen Vorgang umbenennen.
/// </summary>
/// <remarks>
/// <para><b>Die Falle, um die es geht:</b> Der Abgleich läuft vor <em>jedem</em> Lesen der
/// Liste und schreibt Titel, Beschreibung und Frist jedes abgeleiteten Vorgangs zurück. Ohne
/// ein Kennzeichen wäre ein von Hand gegebener Titel beim nächsten Seitenaufruf wieder weg —
/// und niemand hätte einen Hinweis darauf, warum.</para>
/// <para>Deshalb prüft das meiste hier nicht das Umbenennen selbst, sondern <b>was danach
/// passiert</b>: dass der Titel bleibt, und dass Frist und Beschreibung trotzdem weiter dem
/// Bestand folgen. Wer umbenennt, benennt um — er friert nicht ein.</para>
/// </remarks>
public sealed class TaskRenameTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 21);

    private TaskService Dienst(FinanzAppDbContext context) => new(context, clock);

    /// <summary>
    /// Eine Absicherung mit Vertragsende und Kündigungsfrist — daraus leitet der Abgleich einen Vorgang ab.
    /// </summary>
    /// <remarks>
    /// Ein abgeleiteter und kein von Hand angelegter: Nur der abgeleitete wird beim Lesen der
    /// Liste überschrieben, und genau darum geht es.
    /// </remarks>
    private int Police(string name, DateOnly endetAm)
    {
        using var context = database.Context();

        var police = new Policy
        {
            Name = name,
            Provider = "Musterversicherung",
            PolicyNumber = "MV-4711",
            Premium = 42m,
            PremiumInterval = PremiumInterval.Monthly,
            IsCapitalForming = false,
            NoticePeriodMonths = 3,
            EndsOn = endetAm,
        };

        context.Policies.Add(police);
        context.SaveChanges();

        return police.Id;
    }

    /// <summary>Liest die Liste — und löst damit den Abgleich aus.</summary>
    private async Task<TaskItemDto> AbgeleiteterVorgangAsync()
    {
        using var context = database.Context();
        var liste = await Dienst(context).GetListAsync(TaskState.Open);

        return liste.Items.Single(i => i.Source == TaskSource.ContractNotice);
    }

    private async Task<bool> UmbenennenAsync(int id, string titel)
    {
        using var context = database.Context();

        return await Dienst(context).RenameAsync(id, titel);
    }

    // ── Was das Umbenennen verlangt ──────────────────────────────────────────────────────

    [Fact]
    public async Task Ein_Vorgang_bekommt_einen_neuen_Titel()
    {
        Police("Hausrat", new DateOnly(2027, 4, 30));
        var vorher = await AbgeleiteterVorgangAsync();

        Assert.True(await UmbenennenAsync(vorher.Id, "  Hausrat kündigen  "));

        var nachher = await AbgeleiteterVorgangAsync();

        Assert.Equal("Hausrat kündigen", nachher.Title);
        Assert.True(nachher.TitleEdited);
    }

    [Fact]
    public async Task Ein_leerer_Titel_wird_abgewiesen()
    {
        Police("Hausrat", new DateOnly(2027, 4, 30));
        var vorgang = await AbgeleiteterVorgangAsync();

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => UmbenennenAsync(vorgang.Id, "   "));

        Assert.Contains("Titel", fehler.Message);
    }

    /// <summary>
    /// Ein zu langer Titel wird abgewiesen, nicht abgeschnitten.
    /// </summary>
    /// <remarks>
    /// Die Spalte fasst zweihundert Zeichen. Still abzuschneiden hieße, den Rest verschwinden
    /// zu lassen, ohne dass es jemand sagt — und wer einen langen Satz tippt, meint ihn.
    /// </remarks>
    [Fact]
    public async Task Ein_zu_langer_Titel_wird_abgewiesen()
    {
        Police("Hausrat", new DateOnly(2027, 4, 30));
        var vorgang = await AbgeleiteterVorgangAsync();

        var fehler = await Assert.ThrowsAsync<ArgumentException>(
            () => UmbenennenAsync(vorgang.Id, new string('x', 201)));

        Assert.Contains("200", fehler.Message);
    }

    [Fact]
    public async Task Einen_Vorgang_ohne_Nummer_gibt_es_nicht()
    {
        Assert.False(await UmbenennenAsync(99999, "Irgendetwas"));
    }

    // ── Und was danach passiert ──────────────────────────────────────────────────────────

    /// <summary>
    /// Der Abgleich überschreibt den Titel nicht mehr.
    /// </summary>
    /// <remarks>
    /// Die eine Prüfung, ohne die das ganze Vorhaben wirkungslos wäre. Jedes Lesen der Liste
    /// leitet die Vorgänge neu ab; ohne das Kennzeichen stünde hier wieder der alte Name.
    /// </remarks>
    [Fact]
    public async Task Der_Abgleich_laesst_den_neuen_Titel_stehen()
    {
        Police("Hausrat", new DateOnly(2027, 4, 30));
        var vorgang = await AbgeleiteterVorgangAsync();

        await UmbenennenAsync(vorgang.Id, "Hausrat kündigen");

        // Dreimal lesen — jedes Lesen ist ein Abgleich.
        await AbgeleiteterVorgangAsync();
        await AbgeleiteterVorgangAsync();

        Assert.Equal("Hausrat kündigen", (await AbgeleiteterVorgangAsync()).Title);
    }

    /// <summary>
    /// Die Frist folgt weiter dem Bestand.
    /// </summary>
    /// <remarks>
    /// Umbenennen ist kein Einfrieren. Verschiebt sich der Kündigungstermin, verschiebt sich
    /// die Frist des Vorgangs mit — sie ist eine Tatsache aus dem Bestand und keine
    /// Formulierung. Bliebe sie stehen, trüge die Liste ein Datum, das nirgends mehr gilt.
    /// </remarks>
    [Fact]
    public async Task Die_Frist_wandert_trotzdem_mit()
    {
        var police = Police("Hausrat", new DateOnly(2027, 4, 30));
        var vorgang = await AbgeleiteterVorgangAsync();

        await UmbenennenAsync(vorgang.Id, "Hausrat kündigen");

        using (var context = database.Context())
        {
            var eintrag = context.Policies.First(p => p.Id == police);
            eintrag.EndsOn = new DateOnly(2027, 4, 15);
            context.SaveChanges();
        }

        var nachher = await AbgeleiteterVorgangAsync();

        Assert.Equal("Hausrat kündigen", nachher.Title);
        Assert.Equal(new DateOnly(2027, 1, 15), nachher.DueOn);
    }

    public void Dispose() => database.Dispose();
}
