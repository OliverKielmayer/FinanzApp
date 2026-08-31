using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Vorsorge- und Absicherungsverträge. Ein Dienst für beide Bereiche — sie unterscheiden sich
/// in der Kopfzahl, nicht im Modell.
/// </summary>
/// <remarks>
/// <para>Die Kündigungsfrist wird nicht eingegeben, sondern <em>abgeleitet</em>: Vertragsende
/// minus Frist. Ein von Hand gepflegtes Datum liefe irgendwann der Verlängerung hinterher.
/// Beiträge sind Verweise auf Buchungen, keine eigenen Geldsätze.</para>
/// <para>Was ins Vermögen zählt, entscheidet allein <c>Policy.AssetValue</c> — hier wird nie
/// selbst gerechnet, ob ein Vertrag einen Wert hat.</para>
/// </remarks>
public sealed class PolicyService(FinanzAppDbContext db, DocumentService documents, IClock clock)
{
    /// <summary>Wie früh eine Frist als „läuft“ gilt.</summary>
    private const int NoticeWindowDays = 90;

    /// <summary>Einer der beiden Bereiche, mit seiner Kopfzahl.</summary>
    public async Task<PolicyOverviewDto> GetOverviewAsync(
        bool capitalForming, CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming == capitalForming)
            .ToListAsync(ct);

        // Vorsorge nach Wert, Absicherung nach Beitrag — beides absteigend, das Gewichtige oben.
        rows = capitalForming
            ? [.. rows.OrderByDescending(p => p.AssetValue ?? 0m).ThenBy(p => p.Name)]
            : [.. rows.OrderByDescending(p => p.AnnualPremium).ThenBy(p => p.Name)];

