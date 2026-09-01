using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Beteiligung an Immobilien — <b>die eine Stelle, die sie rechnet</b>.
/// </summary>
/// <remarks>
/// <para>Handoff „Gemeinsame Immobilie“, 9.6: „Ein Endpoint liefert das Beteiligungsaggregat.
/// Kein Screen rechnet selbst.“ Der Grund steht daneben — im Entwurf sind dieselben Zahlen
/// siebenmal auseinandergelaufen, weil jede Fläche sie noch einmal gerechnet hat.</para>
/// <para>Genau das ist beim Bauen passiert: der Objektschirm rechnete den Ausgleich aus
/// Eigenkapital <em>und</em> Einlagen, die Bilanz nur aus dem Eigenkapital. 20.750 € gegen
/// 20.000 € für dieselbe Forderung, im selben Programm. Seitdem liegt die Rechnung hier, und
/// beide fragen.</para>
/// <para><b>Der Ausgleich ist abgeleitet:</b> eingebracht minus Eigentumsanteil an der Summe des
/// Eingebrachten. Eingebracht heißt Eigenkapital beim Kauf plus gebuchte Einlagen. Er steht
/// nirgends in der Datenbank und darf nirgends von Hand gesetzt werden.</para>
/// </remarks>
public sealed class ParticipationService(FinanzAppDbContext db, CurrentUser user)
{
    /// <summary>Die Beteiligung an einem Objekt — <c>null</c>, wenn keine Anteile gepflegt sind.</summary>
    public async Task<PropertyParticipationDto?> ForPropertyAsync(
        int propertyId, CancellationToken ct = default)
    {
        var objekt = await db.Properties.AsNoTracking()
            .Include(p => p.Loan)
            .Include(p => p.Shares).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

        if (objekt is null || objekt.Shares.Count == 0)
        {
            return null;
        }

        var einlagen = await DepositsAsync([propertyId], ct);
        var gerechnet = Calculate(objekt, einlagen);
        var meiner = gerechnet.Shares.FirstOrDefault(z => z.Share.UserId == user.UserId);

        return new PropertyParticipationDto
        {
            Participants =
            [
                .. gerechnet.Shares.Select(z => new ParticipantDto
                {
                    UserId = z.Share.UserId,
                    Name = z.Share.User?.Name ?? "Unbekannt",
                    Percent = z.Share.Percent,
                    Equity = z.Share.Equity,
                    Deposits = z.Deposits,
                    Settlement = z.Settlement,
                    IsSelf = z.Share.UserId == user.UserId,
                }),
            ],
            MarketValue = objekt.MarketValue,
            DebtTotal = gerechnet.Debt,

            // Ohne eigenen Anteil gibt es keine eigene Sicht. Der Schirm lässt die Kacheln dann
            // weg, statt eine Null als Anteil auszugeben.
            ValueShare = meiner is null ? null : Round(meiner.Quota * objekt.MarketValue),
            DebtShare = meiner is null ? null : Round(meiner.Quota * gerechnet.Debt),
            Settlement = meiner?.Settlement,
            PercentComplete = objekt.Shares.Sum(s => s.Percent) == 100m,
        };
    }

    /// <summary>
    /// Was Objekte und Objektschulden zum Vermögen des Betrachters beitragen.
    /// </summary>
    /// <remarks>
    /// <para>Objekte ohne gepflegte Anteile gehören dem Haushalt ganz — dort zählt der volle
    /// Wert. Eine Quote zu erfinden, wo keine gepflegt ist, wäre schlimmer als keine.</para>
    /// <para>Die Schuld eines Objekts wird nur geteilt, wenn das Objekt geteilt ist. Ein Darlehen
    /// ohne Objekt — Auto, Anschaffung — trägt der Haushalt allein.</para>
    /// </remarks>
    public async Task<WealthShare> WealthAsync(decimal debtTotal, CancellationToken ct = default)
    {
        var objekte = await db.Properties.AsNoTracking()
            .Include(p => p.Loan)
            .Include(p => p.Shares)
            .ToListAsync(ct);

        var einlagen = await DepositsAsync(
            [.. objekte.Where(p => p.Shares.Count > 0).Select(p => p.Id)], ct);

        var wertGesamt = objekte.Sum(p => p.MarketValue);
        var wertAnteil = 0m;
        var objektSchuld = 0m;
        var objektSchuldAnteil = 0m;
        var forderung = 0m;

        foreach (var objekt in objekte)
        {
            if (objekt.Shares.Count == 0)
            {
                wertAnteil += objekt.MarketValue;
                continue;
            }

            var gerechnet = Calculate(objekt, einlagen);
            objektSchuld += gerechnet.Debt;

            var meiner = gerechnet.Shares.FirstOrDefault(z => z.Share.UserId == user.UserId);

            if (meiner is null)
            {
                continue;
            }

            wertAnteil += Round(meiner.Quota * objekt.MarketValue);
            objektSchuldAnteil += Round(meiner.Quota * gerechnet.Debt);
            forderung += meiner.Settlement;
        }

        return new WealthShare
        {
            TangibleTotal = wertGesamt,
            TangibleShare = wertAnteil,

            // Nur die Schuld geteilter Objekte wird gequotet; alle übrigen Darlehen bleiben ganz.
            DebtShare = debtTotal - objektSchuld + objektSchuldAnteil,
            Receivables = forderung,
        };
    }

