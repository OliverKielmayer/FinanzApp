using FinanzApp.Api.Data;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Umsätze eines Kontos bei der Bank holen — und in dieselbe Vorschau legen wie eine Datei.
/// </summary>
/// <remarks>
/// <para><b>Der Abruf bucht nichts.</b> Er endet in der Vorschau, in der auch ein hochgeladener
/// Auszug endet: neue Sätze angehakt, erkannte Doppelte abgewählt, jede Zeile mit ihrem Grund.
/// Gebucht wird, was der Benutzer dort stehen lässt.</para>
/// <para><b>Die Erkennung war schon da.</b> „Welche Buchungen habe ich schon?“ beantwortet der
/// Import seit jeher: gleiche Importreferenz heißt vorhanden, gleicher Tag mit gleichem
/// Empfänger und Betrag heißt mögliches Duplikat. Dem Abruf eine eigene Prüfung zu geben hieße,
/// zwei Antworten auf dieselbe Frage zu haben.</para>
/// <para><b>Die PIN kommt mit der Anfrage und geht mit ihr.</b> Sie wird nirgends abgelegt und
/// steht in keiner Meldung.</para>
/// </remarks>
public sealed class BankStatementService(
    FinanzAppDbContext db,
    BankAccessOptions options,
    IBankStatementSource source,
    ImportService imports,
    IClock clock)
{
    /// <summary>
    /// Wie weit ein Abruf ohne Angabe zurückreicht.
    /// </summary>
    /// <remarks>
    /// Neunzig Tage, weil die meisten Banken nicht weiter hergeben — und weil ein Fenster, das
    /// länger ist als der Bestand der Bank, keinen Fehler erzeugt, sondern nur eine Antwort mit
    /// weniger darin. Lieber zu weit fragen als zu kurz: Was doppelt kommt, erkennt die
    /// Duplikatprüfung.
    /// </remarks>
    public const int DefaultDays = 90;

    /// <summary>
    /// Holt die Umsätze eines Kontos und legt die Vorschau bereit.
    /// </summary>
    /// <param name="from">Erster Tag. Ohne Angabe <see cref="DefaultDays"/> Tage zurück.</param>
    /// <param name="to">Letzter Tag. Ohne Angabe heute.</param>
    /// <exception cref="RuleViolationException">
    /// Das Konto gibt es nicht, die Angaben reichen nicht, es fehlt die PIN, oder der Zeitraum
    /// ergibt keinen Sinn.
    /// </exception>
    /// <exception cref="BankAccessException">Die Bank hat den Abruf abgelehnt.</exception>
    public async Task<ImportPreviewDto> FetchAsync(
        int accountId, string pin, DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default)
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

        var bis = to ?? clock.Today;
        var von = from ?? bis.AddDays(-DefaultDays);

        if (von > bis)
        {
            throw new RuleViolationException(
                $"Der Zeitraum beginnt nach seinem Ende ({von:dd.MM.yyyy} bis {bis:dd.MM.yyyy}).");
        }

        // In die Zukunft zu fragen ist kein Fehler der Bank, sondern einer der Eingabe. Sie
        // antwortete darauf mit leeren Händen, und niemand wüsste, warum.
        if (von > clock.Today)
        {
            throw new RuleViolationException(
                $"Der Zeitraum beginnt in der Zukunft ({von:dd.MM.yyyy}). Umsätze gibt es nur "
                + "für Tage, die schon waren.");
        }

        var auszug = await source.FetchAsync(account, pin, von, bis, ct);

        return await imports.ReadStatementAsync(auszug, ct);
    }

    /// <summary>
    /// Was dem Abruf dieses Kontos noch fehlt — ohne etwas abzurufen.
    /// </summary>
    /// <remarks>
    /// Dieselbe Liste wie beim Saldo: Beide Abrufe sprechen über denselben Zugang, und ein
    /// Konto, das den einen nicht kann, kann auch den anderen nicht.
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
