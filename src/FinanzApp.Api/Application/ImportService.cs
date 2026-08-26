using System.Globalization;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinanzApp.Api.Application;

/// <summary>
/// Kontoauszug einlesen — Vorschau und Übernahme.
/// </summary>
/// <remarks>
/// <para>Die Duplikatprüfung läuft <b>gegen den Bestand</b>, nicht nur innerhalb der Datei.
/// Ein Satz gilt als <em>bereits vorhanden</em>, wenn seine Importreferenz schon gebucht ist —
/// das ist der verlässliche Weg, denn die Referenz vergibt die Bank. Er gilt als <em>mögliches
/// Duplikat</em>, wenn Tag, Empfänger und Betrag auf eine vorhandene Buchung passen, die Referenz
/// aber neu ist. Derselbe Auszug zweimal eingelesen ergibt beim zweiten Mal null Vorschläge.</para>
/// <para>Was übernommen wird, entscheidet allein die <b>Auswahl des Nutzers</b>. Der Dienst
/// schlägt vor — neue Sätze angehakt, Treffer abgewählt — und führt dann aus, was dasteht. Ein
/// zugeschaltetes Duplikat wird gebucht; sonst widerspräche der Knopf dem Kopf.</para>
/// <para>Zwischen Vorschau und Übernahme liegt die gelesene Datei im Zwischenspeicher, nicht beim
/// Client. Käme sie zurückgereicht, entschiede der Aufrufer über Beträge und Referenzen, und die
/// Duplikatprüfung liefe gegen Daten, die sie selbst nicht gelesen hat.</para>
/// </remarks>
public sealed class ImportService(
    FinanzAppDbContext db,
    IClock clock,
    IStatementParser parser,
    IMemoryCache cache,
    CurrentUser current)
{
    /// <summary>Wie lange eine Vorschau gültig bleibt, wenn niemand sie übernimmt.</summary>
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Der Text, der das Kriterium benennt — er gehört sichtbar an die Prüfung.</summary>
    private const string Criterion =
        "Geprüft gegen den Bestand: gleiche Importreferenz gilt als vorhanden, "
        + "gleicher Tag mit gleichem Empfänger und Betrag als mögliches Duplikat.";

    /// <summary>Ein gelesener Stapel — aus einer Datei oder aus der eingebauten Vorlage.</summary>
    private sealed record Batch(
        Guid Id,
        string FileName,
        string Format,
        string BankName,
        string? Iban,
        decimal? StatementBalance,
        string? Separator,
        IReadOnlyList<ImportRecord> Records);

    /// <summary>
    /// Liest eine hochgeladene Auszugsdatei und legt die Vorschau bereit.
    /// </summary>
    public async Task<ImportPreviewDto> ReadAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        if (!parser.CanRead(fileName))
        {
            throw new StatementFormatException(
                $"„{fileName}“ sieht nicht nach einem camt-Auszug aus — erwartet wird eine XML-Datei.");
        }

        var statement = await parser.ParseAsync(content, fileName, ct);

        var batch = new Batch(
            Id: Guid.NewGuid(),
            FileName: statement.FileName,
            Format: statement.Format,
            BankName: statement.BankName ?? "unbekannt",
            Iban: statement.Iban,
            StatementBalance: statement.ClosingBalance,
            Separator: null,
            Records: statement.Records);

        cache.Set(KeyOf(batch.Id), batch, new MemoryCacheEntryOptions
        {
            SlidingExpiration = PreviewLifetime,
            Size = batch.Records.Count,
        });

        return await BuildAsync(batch, ct);
    }

    /// <summary>Die eingebaute Beispielvorlage — für den Leerzustand und zum Ausprobieren.</summary>
    public Task<ImportPreviewDto> GetPreviewAsync(CancellationToken ct = default)
        => BuildAsync(DemoBatch(), ct);

    /// <summary>
    /// Übernimmt die <b>gewählten</b> Sätze in einer Transaktion — entweder liegen danach alle im
    /// Bestand oder keiner.
    /// </summary>
    /// <remarks>
    /// Fehlerhafte Sätze lassen sich nicht zuschalten: aus einem unlesbaren Betrag wird keine
    /// Buchung, egal wie oft jemand darauf tippt. Alles andere folgt der Auswahl.
    /// </remarks>
    public async Task<ImportCommitResultDto> CommitAsync(
        ImportCommitRequest request, CancellationToken ct = default)
    {
        var batch = Recall(request.PreviewId)
                    ?? throw new ArgumentException(
                        "Diese Vorschau gibt es nicht mehr. Bitte die Datei noch einmal einlesen.",
                        nameof(request));

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
                      ?? throw new ArgumentException("Unbekanntes Zielkonto.", nameof(request));

        var rows = await ClassifyAsync(batch, ct);
        var chosen = request.Indexes.ToHashSet();

        // Was der Nutzer im Import gewählt hat, hat Vorrang vor jeder Regel.
        var byPayee = request.Choices
            .GroupBy(c => Categorization.Normalize(c.Payee))
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var learned = await LearnAsync(request.Choices, ct);

        var imported = 0;
        var forced = 0;
        var withoutCategory = 0;

        foreach (var row in rows)
        {
            if (!chosen.Contains(row.Index) || row.State == ImportRowState.Error)
            {
                continue;
            }

            var record = batch.Records[row.Index];
            var categoryId = byPayee.TryGetValue(Categorization.Normalize(record.Payee), out var choice)
                ? choice.CategoryId
                : row.SuggestedCategoryId;

            db.Transactions.Add(new Transaction
            {
                BookingDate = record.BookingDate!.Value,
                Payee = record.Payee,
                Kind = record.Amount!.Value >= 0 ? TransactionKind.Income : TransactionKind.Expense,
                Amount = record.Amount.Value,
                AccountId = account.Id,
                CategoryId = categoryId,
                ImportReference = record.Reference,
                CreatedAt = clock.Now,
            });

            imported++;
            if (categoryId is null)
            {
                withoutCategory++;
            }

            if (row.State is ImportRowState.Duplicate or ImportRowState.Existing)
            {
                forced++;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Nach der Übernahme ist die Vorschau verbraucht — ein zweiter Klick soll nicht dieselben
        // Sätze noch einmal buchen.
        cache.Remove(KeyOf(batch.Id));

        return new ImportCommitResultDto
        {
            ImportedCount = imported,
            ForcedDuplicates = forced,
            WithoutCategory = withoutCategory,
            LearnedRuleIds = [.. learned],
        };
    }

    /// <summary>
    /// Legt die Regeln an, die der Nutzer sich merken lassen wollte.
    /// </summary>
    /// <remarks>
    /// Erst hier, nicht schon in der Vorschau: wer den Import verwirft, soll keine Regel
    /// hinterlassen haben. Ein durchgeführter Import macht sie dauerhaft.
    ///
    /// Eine bestehende Regel wird überschrieben statt verdoppelt — zwei Regeln auf dasselbe
    /// Muster wären ein Widerspruch, den niemand auflösen kann.
    /// </remarks>
    private async Task<List<int>> LearnAsync(
        IReadOnlyList<ImportCategoryChoice> choices, CancellationToken ct)
    {
        var wanted = choices.Where(c => c.RememberRule).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var existing = await db.CategorizationRules.ToListAsync(ct);
        var learned = new List<int>();

        foreach (var choice in wanted)
        {
            var pattern = Categorization.RulePatternFor(choice.Payee);
            var normalized = Categorization.Normalize(pattern);

            var rule = existing.FirstOrDefault(r =>
                Categorization.Normalize(r.PayeePattern) == normalized);

            if (rule is null)
            {
                rule = new CategorizationRule
                {
                    PayeePattern = pattern,
                    CategoryId = choice.CategoryId,
                    LearnedAt = clock.Now,
                };

                db.CategorizationRules.Add(rule);
                existing.Add(rule);
            }
            else
            {
                rule.CategoryId = choice.CategoryId;
                rule.LearnedAt = clock.Now;
            }

            learned.Add(rule.Id);
        }

        // Die Ids der neuen Regeln stehen erst nach dem Speichern fest.
        await db.SaveChangesAsync(ct);

        return [.. wanted
            .Select(c => Categorization.Normalize(Categorization.RulePatternFor(c.Payee)))
            .Distinct(StringComparer.Ordinal)
            .Select(n => existing.First(r => Categorization.Normalize(r.PayeePattern) == n).Id)];
    }

    public Task<int> GetProfileCountAsync(CancellationToken ct = default)
        => db.ImportProfiles.CountAsync(ct);

    public async Task<string> GetProfileFormatsAsync(CancellationToken ct = default)
    {
        var formats = await db.ImportProfiles.AsNoTracking()
            .Select(p => p.Format)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(ct);

        // „CAMT.053“ und „CSV“ werden auf der Sammelseite als „CAMT & CSV“ genannt.
        return string.Join(" & ", formats.Select(f => f.Split('.')[0]));
    }

    private static Batch DemoBatch() => new(
        Id: DemoImportBatch.PreviewId,
        FileName: DemoImportBatch.FileName,
        Format: DemoImportBatch.Format,
        BankName: DemoImportBatch.BankName,
        Iban: null,
        StatementBalance: null,
        Separator: null,
        Records: DemoImportBatch.Records);

    /// <summary>
    /// Der Schlüssel trägt den Haushalt.
    /// </summary>
    /// <remarks>
    /// Damit kann eine fremde Vorschau-Id nichts erreichen: sie zeigt schlicht auf einen anderen
    /// Schlüssel. Eine Prüfung, die man vergessen kann, gibt es dadurch gar nicht erst.
    /// </remarks>
    private string KeyOf(Guid id)
        => string.Create(CultureInfo.InvariantCulture, $"import:{current.HouseholdId}:{id}");

    private Batch? Recall(Guid id)
    {
        if (id == DemoImportBatch.PreviewId)
        {
            return DemoBatch();
        }

        return cache.TryGetValue(KeyOf(id), out Batch? batch) ? batch : null;
    }

    private async Task<ImportPreviewDto> BuildAsync(Batch batch, CancellationToken ct)
    {
        var rows = await ClassifyAsync(batch, ct);

        var accounts = await db.Accounts.AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ImportAccountDto { Id = a.Id, Name = a.Name, Iban = a.Iban })
            .ToListAsync(ct);

        var dated = rows.Where(r => r.BookingDate is not null).Select(r => r.BookingDate!.Value).ToList();

        return new ImportPreviewDto
        {
            Id = batch.Id,
            FileName = batch.FileName,
            BankName = batch.BankName,
            Format = batch.Format,
            ProfileName = await ProfileNameAsync(batch, ct),
            From = dated.Count == 0 ? null : dated.Min(),
            To = dated.Count == 0 ? null : dated.Max(),

            // Der Saldo aus der Datei, wenn sie einen nennt — sonst die Summe der Sätze. Ein
            // gerechneter Saldo ist nicht derselbe Wert und darf sich nicht als einer ausgeben.
            StatementBalance = batch.StatementBalance
                               ?? (rows.Any(r => r.Amount is not null)
                                   ? rows.Where(r => r.Amount is not null).Sum(r => r.Amount!.Value)
                                   : null),
            Separator = batch.Separator,
            Accounts = accounts,
            SuggestedAccountId = Suggest(batch, accounts)?.Id,
            RecordCount = rows.Count,
            NewCount = rows.Count(r => r.State == ImportRowState.New),
            ExistingCount = rows.Count(r => r.State == ImportRowState.Existing),
            DuplicateCount = rows.Count(r => r.State == ImportRowState.Duplicate),
            ErrorCount = rows.Count(r => r.State == ImportRowState.Error),
            DuplicateCriterion = Criterion,
            Rows = rows,
            LastImport = await LastImportAsync(ct),
        };
    }

    /// <summary>
    /// Das Konto, auf das der Auszug zeigt — zuerst über die IBAN, dann über den Namen der Bank.
    /// </summary>
    /// <remarks>
    /// Die IBAN steht in den Stammdaten mit Leerzeichen und in der Datei ohne. Ohne Normalisierung
    /// träfe der Vergleich nie zu, und die App schlüge stumm das falsche Konto vor.
    /// </remarks>
    private static ImportAccountDto? Suggest(Batch batch, List<ImportAccountDto> accounts)
    {
        if (Compact(batch.Iban) is { Length: > 0 } iban)
        {
            var match = accounts.FirstOrDefault(a =>
                string.Equals(Compact(a.Iban), iban, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return accounts.FirstOrDefault(a => a.Name == DemoImportBatch.AccountName
                                            && batch.Id == DemoImportBatch.PreviewId)
               ?? accounts.FirstOrDefault(a =>
                   batch.BankName.Length > 0
                   && a.Name.Contains(FirstWord(batch.BankName), StringComparison.OrdinalIgnoreCase));
    }

    private static string? Compact(string? iban)
        => iban is null ? null : new string([.. iban.Where(c => !char.IsWhiteSpace(c))]);

    /// <summary>„Sparkasse Heidelberg“ soll auf das Konto „Sparkasse Giro“ treffen.</summary>
    private static string FirstWord(string text)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    private async Task<string> ProfileNameAsync(Batch batch, CancellationToken ct)
    {
        if (batch.BankName is not { Length: > 0 } bank)
        {
            return "ohne Profil";
        }

        var word = FirstWord(bank);
        var profile = await db.ImportProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.BankName == word, ct);

        return profile?.Name ?? "ohne Profil";
    }

    /// <summary>
    /// Die Regel, die auf einen Empfänger greift.
    /// </summary>
    /// <remarks>
    /// Die längste zuerst: greifen „Amazon“ und „Amazon Prime“, ist die genauere gemeint. Ohne
    /// diese Ordnung entschiede die Reihenfolge in der Tabelle, und dieselbe Buchung landete je
    /// nach Anlagezeitpunkt woanders.
    /// </remarks>
    private static CategorizationRule? MatchRule(string payee, List<CategorizationRule> rules)
        => rules
            .Where(r => Categorization.Matches(payee, r.PayeePattern))
            .OrderByDescending(r => Categorization.Normalize(r.PayeePattern).Length)
            .FirstOrDefault();

    /// <summary>
    /// Der letzte tatsächlich erfolgte Import, abgeleitet aus den Buchungen mit Importreferenz.
    /// </summary>
    /// <remarks>
    /// Abgeleitet statt gespeichert: eine eigene Importhistorie wäre eine zweite Wahrheit über
    /// dasselbe Ereignis, und sie liefe der Wirklichkeit hinterher, sobald jemand eine der
    /// importierten Buchungen löscht.
    /// </remarks>
    private async Task<ImportHistoryDto?> LastImportAsync(CancellationToken ct)
    {
        var last = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.ImportReference != null && t.ImportReference.StartsWith("CAMT"))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (last is null)
        {
            return null;
        }

        var day = last.CreatedAt.Date;
        var count = await db.Transactions.CountAsync(
            t => t.ImportReference != null && t.CreatedAt.Date == day, ct);

        return new ImportHistoryDto
        {
            FileName = DemoImportBatch.FileName,
            ImportedOn = DateOnly.FromDateTime(last.CreatedAt),
            AccountName = last.Account?.Name ?? "—",
            RecordCount = count,
        };
    }

    private async Task<List<ImportRowDto>> ClassifyAsync(Batch batch, CancellationToken ct)
    {
        var knownReferences = (await db.Transactions.AsNoTracking()
                .Where(t => t.ImportReference != null)
                .Select(t => t.ImportReference!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var knownBookings = (await db.Transactions.AsNoTracking()
                .Select(t => new { t.BookingDate, t.Payee, t.Amount })
                .ToListAsync(ct))
            .Select(t => BookingKey(t.BookingDate, t.Payee, t.Amount))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rules = await db.CategorizationRules.AsNoTracking()
            .Include(r => r.Category)
            .ToListAsync(ct);

        return
        [
            .. batch.Records.Select((record, index) =>
            {
                var match = MatchRule(record.Payee, rules);
                var state = record switch
                {
                    { Problem: not null } => ImportRowState.Error,
                    { BookingDate: null } or { Amount: null } => ImportRowState.Error,
                    _ when knownReferences.Contains(record.Reference) => ImportRowState.Existing,
                    _ when knownBookings.Contains(
                        BookingKey(record.BookingDate.Value, record.Payee, record.Amount.Value))
                        => ImportRowState.Duplicate,
                    _ => ImportRowState.New,
                };

                return new ImportRowDto
                {
                    Index = index,
                    BookingDate = record.BookingDate,
                    Payee = record.Payee,
                    Amount = record.Amount,
                    State = state,
                    BookingText = record.BookingText,
                    Problem = state != ImportRowState.Error
                        ? null
                        : record.Problem
                          ?? (record.BookingDate is null ? "Datum nicht lesbar" : "Betrag nicht lesbar"),
                    SuggestedCategoryId = state == ImportRowState.Error ? null : match?.CategoryId,
                    CategoryName = state == ImportRowState.Error ? null : match?.Category?.Name,
                    RuleId = state == ImportRowState.Error ? null : match?.Id,

                    // Neue Sätze angehakt, Treffer abgewählt — ein Vorschlag, keine Entscheidung.
                    PreSelected = state == ImportRowState.New,
                };
            }),
        ];
    }

    /// <summary>
    /// Schlüssel für die Duplikatprüfung.
    /// </summary>
    /// <remarks>
    /// Der Betrag wird fest auf zwei Nachkommastellen formatiert. <c>decimal</c> merkt sich seine
    /// Skala: <c>-92.30m</c> aus einem Literal und derselbe Wert aus der Datenbank
    /// (<c>-9230 / 100m</c>) sind zwar gleich, ergeben als Text aber „-92.30“ und „-92.3“.
    /// </remarks>
    private static string BookingKey(DateOnly date, string payee, decimal amount)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{date:yyyy-MM-dd}|{payee.Trim()}|{amount:0.00}");
}
