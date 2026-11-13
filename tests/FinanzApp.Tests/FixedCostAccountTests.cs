using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Fixkosten nach Konto.
/// </summary>
/// <remarks>
/// <para>Die andere Frage an dieselbe Liste. Nach Kostenart: wofür geht das Geld — was könnte
/// weg? Nach Konto: von wo geht es ab — welches Konto muss jeden Monat gedeckt sein? Wer zwei
/// Konten führt, braucht beides.</para>
/// <para><b>Das Konto wird gepflegt, nie abgeleitet.</b> Es aus dem Betrag zu erraten wäre in
/// genau der Auswertung, die Konten gegenüberstellt, der eine Fehler, der alle Zahlen darin
/// wertlos machte.</para>
/// <para>Die Zahlen und Namen hier sind erfunden.</para>
/// </remarks>
public sealed class FixedCostAccountTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 5, 31);

    private Task<FixedCostsDto> Bericht()
        => database.Reports(clock).GetFixedCostsAsync(new FixedCostsRequest());

    private int Konto(string kurzname)
    {
        using var context = database.Context();

        var konto = new Account
        {
            Name = $"{kurzname} Girokonto",
            ShortName = kurzname,
            BankName = "Musterbank",
            Kind = AccountKind.Checking,
            BalanceAsOf = new DateOnly(2026, 5, 31),
        };

        context.Accounts.Add(konto);
        context.SaveChanges();

        return konto.Id;
    }

    private void Vertrag(string name, decimal betrag, int? konto)
    {
        using var context = database.Context();

        context.Contracts.Add(new Contract
        {
            Name = name,
            Provider = "Anbieter",
            Amount = betrag,
            PaymentInterval = PremiumInterval.Monthly,
            Area = DocumentArea.Housing,
            AccountId = konto,
        });

        context.SaveChanges();
    }

    private void Police(string name, decimal beitrag, int? konto)
    {
        using var context = database.Context();

        context.Policies.Add(new Policy
        {
            Name = name,
            Provider = "Versicherer",
            Kind = PolicyKind.Liability,
            IsCapitalForming = false,
            Premium = beitrag,
            PremiumInterval = PremiumInterval.Monthly,
            AccountId = konto,
        });

        context.SaveChanges();
    }

    private void Darlehen(string name, decimal rate, int? konto)
    {
        using var context = database.Context();

        context.Loans.Add(new Loan
        {
            Name = name,
            Lender = "Musterbank",
            RemainingDebt = 143700m,
            InterestRatePercent = 2.4m,
            Installment = rate,
            NextPaymentDate = new DateOnly(2026, 6, 1),
            AccountId = konto,
        });

        context.SaveChanges();
    }

    // ── Die Gliederung ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vertrag, Police und Darlehen finden alle drei ihr Konto.
    /// </summary>
    /// <remarks>
    /// Die drei Quellen des Berichts werden an drei Stellen gebaut. Eine davon zu vergessen
    /// bliebe unbemerkt, solange nur die Gesamtsumme stimmt — sie stimmt auch dann, wenn alles
    /// unter „Ohne Konto“ landet.
    /// </remarks>
    [Fact]
    public async Task Alle_drei_Quellen_finden_ihr_Konto()
    {
        var giro = Konto("Giro");

        Vertrag("Internet", 31.85m, giro);
        Police("Haftpflicht", 9.65m, giro);
        Darlehen("Autodarlehen", 318.40m, giro);

        var konto = Assert.Single((await Bericht()).Accounts);

        Assert.Equal(giro, konto.AccountId);
        Assert.Equal("Giro", konto.Name);
        Assert.Equal(3, konto.Rows.Count);
        Assert.Equal(359.90m, konto.MonthlyAmount);
    }

    /// <summary>
    /// <b>Die Probe aufs Ganze.</b> Was die Konten tragen, muss zusammen die Fixkostensumme
    /// ergeben — sonst zeigt eine Gliederung weniger an, als der Bericht darüber behauptet.
    /// </summary>
    [Fact]
    public async Task Die_Kontosummen_ergeben_die_Gesamtsumme()
    {
        var giro = Konto("Giro");
        var zweit = Konto("Zweitkonto");

        Vertrag("Internet", 31.85m, giro);
        Vertrag("Abfall", 23.15m, zweit);
        Police("Haftpflicht", 9.65m, null);

        var bericht = await Bericht();

        Assert.Equal(bericht.MonthlyFixed, bericht.Accounts.Sum(k => k.MonthlyAmount));
        Assert.Equal(100m, bericht.Accounts.Sum(k => k.SharePercent));
    }

    /// <summary>
    /// <b>Ohne Konto steht am Ende</b> — auch wenn die Gruppe schwerer wiegt als jedes Konto.
    /// Sie ist keine Antwort auf die Frage, von wo das Geld abgeht, sondern die Stelle, an der
    /// die Antwort fehlt.
    /// </summary>
    [Fact]
    public async Task Ohne_Konto_steht_am_Ende_auch_wenn_es_schwerer_wiegt()
    {
        var giro = Konto("Giro");

        Vertrag("Internet", 31.85m, giro);
        Darlehen("Immobiliendarlehen", 1275.60m, null);

        var konten = (await Bericht()).Accounts;

        Assert.Equal(2, konten.Count);
        Assert.Equal(giro, konten[0].AccountId);
        Assert.Null(konten[1].AccountId);
        Assert.Equal("Ohne Konto", konten[1].Name);
        Assert.Equal(1275.60m, konten[1].MonthlyAmount);
    }

    /// <summary>
    /// Die Kontosicht ändert an den Kostenarten nichts — beide gliedern dieselben Zeilen.
    /// </summary>
    [Fact]
    public async Task Beide_Gliederungen_zeigen_dieselben_Zeilen()
    {
        var giro = Konto("Giro");
        var zweit = Konto("Zweitkonto");

        Vertrag("Internet", 31.85m, giro);
        Vertrag("Abfall", 23.15m, zweit);
        Police("Haftpflicht", 9.65m, giro);

        var bericht = await Bericht();

        Assert.Equal(
            bericht.Rows.Count,
            bericht.Accounts.Sum(k => k.Rows.Count));

        Assert.Equal(
            bericht.Groups.Sum(g => g.Rows.Count),
            bericht.Accounts.Sum(k => k.Rows.Count));
    }

    /// <summary>
    /// Die Zeile trägt den Kurznamen des Kontos, nicht seine Kennziffer.
    /// </summary>
    [Fact]
    public async Task Die_Zeile_nennt_das_Konto_beim_Namen()
    {
        var giro = Konto("Giro");

        Vertrag("Internet", 31.85m, giro);

        var zeile = Assert.Single((await Bericht()).Rows);

        Assert.Equal(giro, zeile.AccountId);
        Assert.Equal("Giro", zeile.AccountName);
    }

    /// <summary>
    /// Ohne eingetragenes Konto bleibt die Zeile leer — und behauptet keines.
    /// </summary>
    [Fact]
    public async Task Ohne_Eintrag_nennt_die_Zeile_kein_Konto()
    {
        Vertrag("Internet", 31.85m, null);

        var zeile = Assert.Single((await Bericht()).Rows);

        Assert.Null(zeile.AccountId);
        Assert.Null(zeile.AccountName);
    }

    public void Dispose() => database.Dispose();
}
