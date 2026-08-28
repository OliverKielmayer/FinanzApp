using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Dokumenttypen pflegen — v5-Handoff, Abschnitt 9.
/// </summary>
/// <remarks>
/// Dieselbe Aufgabe wie bei den Kategorien und darum dieselbe Bauform: gepflegte Stammdaten mit
/// Verwendungsnachweis. Ein Typ bestimmt den Ablagepfad-Vorschlag und was die Beleganalyse zu
/// erkennen versucht; wer ihn ändert, greift in beides ein.
/// </remarks>
public sealed class DocumentTypeService(FinanzAppDbContext db)
{
    /// <summary>Die Bereiche in der Reihenfolge der Chipreihe.</summary>
    private static readonly DocumentArea[] Areas =
    [
        DocumentArea.Work,
        DocumentArea.Insurance,
        DocumentArea.Health,
        DocumentArea.Housing,
        DocumentArea.Finance,
        DocumentArea.Other,
    ];

    public async Task<DocumentTypeOverviewDto> GetAsync(CancellationToken ct = default)
    {
        // Nur die gepflegten. Ein stillgelegter Typ hängt noch an seinen Dokumenten, aber er
        // steht nicht mehr zur Auswahl — sonst wäre das Löschen folgenlos.
        var typen = await db.DocumentTypes.AsNoTracking()
            .Where(t => !t.IsRetired)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new DocumentTypeUsageDto
            {
                Id = t.Id,
                Name = t.Name,
                Area = t.Area,
                DocumentCount = t.Documents.Count,
            })
            .ToListAsync(ct);

        return new DocumentTypeOverviewDto
        {
            TotalCount = typen.Count,
            Areas =
            [
                new(null, "Alle", typen.Count),
                .. Areas.Select(a =>
                    new DocumentAreaCountDto(a, Label(a), typen.Count(t => t.Area == a))),
            ],
            Types = typen,
        };
    }

    /// <summary>
    /// Legt einen Typ an.
    /// </summary>
    /// <remarks>
    /// Doppelte Namen werden ohne Rücksicht auf Groß- und Kleinschreibung abgewiesen: „Police“
    /// und „police“ wären in jeder Liste dasselbe, und die Ablage bekäme zwei Ordner, die
    /// niemand auseinanderhält.
    /// </remarks>
    public async Task<DocumentTypeUsageDto> CreateAsync(
        DocumentTypeNameRequest request, CancellationToken ct = default)
    {
        var name = Clean(request.Name);

        await EnsureFreeAsync(name, keeping: null, ct);

        // Ans Ende, nicht an den Anfang: eine gepflegte Reihenfolge gehört dem Nutzer, und ein
        // neuer Typ soll die bestehende nicht umwerfen.
        var letzte = await db.DocumentTypes.MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;

        var typ = new DocumentType { Name = name, Area = request.Area, SortOrder = letzte + 1 };

        db.DocumentTypes.Add(typ);
        await db.SaveChangesAsync(ct);

        return new DocumentTypeUsageDto
        {
            Id = typ.Id, Name = typ.Name, Area = typ.Area, DocumentCount = 0,
        };
    }

    /// <summary>
    /// Benennt um. Wirkt auf alle Dokumente dieses Typs, weil sie ihn über die Id tragen.
    /// </summary>
    public async Task<DocumentTypeChangeResultDto> RenameAsync(
        int id, string name, CancellationToken ct = default)
    {
        var typ = await Load(id, ct);
        var sauber = Clean(name);

        await EnsureFreeAsync(sauber, keeping: id, ct);

        typ.Name = sauber;
        await db.SaveChangesAsync(ct);

        return new DocumentTypeChangeResultDto(sauber, typ.Documents.Count);
    }

    /// <summary>
    /// Löscht einen Typ und lässt die Dokumente unangetastet.
    /// </summary>
    /// <remarks>
    /// Die Dokumente behalten ihre Typ-Id und bleiben in der Suche: die Historie darf nicht
    /// zerreißen, weil jemand einen Typ nicht mehr pflegen will. Was sie verlieren, ist der
    /// gepflegte Name — und genau das sagt die Meldung.
    /// </remarks>
    public async Task<DocumentTypeChangeResultDto> DeleteAsync(
        int id, CancellationToken ct = default)
    {
        var typ = await Load(id, ct);
        var betroffen = typ.Documents.Count;

        // Stilllegen statt entfernen. Die Fremdschlüsselregel setzt beim echten Löschen die
        // Typ-Kennung der Dokumente auf null — der Beleg wüsste danach nicht mehr, was er ist.
        typ.IsRetired = true;
        await db.SaveChangesAsync(ct);

        return new DocumentTypeChangeResultDto(typ.Name, betroffen);
    }

    private async Task<DocumentType> Load(int id, CancellationToken ct)
        => await db.DocumentTypes.Include(t => t.Documents)
               .FirstOrDefaultAsync(t => t.Id == id && !t.IsRetired, ct)
           ?? throw new RuleViolationException("Diesen Dokumenttyp gibt es nicht.");

    private static string Clean(string name)
    {
        var sauber = name.Trim();

        return sauber.Length == 0
            ? throw new RuleViolationException("Der Dokumenttyp braucht einen Namen.")
            : sauber;
    }

    private async Task EnsureFreeAsync(string name, int? keeping, CancellationToken ct)
    {
        // Ein stillgelegter Typ blockiert seinen Namen nicht: er steht in keiner Liste, und
        // wer ihn neu anlegt, meint offensichtlich einen neuen.
        var belegt = await db.DocumentTypes
            .AnyAsync(t => !t.IsRetired && t.Id != keeping && t.Name.ToLower() == name.ToLower(), ct);

        if (belegt)
        {
            throw new RuleViolationException($"Einen Dokumenttyp „{name}“ gibt es schon.");
        }
    }

    public static string Label(DocumentArea area) => area switch
    {
        DocumentArea.Insurance => "Absicherung",
        DocumentArea.Health => "Gesundheit",
        DocumentArea.Housing => "Wohnen",
        DocumentArea.Work => "Arbeit",
        DocumentArea.Finance => "Finanzen",
        _ => "Sonstiges",
    };
}
