using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace FinanzApp.Client.Services;

/// <summary>
/// Ein begonnener Import — die gelesene Vorschau und alles, was der Nutzer dazu entschieden hat.
/// </summary>
/// <remarks>
/// <para>Der Import ist der einzige Bereich, in dem vor dem Speichern viel Arbeit anfällt: bei
/// dreihundert Sätzen sind das Dutzende Zuordnungen. Läge dieser Zustand in der Seite, wäre er
/// bei jedem Bereichswechsel weg, und ein Blick in die Kategorienliste käme einem Neuanfang
/// gleich. Deshalb liegt er hier: im Browser lebt ein <c>Scoped</c>-Dienst so lange wie die
/// Anwendung und überdauert damit die Seite.</para>
/// <para>Die gelesene Datei selbst bleibt auf dem Server; hier steht nur ihre Vorschau samt Id.
/// Nach <see cref="ImportPreviewCache.Lifetime"/> räumt der Server sie weg — ein älterer Entwurf
/// zeigt ins Leere und gilt darum als <see cref="Expired">abgelaufen</see>.</para>
/// <para>Was nur einen Augenblick lang gilt, steht bewusst <b>nicht</b> hier: ein offenes
/// Detailfenster, eine Fehlermeldung, der Fortschritt des Einlesens. Sie wiederherzustellen
/// hieße, eine Sitzung nachzustellen, statt Arbeit zu bewahren.</para>
/// </remarks>
public sealed class ImportDraft : IDisposable
{
    private readonly AuthenticationStateProvider auth;
    private readonly TimeProvider time;
    private DateTimeOffset readAt;

    public ImportDraft(AuthenticationStateProvider auth, TimeProvider time)
    {
        this.auth = auth;
        this.time = time;

        // Ein Auszug nennt Empfänger, Beträge und IBANs. Wer sich abmeldet, soll ihn nicht dem
        // nächsten Benutzer am selben Gerät hinterlassen.
        auth.AuthenticationStateChanged += OnUserChanged;
    }

    /// <summary>Die gelesene Vorschau, oder <c>null</c>, wenn kein Import begonnen ist.</summary>
    public ImportPreviewDto? Preview { get; private set; }

    /// <summary>Das gewählte Zielkonto.</summary>
    public int AccountId { get; set; }

    /// <summary>Ob nach Empfängern gruppiert wird oder alle Zeilen einzeln stehen.</summary>
    public bool Grouped { get; set; } = true;

    /// <summary>Die aufgeklappte Empfängergruppe.</summary>
    public string? OpenGroup { get; set; }

    /// <summary>Ob aus der nächsten Zuordnung eine Regel wird.</summary>
    public bool Remember { get; set; } = true;

    /// <summary>Was standardmäßig aus dem Auszug mitgespeichert wird.</summary>
    public ImportKeepFields Keep { get; set; } = new();

    /// <summary>Die Sätze, aus denen eine Buchung werden soll.</summary>
    public HashSet<int> Selected { get; } = [];

    /// <summary>Zuordnung je Empfänger, im Import getroffen. Schlägt jede Regel.</summary>
    public Dictionary<string, ImportCategoryChoice> Choices { get; } = new(StringComparer.Ordinal);

    /// <summary>Empfänger, die der Nutzer bewusst offengelassen hat.</summary>
    public HashSet<string> Deferred { get; } = new(StringComparer.Ordinal);

    /// <summary>Sätze, für die etwas anderes gilt als <see cref="Keep"/>.</summary>
    public Dictionary<int, ImportKeepFields> KeepOverrides { get; } = [];

    /// <summary>Die Gruppe, für die gerade eine Kategorie angelegt wird.</summary>
    public string? CreatingFor { get; set; }

    /// <summary>
    /// Der schon getippte Name der neuen Kategorie.
    /// </summary>
    /// <remarks>
    /// Ein halb eingegebener Name ist ebenfalls Arbeit. Ihn als Einzigen fallen zu lassen, wäre
    /// die eine Lücke, die auffällt — die Gruppe steht ja wieder offen da.
    /// </remarks>
    public string FreshCategory { get; set; } = string.Empty;

    /// <summary>Ob ein Import begonnen ist.</summary>
    public bool HasWork => Preview is not null;

    /// <summary>Ob die Vorschau auf dem Server nicht mehr liegt.</summary>
    public bool Expired
        => Preview is not null && time.GetUtcNow() - readAt >= ImportPreviewCache.Lifetime;

    /// <summary>
    /// Übernimmt eine frisch gelesene Datei und verwirft alles Vorherige.
    /// </summary>
    /// <remarks>
    /// Zuordnungen gehören zu der Datei, für die sie getroffen wurden. Sie auf die nächste zu
    /// übertragen hieße, sie zu erfinden.
    /// </remarks>
    public void Start(ImportPreviewDto preview)
    {
        Clear();

        Preview = preview;
        readAt = time.GetUtcNow();
        AccountId = preview.SuggestedAccountId ?? preview.Accounts.FirstOrDefault()?.Id ?? 0;

        foreach (var row in preview.Rows.Where(r => r.PreSelected))
        {
            Selected.Add(row.Index);
        }
    }

    /// <summary>
    /// Wirft Zuordnungen weg, deren Kategorie es nicht mehr gibt, und meldet, wie viele.
    /// </summary>
    /// <remarks>
    /// Der Entwurf überdauert den Bereichswechsel — auch den in die Kategorienverwaltung. Wird
    /// dort eine Kategorie gelöscht, zeigt eine hier liegende Zuordnung ins Leere. Der Empfänger
    /// gehört dann zurück in die Fragenliste; ihn mit einer toten Kategorie stehen zu lassen
    /// hieße, die Antwort erst bei der Übernahme zu verweigern.
    /// </remarks>
    public int DropChoicesOutside(IEnumerable<int> categoryIds)
    {
        var vorhanden = categoryIds.ToHashSet();
        var verwaist = Choices
            .Where(x => !vorhanden.Contains(x.Value.CategoryId))
            .Select(x => x.Key)
            .ToList();

        foreach (var key in verwaist)
        {
            Choices.Remove(key);
        }

        return verwaist.Count;
    }

    /// <summary>Nach dem Verwerfen, nach der Übernahme und beim Benutzerwechsel.</summary>
    public void Clear()
    {
        Preview = null;
        readAt = default;
        AccountId = 0;
        Grouped = true;
        OpenGroup = null;
        Remember = true;
        Keep = new ImportKeepFields();
        CreatingFor = null;
        FreshCategory = string.Empty;

        Selected.Clear();
        Choices.Clear();
        Deferred.Clear();
        KeepOverrides.Clear();
    }

    public void Dispose() => auth.AuthenticationStateChanged -= OnUserChanged;

    private void OnUserChanged(Task<AuthenticationState> state) => Clear();
}
