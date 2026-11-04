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
            Provider = "Musterleben",
            Kind = PolicyKind.TermLife,
            IsCapitalForming = false,
            CurrentValue = 164000m,
            ValuationDate = new DateOnly(2025, 12, 31),
        };

        Assert.Null(policy.AssetValue);
    }

    [Fact]
    public void Kapitalbildender_Vertrag_traegt_seinen_erreichten_Wert()
    {
        var policy = new Policy
        {
            Name = "Musterleben",
            Provider = "Musterleben",
            Kind = PolicyKind.CapitalLife,
            IsCapitalForming = true,
            CurrentValue = 14726.80m,
            ValuationDate = new DateOnly(2025, 7, 31),
        };

        Assert.Equal(14726.80m, policy.AssetValue);
    }

    [Fact]
    public void Ohne_erreichten_Wert_traegt_auch_die_Vorsorge_nichts()
    {
        var policy = new Policy
        {
            Name = "Neuer Vertrag",
            Provider = "Musterkasse",
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
    /// Die acht Absicherungsverträge des Bestands summieren sich auf 9.840 € im Jahr. Der Test
    /// hält die Rechnung fest, nicht die Beispieldaten — er rechnet dieselben Sätze nach.
    /// </summary>
    [Fact]
    public void Die_acht_Absicherungen_ergeben_neuntausendachthundertvierzig()
    {
        (decimal Premium, PremiumInterval Interval)[] rows =
        [
            (585m, PremiumInterval.Monthly),
            (494m, PremiumInterval.Yearly),
            (96m, PremiumInterval.Monthly),
            (35m, PremiumInterval.Monthly),
            (348m, PremiumInterval.Yearly),
            (198m, PremiumInterval.Yearly),
            (132m, PremiumInterval.Yearly),
            (76m, PremiumInterval.Yearly),
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

        Assert.Equal(9840m, total);
    }

    /// <summary>
    /// Vier Vorsorgeverträge, 42.760 € — der Betrag, der im Bruttovermögen steht.
    /// </summary>
    [Fact]
    public void Die_vier_Vorsorgevertraege_ergeben_zweiundvierzigtausendsiebenhundertsechzig()
    {
        decimal[] values = [14726.80m, 10590m, 8715.60m, 8727.60m];

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

        Assert.Equal(42760m, total);
    }

    [Fact]
    public void Kuendigungsfrist_bleibt_Vertragsende_minus_Frist()
    {
        var policy = new Policy
        {
            Name = "Hausrat Nordstern",
            Provider = "Nordstern Versicherung",
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
