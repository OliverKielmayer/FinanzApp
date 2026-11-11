using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Was einem Konto zum Bankabruf noch fehlt.
/// </summary>
/// <remarks>
/// <para>Damit die Oberfläche den Schalter gar nicht erst anbietet, wo er nur in eine
/// Fehlermeldung führte. Geprüft wird trotzdem noch einmal beim Abruf selbst: Ein Schalter,
/// der nicht erscheint, ist keine Zusicherung.</para>
/// <para><b>Hier stand einmal mehr.</b> Dieser Dienst fragte auch den Kontostand bei der Bank
/// ab (FinTS-Geschäftsvorfall HKSAL) und stellte ihn neben den gerechneten. Der Abruf ist
/// entfernt, weil die Bibliothek zu diesem Saldo <em>kein Datum</em> liefert: Die Anwendung
/// stempelte ihn mit der eigenen Uhr, und ein vormittags gelesener Zwischenstand wurde so zum
/// Stand des ganzen Tages. Alles, was die Bank danach noch buchte, fiel aus der Rechnung.</para>
/// <para>Den Stand bringt jetzt der Kontoauszug mit — er nennt seinen Schlusssaldo samt dem
/// Tag, auf den der sich bezieht. Siehe <see cref="BankStatementService"/>.</para>
/// </remarks>
public sealed class BankAccessService(FinanzAppDbContext db, BankAccessOptions options)
{
    /// <summary>
    /// Die Mängelliste eines Kontos — ohne Abruf und ohne PIN.
    /// </summary>
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
