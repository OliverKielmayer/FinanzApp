using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using FinanzApp.Shared.Formatting;

namespace FinanzApp.Tests;

/// <summary>Die Rechenwege — Tilgungsplan, Budgetzeiträume, abgeleitete Fristen, Formatierung.</summary>
public sealed class CalculationTests
{
    [Fact]
    public void Tilgungsplan_zehrt_die_Restschuld_ab()
    {
        var schedule = LoanService.BuildSchedule(
            remainingDebt: 121500m,
            interestRatePercent: 2.15m,
            installment: 960m,
            firstPayment: new DateOnly(2026, 9, 1),
            months: 12);

        Assert.Equal(12, schedule.Count);
        Assert.Equal(217.69m, schedule[0].Interest);
        Assert.Equal(742.31m, schedule[0].Principal);
        Assert.Equal(120757.69m, schedule[0].RemainingDebt);

        // Zins plus Tilgung ergibt in jedem Monat genau die Rate.
        Assert.All(schedule, entry => Assert.Equal(960m, entry.Interest + entry.Principal));

        // Und die Restschuld nimmt monoton ab.
        Assert.All(
            schedule.Zip(schedule.Skip(1)),
            pair => Assert.True(pair.Second.RemainingDebt < pair.First.RemainingDebt));
    }

    [Fact]
    public void Letzte_Rate_wird_auf_die_Restschuld_gekappt()
    {
        var schedule = LoanService.BuildSchedule(
            remainingDebt: 500m,
            interestRatePercent: 2m,
            installment: 960m,
            firstPayment: new DateOnly(2026, 9, 1),
            months: 12);

        Assert.Single(schedule);
        Assert.Equal(0m, schedule[0].RemainingDebt);
    }

    [Theory]
    [InlineData(PeriodScope.Month, 1, "August 2026")]
    [InlineData(PeriodScope.Quarter, 3, "Q3 2026")]
    [InlineData(PeriodScope.Year, 12, "2026")]
    public void Budgetzeitraum_rechnet_richtig_hoch(PeriodScope period, int months, string label)
    {
        var (from, to, resolvedMonths, resolvedLabel) =
            Periods.Resolve(period, new DateOnly(2026, 8, 23));

        Assert.Equal(months, resolvedMonths);
        Assert.Equal(label, resolvedLabel);
        Assert.True(from <= new DateOnly(2026, 8, 23));
        Assert.True(to >= new DateOnly(2026, 8, 23));
    }

    [Fact]
    public void Kuendigungsfrist_ergibt_sich_aus_Vertragsende_minus_Frist()
    {
        var policy = new Policy
        {
            Name = "Hausrat",
            Provider = "Nordstern",
            Premium = 132m,
            PremiumInterval = PremiumInterval.Yearly,
            EndsOn = new DateOnly(2026, 12, 10),
            NoticePeriodMonths = 3,
        };

        Assert.Equal(new DateOnly(2026, 9, 10), policy.NoticeDeadline);
    }

    [Fact]
    public void Ohne_Vertragsende_gibt_es_keine_Frist()
    {
        var policy = new Policy { Name = "Risikoleben", Provider = "HL", Premium = 42m };

        Assert.Null(policy.NoticeDeadline);
    }

    [Theory]
    [InlineData(PremiumInterval.Monthly, 120, 120)]
    [InlineData(PremiumInterval.Quarterly, 300, 100)]
    [InlineData(PremiumInterval.HalfYearly, 600, 100)]
    [InlineData(PremiumInterval.Yearly, 1200, 100)]
    public void Beitrag_wird_auf_den_Monat_gerechnet(PremiumInterval interval, decimal premium, decimal monthly)
    {
        var policy = new Policy
        {
            Name = "Test",
            Provider = "Test",
            Premium = premium,
            PremiumInterval = interval,
        };

        Assert.Equal(monthly, policy.MonthlyPremium);
    }

    [Fact]
    public void Vertragsfrist_zieht_die_Wochen_vom_Stichtag_ab()
    {
        var contract = new Contract
        {
            Name = "Strom",
            Provider = "Stadtwerke",
            NoticePeriodWeeks = 6,
            NoticeToDate = new DateOnly(2027, 3, 31),
        };

        Assert.Equal(new DateOnly(2027, 2, 17), contract.NoticeDeadline);
    }

    [Fact]
    public void Regelpraefix_ist_das_erste_Wort_des_Empfaengers()
    {
        Assert.Equal("REWE", Categorization.RulePatternFor("REWE Markt Heidelberg"));
        Assert.Equal("PAYPAL", Categorization.RulePatternFor("PAYPAL .PAYCOMET SL"));
        Assert.Equal("Shell", Categorization.RulePatternFor("  Shell Tankstelle "));
    }

    [Fact]
    public void Betrag_wird_deutsch_formatiert()
    {
        // Punkt als Tausendertrenner, Komma als Dezimaltrenner, geschuetztes Leerzeichen vor dem Euro.
        Assert.Equal("1.234,56 €", GermanFormat.Euro(1234.56m));
        Assert.Equal("0,00 €", GermanFormat.Euro(0m));
    }

    [Fact]
    public void Negativer_Betrag_traegt_das_typografische_Minus()
    {
        var text = GermanFormat.Euro(-58.35m);

        Assert.StartsWith("−", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-", text, StringComparison.Ordinal);
        Assert.Equal("−58,35 €", text);
    }

    [Fact]
    public void Vorzeichen_wird_auf_Wunsch_auch_bei_Einnahmen_gezeigt()
        => Assert.Equal("+4.180,00 €", GermanFormat.Euro(4180m, withPlusSign: true));

    [Theory]
    [InlineData(null, PasswordStrength.None)]
    [InlineData("", PasswordStrength.None)]
    [InlineData("passwort123", PasswordStrength.TooWeak)]
    [InlineData("aaaaaaaaaaaa", PasswordStrength.TooWeak)]
    [InlineData("Demo-Haushalt-2026!", PasswordStrength.Strong)]
    public void Passwortstaerke_wird_bewertet(string? password, PasswordStrength expected)
        => Assert.Equal(expected, PasswordPolicy.Evaluate(password));

    [Fact]
    public void Zu_kurzes_Passwort_wird_abgelehnt_auch_wenn_es_bunt_ist()
        => Assert.False(PasswordPolicy.IsAcceptable("Ab1!xY"));
}