    /// <summary>
    /// Die Gemeinschaftskonten mit Soll und Eingang — Handoff „Gemeinsame Immobilie“, 3.3.
    /// </summary>
    /// <remarks>
    /// <para>Verglichen wird der laufende Monat, daneben steht der Jahresstand: ein einzelner
    /// Monat sagt wenig, wer im Mai zweimal gezahlt hat steht im Juni scheinbar im Rückstand.</para>
    /// <para>Gezählt werden <b>Einlagen</b> auf dieses Konto — nicht jeder Eingang. Eine
    /// Lohnzahlung auf das Haushaltskonto ist keine Einlage, und sie als eine zu zählen machte
    /// aus einem Rückstand einen Überschuss.</para>
    /// <para><b>Kein Mahnen.</b> Der Rückstand ist eine Feststellung; was zwischen zwei Menschen
    /// vereinbart wurde, nachdem das Soll eingetragen war, weiß die Anwendung nicht.</para>
    /// </remarks>
    public async Task<IReadOnlyList<JointAccountDto>> JointAccountsAsync(
        DateOnly today, CancellationToken ct = default)
    {
        var konten = await db.Accounts.AsNoTracking()
            .Where(a => a.Sharing == AccountSharing.Shared)
            .Include(a => a.Shares).ThenInclude(s => s.User)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        if (konten.Count == 0)
        {
            return [];
        }

        var kontoIds = konten.Select(k => k.Id).ToList();
        var monatsAnfang = new DateOnly(today.Year, today.Month, 1);
        var jahresAnfang = new DateOnly(today.Year, 1, 1);

        // Beide Zeiträume haben ein Ende. Ohne es zählte eine vordatierte Einlage in jeden Monat
        // vor ihr — „im September eingezahlt“ stünde dann schon im Juni.
        var monatsEnde = monatsAnfang.AddMonths(1);
        var jahresEnde = jahresAnfang.AddYears(1);

        // Nur Zuflüsse: eine Einlage, die von diesem Konto abgeht, ist kein Eingang auf ihm.
        // Das Vorzeichen trägt die Richtung — siehe TransactionService.Vorzeichen.
        var einlagen = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Deposit
                        && t.DepositUserId != null
                        && t.Amount > 0
                        && kontoIds.Contains(t.AccountId)
                        && t.BookingDate >= jahresAnfang
                        && t.BookingDate < jahresEnde)
            .Select(t => new { t.AccountId, User = t.DepositUserId!.Value, t.BookingDate, t.Amount })
            .ToListAsync(ct);

