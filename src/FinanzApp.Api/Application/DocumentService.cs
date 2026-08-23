using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

public sealed class DocumentService(
    FinanzAppDbContext db,
    DocumentPathService paths,
    ObjectLabelService labels,
    IClock clock,
    ILogger<DocumentService> log)
{
    public async Task<IReadOnlyList<DocumentTypeDto>> GetTypesAsync(CancellationToken ct = default)
        => await db.DocumentTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new DocumentTypeDto { Id = t.Id, Name = t.Name, Area = t.Area })
            .ToListAsync(ct);

    public async Task<DocumentPageDto> GetPageAsync(
        DocumentArea? area = null, string? search = null, CancellationToken ct = default)
    {
        var documents = await LoadAsync(ct);

        var filtered = documents
            .Where(d => area is null || d.Area == area)
            .Where(d => string.IsNullOrWhiteSpace(search) || Matches(d, search))
            .ToList();

        return new DocumentPageDto
        {
            Items = filtered,
            TotalCount = documents.Count,
            MissingFileCount = documents.Count(d => !d.FileExists),
        };
    }

    /// <summary>
    /// Sucht über Dokumente <em>und</em> Fachobjekte. Wer „hausrat“ eintippt, meint meistens den
    /// Vertrag und nicht den Dateinamen — eine Suche, die nur Dateien fände, ginge am Zweck vorbei.
    /// </summary>
    public async Task<DocumentSearchResultDto> SearchAsync(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return new DocumentSearchResultDto { Documents = [], Objects = [] };
        }

        var documents = (await LoadAsync(ct)).Where(d => Matches(d, term)).ToList();
        var objects = new List<ObjectHitDto>();

        void Add(LinkTargetType type, int id, string label, string subtitle)
        {
            if (label.Contains(term, StringComparison.OrdinalIgnoreCase)
                || subtitle.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                objects.Add(new ObjectHitDto
                {
                    TargetType = type,
                    TargetId = id,
                    Label = label,
                    Subtitle = subtitle,
                    TargetLabel = ObjectLabelService.TargetLabel(type),
                });
            }
        }

        foreach (var row in await db.Insurances.AsNoTracking()
                     .Select(x => new { x.Id, x.Name, x.Insurer }).ToListAsync(ct))
        {
            Add(LinkTargetType.Insurance, row.Id, row.Name, row.Insurer);
        }

        foreach (var row in await db.Contracts.AsNoTracking()
                     .Select(x => new { x.Id, x.Name, x.Provider }).ToListAsync(ct))
        {
            Add(LinkTargetType.Contract, row.Id, row.Name, row.Provider);
        }

        foreach (var row in await db.Properties.AsNoTracking()
                     .Select(x => new { x.Id, x.Name, x.Address }).ToListAsync(ct))
        {
            Add(LinkTargetType.Property, row.Id, row.Name, row.Address ?? string.Empty);
        }

        foreach (var row in await db.MedicalBills.AsNoTracking()
                     .Select(x => new { x.Id, x.Provider, x.BillNumber }).ToListAsync(ct))
        {
            Add(LinkTargetType.MedicalBill, row.Id, row.Provider, row.BillNumber ?? string.Empty);
        }

        foreach (var row in await db.Transactions.AsNoTracking()
                     .OrderByDescending(x => x.BookingDate).Take(300)
                     .Select(x => new { x.Id, x.Payee, x.BookingDate }).ToListAsync(ct))
        {
            Add(LinkTargetType.Transaction, row.Id, row.Payee, GermanDate(row.BookingDate));
        }

        return new DocumentSearchResultDto { Documents = documents, Objects = objects };
    }

    public async Task<DocumentDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var document = await db.Documents.AsNoTracking()
            .Include(d => d.DocumentType)
            .Include(d => d.Links)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (document is null)
        {
            return null;
        }

        var links = new List<DocumentLinkDto>();
        foreach (var link in document.Links)
        {
            links.Add(new DocumentLinkDto
            {
                Id = link.Id,
                TargetType = link.TargetType,
                TargetId = link.TargetId,
                Label = await labels.ResolveAsync(link.TargetType, link.TargetId, ct) ?? "Nicht mehr vorhanden",
                TargetLabel = ObjectLabelService.TargetLabel(link.TargetType),
            });
        }

        return new DocumentDetailDto
        {
            Id = document.Id,
            Title = document.Title,
            FileName = document.FileName,
            RelativePath = document.RelativePath,
            Extension = document.Extension,
            DocumentTypeId = document.DocumentTypeId,
            TypeName = document.DocumentType?.Name,
            Area = document.Area,
            Description = document.Description,
            DocumentDate = document.DocumentDate,
            ValidFrom = document.ValidFrom,
            ValidUntil = document.ValidUntil,
            Status = document.Status,
            Tags = SplitTags(document.Tags),
            FileExists = paths.Exists(document.RelativePath),
            Links = links,
        };
    }

    /// <summary>Dokumente, die an einem bestimmten Objekt hängen.</summary>
    public async Task<IReadOnlyList<DocumentListItemDto>> GetForTargetAsync(
        LinkTargetType type, int targetId, CancellationToken ct = default)
    {
        var ids = await db.DocumentLinks.AsNoTracking()
            .Where(l => l.TargetType == type && l.TargetId == targetId)
            .Select(l => l.DocumentId)
            .ToListAsync(ct);

        return (await LoadAsync(ct)).Where(d => ids.Contains(d.Id)).ToList();
    }

    /// <summary>
    /// Legt eine hochgeladene Datei ab und verbucht sie als Dokument. Schlägt das Speichern des
    /// Datensatzes fehl, wird die Datei wieder entfernt — sonst bliebe eine Waise im Ordner.
    /// </summary>
    public async Task<DocumentUploadResultDto> UploadAsync(
        Stream content,
        string fileName,
        DocumentArea area,
        string? title,
        int? documentTypeId,
        DateOnly? documentDate,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!paths.IsAllowedExtension(extension))
        {
            throw new ArgumentException($"Dateityp nicht zugelassen. Erlaubt: {paths.AllowedExtensionList}.");
        }

        var relativePath = await paths.StoreAsync(content, area, fileName, ct);

        try
        {
            var now = clock.Now;
            var document = new Document
            {
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
                DocumentTypeId = documentTypeId,
                Area = area,
                RelativePath = relativePath,
                FileName = Path.GetFileName(relativePath),
                Extension = extension,
                DocumentDate = documentDate ?? clock.Today,
                Status = DocumentStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync(ct);

            return new DocumentUploadResultDto { DocumentId = document.Id, RelativePath = relativePath };
        }
        catch
        {
            if (paths.Resolve(relativePath) is { } absolute && File.Exists(absolute))
            {
                File.Delete(absolute);
            }

            throw;
        }
    }

    public async Task<DocumentDetailDto?> UpdateAsync(
        int id, UpdateDocumentRequest request, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return null;
        }

        document.Title = request.Title.Trim();
        document.DocumentTypeId = request.DocumentTypeId;
        document.Area = request.Area;
        document.Description = request.Description;
        document.DocumentDate = request.DocumentDate;
        document.ValidFrom = request.ValidFrom;
        document.ValidUntil = request.ValidUntil;
        document.Tags = request.Tags is { Count: > 0 } ? string.Join(',', request.Tags) : null;
        document.UpdatedAt = clock.Now;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Setzt den Pfad eines Dokuments neu, dessen Datei verschoben wurde. Der Eintrag bleibt
    /// derselbe — Verknüpfungen, Tags und Metadaten gehen dabei nicht verloren.
    /// </summary>
    public async Task<DocumentDetailDto?> FixPathAsync(
        int id, string relativePath, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/').Trim().TrimStart('/');
        if (paths.Resolve(normalized) is not { } absolute)
        {
            throw new ArgumentException("Der Pfad führt aus dem Dokumentordner heraus.");
        }

        if (!File.Exists(absolute))
        {
            throw new ArgumentException("Unter diesem Pfad liegt keine Datei.");
        }

        document.RelativePath = normalized;
        document.FileName = Path.GetFileName(normalized);
        document.Extension = Path.GetExtension(normalized).ToLowerInvariant();
        document.UpdatedAt = clock.Now;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Hängt ein Dokument an ein Fachobjekt. Prüft, dass das Ziel im Haushalt existiert.</summary>
    public async Task<DocumentLinkDto?> LinkAsync(
        int documentId, LinkTargetType type, int targetId, CancellationToken ct = default)
    {
        if (!await db.Documents.AnyAsync(d => d.Id == documentId, ct))
        {
            return null;
        }

        var label = await labels.ResolveAsync(type, targetId, ct);
        if (label is null)
        {
            throw new ArgumentException("Das Ziel der Verknüpfung gibt es nicht.");
        }

        var existing = await db.DocumentLinks.FirstOrDefaultAsync(
            l => l.DocumentId == documentId && l.TargetType == type && l.TargetId == targetId, ct);

        if (existing is null)
        {
            existing = new DocumentLink
            {
                DocumentId = documentId,
                TargetType = type,
                TargetId = targetId,
                CreatedAt = clock.Now,
            };

            db.DocumentLinks.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        return new DocumentLinkDto
        {
            Id = existing.Id,
            TargetType = type,
            TargetId = targetId,
            Label = label,
            TargetLabel = ObjectLabelService.TargetLabel(type),
        };
    }

    public async Task<bool> UnlinkAsync(int linkId, CancellationToken ct = default)
    {
        var link = await db.DocumentLinks.FirstOrDefaultAsync(l => l.Id == linkId, ct);
        if (link is null)
        {
            return false;
        }

        db.DocumentLinks.Remove(link);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Löscht den Eintrag. Die Datei bleibt liegen — sie kann anderswo gebraucht werden, und ein
    /// versehentliches Löschen im Dateisystem lässt sich nicht rückgängig machen.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return false;
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Dokumenteintrag {Id} gelöscht. Die Datei {Pfad} bleibt bestehen.",
            id, document.RelativePath);

        return true;
    }

    /// <summary>Öffnet die Datei zum Ausliefern, oder <c>null</c>, wenn sie fehlt.</summary>
    public async Task<(Stream Content, string FileName, string ContentType)?> OpenAsync(
        int id, CancellationToken ct = default)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || paths.Resolve(document.RelativePath) is not { } absolute
            || !File.Exists(absolute))
        {
            return null;
        }

        return (File.OpenRead(absolute), document.FileName, ContentTypeFor(document.Extension));
    }

    private async Task<List<DocumentListItemDto>> LoadAsync(CancellationToken ct)
    {
        var rows = await db.Documents.AsNoTracking()
            .Include(d => d.DocumentType)
            .Include(d => d.Links)
            .OrderByDescending(d => d.DocumentDate)
            .ThenByDescending(d => d.Id)
            .ToListAsync(ct);

        var items = new List<DocumentListItemDto>(rows.Count);
        foreach (var row in rows)
        {
            var firstLink = row.Links.FirstOrDefault();
            var linkedLabel = firstLink is null
                ? null
                : await labels.ResolveAsync(firstLink.TargetType, firstLink.TargetId, ct);

            items.Add(new DocumentListItemDto
            {
                Id = row.Id,
                Title = row.Title,
                FileName = row.FileName,
                TypeName = row.DocumentType?.Name,
                Area = row.Area,
                DocumentDate = row.DocumentDate,
                LinkedLabel = linkedLabel,
                FileExists = paths.Exists(row.RelativePath),
            });
        }

        return items;
    }

    private static bool Matches(DocumentListItemDto document, string term)
        => document.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
           || document.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
           || (document.TypeName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
           || (document.LinkedLabel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    private static IReadOnlyList<string> SplitTags(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? []
            : [.. tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string GermanDate(DateOnly date)
        => date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string ContentTypeFor(string? extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".txt" => "text/plain",
        ".xml" => "application/xml",
        ".csv" => "text/csv",
        _ => "application/octet-stream",
    };
}
