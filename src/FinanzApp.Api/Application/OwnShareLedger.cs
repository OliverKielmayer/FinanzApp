using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinanzApp.Api.Application;

/// <summary>
/// Die Eigenanteilsregel des Krankenversicherungsvertrags und was dieses Jahr davon
/// verbraucht ist.
/// </summary>
/// <remarks>
/// <para><b>Eine Abfrage, zwei Aufrufer.</b> Beim Erfassen einer Rechnung schlägt sie den
/// Eigenanteil vor; am Vertrag beantwortet sie, ob der Jahresdeckel voll ist. Stünde sie
/// zweimal da, gingen beide Antworten irgendwann auseinander — und bemerkt würde es an der,
/// die seltener jemand ansieht.</para>
/// <para><b>Zahnbehandlungen zählen nicht gegen den Deckel.</b> Sie haben ihren eigenen Satz;
/// dürften sie den Deckel aufbrauchen, wäre er nach zwei Zahnarztrechnungen erschöpft, ohne
/// dass je etwas darauf gezahlt wurde.</para>
/// <para><b>Das Jahr kommt von außen</b>, weil es das Jahr der Rechnung ist und nicht das
/// heutige: Eine im Dezember eingereichte Rechnung vom Januar zählt gegen den Deckel des
/// Januars.</para>
/// </remarks>
public static class OwnShareLedger
{
    /// <summary>
    /// Die Regel mit dem bisher Verbrauchten — <c>null</c>, wenn kein Vertrag einen
    /// Eigenanteilssatz führt.
    /// </summary>
    public static async Task<OwnShareRuleDto?> RuleAsync(
        FinanzAppDbContext db, int year, CancellationToken ct = default)
    {
        var vertrag = await db.Policies.AsNoTracking()
            .Where(p => p.Kind == PolicyKind.Health && p.OwnSharePercent != null)
            .OrderBy(p => p.Id)
            .Select(p => new
            {
                p.Name,
                p.OwnSharePercent,
                p.OwnShareAnnualCap,
                p.DentalOwnSharePercent,
            })
            .FirstOrDefaultAsync(ct);

        if (vertrag is null)
        {
            return null;
        }

        var von = new DateOnly(year, 1, 1);
        var bis = new DateOnly(year, 12, 31);

        var verbraucht = await db.MedicalBills.AsNoTracking()
            .Where(b => !b.IsDental && b.BillDate >= von && b.BillDate <= bis)
            .SumAsync(b => (decimal?)b.OwnShare, ct) ?? 0m;

        return new OwnShareRuleDto(
            vertrag.OwnSharePercent,
            vertrag.OwnShareAnnualCap,
            vertrag.DentalOwnSharePercent,
            verbraucht,
            year,
            vertrag.Name);
    }
}