        return
        [
            .. konten.Select(konto =>
            {
                decimal Summe(int userId, DateOnly ab, DateOnly bis)
                    => einlagen
                        .Where(e => e.AccountId == konto.Id && e.User == userId
                                    && e.BookingDate >= ab && e.BookingDate < bis)
                        .Sum(e => e.Amount);

                // Wann zuletzt — der Termin steht neben dem Stand, weil „erfüllt“ ohne Datum
                // nicht sagt, ob es rechtzeitig war.
                DateOnly? Zuletzt(int userId)
                    => einlagen
                        .Where(e => e.AccountId == konto.Id && e.User == userId
                                    && e.BookingDate >= monatsAnfang && e.BookingDate < monatsEnde)
                        .Select(e => (DateOnly?)e.BookingDate)
                        .Max();

                return new JointAccountDto
                {
                    AccountId = konto.Id,
                    Name = konto.Name,
                    Month = monatsAnfang,
                    PaidThisYear = einlagen.Where(e => e.AccountId == konto.Id).Sum(e => e.Amount),
                    Contributors =
                    [
                        .. konto.Shares
                            .OrderBy(anteil => anteil.User?.Name)
                            .Select(anteil => new JointContributorDto
                            {
                                UserId = anteil.UserId,
                                Name = anteil.User?.Name ?? "Unbekannt",
                                MonthlyTarget = anteil.MonthlyTarget,
                                DueDay = anteil.DueDay,
                                PaidThisMonth = Summe(anteil.UserId, monatsAnfang, monatsEnde),
                                PaidThisYear = Summe(anteil.UserId, jahresAnfang, jahresEnde),
                                LastPaidOn = Zuletzt(anteil.UserId),
                            }),
                    ],
                };
            }),
        ];
    }

    /// <summary>
    /// Die gebuchten Einlagen je Objekt und Person.
    /// </summary>
    /// <remarks>
    /// Eingebracht ist der Betrag, nicht seine Richtung — deshalb der Betragswert, und zwar
    /// <b>je Zeile</b>: eine Einlage steht auf dem eigenen Konto als Abfluss, auf dem
    /// Gemeinschaftskonto als Zufluss. Über die Summe abgewertet hoben sich beide auf.
    /// </remarks>
    private async Task<Dictionary<(int Property, int User), decimal>> DepositsAsync(
        IReadOnlyList<int> propertyIds, CancellationToken ct)
    {
        if (propertyIds.Count == 0)
        {
            return [];
        }

        // Zusammengefasst wird hier, nicht in der Abfrage: der Betragswert je Zeile lässt sich
        // nicht in eine SQL-Gruppierung übersetzen. Einlagen sind wenige Zeilen im Jahr.
        var zeilen = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Deposit
                        && t.PropertyId != null
                        && t.DepositUserId != null
                        && propertyIds.Contains(t.PropertyId!.Value))
            .Select(t => new
            {
                Property = t.PropertyId!.Value,
                User = t.DepositUserId!.Value,
                t.Amount,
            })
            .ToListAsync(ct);

        return zeilen
            .GroupBy(z => (z.Property, z.User))
            .ToDictionary(g => g.Key, g => g.Sum(z => Math.Abs(z.Amount)));
    }

    /// <summary>Die abgeleiteten Größen eines Objekts — Quote, Einlagen, Ausgleich je Person.</summary>
    private static (decimal Debt, List<Computed> Shares) Calculate(
        Property objekt, Dictionary<(int Property, int User), decimal> einlagen)
    {
        var schuld = objekt.Loan?.RemainingDebt ?? 0m;

        decimal Eingelegt(int userId)
            => einlagen.GetValueOrDefault((objekt.Id, userId), 0m);

        var eingebrachtGesamt = objekt.Shares.Sum(s => s.Equity + Eingelegt(s.UserId));

        return (schuld,
        [
            .. objekt.Shares
                .OrderByDescending(s => s.Percent)
                .ThenBy(s => s.User?.Name)
                .Select(s => new Computed
                {
                    Share = s,
                    Quota = s.Percent / 100m,
                    Deposits = Eingelegt(s.UserId),
                    Settlement = Round(
                        s.Equity + Eingelegt(s.UserId) - (s.Percent / 100m * eingebrachtGesamt)),
                }),
        ]);
    }

    private static decimal Round(decimal wert)
        => decimal.Round(wert, 2, MidpointRounding.AwayFromZero);

    private sealed record Computed
    {
        public required PropertyShare Share { get; init; }
        public required decimal Quota { get; init; }
        public required decimal Deposits { get; init; }
        public required decimal Settlement { get; init; }
    }

    /// <summary>Was die Beteiligung zum Vermögen des Betrachters beiträgt.</summary>
    public sealed record WealthShare
    {
        /// <summary>Der volle Wert aller Objekte, ohne Quote.</summary>
        public required decimal TangibleTotal { get; init; }

        /// <summary>Der eigene Anteil an den Objektwerten.</summary>
        public required decimal TangibleShare { get; init; }

        /// <summary>Alle Verbindlichkeiten, Objektschulden nur zum eigenen Haftungsanteil.</summary>
        public required decimal DebtShare { get; init; }

        /// <summary>Forderungen an Beteiligte, vorzeichenbehaftet.</summary>
        public required decimal Receivables { get; init; }
    }
}
