using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Auswertungen — Abschnitt 10b.
/// </summary>
/// <remarks>
/// <para>Die Aggregation liegt hier und nicht im Client. Der Client zeigt nur; er rechnet nichts
/// nach. Sonst gäbe es zwei Rechnungen für dieselbe Zahl, und die dritte Regel des Handoffs
/// berichtet, wie das ausgeht: zwei Angaben über dieselbe Menge, die verschieden zählen.</para>
/// <para>Der <b>Ausschluss einzelner Buchungen</b> kommt als Parameter herein und geht nirgends
/// in den Bestand: er ist eine Eigenschaft der Auswertung, keine der Buchung.</para>
/// <para>Die Kontosichtbarkeit wirkt vor jeder Aggregation, weil sie als Abfragefilter im
/// <c>DbContext</c> sitzt. Hier steht deshalb <b>kein</b> <c>IgnoreQueryFilters</c> — ein Bericht
/// darf keine Beträge aus nicht freigegebenen Konten enthalten, auch nicht summiert.</para>
/// </remarks>
public sealed class ReportService(
    FinanzAppDbContext db, IClock clock, DocumentService documents, CurrentUser current)
{
    /// <summary>Ab hier gilt eine Kategorie als steigend oder sinkend.</summary>
    private const decimal Threshold = 5m;

    /// <summary>So viele Monate zeigt die Sparkline.</summary>
    private const int SparkMonths = 24;

    /// <summary>So viele Namen nennt die Riser-Zeile.</summary>
    private const int NamedRisers = 3;

    /// <summary>Ein Satz aus der Datenbank, auf das Nötige verkürzt.</summary>
    private sealed record Entry(int Id, DateOnly Day, int CategoryId, decimal Amount);

    public async Task<CostTrendDto> GetCostTrendAsync(
        CostTrendRequest request, CancellationToken ct = default)
    {
        var fenster = await ResolveAsync(request.Period, request.Comparison, ct);
        var excluded = request.ExcludedTransactionIds?.ToHashSet() ?? [];

        // Ein Zug für alles: Zeitraum, Vergleichszeitraum, Zwölfmonatsmittel und Sparkline lesen
        // dieselben Sätze, nur über verschiedene Ausschnitte. Vier Abfragen für vier Ausschnitte
        // derselben Menge wären vier Gelegenheiten, sie verschieden zu filtern.
        var alle = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId != null
                        && t.BookingDate >= fenster.WindowFrom
                        && t.BookingDate <= fenster.WindowTo)
            .Select(t => new Entry(t.Id, t.BookingDate, t.CategoryId!.Value, t.Amount))
            .ToListAsync(ct);

        var gezaehlt = alle.Where(e => !excluded.Contains(e.Id)).ToList();

        // Alle Kategorien, nicht nur die der Ausgabenrichtung. Eine Ausgabe auf einer
        // Einnahmekategorie ist nicht vorgesehen, kommt aber vor — und fiele sonst aus den
        // Zeilen, während sie in der Monatsbasis steht. Zwei Zahlen über dieselbe Menge.
        var namen = await db.Categories.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var budgets = (await db.Budgets.AsNoTracking().Select(b => b.CategoryId).ToListAsync(ct))
            .ToHashSet();

        var zeilen = new List<CostTrendRowDto>();

        foreach (var gruppe in gezaehlt
                     .Where(e => e.Day >= fenster.From && e.Day <= fenster.To)
                     .GroupBy(e => e.CategoryId))
        {
            if (!namen.TryGetValue(gruppe.Key, out var name))
            {
                // Eine Einnahmekategorie an einer Ausgabe: nicht vorgesehen, aber auch kein
                // Grund, den ganzen Bericht zu verweigern.
                continue;
            }

            var betrag = Sum(gruppe);
            var mittel = Average(gezaehlt, gruppe.Key, fenster.AverageFrom, fenster.AverageTo);

            var vergleich = fenster.Comparison == ComparisonBasis.TwelveMonthAverage
                ? mittel * fenster.Months
                : Sum(gezaehlt.Where(e => e.CategoryId == gruppe.Key
                                          && e.Day >= fenster.ComparisonFrom
                                          && e.Day <= fenster.ComparisonTo));

            var prozent = Change(betrag, vergleich);
            var imZeitraum = alle.Count(e => e.CategoryId == gruppe.Key
                                             && e.Day >= fenster.From && e.Day <= fenster.To);

            zeilen.Add(new CostTrendRowDto
            {
                CategoryId = gruppe.Key,
                Name = name,
                Amount = betrag,
                ComparisonAmount = vergleich,
                TwelveMonthAverage = mittel,
                ChangePercent = prozent,
                Status = Status(fenster.HasComparison, betrag, prozent),
                Spark = Spark(gezaehlt, gruppe.Key, fenster.SparkFrom),
                HasBudget = budgets.Contains(gruppe.Key),
                TransactionCount = imZeitraum,
                ExcludedCount = imZeitraum - gruppe.Count(),
                Payees = [],
                Entries = [],
            });
        }

        var sortiert = Sort(zeilen, request.Sort);

        if (request.OpenCategoryId is { } offen)
        {
            sortiert = await WithDrilldownAsync(sortiert, offen, fenster, excluded, ct);
        }

        var summe = sortiert.Sum(z => z.Amount);
        var steigend = sortiert.Where(z => z.Status == CostTrendStatus.Rising).ToList();
        var ohne = await UncategorisedAsync(fenster, excluded, ct);

        return new CostTrendDto
        {
            Range = await RangeAsync(fenster, summe, ct),
            Total = summe,
            ComparisonTotal = sortiert.Sum(z => z.ComparisonAmount),
            ChangePercent = Change(summe, sortiert.Sum(z => z.ComparisonAmount)),
            RisingCount = steigend.Count,
            RisingLine = RisingLine(fenster, steigend),
            ExcludedCount = sortiert.Sum(z => z.ExcludedCount),
            UncategorisedCount = ohne.Count,
            UncategorisedAmount = ohne.Amount,
            Rows = sortiert,
        };
    }

    // ── Zeitraum und Vergleichszeitraum ────────────────────────────────────────────────────

    /// <summary>Alle Grenzen, die ein Bericht braucht.</summary>
    /// <param name="To">
    /// Das Ende des Zeitraums, <b>gekappt auf heute</b>. Ohne die Kappung hielte der
    /// Jahreszeitraum acht gelaufene Monate gegen zwölf des Vorjahres, und jede Kategorie sänke.
    /// </param>
    private sealed record Window(
        PeriodScope Period, ComparisonBasis Comparison,
        DateOnly From, DateOnly To, int Months, string Label,
        DateOnly ComparisonFrom, DateOnly ComparisonTo, string ComparisonLabel,
        DateOnly AverageFrom, DateOnly AverageTo, DateOnly SparkFrom,
        DateOnly WindowFrom, DateOnly WindowTo, bool Truncated, bool HasComparison);

    private async Task<Window> ResolveAsync(
        PeriodScope period, ComparisonBasis comparison, CancellationToken ct)
    {
        var heute = clock.Today;
        var (from, to, months, label) = Periods.Resolve(period, heute);

        // Ein laufender Zeitraum reicht nur bis heute, und der Vergleich muss denselben
        // Ausschnitt treffen — sonst misst er die Länge und nicht die Kosten.
        var truncated = to > heute;
        var bis = truncated ? heute : to;

        // Die gelaufenen Monate, nicht die nominellen. Ein Quartal, von dem sechs Wochen um
        // sind, durch drei zu teilen ergaebe eine Monatsbasis, die es nie gab — und dieselbe
        // Zahl haelt beim Zwoelfmonatsmittel den Vergleich gerade.
        var monate = ((bis.Year - from.Year) * 12) + bis.Month - from.Month + 1;

        var (vonV, bisV, labelV) = comparison switch
        {
            ComparisonBasis.PreviousPeriod
                => (from.AddMonths(-months), bis.AddMonths(-months),
                    Periods.Resolve(period, from.AddDays(-1)).Label),

            ComparisonBasis.PreviousYear
                => (from.AddYears(-1), bis.AddYears(-1),
                    Periods.Resolve(period, from.AddYears(-1)).Label),

            _ => (Periods.FirstOfMonth(from).AddMonths(-12), from.AddDays(-1), "Ø 12 Monate"),
        };

        // Das Mittel nimmt die zwölf Monate vor dem Zeitraum. Ihn selbst mitzurechnen hieße,
        // ihn gegen sich selbst zu halten.
        var mittelVon = Periods.FirstOfMonth(from).AddMonths(-12);
        var mittelBis = Periods.FirstOfMonth(from).AddDays(-1);

        var sparkVon = Periods.FirstOfMonth(bis).AddMonths(-(SparkMonths - 1));

        // Liegt im Vergleichsfenster überhaupt etwas? Gefragt wird nach Buchungen jeder Art:
        // „damals nichts ausgegeben“ und „damals die Anwendung noch nicht benutzt“ sind
        // verschiedene Auskünfte, und nur die zweite verbietet einen Trend.
        var hat = await db.Transactions.AsNoTracking()
            .AnyAsync(t => t.BookingDate >= vonV && t.BookingDate <= bisV, ct);

        return new Window(
            period, comparison, from, bis, monate, label,
            vonV, bisV, labelV,
            mittelVon, mittelBis, sparkVon,
            new[] { sparkVon, vonV, mittelVon }.Min(), bis, truncated, hat);
    }

    private async Task<ReportRangeDto> RangeAsync(Window fenster, decimal summe, CancellationToken ct)
    {
        var konten = await db.Accounts.AsNoTracking().CountAsync(ct);

        var zeitraum = fenster.Truncated
            ? $"{fenster.Label} (bis {GermanFormat.Date(fenster.To)})"
            : fenster.Label;

        var gegen = fenster.Comparison == ComparisonBasis.TwelveMonthAverage || !fenster.Truncated
            ? fenster.ComparisonLabel
            : $"{fenster.ComparisonLabel} (bis {GermanFormat.Date(fenster.ComparisonTo)})";

        return new ReportRangeDto
        {
            Period = fenster.Period,
            Comparison = fenster.Comparison,
            From = fenster.From,
            To = fenster.To,
            Months = fenster.Months,
            PeriodLabel = zeitraum,
            ComparisonLabel = gegen,
            Line = $"{zeitraum} gegen {gegen} · {konten} "
                   + (konten == 1 ? "sichtbares Konto" : "sichtbare Konten"),
            VisibleAccountCount = konten,
            HasComparison = fenster.HasComparison,
            MonthlyExpenseBase = fenster.Months == 0 ? 0m : Math.Round(summe / fenster.Months, 2),
        };
    }

    /// <summary>
    /// Die Ausgaben des Zeitraums mit Kategorie, ohne die ausgeschlossenen.
    /// </summary>
    /// <remarks>
    /// Die eine Zahl, aus der die Monatsbasis wird. Kostentrend und Fixkosten holen sie hier
    /// und rechnen sie nicht jeder für sich — das ist die erste Regel des Handoffs, und sie
    /// fällt sonst genau zwischen diesen beiden Berichten auseinander.
    /// </remarks>
    private async Task<decimal> PeriodExpensesAsync(
        Window fenster, HashSet<int> excluded, CancellationToken ct)
    {
        var saetze = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId != null
                        && t.BookingDate >= fenster.From
                        && t.BookingDate <= fenster.To)
            .Select(t => new { t.Id, t.Amount })
            .ToListAsync(ct);

        return Math.Abs(saetze.Where(t => !excluded.Contains(t.Id)).Sum(t => t.Amount));
    }

    // ── Fixkosten & vertragliche Bindung ───────────────────────────────────────────────────

    /// <summary>
    /// Was fest liegt, gegen das, was frei ist.
    /// </summary>
    /// <remarks>
    /// <para>Die gebundenen Posten kommen aus Darlehen, Verträgen und Policen — aus den
    /// <b>Rohfeldern</b>, nie aus einer Anzeigezeile. Ein Takt ist ein Aufzählungswert und
    /// keine Zeichenkette, aus der man „vierteljährlich“ herauslesen müsste.</para>
    /// <para>Die freie Seite ist ein Restwert: Monatsbasis minus gebunden. Beide Seiten stammen
    /// aus verschiedenen Quellen — die eine aus Verträgen, die andere aus Buchungen — und
    /// müssen darum nicht aufgehen. Wo sie es nicht tun, sagt es die Anmerkung, statt eine
    /// negative Zahl als „frei verfügbar“ auszugeben.</para>
    /// </remarks>
    public async Task<FixedCostsDto> GetFixedCostsAsync(
        FixedCostsRequest request, CancellationToken ct = default)
    {
        var fenster = await ResolveAsync(request.Period, request.Comparison, ct);
        var excluded = request.ExcludedTransactionIds?.ToHashSet() ?? [];

        var basis = await PeriodExpensesAsync(fenster, excluded, ct);
        var range = await RangeAsync(fenster, basis, ct);

        var zeilen = new List<FixedCostRowDto>();

        foreach (var darlehen in await db.Loans.AsNoTracking().OrderBy(l => l.Id).ToListAsync(ct))
        {
            zeilen.Add(new FixedCostRowDto
            {
                Name = darlehen.Name,
                MonthlyAmount = darlehen.Installment,
                Note = $"{darlehen.Lender} · "
                       + $"{GermanFormat.Percent(darlehen.InterestRatePercent, 2)} · nicht kündbar",
                Binding = FixedCostBinding.Fixed,
                NoticeDue = false,
            });
        }

        foreach (var vertrag in await db.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct))
        {
            zeilen.Add(new FixedCostRowDto
            {
                Name = vertrag.Name,
                MonthlyAmount = vertrag.MonthlyAmount,
                Note = $"{AreaLabel(vertrag.Area)} · "
                       + Notice(vertrag.NoticePeriodWeeks, "Woche", "Wochen", vertrag.NoticeToDate),
                Binding = FixedCostBinding.Cancellable,
                NoticeDue = false,
            });
        }

        foreach (var police in await db.Policies.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct))
        {
            zeilen.Add(new FixedCostRowDto
            {
                Name = police.Name,
                MonthlyAmount = PerMonth(police.Premium, police.PremiumInterval),

                // Ein kapitalbildender Beitrag fließt ab wie jeder andere, verschwindet aber
                // nicht: er wird Vermögen. Ihn unter „Kündigungsfrist“ zu führen, hieße ihn
                // als Kostenposten auszugeben.
                Note = police.IsCapitalForming
                    ? "kapitalbildend · zählt als Sparen"
                    : "Absicherung · "
                      + Notice(police.NoticePeriodMonths, "Monat", "Monate", police.EndsOn),
                Binding = police.IsCapitalForming
                    ? FixedCostBinding.Saving
                    : FixedCostBinding.Cancellable,
                NoticeDue = police.NoticeReminderOn is { } tag && tag <= clock.Today,
            });
        }

        // Posten ohne Beitrag stehen in keiner Zeile. Eine Null in einer Kostenliste ist kein
        // Eintrag, sondern eine Lücke im Bestand — sie wird gezählt, nicht gezeigt.
        var mitBetrag = zeilen.Where(z => z.MonthlyAmount != 0m).ToList();

        var fix = decimal.Round(mitBetrag.Sum(z => z.MonthlyAmount), 2);
        var monatsbasis = range.MonthlyExpenseBase;

        return new FixedCostsDto
        {
            Range = range,
            MonthlyFixed = fix,
            MonthlyFree = decimal.Round(monatsbasis - fix, 2),
            FixedSharePercent = monatsbasis <= 0m
                ? 0m
                : decimal.Round(fix / monatsbasis * 100m, 1),
            Note = Bound(fix, monatsbasis),
            Rows = [.. mitBetrag.OrderByDescending(z => z.MonthlyAmount)],
            WithoutAmountCount = zeilen.Count - mitBetrag.Count,
        };
    }

    /// <summary>Ein Beitrag auf den Monat gerechnet, egal in welchem Takt er fällt.</summary>
    private static decimal PerMonth(decimal amount, PremiumInterval interval) => interval switch
    {
        PremiumInterval.Monthly => amount,
        PremiumInterval.Quarterly => decimal.Round(amount / 3m, 2),
        PremiumInterval.HalfYearly => decimal.Round(amount / 6m, 2),
        _ => decimal.Round(amount / 12m, 2),
    };

    /// <summary>
    /// Die Kündigungsfrist im Klartext.
    /// </summary>
    /// <remarks>
    /// Steht keine Frist im Vertrag, heißt das „unbekannt“ und nicht „null Wochen“ — sonst
    /// läse sich eine fehlende Angabe wie eine jederzeitige Kündbarkeit. Ist dann trotzdem ein
    /// Datum hinterlegt, ist es das <em>Vertragsende</em> und keine Frist: „unbekannt zum
    /// 31.12.2027“ wäre ein Satz, der sich selbst widerspricht.
    /// </remarks>
    private static string Notice(int laenge, string einzahl, string mehrzahl, DateOnly? termin)
    {
        if (laenge <= 0)
        {
            return termin is { } ende
                ? $"Kündigungsfrist unbekannt · Ende {GermanFormat.Date(ende)}"
                : "Kündigungsfrist unbekannt";
        }

        var frist = $"Kündigungsfrist {laenge} {(laenge == 1 ? einzahl : mehrzahl)}";

        return termin is { } tag ? $"{frist} zum {GermanFormat.Date(tag)}" : frist;
    }

    /// <summary>
    /// Der Bereich eines Vertrags im Klartext.
    /// </summary>
    /// <remarks>
    /// Bewusst nicht der Ordnername aus der Dokumentablage: der muss auf der Platte stabil
    /// bleiben, diese Zeile darf jederzeit umformuliert werden.
    /// </remarks>
    private static string AreaLabel(DocumentArea area) => area switch
    {
        DocumentArea.Insurance => "Versicherung",
        DocumentArea.Health => "Gesundheit",
        DocumentArea.Housing => "Wohnen",
        DocumentArea.Work => "Arbeit",
        DocumentArea.Finance => "Finanzen",
        _ => "Sonstiges",
    };

    private static string Bound(decimal fix, decimal basis)
    {
        const string kern = "Gebunden heißt: erst nach Ablauf der Kündigungsfrist veränderbar. "
                            + "Die Fristen stammen aus den Verträgen.";

        if (basis <= 0m)
        {
            return kern + " Im Zeitraum sind keine Ausgaben gebucht — es gibt nichts, wogegen "
                   + "sich der Anteil rechnen ließe.";
        }

        return fix <= basis
            ? kern
            : kern + " Die gebundenen Beträge übersteigen die gebuchten Ausgaben des Zeitraums: "
              + "sie stammen aus den Verträgen, nicht aus dem Kontoauszug, und nicht jeder "
              + "Vertrag wurde in diesem Zeitraum abgebucht.";
    }

    // ── Depot: Gewinn und Verlust ──────────────────────────────────────────────────────────

    /// <summary>
    /// Gewinn und Verlust eines Depots — unrealisiert, ohne Steuern und Gebühren.
    /// </summary>
    /// <remarks>
    /// Ohne Depot im Bestand gibt es <c>null</c> und keine leere Hülle mit Nullwerten: „0 €
    /// Gewinn“ ist eine Aussage über ein Depot, und es gibt keines.
    /// </remarks>
    public async Task<PortfolioGainDto?> GetPortfolioGainAsync(
        int? depotId = null, CancellationToken ct = default)
    {
        var depots = await db.Depots.AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new DepotChoiceDto(d.Id, d.Name))
            .ToListAsync(ct);

        if (depots.Count == 0)
        {
            return null;
        }

        var gewaehlt = depots.FirstOrDefault(d => d.Id == depotId) ?? depots[0];

        var positionen = await db.PortfolioPositions.AsNoTracking()
            .Where(p => p.DepotId == gewaehlt.Id)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var zeilen = positionen.Select(p =>
        {
            var wert = decimal.Round(p.Quantity * p.Price, 2);
            var gewinn = decimal.Round(wert - p.CostBasis, 2);

            return new PortfolioGainRowDto
            {
                Name = p.Name,
                Isin = p.Isin,
                Quantity = p.Quantity,
                CostPerUnit = p.Quantity == 0m ? null : decimal.Round(p.CostBasis / p.Quantity, 2),
                Price = p.Price,
                Value = wert,
                Gain = gewinn,
                GainPercent = Change(wert, p.CostBasis),
            };
        }).ToList();

        var einstand = zeilen.Sum(z => z.Value) - zeilen.Sum(z => z.Gain);
        var wertJetzt = zeilen.Sum(z => z.Value);

        return new PortfolioGainDto
        {
            Depots = depots,
            DepotId = gewaehlt.Id,
            DepotName = gewaehlt.Name,
            CostBasis = einstand,
            CurrentValue = wertJetzt,
            Gain = decimal.Round(wertJetzt - einstand, 2),
            GainPercent = Change(wertJetzt, einstand),

            // Der älteste Stichtag, nicht der jüngste: die Summe ist nur so frisch wie ihr
            // ältester Bestandteil.
            PricesAsOf = positionen.Count == 0 ? null : positionen.Min(p => p.PriceAsOf),
            Positions = zeilen,
        };
    }

    // ── Gespeicherte Ansichten ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Ansichten des angemeldeten Benutzers.
    /// </summary>
    /// <remarks>
    /// Nur seine eigenen: ein Ausschluss ist eine persönliche Entscheidung, und die
    /// ausgeschlossenen Buchungen können auf Konten liegen, die ein anderes Mitglied gar nicht
    /// sieht. Eine geteilte Ansicht würde dort anders rechnen als hier.
    /// </remarks>
    public async Task<IReadOnlyList<ReportViewDto>> GetViewsAsync(CancellationToken ct = default)
        => [.. (await db.ReportViews.AsNoTracking()
                .Where(v => v.OwnerUserId == (current.UserId ?? 0))
                .OrderBy(v => v.Id)
                .ToListAsync(ct))
            .Select(Map)];

    public async Task<ReportViewDto> SaveViewAsync(
        SaveReportViewRequest request, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? DescribeView(request)
            : request.Name.Trim();

        if (name.Length > 80)
        {
            name = name[..80];
        }

        var besteht = await db.ReportViews
            .AnyAsync(v => v.OwnerUserId == (current.UserId ?? 0) && v.Name == name, ct);

        if (besteht)
        {
            throw new RuleViolationException($"Eine Ansicht „{name}“ gibt es schon.");
        }

        var ansicht = new ReportView
        {
            OwnerUserId = current.UserId ?? 0,
            Name = name,
            Report = request.Report,
            Period = request.Period,
            Comparison = request.Comparison,
            Sort = request.Sort,
            DepotId = request.DepotId,
            ExcludedTransactionIds = [.. request.ExcludedTransactionIds ?? []],
            CreatedAt = clock.Now,
        };

        db.ReportViews.Add(ansicht);
        await db.SaveChangesAsync(ct);

        return Map(ansicht);
    }

    /// <summary>Löscht eine eigene Ansicht. Eine fremde findet die Abfrage gar nicht erst.</summary>
    public async Task<bool> DeleteViewAsync(int id, CancellationToken ct = default)
    {
        var ansicht = await db.ReportViews
            .FirstOrDefaultAsync(v => v.Id == id && v.OwnerUserId == (current.UserId ?? 0), ct);

        if (ansicht is null)
        {
            return false;
        }

        db.ReportViews.Remove(ansicht);
        await db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Benennt eine Ansicht nach dem, was sie einstellt.
    /// </summary>
    /// <remarks>
    /// „Kostentrend · Monat / Vorjahr“ sagt beim Wiedersehen, was der Chip tut. Eine laufende
    /// Nummer wäre kürzer und nützte niemandem.
    /// </remarks>
    private static string DescribeView(SaveReportViewRequest request)
    {
        var bericht = request.Report switch
        {
            ReportKind.FixedCosts => "Fixkosten",
            ReportKind.PortfolioGainLoss => "Depot G/V",
            ReportKind.DataQuality => "Datenqualität",
            _ => "Kostentrend",
        };

        // Die Datenqualität kennt weder Zeitraum noch Vergleich — sie beide anzuhängen wäre
        // ein Name, der mehr verspricht, als die Ansicht einstellt.
        if (request.Report == ReportKind.DataQuality)
        {
            return bericht;
        }

        if (request.Report == ReportKind.PortfolioGainLoss)
        {
            return bericht;
        }

        var zeitraum = request.Period switch
        {
            PeriodScope.Quarter => "Quartal",
            PeriodScope.Year => "Jahr",
            _ => "Monat",
        };

        var vergleich = request.Comparison switch
        {
            ComparisonBasis.PreviousPeriod => "Vorperiode",
            ComparisonBasis.TwelveMonthAverage => "Ø 12 Monate",
            _ => "Vorjahr",
        };

        return $"{bericht} · {zeitraum} / {vergleich}";
    }

    private static ReportViewDto Map(ReportView v) => new()
    {
        Id = v.Id,
        Name = v.Name,
        Report = v.Report,
        Period = v.Period,
        Comparison = v.Comparison,
        Sort = v.Sort,
        DepotId = v.DepotId,
        ExcludedTransactionIds = v.ExcludedTransactionIds,
    };

    // ── Datenqualität ──────────────────────────────────────────────────────────────────────

    /// <summary>Ab wann ein Kontostand als nicht mehr frisch gilt.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    /// <summary>
    /// Die Lücken, die jede Auswertung darüber verzerren.
    /// </summary>
    /// <remarks>
    /// Jede Zeile nennt eine Folge und ein Ziel. Eine Zahl ohne beides ist ein Vorwurf: sie
    /// sagt, dass etwas fehlt, aber nicht, was es anrichtet und wo man es abstellt.
    /// </remarks>
    public async Task<DataQualityDto> GetDataQualityAsync(CancellationToken ct = default)
    {
        var stichtag = clock.Today.AddDays(-(int)StaleAfter.TotalDays);

        // Umbuchungen tragen zu Recht keine Kategorie — sie sind keine Lücke.
        var ohneKategorie = await db.Transactions
            .CountAsync(t => t.CategoryId == null && t.Kind != TransactionKind.Transfer, ct);

        // „Ohne Datei“ heißt: der gespeicherte Pfad zeigt ins Leere. Das weiß nur, wer gegen
        // die Platte prüft — in der Tabelle steht ein Pfad, und er sieht immer gleich aus.
        var ohneDatei = (await documents.GetPageAsync(ct: ct)).MissingFileCount;

        var belegt = await db.DocumentLinks
            .Select(l => new { l.TargetType, l.TargetId })
            .Distinct()
            .ToListAsync(ct);

        var vertraege = belegt.Where(l => l.TargetType == LinkTargetType.Contract)
            .Select(l => l.TargetId).ToHashSet();
        var policen = belegt.Where(l => l.TargetType == LinkTargetType.Policy)
            .Select(l => l.TargetId).ToHashSet();

        var vertraegeOhneBeleg = await db.Contracts
            .CountAsync(c => !vertraege.Contains(c.Id), ct);
        var policenOhneBeleg = await db.Policies
            .CountAsync(p => !policen.Contains(p.Id), ct);

        var policenOhneBeitrag = await db.Policies.CountAsync(p => p.Premium == 0m, ct);

        var alteStaende = await db.Accounts.CountAsync(a => a.BalanceAsOf < stichtag, ct);

        // Die Beschriftung richtet sich nach der Zahl daneben. „1 Dokumente ohne Datei“ ist
        // ein Zahlwort, das seinem eigenen Substantiv widerspricht.
        static DataQualityRowDto Gap(
            int anzahl, string einzahl, string mehrzahl, string folge, string tat, string ziel)
            => new(anzahl, anzahl == 1 ? einzahl : mehrzahl, folge, tat, ziel);

        List<DataQualityRowDto> zeilen =
        [
            Gap(ohneKategorie, "Buchung ohne Kategorie", "Buchungen ohne Kategorie",
                "fehlt in jeder Kategorieauswertung — gezählt über den ganzen Bestand, "
                + "nicht nur im gewählten Zeitraum",
                "zuordnen", "/konten?offen=1"),

            Gap(ohneDatei, "Dokument ohne Datei", "Dokumente ohne Datei",
                "der Pfad zeigt ins Leere", "prüfen", "/dokumente"),

            Gap(vertraegeOhneBeleg, "Vertrag ohne Beleg", "Verträge ohne Beleg",
                "die Kündigungsfrist ist nicht belegt", "ergänzen", "/wohnen"),

            Gap(policenOhneBeleg, "Police ohne Beleg", "Policen ohne Beleg",
                "die Kündigungsfrist ist nicht belegt", "ergänzen", "/absicherung"),

            // Eigene Zeile, nicht die des Fixkostenberichts: dort steht, was fehlt; hier steht,
            // dass es fehlt und wo man es einträgt.
            Gap(policenOhneBeitrag, "Police ohne Beitrag", "Policen ohne Beitrag",
                "fehlt in den Fixkosten", "erfassen", "/absicherung"),

            Gap(alteStaende, "Konto ohne frischen Stand", "Konten ohne frischen Stand",
                $"älter als {StaleAfter.TotalDays:0} Tage", "aktualisieren", "/konten"),
        ];

        var offen = zeilen.Sum(z => z.Count);

        return new DataQualityDto
        {
            OpenCount = offen,
            Headline = offen == 0 ? "vollständig" : $"{offen} {(offen == 1 ? "Lücke" : "Lücken")}",
            Line = offen == 0
                ? "Alle Auswertungen rechnen auf vollständigen Daten."
                : "Solange Lücken offen sind, bleiben die Summen darüber unvollständig.",

            // Volle Zeilen zuerst; die erledigten stehen hinten und bleiben trotzdem sichtbar.
            // Eine verschwundene Zeile sähe aus, als wäre nie danach gefragt worden.
            Rows = [.. zeilen.OrderByDescending(z => z.Count)],
        };
    }

    // ── Rechnen ────────────────────────────────────────────────────────────────────────────

    private static decimal Sum(IEnumerable<Entry> entries) => Math.Abs(entries.Sum(e => e.Amount));

    /// <summary>Monatsmittel einer Kategorie über ein Fenster.</summary>
    private static decimal Average(List<Entry> entries, int categoryId, DateOnly from, DateOnly to)
    {
        var monate = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        if (monate <= 0)
        {
            return 0m;
        }

        return Math.Round(
            Sum(entries.Where(e => e.CategoryId == categoryId && e.Day >= from && e.Day <= to))
            / monate, 2);
    }

    /// <summary>
    /// Prozentuale Änderung, oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Ohne Vergleichswert gibt es keinen Prozentsatz. „+100 %“ wäre eine Zahl, die niemand
    /// gerechnet hat, und „0 %“ eine Behauptung von Gleichstand.
    /// </remarks>
    private static decimal? Change(decimal now, decimal before)
        => before == 0m ? null : Math.Round((now / before - 1m) * 100m, 1);

    private static CostTrendStatus Status(bool hasComparison, decimal now, decimal? change)
    {
        if (!hasComparison)
        {
            return CostTrendStatus.Unknown;
        }

        if (change is null)
        {
            // Das Vergleichsfenster hat Daten, diese Kategorie darin aber keine: sie ist neu.
            return now > 0m ? CostTrendStatus.Rising : CostTrendStatus.Stable;
        }

        return change >= Threshold ? CostTrendStatus.Rising
            : change <= -Threshold ? CostTrendStatus.Falling
            : CostTrendStatus.Stable;
    }

    /// <summary>24 Monatssummen, älteste zuerst — auch die leeren, sonst verrutscht die Kurve.</summary>
    private static List<decimal> Spark(List<Entry> entries, int categoryId, DateOnly from)
    {
        var werte = new List<decimal>(SparkMonths);

        for (var i = 0; i < SparkMonths; i++)
        {
            var monat = from.AddMonths(i);
            werte.Add(Sum(entries.Where(e => e.CategoryId == categoryId
                                             && e.Day.Year == monat.Year
                                             && e.Day.Month == monat.Month)));
        }

        return werte;
    }

    private static List<CostTrendRowDto> Sort(List<CostTrendRowDto> rows, CostTrendSort sort)
        => sort switch
        {
            // Ohne Vergleich gibt es keinen Anstieg — solche Zeilen stehen hinten, nicht oben.
            CostTrendSort.Increase =>
            [
                .. rows.OrderByDescending(r => r.ChangePercent.HasValue)
                    .ThenByDescending(r => r.ChangePercent ?? 0m)
                    .ThenByDescending(r => r.Amount),
            ],

            CostTrendSort.Amount => [.. rows.OrderByDescending(r => r.Amount)],

            _ => [.. rows.OrderBy(r => r.Name, StringComparer.CurrentCulture)],
        };

    private static string RisingLine(Window fenster, List<CostTrendRowDto> rising)
    {
        if (!fenster.HasComparison)
        {
            return $"Für {fenster.ComparisonLabel} liegen keine Buchungen vor — "
                   + "ohne Vergleichszeitraum kein Trend.";
        }

        if (rising.Count == 0)
        {
            return $"Keine Kategorie steigt um mehr als {Threshold:0} %.";
        }

        // Eine neue Kategorie hat keinen Prozentsatz und steht trotzdem vorn: sie ist der
        // stärkste denkbare Anstieg.
        var namen = rising
            .OrderByDescending(r => r.ChangePercent ?? decimal.MaxValue)
            .Take(NamedRisers)
            .Select(r => r.Name);

        return $"{rising.Count} "
               + (rising.Count == 1 ? "Kategorie steigt" : "Kategorien steigen")
               + $" um mehr als {Threshold:0} % — {string.Join(", ", namen)}";
    }

    // ── Drilldown ──────────────────────────────────────────────────────────────────────────

    private async Task<List<CostTrendRowDto>> WithDrilldownAsync(
        List<CostTrendRowDto> rows, int categoryId, Window fenster,
        HashSet<int> excluded, CancellationToken ct)
    {
        var index = rows.FindIndex(r => r.CategoryId == categoryId);
        if (index < 0)
        {
            return rows;
        }

        var saetze = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId == categoryId
                        && t.BookingDate >= fenster.From
                        && t.BookingDate <= fenster.To)
            .OrderByDescending(t => t.BookingDate).ThenBy(t => t.Id)
            .Select(t => new CostTrendEntryDto(
                t.Id, t.BookingDate, t.Payee, t.Amount, t.Account!.Name, false))
            .ToListAsync(ct);

        var mitFlagge = saetze
            .Select(e => e with { Excluded = excluded.Contains(e.Id) })
            .ToList();

        var empfaenger = mitFlagge
            .Where(e => !e.Excluded)
            .GroupBy(e => e.Payee, StringComparer.CurrentCulture)
            .Select(g => new CostTrendPayeeDto(g.Key, g.Count(), Math.Abs(g.Sum(e => e.Amount))))
            .OrderByDescending(p => p.Amount)
            .ToList();

        rows[index] = rows[index] with { Payees = empfaenger, Entries = mitFlagge };

        return rows;
    }

    private async Task<(int Count, decimal Amount)> UncategorisedAsync(
        Window fenster, HashSet<int> excluded, CancellationToken ct)
    {
        var offen = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Expense
                        && t.CategoryId == null
                        && t.BookingDate >= fenster.From
                        && t.BookingDate <= fenster.To)
            .Select(t => new { t.Id, t.Amount })
            .ToListAsync(ct);

        var zaehlt = offen.Where(t => !excluded.Contains(t.Id)).ToList();

        return (zaehlt.Count, Math.Abs(zaehlt.Sum(t => t.Amount)));
    }
}
