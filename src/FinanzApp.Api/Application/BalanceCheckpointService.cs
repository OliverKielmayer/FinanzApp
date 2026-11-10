using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Kontrollpunkte setzen, ansehen und zurücknehmen.
/// </summary>
/// <remarks>
/// <para><b>Ein Kontrollpunkt ist der Abgleich.</b> Die Anwendung rechnet den Saldo aus
/// Anfangsbestand und Buchungen; die Bank weiß ihn. Weichen beide ab, fehlen Buchungen oder
/// stehen welche zu viel da — und diese Lücke schleppt sich sonst bis in alle Ewigkeit mit.
/// Der Kontrollpunkt sagt: Zu diesem Tag war es diese Zahl. Ab hier rechnet die Anwendung
/// darauf weiter.</para>
/// <para><b>Von Hand geht es immer.</b> Nicht jedes Konto hat einen Bankzugang, und auch wo es
/// einen gibt, ist er mal nicht erreichbar. Deshalb ist der Weg über die Tastatur nicht der
/// Notnagel, sondern gleichberechtigt — er unterscheidet sich allein in der Herkunft, die am
/// Punkt vermerkt wird.</para>
/// <para><b>Geschrieben wird nur auf Ansage.</b> Weder die Saldenabfrage noch der Umsatzabruf
/// legt von sich aus einen Punkt an. Eine Zahl der Bank zum Saldo zu erheben ist eine
/// Entscheidung: Sie verschiebt alles, was die Anwendung künftig über dieses Konto sagt.</para>
/// </remarks>
public sealed class BalanceCheckpointService(
    FinanzAppDbContext db, AccountService accounts, IClock clock)
{
    /// <summary>Die Kontrollpunkte eines Kontos, der jüngste zuerst.</summary>
    public async Task<IReadOnlyList<BalanceCheckpointDto>> ListAsync(
        int accountId, CancellationToken ct = default)
        => await db.BalanceCheckpoints.AsNoTracking()
            .Where(k => k.AccountId == accountId)
            .OrderByDescending(k => k.AsOf)
            .ThenByDescending(k => k.Id)
            .Select(k => new BalanceCheckpointDto
            {
                Id = k.Id,
                AccountId = k.AccountId,
                AsOf = k.AsOf,
                Balance = k.Balance,
                LedgerBalance = k.LedgerBalance,
                Source = k.Source,
                Note = k.Note,
                CreatedAt = k.CreatedAt,
            })
            .ToListAsync(ct);

    /// <summary>
    /// Trägt einen festgestellten Stand ein.
    /// </summary>
    /// <remarks>
    /// Festgehalten wird nicht nur die Zahl der Bank, sondern auch die eigene zum selben
    /// Stichtag. Erst beide nebeneinander machen den Punkt zu einem Beleg: Ohne die eigene Zahl
    /// stünde später da, dass abgeglichen wurde, aber nicht, was dabei zusammenlief.
    /// </remarks>
    /// <exception cref="RuleViolationException">
    /// Das Konto gibt es nicht, oder der Stichtag liegt in der Zukunft.
    /// </exception>
    public async Task<BalanceCheckpointDto> AddAsync(
        int accountId, BalanceCheckpointRequest request, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
                      ?? throw new RuleViolationException("Dieses Konto gibt es nicht.");

        var heute = clock.Today;

        // Ein Kontostand für morgen ist keine Feststellung, sondern eine Vorhersage. Und ein
        // Punkt in der Zukunft hielte jede spätere Buchung aus dem Saldo heraus, bis das Datum
        // erreicht ist — das fiele frühestens in Wochen auf.
        if (request.AsOf > heute)
        {
            throw new RuleViolationException(
                $"Der Stichtag liegt in der Zukunft ({GermanFormat.Date(request.AsOf)}). "
                + "Ein Kontostand lässt sich nur für einen Tag feststellen, der schon war.");
        }

        var gerechnet = await accounts.GetBalanceAtAsync(accountId, request.AsOf, ct);

        var punkt = new BalanceCheckpoint
        {
            AccountId = accountId,
            AsOf = request.AsOf,
            Balance = request.Balance,
            LedgerBalance = gerechnet,
            Source = request.Source,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAt = clock.Now,
        };

        db.BalanceCheckpoints.Add(punkt);

        // „Stand vom“ am Konto meint genau das: wann zuletzt abgeglichen wurde. Ein neuer Punkt
        // beantwortet die Frage neu — aber nur, wenn er auch der jüngste ist. Wer eine alte
        // Lücke nachträgt, hat deshalb nicht zuletzt abgeglichen.
        if (request.AsOf > account.BalanceAsOf)
        {
            account.BalanceAsOf = request.AsOf;
        }

        await db.SaveChangesAsync(ct);

        return new BalanceCheckpointDto
        {
            Id = punkt.Id,
            AccountId = punkt.AccountId,
            AsOf = punkt.AsOf,
            Balance = punkt.Balance,
            LedgerBalance = punkt.LedgerBalance,
            Source = punkt.Source,
            Note = punkt.Note,
            CreatedAt = punkt.CreatedAt,
        };
    }

    /// <summary>
    /// Nimmt einen Kontrollpunkt zurück.
    /// </summary>
    /// <remarks>
    /// Ein vertippter Betrag verschiebt den Saldo des Kontos, bis er weg ist — und er lässt
    /// sich nicht durch einen zweiten Punkt heilen, wenn der Vertipper auf einem späteren
    /// Stichtag steht. Löschen muss deshalb gehen.
    /// </remarks>
    /// <exception cref="RuleViolationException">Diesen Punkt gibt es an diesem Konto nicht.</exception>
    public async Task RemoveAsync(int accountId, int checkpointId, CancellationToken ct = default)
    {
        var punkt = await db.BalanceCheckpoints
                        .FirstOrDefaultAsync(k => k.Id == checkpointId && k.AccountId == accountId, ct)
                    ?? throw new RuleViolationException(
                        "Diesen Kontrollpunkt gibt es an diesem Konto nicht.");

        db.BalanceCheckpoints.Remove(punkt);
        await ZiehDenStandZurueckAsync(accountId, punkt, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Setzt „Stand vom“ auf den vorigen Abgleich, wenn der gelöschte Punkt ihn gesetzt hatte.
    /// </summary>
    /// <remarks>
    /// <para>Sonst behauptete das Konto weiter, an jenem Tag abgeglichen worden zu sein — und
    /// die Gesundheitsprüfung zählt Konten mit altem Stand an genau diesem Datum. Ein Konto
    /// sähe frisch aus, während der Abgleich gerade zurückgenommen wurde.</para>
    /// <para><b>Was sich nicht wiederherstellen lässt:</b> der Tag vor dem allerersten
    /// Abgleich. Den hat das Setzen überschrieben, und niemand hat ihn aufgehoben. Bleibt kein
    /// Punkt übrig, steht das Feld deshalb weiter auf dem Tag des gelöschten — am Konto selbst
    /// lässt es sich berichtigen.</para>
    /// </remarks>
    private async Task ZiehDenStandZurueckAsync(
        int accountId, BalanceCheckpoint geloescht, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

        // Zeigt das Feld woandershin, hat es jemand anders gesetzt — etwa am Kontoformular.
        // Das ist seine Angabe und nicht unsere, also bleibt sie stehen.
        if (account is null || account.BalanceAsOf != geloescht.AsOf)
        {
            return;
        }

        var vorheriger = await db.BalanceCheckpoints
            .Where(k => k.AccountId == accountId && k.Id != geloescht.Id)
            .OrderByDescending(k => k.AsOf)
            .ThenByDescending(k => k.Id)
            .FirstOrDefaultAsync(ct);

        if (vorheriger is not null)
        {
            account.BalanceAsOf = vorheriger.AsOf;
        }
    }
}
