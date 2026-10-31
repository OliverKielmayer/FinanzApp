using System.Security.Cryptography;
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
    IPdfTextReader reader,
    ILogger<DocumentService> log)
{
    /// <summary>
    /// Wie viele Zeichen Text je Dokument aufgehoben werden.
    /// </summary>
    /// <remarks>
    /// Eine Million Zeichen sind rund vierhundert eng beschriebene Seiten — mehr Post bekommt
    /// niemand in einem Umschlag. Die Grenze steht nicht gegen den Platzbedarf, sondern gegen
    /// den Einzelfall, der die Datenbank aufbläht: ein fehlgeschlagener Scan, der statt Text
    /// Zeichensalat liefert.
    /// </remarks>
    private const int MaxTextLength = 1_000_000;
    /// <summary>
    /// Die Typen, die sich vergeben lassen.
    /// </summary>
    /// <remarks>
    /// Ohne die stillgelegten: sie stehen nur noch an den Dokumenten, die sie schon tragen. In
    /// einer Auswahlliste wären sie eine Falle — anbieten und beim Speichern ablehnen ist
    /// schlechter, als sie gar nicht erst zu zeigen. Wer sie <em>mit</em> ihrer Verwendung sehen
    /// will, nimmt die Typenverwaltung.
    /// </remarks>
    public async Task<IReadOnlyList<DocumentTypeDto>> GetTypesAsync(CancellationToken ct = default)
        => await db.DocumentTypes.AsNoTracking()
            .Where(t => !t.IsRetired)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new DocumentTypeDto { Id = t.Id, Name = t.Name, Area = t.Area })
            .ToListAsync(ct);

    public async Task<DocumentPageDto> GetPageAsync(
        DocumentArea? area = null, string? search = null, CancellationToken ct = default)
    {
        var documents = await LoadAsync(ct);
        var imText = await TextHitsAsync(search, ct);

        var filtered = documents
            .Where(d => area is null || d.Area == area)
            .Where(d => string.IsNullOrWhiteSpace(search) || Matches(d, search, imText))
            .ToList();

        return new DocumentPageDto
        {
            Items = filtered,
            TotalCount = documents.Count,
            MissingFileCount = documents.Count(d => !d.FileExists),
            WithoutTextCount = await db.Documents.CountAsync(d => d.ContentText == null, ct),
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

        var imText = await TextHitsAsync(term, ct);
        var documents = (await LoadAsync(ct)).Where(d => Matches(d, term, imText)).ToList();
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

        // Ganze Datensätze statt einer Projektion auf zwei Felder: der gemeinsame Builder
        // baut den Untertitel aus allen Rohfeldern, und die Suche findet damit auch, was nur
        // dort steht — eine Vertragsnummer etwa.
        foreach (var row in await db.Policies.AsNoTracking().ToListAsync(ct))
        {
            Add(LinkTargetType.Policy, row.Id, row.Name, HoldingMeta.ForPolicy(row));
        }

        foreach (var row in await db.Contracts.AsNoTracking().ToListAsync(ct))
        {
            Add(LinkTargetType.Contract, row.Id, row.Name, HoldingMeta.ForContract(row));
        }

        foreach (var row in await db.Properties.AsNoTracking().ToListAsync(ct))
        {
            Add(LinkTargetType.Property, row.Id, row.Name, HoldingMeta.ForProperty(row));
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

        // Gelesene Werte, die noch niemand bestätigt hat. Sie sind der Unterschied zwischen
        // „abgelegt“ und „übernommen“ — und der war bisher nirgends zu sehen.
        var offen = await db.DocumentExtractions
            .CountAsync(x => x.DocumentId == document.Id && !x.Confirmed, ct);

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
            Sharing = document.Sharing,
            FileExists = paths.Exists(document.RelativePath),
            Links = links,
            UnconfirmedValues = offen,

            // Nur wo etwas offen ist. Ein Weg zum Prüfschritt an einem Beleg ohne gelesene Werte
            // führte in eine Meldung statt in eine Fläche — angeboten wird, was auch hält.
            ValuesHref = offen == 0 ? null : ScanReview.Href(document.ScanKind, document.Id),
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
    /// <param name="subFolder">
    /// Vorgeschlagene Ablageschublade unterhalb des Bereichs, etwa
    /// <c>Lebensversicherung/Heidelberger Leben/2025</c>. Ohne Angabe direkt in den Bereich.
    /// </param>
    /// <param name="preferredName">
    /// Vorgeschlagener Dateiname ohne Erweiterung. Ohne Angabe bleibt der hochgeladene.
    /// </param>
    public async Task<DocumentUploadResultDto> UploadAsync(
        Stream content,
        string fileName,
        DocumentArea area,
        string? title,
        int? documentTypeId,
        DateOnly? documentDate,
        string? subFolder = null,
        string? preferredName = null,
        string? contentText = null,
        TextSource? textSource = null,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!paths.IsAllowedExtension(extension))
        {
            throw new ArgumentException($"Dateityp nicht zugelassen. Erlaubt: {paths.AllowedExtensionList}.");
        }

        var relativePath = await paths.StoreAsync(content, area, fileName, subFolder, preferredName, ct);

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

                // Aus der abgelegten Datei und nicht aus dem übergebenen Strom: der ist an
                // dieser Stelle gelesen, und ob er sich zurückspulen lässt, weiß hier niemand.
                // Was zählt, ist ohnehin, was auf der Platte liegt.
                ContentHash = await FingerprintAsync(relativePath, ct),

                // Der Text, falls der Aufrufer ihn ohnehin gelesen hat — die Einlieferung und
                // der Prüfschritt tun das. Wer ihn nicht mitbringt, bekommt ihn später über
                // EnsureTextAsync; zweimal durch die Erkennung zu laufen kostet Sekunden und
                // bringt dasselbe Ergebnis.
                ContentText = Shorten(contentText),
                TextSource = textSource,

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

    /// <summary>
    /// Das Dokument, dessen Datei Byte für Byte dieselbe ist — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>Der zweite Durchgang trägt Abdrücke nach. Alles, was vor dieser Spalte abgelegt
    /// wurde, hat keinen; ohne das Nachtragen sähe die Einlieferung den gewachsenen Bestand
    /// nicht und meldete ausgerechnet für die Belege keine Dublette, um die es geht. Es kostet
    /// einmal den ganzen Bestand und danach nichts mehr.</para>
    /// <para>Eine fehlende Datei bleibt ohne Abdruck und wird beim nächsten Mal wieder versucht.
    /// Sie kann zurückkommen — ein Merkmal „nachgesehen und nicht da“ wäre eine zweite Wahrheit
    /// über denselben Zustand, und die stimmte nach dem ersten Umzug nicht mehr.</para>
    /// </remarks>
    public async Task<Document?> FindByContentAsync(string hash, CancellationToken ct = default)
    {
        if (await db.Documents.FirstOrDefaultAsync(d => d.ContentHash == hash, ct) is { } bekannt)
        {
            return bekannt;
        }

        var ohneAbdruck = await db.Documents.Where(d => d.ContentHash == null).ToListAsync(ct);
        Document? gefunden = null;

        foreach (var dokument in ohneAbdruck)
        {
            if (await FingerprintAsync(dokument.RelativePath, ct) is not { } abdruck)
            {
                continue;
            }

            dokument.ContentHash = abdruck;

            if (gefunden is null && abdruck == hash)
            {
                gefunden = dokument;
            }
        }

        if (ohneAbdruck.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return gefunden;
    }

    /// <summary>Die Auskunft über eine Datei, die schon abgelegt ist.</summary>
    /// <remarks>
    /// An einer Stelle, weil zwei Wege sie geben: die Einlieferung aus dem überwachten Ordner
    /// und das Einlesen von Hand. Zwei Fassungen desselben Satzes wären zwei Auskünfte über
    /// denselben Sachverhalt.
    /// </remarks>
    public static string DuplicateNote(Document vorhanden, string eingereicht)
        => $"„{eingereicht}“ ist Byte für Byte dieselbe Datei wie „{vorhanden.Title}“, "
           + $"abgelegt unter {vorhanden.RelativePath} — nicht ein zweites Mal abgelegt.";

    /// <summary>
    /// Ein anderer Beleg, den die Ablage unter denselben Namen gelegt hätte.
    /// </summary>
    /// <remarks>
    /// <para>Die zweite Stufe, für den Fall, den der Abdruck nicht trifft: dasselbe Schreiben ein
    /// zweites Mal eingescannt ergibt eine andere Datei — anderes Bild, andere Bytes —, und
    /// derselbe Beleg ist es trotzdem.</para>
    /// <para><b>Gemessen wird am Ablagenamen</b>, denn den bildet die Analyse aus genau den
    /// Merkmalen, die einen Beleg ausmachen: Art, Objekt und Stichtag stehen in Ordner und
    /// Dateiname. Am <see cref="Document.DocumentDate"/> hinge der Vergleich schief — es trägt
    /// den Ablagetag, wo kein Schreibdatum gelesen wurde, und dann wären zwei Berichte aus
    /// verschiedenen Jahren, am selben Tag abgelegt, plötzlich derselbe Beleg.</para>
    /// <para>Aufgehalten wird nichts: eine berichtigte Fassung desselben Schreibens bekäme
    /// denselben Namen. Der Verdacht steht dort, wo ein Mensch ihn sieht — im Eingang und im
    /// Prüfschritt.</para>
    /// </remarks>
    public async Task<Document?> FindSameFilingAsync(int documentId, CancellationToken ct = default)
    {
        var abgelegt = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == documentId, ct);

        // Nur bei erkannter Art. Sonst ist der Ablagename der eingereichte Dateiname, und der
        // sagt über den Inhalt nichts: ein Scanner, der jedes Blatt „scan_0001.pdf“ nennt, ließe
        // den Verdacht bei jedem zweiten Zettel aufleuchten — und ein Hinweis, der immer
        // dasteht, wird nicht mehr gelesen.
        if (abgelegt.ScanKind is not { Length: > 0 } art)
        {
            return null;
        }

        var stamm = FilingStem(abgelegt.RelativePath);
        var ordner = FilingFolder(abgelegt.RelativePath);

        // Vorgesiebt über Art und Ordner: beides kann die Datenbank. Den Namen selbst vergleicht
        // der Dienst — die Ausweichziffer abzuschneiden ist keine Abfrage, die SQLite kennt.
        var kandidaten = await db.Documents.AsNoTracking()
            .Where(d => d.Id != documentId && d.ScanKind == art && d.RelativePath.StartsWith(ordner))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        return kandidaten.FirstOrDefault(d => FilingStem(d.RelativePath) == stamm);
    }

    /// <summary>Der Ordner eines Ablagepfads, mit Schrägstrich am Ende.</summary>
    private static string FilingFolder(string relativePath)
    {
        var strich = relativePath.LastIndexOf('/');
        return strich < 0 ? string.Empty : relativePath[..(strich + 1)];
    }

    /// <summary>
    /// Der Ablagepfad ohne die Ziffer, mit der die Ablage einem besetzten Namen ausweicht.
    /// </summary>
    /// <remarks>
    /// <c>Statusreport_2016-07-31_1.pdf</c> und <c>Statusreport_2016-07-31.pdf</c> sind derselbe
    /// Name. Abgeschnitten wird nur eine <em>kurze</em> Zahl: Ausweichziffern zählen bei eins los
    /// und bleiben klein, während <c>_2016</c> zum Namen gehört. Vier Stellen sind eine
    /// Jahreszahl und bleiben stehen.
    /// </remarks>
    private static string FilingStem(string relativePath)
    {
        var ohneEndung = relativePath[..^Path.GetExtension(relativePath).Length];
        var strich = ohneEndung.LastIndexOf('_');

        if (strich <= 0)
        {
            return ohneEndung;
        }

        var hinten = ohneEndung[(strich + 1)..];

        return hinten is { Length: > 0 and < 4 } && hinten.All(char.IsAsciiDigit)
            ? ohneEndung[..strich]
            : ohneEndung;
    }

    /// <summary>Der Abdruck eines Stroms — SHA-256, klein geschrieben.</summary>
    /// <remarks>
    /// Der Strom wird dabei bis zum Ende gelesen. Wer ihn danach noch braucht, spult ihn zurück.
    /// </remarks>
    public static async Task<string> FingerprintAsync(Stream content, CancellationToken ct = default)
        => Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct));

    /// <summary>Der Abdruck einer abgelegten Datei — <c>null</c>, wenn sie nicht (mehr) da ist.</summary>
    private async Task<string?> FingerprintAsync(string relativePath, CancellationToken ct)
    {
        if (paths.Resolve(relativePath) is not { } absolut || !File.Exists(absolut))
        {
            return null;
        }

        await using var datei = File.OpenRead(absolut);
        return await FingerprintAsync(datei, ct);
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

    /// <summary>
    /// Die Namen, die ein Gerät vergibt, wenn niemand einen nennt.
    /// </summary>
    /// <remarks>
    /// Sie sind nicht falsch, sie sagen nur nichts: Ein Scanner zählt durch, ein Telefon nimmt
    /// das Datum der Aufnahme. Wer im Dateimanager sucht, unterscheidet daran keine zwei
    /// Belege. Alles andere gilt als <em>gepflegt</em> und bleibt stehen — die Grenze muss
    /// eng sein, denn ein überschriebener Name ist nicht zurückzuholen.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex GeraeteName =
        new(@"^(scannen|scan|bild|image|img|dokument|document|doc|pdf|datei|file)[ _-]*\d*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Benennt einen Beleg um, der noch den Namen seines Geräts trägt.
    /// </summary>
    /// <remarks>
    /// <para>Nach dem Zuordnen im Eingang liegt der Beleg beim richtigen Vertrag und heißt
    /// weiter „Scannen0007“. Der Ordner stimmt dann, der Name nicht — und im Dateimanager
    /// unterscheidet nichts die eine Seite von der nächsten.</para>
    /// <para>Angefasst wird <b>nur</b>, was nach einem Gerätenamen aussieht. Was ein Mensch
    /// benannt hat, bleibt: Ein überschriebener Name ist nicht zurückzuholen, und die Anwendung
    /// weiß nicht besser als er, wie sein Beleg heißen soll.</para>
    /// <para>Gibt den neuen Pfad zurück oder <c>null</c>, wenn nichts zu tun war.</para>
    /// </remarks>
    public async Task<string?> RenameIfScannedAsync(
        int documentId, string typeName, string targetName, DateOnly? documentDate,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var stamm = Path.GetFileNameWithoutExtension(document.FileName);
        if (!GeraeteName.IsMatch(stamm))
        {
            return null;
        }

        var teile = new List<string> { typeName, targetName };
        if (documentDate is { } tag)
        {
            teile.Add(tag.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }

        var neu = DocumentPathService.Sanitize(
            string.Join("_", teile.Where(s => !string.IsNullOrWhiteSpace(s)))
            + Path.GetExtension(document.FileName));

        if (string.Equals(neu, document.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var ordner = FilingFolder(document.RelativePath);
        var ziel = paths.Move(document.RelativePath, ordner + neu);

        if (ziel is null)
        {
            return null;
        }

        document.RelativePath = ziel;
        document.FileName = Path.GetFileName(ziel);
        document.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);

        return ziel;
    }

    /// <summary>
    /// Holt einen Beleg aus dem Ordner <c>Unbekannt</c> heraus, sobald sein Objekt feststeht.
    /// </summary>
    /// <remarks>
    /// <para>Der Ablagepfad wird aus dem Objekt gebildet. Konnte die Einlieferung es nicht
    /// bestimmen, steht dort <c>Unbekannt</c> — und dabei bliebe es für immer, obwohl der Beleg
    /// im Scaneingang längst zugeordnet wurde. Der Ordner behauptete dann etwas, das nicht mehr
    /// stimmt.</para>
    /// <para><b>Nur diesen einen Ordner.</b> Kommt <c>Unbekannt</c> im Pfad nicht genau einmal
    /// vor, liegt der Beleg dort, wo ein Mensch ihn haben wollte — und dann fasst ihn niemand
    /// an. Was der Dienst selbst abgelegt hat, räumt er nach; eingerichtete Ablagen bleiben.</para>
    /// <para>Gibt den neuen Pfad zurück oder <c>null</c>, wenn nichts zu tun war.</para>
    /// </remarks>
    public async Task<string?> RefileAsync(
        int documentId, string targetName, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);

        // Der Name zuerst, dann die Entschärfung: aus einem leeren Namen macht sie „Dokument“,
        // und der Beleg läge danach in einem Ordner, den niemand so genannt hat.
        if (document is null
            || string.IsNullOrWhiteSpace(targetName)
            || DocumentPathService.Sanitize(targetName) is not { Length: > 0 } ordner)
        {
            return null;
        }

        var segmente = document.RelativePath.Split('/');

        if (segmente.Count(s => s == DocumentKind.UnknownTarget) != 1)
        {
            return null;
        }

        segmente[Array.IndexOf(segmente, DocumentKind.UnknownTarget)] = ordner;

        if (paths.Move(document.RelativePath, string.Join('/', segmente)) is not { } neu)
        {
            return null;
        }

        document.RelativePath = neu;
        document.FileName = Path.GetFileName(neu);
        document.UpdatedAt = clock.Now;

        await db.SaveChangesAsync(ct);
        return neu;
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

        // Die Erweiterung ist nicht immer gepflegt — Belege aus dem Scaneingang kamen ohne.
        // Ohne den Rückfall auf den Dateinamen ginge ein sichtbares PDF als
        // „application/octet-stream" hinaus, und der Browser lüde es herunter statt es zu zeigen.
        var extension = document.Extension is { Length: > 0 } gepflegt
            ? gepflegt
            : Path.GetExtension(document.FileName);

        return (File.OpenRead(absolute), document.FileName, ContentTypeFor(extension));
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
                Tags = SplitTags(row.Tags),
                Sharing = row.Sharing,
                FileExists = paths.Exists(row.RelativePath),
            });
        }

        return items;
    }

    /// <summary>
    /// Ob ein Dokument zum Suchbegriff passt.
    /// </summary>
    /// <remarks>
    /// <para>Die ersten vier Felder sind gepflegt: Titel, Dateiname, Typ, verknüpftes Objekt.
    /// Wer danach suchte, fand nur, was vorher jemand von Hand benannt hatte — und genau das ist
    /// bei einem Stapel eingescannter Post niemand.</para>
    /// <para>Schlagworte und <b>Text</b> kommen dazu. Der Text ist der eigentliche Gewinn: Er
    /// wird beim Einlesen ohnehin gewonnen, und eine Rechnungsnummer oder ein Absender steht
    /// darin, ohne dass ihn jemand abgeschrieben hat.</para>
    /// </remarks>
    private static bool Matches(DocumentListItemDto document, string term, IReadOnlySet<int> imText)
        => document.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
           || document.FileName.Contains(term, StringComparison.OrdinalIgnoreCase)
           || (document.TypeName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
           || (document.LinkedLabel?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
           || document.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase))
           || imText.Contains(document.Id);

    /// <summary>
    /// Welche Dokumente den Suchbegriff <em>im Text</em> tragen.
    /// </summary>
    /// <remarks>
    /// <para>Die Frage geht an die Datenbank und nicht an die geladene Liste: Der Text bleibt
    /// damit, wo er hingehört. Ihn in die Listenzeile zu hängen hieße, ihn bei jedem Aufruf
    /// über die Leitung zu schicken — für hundertdreißig Belege mehrere Megabyte, damit die
    /// Suche zwei Zeilen findet.</para>
    /// <para><c>LIKE</c> in SQLite vergleicht Groß- und Kleinschreibung nur bei ASCII-Zeichen
    /// ohne Unterschied. „rechnung“ findet „Rechnung“, „über“ findet „Über“ nicht. Das ist eine
    /// Einschränkung und keine Absicht.</para>
    /// </remarks>
    private async Task<IReadOnlySet<int>> TextHitsAsync(string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return new HashSet<int>();
        }

        // Die Platzhalter von LIKE entschärfen: Wer nach „50 %“ sucht, meint das Zeichen und
        // nicht „irgendetwas“. Ohne das fände ein einzelnes Prozentzeichen jedes Dokument.
        var muster = "%" + search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

        var treffer = await db.Documents.AsNoTracking()
            .Where(d => d.ContentText != null && EF.Functions.Like(d.ContentText, muster, @"\"))
            .Select(d => d.Id)
            .ToListAsync(ct);

        return treffer.ToHashSet();
    }

    /// <summary>Kappt den Text auf das, was aufgehoben wird — <c>null</c> bleibt <c>null</c>.</summary>
    private static string? Shorten(string? text)
        => text is null ? null
            : text.Length <= MaxTextLength ? text
            : text[..MaxTextLength];

    /// <summary>
    /// Liest den Text eines abgelegten Dokuments nach, falls er fehlt.
    /// </summary>
    /// <remarks>
    /// <para>Für alles, was vor dieser Spalte abgelegt wurde, und für die Wege, die den Text
    /// nicht ohnehin schon in der Hand haben. Gelesen wird die <em>abgelegte</em> Datei; liegt
    /// dort keine, bleibt der Text leer und der Versuch wird nicht wiederholt.</para>
    /// <para>Der Unterschied zwischen <c>null</c> und der leeren Zeichenkette trägt hier: null
    /// heißt „noch nie gelesen“, leer heißt „gelesen, nichts gefunden“. Ohne ihn liefe ein
    /// bildhaftes PDF bei jedem Aufruf erneut durch die Erkennung.</para>
    /// <para>Gibt zurück, ob etwas geschrieben wurde.</para>
    /// </remarks>
    public async Task<bool> EnsureTextAsync(int documentId, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null || document.ContentText is not null)
        {
            return false;
        }

        if (paths.Resolve(document.RelativePath) is not { } absolute || !File.Exists(absolute))
        {
            // Keine Datei, kein Text — und kein Eintrag, der das Gegenteil behauptet.
            return false;
        }

        try
        {
            await using var strom = File.OpenRead(absolute);
            var inhalt = reader.Read(strom);

            document.ContentText = Shorten(string.Join("\n", inhalt.Lines.Select(z => z.Text)));
            document.TextSource = inhalt.Source;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Text von Dokument {Id} ließ sich nicht lesen.", documentId);
            return false;
        }
    }

    /// <summary>
    /// Stellt ein Dokument auf "nur ich" oder zurück auf den Haushalt.
    /// </summary>
    /// <remarks>
    /// <para>Wer es privat stellt, wird im selben Zug sein Eigentümer. Ohne Eigentümer sähe ein
    /// privates Dokument niemand mehr — auch der nicht, der es abgelegt hat.</para>
    /// <para>Zurück auf "Haushalt" verliert es den Eigentümer wieder: Ein Eigentümer ohne
    /// Wirkung ist eine Angabe, die beim nächsten Umstellen stillschweigend mitspielte.</para>
    /// </remarks>
    public async Task<bool> SetSharingAsync(
        int id, DocumentSharing sharing, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null)
        {
            return false;
        }

        document.Sharing = sharing;
        document.OwnerUserId = sharing == DocumentSharing.Private ? db.CurrentUserId : null;
        document.UpdatedAt = clock.Now;

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Holt den Text für alle Dokumente nach, die noch keinen haben.
    /// </summary>
    /// <remarks>
    /// Einmalig gedacht, für den Bestand, der vor der Suche nach Text entstanden ist. Die
    /// Obergrenze steht da, damit ein Aufruf endet: Hundert Belege durch eine Texterkennung
    /// sind Minuten, und eine Anfrage, die Minuten braucht, bricht unterwegs ab.
    /// </remarks>
    public async Task<int> BackfillTextAsync(int limit = 50, CancellationToken ct = default)
    {
        var offen = await db.Documents.AsNoTracking()
            .Where(d => d.ContentText == null)
            .OrderBy(d => d.Id)
            .Select(d => d.Id)
            .Take(limit)
            .ToListAsync(ct);

        var gelesen = 0;
        foreach (var id in offen)
        {
            if (await EnsureTextAsync(id, ct))
            {
                gelesen++;
            }
        }

        return gelesen;
    }

    private static IReadOnlyList<string> SplitTags(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? []
            : [.. tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string GermanDate(DateOnly date)
        => date.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string ContentTypeFor(string? extension) => extension?.ToLowerInvariant() switch
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
