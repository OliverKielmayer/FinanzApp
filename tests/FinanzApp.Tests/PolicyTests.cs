using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Die Regel, an der die alte Sammelkategorie „Versicherungen" gescheitert ist: nur ein
/// kapitalbildender Vertrag trägt einen Wert. Sie steht deshalb hier als Test und nicht nur
/// als Kommentar.
/// </summary>
public sealed class PolicyTests
{
    [Fact]
    public void Risikoleben_zaehlt_nie_zum_Vermoegen()
    {
        // Selbst wenn versehentlich ein Wert eingetragen ist: eine Absicherung hat keinen.
        var policy = new Policy
        {
            Name = "Risikoleben",
            Provider = "Heidelberger Leben",
            Kind = PolicyKind.TermLife,
            IsCapitalForming = false,
            CurrentValue = 250000m,
            ValuationDate = new DateOnly(2025, 12, 31),
        };

        Assert.Null(policy.AssetValue);
    }

    [Fact]
    public void Kapitalbildender_Vertrag_traegt_seinen_erreichten_Wert()
    {
        var policy = new Policy
        {
            Name = "Heidelberger Leben",
            Provider = "Heidelberger Leben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            CurrentValue = 20481.52m,
            ValuationDate = new DateOnly(2025, 7, 31),
        };

        Assert.Equal(20481.52m, policy.AssetValue);
    }

    [Fact]
    public void Ohne_erreichten_Wert_traegt_auch_die_Vorsorge_nichts()
    {
        var policy = new Policy
        {
            Name = "Neuer Vertrag",
            Provider = "Debeka",
            Kind = PolicyKind.Riester,
            IsCapitalForming = true,
        };

        Assert.Null(policy.AssetValue);
    }

    [Theory]
    [InlineData(PremiumInterval.Monthly, 742, 8904)]
    [InlineData(PremiumInterval.Quarterly, 100, 400)]
    [InlineData(PremiumInterval.HalfYearly, 100, 200)]
    [InlineData(PremiumInterval.Yearly, 618, 618)]
    public void Beitrag_wird_auf_das_Jahr_gerechnet(
        PremiumInterval interval, decimal premium, decimal annual)
    {
        var policy = new Policy
        {
            Name = "Test",
            Provider = "Test",
            Premium = premium,
            PremiumInterval = interval,
        };

        Assert.Equal(annual, policy.AnnualPremium);
    }

    /// <summary>
    /// Die acht Absicherungsverträge des Handoffs summieren sich auf 12.330 € im Jahr. Der Test
    /// hält die Rechnung fest, nicht die Beispieldaten — er rechnet dieselben Sätze nach.
    /// </summary>
    [Fact]
    public void Die_acht_Absicherungen_ergeben_zwoelftausenddreihundertdreissig()
    {
        (decimal Premium, PremiumInterval Interval)[] rows =
        [
            (742m, PremiumInterval.Monthly),
            (618m, PremiumInterval.Yearly),
            (118m, PremiumInterval.Monthly),
            (42m, PremiumInterval.Monthly),
            (412m, PremiumInterval.Yearly),
            (231m, PremiumInterval.Yearly),
            (156m, PremiumInterval.Yearly),
            (89m, PremiumInterval.Yearly),
        ];

        var total = rows
            .Select(r => new Policy
            {
                Name = "x",
                Provider = "x",
                Premium = r.Premium,
                PremiumInterval = r.Interval,
            })
            .Sum(p => p.AnnualPremium);

        Assert.Equal(12330m, total);
    }

    /// <summary>
    /// Vier Vorsorgeverträge, 58.940 € — der Betrag, der im Prototyp im Bruttovermögen steht.
    /// </summary>
    [Fact]
    public void Die_vier_Vorsorgevertraege_ergeben_achtundfuenfzigtausendneunhundertvierzig()
    {
        decimal[] values = [20481.52m, 14208m, 11930.40m, 12320.08m];

        var total = values
            .Select(v => new Policy
            {
                Name = "x",
                Provider = "x",
                IsCapitalForming = true,
                CurrentValue = v,
                ValuationDate = new DateOnly(2025, 12, 31),
            })
            .Sum(p => p.AssetValue ?? 0m);

        Assert.Equal(58940m, total);
    }

    [Fact]
    public void Kuendigungsfrist_bleibt_Vertragsende_minus_Frist()
    {
        var policy = new Policy
        {
            Name = "Hausrat HUK",
            Provider = "HUK-Coburg",
            Kind = PolicyKind.HouseholdContents,
            EndsOn = new DateOnly(2027, 12, 31),
            NoticePeriodMonths = 3,
        };

        Assert.Equal(new DateOnly(2027, 9, 30), policy.NoticeDeadline);
    }

    [Fact]
    public void Ohne_Vertragsende_gibt_es_keine_Frist()
    {
        var policy = new Policy { Name = "Risikoleben", Provider = "HL" };

        Assert.Null(policy.NoticeDeadline);
    }
}
