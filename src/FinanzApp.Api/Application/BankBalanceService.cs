using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Den Kontostand bei der Bank erfragen und gegen den gerechneten halten.
/// </summary>
/// <remarks>
/// <para><b>Der Abruf schreibt nichts.</b> Er holt eine Zahl und stellt sie neben die, die die
/// Anwendung ohnehin rechnet. Den gerechneten Saldo zu überschreiben hiesse, eine Rechnung durch
/// eine Behauptung zu ersetzen — und die Differenz, die man eigentlich sucht, verschwände im
/// selben Zug.</para>
/// <para><b>Eine Abweichung ist der Befund, nicht der Fehler.</b> Sie bedeutet: Es fehlen
/// Buchungen, oder es stehen welche zu viel da. Wer das sieht, lädt den Kontoauszug nach. Wer
/// es nicht sieht, hält eine falsche Summe für richtig.</para>
/// <para><b>Die PIN kommt mit der Anfrage und geht mit ihr.</b> Sie wird nirgends abgelegt und
/// steht in keiner Meldung — auch nicht in einer Fehlermeldung.</para>
/// </remarks>
public sealed class BankBalanceService(
    FinanzAppDbContext db,
    AccountService accounts,
    BankAccessOptions options,
    IBankBalanceSource source)
{
    /// <summary>
    /// Fragt den Stand eines Kontos bei seiner Bank ab.
    /// </summary>
    /// <exception cref="RuleViolationException">
    /// Das Konto gibt es nicht, die Angaben reichen nicht, oder es fehlt die PIN.
    /// </exception>
    /// <exception cref="BankAccessException">Die Bank hat den Abruf abgelehnt.</exception>
    public async Task<BankBalanceDto> CheckAsync(
        int accountId, string pin, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking()
                          .FirstOrDefaultAsync(a => a.Id == accountId, ct)
                      ?? throw new RuleViolationException("Dieses Konto gibt es nicht.");

        // Erst die Vollständigkeit, dann die PIN: Wer die Angaben noch gar nicht hat, soll nicht
        // zuerst nach einer PIN gefragt werden.
        if (BankAccessReadiness.Problems(account, options) is { Count: > 0 } probleme)
        {
            throw new RuleViolationException(string.Join(" ", probleme));
        }

        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new RuleViolationException(
                "Ohne PIN kein Abruf. Sie wird nicht hinterlegt und bei jedem Mal neu gefragt.");
        }

        var gelesen = await source.FetchAsync(account, pin, ct);
        var gerechnet = (await accounts.GetBalancesAsync(ct))
            .TryGetValue(accountId, out var saldo) ? saldo : 0m;

        return new BankBalanceDto
        {
            AccountId = account.Id,
            AccountName = account.Name,
            BankBalance = gelesen.Balance,
            RetrievedAt = gelesen.RetrievedAt,
            LedgerBalance = gerechnet,
        };
    }

    /// <summary>
    /// Was dem Abruf dieses Kontos noch fehlt — ohne etwas abzurufen.
    /// </summary>
    /// <remarks>
    /// Damit die Oberfläche den Schalter gar nicht erst anbietet, wo er nur in eine Fehlermeldung
    /// führte. Geprüft wird trotzdem noch einmal beim Abruf: Ein Schalter, der nicht erscheint,
    /// ist keine Zusicherung.
    /// </remarks>
    public async Task<IReadOnlyList<string>> ProblemsAsync(
        int accountId, CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        return account is null
            ? ["Dieses Konto gibt es nicht."]
            : BankAccessReadiness.Problems(account, options);
    }
}