        return new PolicyOverviewDto
        {
            CapitalForming = capitalForming,
            Title = capitalForming ? "Vorsorge & Kapital" : "Absicherung",

            // Eine Absicherung hat keinen Wert. Dort eine Summe zu zeigen, wäre falsch.
            TotalValue = capitalForming ? rows.Sum(p => p.AssetValue ?? 0m) : null,
            OldestValuationDate = capitalForming
                ? rows.Select(p => p.ValuationDate).Where(d => d is not null).Min()
                : null,
            TotalAnnualPremium = capitalForming ? null : rows.Sum(p => p.AnnualPremium),
            Items = [.. rows.Select(ToListItem)],
        };
    }

    public async Task<PolicyDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var policy = await db.Policies.AsNoTracking()
            .Include(p => p.Account)
            .Include(p => p.Reports)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (policy is null)
        {
            return null;
        }

        return new PolicyDetailDto
        {
            Id = policy.Id,
            Name = policy.Name,
            Provider = policy.Provider,
            Kind = policy.Kind,
            KindLabel = KindLabel(policy.Kind),
            IsCapitalForming = policy.IsCapitalForming,
            PolicyNumber = policy.PolicyNumber,
            Premium = policy.Premium,
            PremiumInterval = policy.PremiumInterval,
            MonthlyPremium = Math.Round(policy.MonthlyPremium, 2, MidpointRounding.AwayFromZero),
            AnnualPremium = Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero),
            StartsOn = policy.StartsOn,
            EndsOn = policy.EndsOn,
            NoticePeriodMonths = policy.NoticePeriodMonths,
            NoticeDeadline = policy.NoticeDeadline,
            DaysUntilNotice = DaysUntilNotice(policy),
            NoticeReminderOn = policy.NoticeReminderOn,
            DaysUntilReminder = DaysUntilReminder(policy),
            NoticeIsDue = NoticeIsDue(policy),
            CurrentValue = policy.AssetValue,
            ValuationDate = policy.IsCapitalForming ? policy.ValuationDate : null,
            MaturityValue = policy.IsCapitalForming ? policy.MaturityValue : null,
            MaturesOn = policy.IsCapitalForming ? policy.MaturesOn : null,
            SumInsured = policy.IsCapitalForming ? null : policy.SumInsured,
            Deductible = policy.IsCapitalForming ? null : policy.Deductible,
            AccountName = policy.Account?.Name,
            Notes = policy.Notes,
            Documents = await documents.GetForTargetAsync(LinkTargetType.Policy, policy.Id, ct),
            Payments = await LoadPaymentsAsync(policy, ct),
            ValueParts = ValueParts(policy),
            Reports = await ReportsAsync(policy, ct),
        };
    }

    /// <summary>
    /// Die gemeldeten Stände samt dem, was im Beleg dazu gelesen wurde.
    /// </summary>
    /// <remarks>
    /// <para>Die ausgelesenen Werte kommen mit: erst neben dem Stand lässt sich sehen, warum am
    /// Vertrag steht, was dort steht — und ob der Bericht der richtige war. Ohne sie bliebe ein
    /// falsch gelesener Stichtag unauffindbar.</para>
    /// <para>Beträge gehen als Zahl hinaus, alles andere als Text. Ein hier formatierter
    /// Euro-Betrag ließe sich von „Beträge verbergen“ nicht mehr maskieren.</para>
    /// </remarks>
    private async Task<List<PolicyReportDto>> ReportsAsync(Policy policy, CancellationToken ct)
    {
        var belegIds = policy.Reports
            .Where(r => r.DocumentId is not null)
            .Select(r => r.DocumentId!.Value)
            .Distinct()
            .ToList();

        var belege = belegIds.Count == 0
            ? []
            : await db.Documents.AsNoTracking()
                .Where(d => belegIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);

        var gelesen = belegIds.Count == 0
            ? []
            : (await db.DocumentExtractions.AsNoTracking()
                .Where(e => belegIds.Contains(e.DocumentId))
                .OrderBy(e => e.Id)
                .ToListAsync(ct))
                .GroupBy(e => e.DocumentId)
                .ToDictionary(g => g.Key, g => g.ToList());

        return
        [
            .. policy.Reports
                .OrderBy(r => r.AsOf)
                .Select(r =>
                {
                    var beleg = r.DocumentId is { } id ? belege.GetValueOrDefault(id) : null;

                    return new PolicyReportDto
                    {
                        Id = r.Id,
                        AsOf = r.AsOf,
                        Value = r.Value,
                        BaseValue = r.BaseValue,
                        AccruedBonus = r.AccruedBonus,
                        Source = r.Source,
                        DocumentId = r.DocumentId,
                        DocumentTitle = beleg?.Title,
                        Values = beleg is null
                            ? []
                            : Values(beleg, gelesen.GetValueOrDefault(beleg.Id, [])),
                    };
                }),
        ];
    }

    /// <summary>Die gelesenen Zeilen eines Belegs, in der Sprache seiner Dokumentart.</summary>
    /// <remarks>
    /// Ob eine Zeile ein Betrag ist, entscheidet die Feldregel der Dokumentart und nicht das
    /// Aussehen des Textes: eine Seitenzahl, die sich als Zahl lesen ließe, wäre sonst plötzlich
    /// ein maskierbarer Euro-Betrag.
    /// </remarks>
    private static List<PolicyReportValueDto> Values(
        Document beleg, IReadOnlyList<DocumentExtraction> zeilen)
    {
        var art = DocumentKindLibrary.All.FirstOrDefault(k => k.Key == beleg.ScanKind);

        return
        [
            .. zeilen.Select(zeile =>
            {
                var regel = art?.Fields.FirstOrDefault(f => f.Key == zeile.FieldKey)
                            ?? art?.Repeat?.Fields.FirstOrDefault(f => f.Key == zeile.FieldKey);

                var geld = regel?.Kind == DocumentValueKind.Money;
                var zahl = regel is null ? null : DocumentFieldExtractor.Read(regel, zeile.Value)?.Number;

                return new PolicyReportValueDto
                {
                    Label = zeile.Label,
                    Display = geld && zahl is not null ? string.Empty : zeile.Value,
                    Number = zahl,
                    IsMoney = geld && zahl is not null,
                    SourcePage = zeile.SourcePage,
                    Confidence = zeile.Confidence,
                };
            }),
        ];
    }

    /// <summary>
    /// Woraus der erreichte Wert besteht — v5-Handoff, Abschnitt 19.5.
    /// </summary>
    /// <remarks>
    /// <para>Die Bezeichnungen folgen der Vertragsart: Rückkaufswert, Deckungskapital oder
    /// Sparguthaben. Ein Bausparvertrag hat keinen Rückkaufswert, und ihn so zu nennen machte
    /// aus einer richtigen Zahl eine falsche Aussage.</para>
    /// <para>Führt ein Vertrag nur einen Bestandteil, kommt <b>eine</b> Zeile zurück und keine
    /// Summe: eine Summe aus einem Summanden ist keine.</para>
    /// </remarks>
    private static List<PolicyValuePartDto> ValueParts(Policy policy)
    {
        if (!policy.IsCapitalForming)
        {
            return [];
        }

        // Die Herkunft steht am Bericht, aus dem der Wert stammt: „erfasst 31.07.2025“ ist eine
        // andere Aussage als „Statusreport 31.07.2025“, und nur eine von beiden stimmt. Ohne
        // Bericht bleibt die Bezeichnung der Vertragsart — dann gibt es nichts Genaueres.
        var neuester = policy.Reports.OrderByDescending(r => r.AsOf).FirstOrDefault();

        var herkunft = neuester is not null
            ? $"{neuester.Source} {GermanFormat.Date(neuester.AsOf)}"
            : policy.ValuationDate is { } stichtag
                ? $"{PolicyValueNaming.ReportLabel(policy.Kind)} {GermanFormat.Date(stichtag)}"
                : "von Hand erfasst";

        var teile = new List<PolicyValuePartDto>();

        if (policy.BaseValue is { } basis)
        {
            teile.Add(new PolicyValuePartDto
            {
                Label = PolicyValueNaming.BaseValueLabel(policy.Kind),
                Amount = basis,
                Origin = herkunft,
            });
        }

        if (policy.AccruedBonus is { } ueberschuss && PolicyValueNaming.HasAccruedBonus(policy.Kind))
        {
            teile.Add(new PolicyValuePartDto
            {
                Label = "Ansammlungsguthaben",
                Amount = ueberschuss,
                Origin = herkunft,
            });
        }

        return teile;
    }

    /// <summary>
    /// Was die kapitalbildenden Verträge zum Bruttovermögen beitragen, plus den ältesten Stichtag.
    /// </summary>
    /// <remarks>
    /// Der Stichtag gehört zwingend dazu: ein Jahresstand ist kein Tageskurs, und die Kachel im
    /// Vermögen muss das sagen dürfen.
    /// </remarks>
    public async Task<(decimal Total, DateOnly? AsOf, string Provider)> GetCapitalTotalAsync(
        CancellationToken ct = default)
    {
        var rows = await db.Policies.AsNoTracking()
            .Where(p => p.IsCapitalForming)
            .Select(p => new { p.CurrentValue, p.ValuationDate, p.Provider })
            .ToListAsync(ct);

        return (
            rows.Sum(r => r.CurrentValue ?? 0m),
            rows.Select(r => r.ValuationDate).Where(d => d is not null).Min(),
            rows.Count == 1 ? rows[0].Provider : $"{rows.Count} Verträge");
    }

    /// <summary>
    /// Summe der Monatsbeiträge <b>der Absicherung</b> — für die Kostenrechnung der Immobilie und
    /// das Sparpotential.
    /// </summary>
    /// <remarks>
    /// Vorsorgebeiträge bleiben bewusst draußen: sie sind Sparen, keine Ausgabe (Handoff v4,
    /// Abschnitt 10). Wer sie mitzählte, würde die eigene Sparquote als Kosten ausweisen.
    /// </remarks>
    public async Task<decimal> GetMonthlyPremiumTotalAsync(CancellationToken ct = default)
        => (await db.Policies.AsNoTracking().Where(p => !p.IsCapitalForming).ToListAsync(ct))
            .Sum(p => p.MonthlyPremium);

    private int? DaysUntilNotice(Policy policy)
        => policy.NoticeDeadline is { } deadline
            ? deadline.DayNumber - clock.Today.DayNumber
            : null;

    /// <summary>
    /// Ob die Frist jetzt auf den Tisch gehört — entweder weil der Termin nah ist, oder weil
    /// die gesetzte Erinnerung erreicht wurde. Verstrichene Termine zählen nicht mehr.
    /// </summary>
    private bool NoticeIsDue(Policy policy)
    {
        if (DaysUntilNotice(policy) is not { } days || days < 0)
        {
            return false;
        }

        // Die Erinnerung zählt schon, wenn sie in Sicht ist — nicht erst am Tag selbst. Ein
        // Vergleich braucht Vorlauf, und genau dafür ist sie da.
        return days <= NoticeWindowDays
               || (DaysUntilReminder(policy) is { } remind && remind <= NoticeWindowDays);
    }

    /// <summary>Tage bis zur gesetzten Erinnerung. Negativ heißt: sie ist bereits gefallen.</summary>
    private int? DaysUntilReminder(Policy policy)
        => policy.NoticeReminderOn is { } remind
            ? remind.DayNumber - clock.Today.DayNumber
            : null;

    /// <summary>
    /// Beitragszahlungen — zugeordnet <b>ausschließlich über die Vertragsnummer</b>.
    /// </summary>
    /// <remarks>
    /// <para>Vorher lief die Suche über den Namen des Anbieters. Bei vier Verträgen desselben
    /// Hauses hängt damit jede Beitragsbuchung an jedem Vertrag, und im Beispiel stand eine
    /// Buchung über 212 € unter einem Vertrag, der 42 € im Monat kostet. Eine Zuordnung, die
    /// nach Anbieter geht, ist bei mehreren Verträgen keine Zuordnung.</para>
    /// <para>Gesucht wird in Verwendungszweck, Buchungstext und Empfänger — die Nummer steht je
    /// nach Bank an verschiedenen Stellen. Verglichen wird <em>normalisiert</em>: Groß- und
    /// Kleinschreibung, Leerzeichen, Punkte, Schräg- und Bindestriche fallen weg, weil
    /// „01511104-01“ und „01511104 01“ dieselbe Nummer sind.</para>
    /// <para>Die Zahlungen bleiben Buchungen und werden nicht ein zweites Mal geführt.</para>
    /// </remarks>
    private async Task<IReadOnlyList<LinkedPaymentDto>> LoadPaymentsAsync(
        Policy policy, CancellationToken ct)
    {
        if (Normalise(policy.PolicyNumber) is not { Length: >= 4 } nummer)
        {
            return [];
        }

        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => t.Kind == TransactionKind.Expense)
            .OrderByDescending(t => t.BookingDate)
            .Take(400)
            .ToListAsync(ct);

        return
        [
            .. rows
                .Select(t => new { Buchung = t, Fund = Match(t, nummer) })
                .Where(x => x.Fund is not null)
                .Take(12)
                .Select(x => new LinkedPaymentDto
                {
                    TransactionId = x.Buchung.Id,
                    BookingDate = x.Buchung.BookingDate,
                    Amount = Math.Abs(x.Buchung.Amount),
                    Payee = x.Buchung.Payee,
                    AccountName = x.Buchung.Account?.Name ?? string.Empty,
                    MatchReason = $"Vertragsnummer {policy.PolicyNumber} {x.Fund}",
                    Reference = x.Buchung.Purpose,
                }),
        ];
    }

    /// <summary>Wo die Nummer steht — oder <c>null</c>, wenn sie nirgends steht.</summary>
    private static string? Match(Transaction t, string nummer)
    {
        if (Normalise(t.Purpose)?.Contains(nummer, StringComparison.Ordinal) == true)
        {
            return "im Verwendungszweck";
        }

        if (Normalise(t.BookingText)?.Contains(nummer, StringComparison.Ordinal) == true)
        {
            return "im Buchungstext";
        }

        return Normalise(t.Payee)?.Contains(nummer, StringComparison.Ordinal) == true
            ? "im Empfänger"
            : null;
    }

    /// <summary>
    /// Eine Nummer ohne ihre Schreibweise.
    /// </summary>
    /// <remarks>
    /// „01511104-01“, „01511104 01“ und „01511104/01“ sind dieselbe Vertragsnummer. Ohne diesen
    /// Schritt fände die Zuordnung nur die eine Schreibweise, die die Bank gerade verwendet.
    /// </remarks>
    private static string? Normalise(string? text)
        => text is not { Length: > 0 }
            ? null
            : new string([.. text.Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    private PolicyListItemDto ToListItem(Policy policy) => new()
    {
        Id = policy.Id,
        Name = policy.Name,
        Provider = policy.Provider,
        Kind = policy.Kind,
        IsCapitalForming = policy.IsCapitalForming,
        Meta = Meta(policy),
        Premium = policy.Premium,
        PremiumInterval = policy.PremiumInterval,
        AnnualPremium = Math.Round(policy.AnnualPremium, 2, MidpointRounding.AwayFromZero),
        Value = policy.AssetValue,
        ValuationDate = policy.IsCapitalForming ? policy.ValuationDate : null,
        EndsOn = policy.EndsOn,
        NoticeDeadline = policy.NoticeDeadline,
        DaysUntilNotice = DaysUntilNotice(policy),
        DaysUntilReminder = DaysUntilReminder(policy),
        NoticeIsDue = NoticeIsDue(policy),
    };

    /// <summary>Zweite Zeile der Liste: Vertragsart, dann das Kennzeichnende.</summary>
    /// <summary>
    /// Der Untertitel kommt aus dem gemeinsamen Builder.
    /// </summary>
    /// <remarks>
    /// Er nannte früher nur Vertragsart und Notiz. Die Bestandsliste zeigte für dasselbe
    /// Objekt mehr, die Suche zeigte weniger — drei Antworten auf dieselbe Frage. Jetzt gibt
    /// es eine, und sie steht in <see cref="HoldingMeta"/>.
    /// </remarks>
    private static string Meta(Policy policy) => HoldingMeta.ForPolicy(policy);

    /// <summary>
    /// Die Vertragsart im Klartext.
    /// </summary>
    /// <remarks>
    /// Die Tabelle steht in <see cref="HoldingMeta"/>, weil sie dort ohnehin in jede Metazeile
    /// eingeht. Zwei Tabellen liefen auseinander — meine zweite Kopie kannte Rechtsschutz und
    /// Krankenversicherung nicht, und die Suche zeigte darum „Vertrag · Vertrag“.
    /// </remarks>
    public static string KindLabel(PolicyKind kind) => HoldingMeta.KindLabel(kind);

    /// <summary>
    /// Hält einen gemeldeten Stand in der Berichtsreihe fest.
    /// </summary>
    /// <remarks>
    /// <para>Nur aus dieser Reihe entsteht später ein Verlauf. Ohne sie bliebe am Vertrag ein
    /// einziger Wert, und jede gezeichnete Kurve wäre erfunden — genau das ist beim ersten Bau
    /// passiert.</para>
    /// <para>Ein Stichtag, ein Stand: kommt derselbe Tag ein zweites Mal — korrigierter Bericht,
    /// nachgetragene Zahl —, wird der vorhandene überschrieben statt ein zweiter Punkt neben den
    /// ersten gelegt.</para>
    /// <para>Statisch, weil zwei Wege Stände melden: der eingelesene Bericht und die Maske. Beide
    /// dürfen das nicht jeder auf seine Art tun.</para>
    /// </remarks>
    public static async Task RecordReportAsync(
        FinanzAppDbContext db,
        IClock clock,
        int policyId,
        DateOnly asOf,
        decimal value,
        string source,
        CancellationToken ct,
        decimal? baseValue = null,
        decimal? accruedBonus = null,
        int? documentId = null)
    {
        var vorhanden = await db.PolicyReports
            .FirstOrDefaultAsync(r => r.PolicyId == policyId && r.AsOf == asOf, ct);

        if (vorhanden is null)
        {
            db.PolicyReports.Add(new PolicyReport
            {
                PolicyId = policyId,
                AsOf = asOf,
                Value = value,
                BaseValue = baseValue,
                AccruedBonus = accruedBonus,
                Source = source,
                DocumentId = documentId,
                CreatedAt = clock.Now,
            });
        }
        else
        {
            vorhanden.Value = value;
            vorhanden.BaseValue = baseValue;
            vorhanden.AccruedBonus = accruedBonus;
            vorhanden.Source = source;
            vorhanden.DocumentId = documentId ?? vorhanden.DocumentId;
        }

        await DeriveAsync(db, policyId, ct);
    }

    /// <summary>
    /// Entfernt einen gemeldeten Stand.
    /// </summary>
    /// <remarks>
    /// Ein Bericht, der nicht hierher gehörte — falscher Vertrag, falsch gelesener Stichtag —,
    /// verzerrt den Verlauf dauerhaft. Nach dem Entfernen zählt wieder der neueste verbliebene
    /// Stand; war es der letzte, hat der Vertrag keinen erreichten Wert mehr. Beides ist die
    /// Wahrheit über die verbliebenen Belege und keine Nebenwirkung.
    /// </remarks>
    public async Task<bool> DeleteReportAsync(int reportId, CancellationToken ct = default)
    {
        var bericht = await db.PolicyReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);

        if (bericht is null)
        {
            return false;
        }

        var vertragId = bericht.PolicyId;
        db.PolicyReports.Remove(bericht);
        await db.SaveChangesAsync(ct);

        await DeriveAsync(db, vertragId, ct);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Schreibt den erreichten Wert des Vertrags aus seinem neuesten Bericht.
    /// </summary>
    /// <remarks>
    /// <para>Der Kopfwert ist keine eigene Größe, sondern der jüngste gemeldete Stand — sonst
    /// stünde nach dem Entfernen eines Berichts weiter dessen Zahl da, und niemand könnte sagen,
    /// woher sie kommt. Mit ihm wandern Stichtag und Bestandteile.</para>
    /// <para><b>Nach Stichtag, nicht nach Einlesezeitpunkt.</b> Wer einen alten Bericht
    /// nachträgt, ergänzt die Reihe hinten und setzt den aktuellen Wert nicht zurück.</para>
    /// <para>Bleibt kein Bericht übrig, bleibt kein Wert übrig. Ein Vertrag ohne gemeldeten Stand
    /// zählt in keiner Vermögenssumme mit — eine Zahl ohne Beleg wäre dort schlimmer als eine
    /// Lücke.</para>
    /// </remarks>
    private static async Task DeriveAsync(FinanzAppDbContext db, int policyId, CancellationToken ct)
    {
        var vertrag = await db.Policies.FirstOrDefaultAsync(p => p.Id == policyId, ct);

        if (vertrag is null || !vertrag.IsCapitalForming)
        {
            return;
        }

        // Erst laden, dann aus dem Änderungsverfolger lesen: ein soeben hinzugefügter Bericht
        // steht noch nicht in der Datenbank und käme in einer Abfrage nicht zurück — der Vertrag
        // bekäme dann den vorletzten Stand. Was gelöscht ist, zählt umgekehrt nicht mehr mit.
        await db.PolicyReports.Where(r => r.PolicyId == policyId).LoadAsync(ct);

        var neuester = db.PolicyReports.Local
            .Where(r => r.PolicyId == policyId && db.Entry(r).State != EntityState.Deleted)
            .OrderByDescending(r => r.AsOf)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        vertrag.CurrentValue = neuester?.Value;
        vertrag.ValuationDate = neuester?.AsOf;
        vertrag.BaseValue = neuester?.BaseValue;
        vertrag.AccruedBonus = neuester?.AccruedBonus;
    }
}
