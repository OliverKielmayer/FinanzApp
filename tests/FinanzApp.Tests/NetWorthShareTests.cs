using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Das Vermögen mit vier Größen — Handoff „Gemeinsame Immobilie“, 3.2 und 9.1.
/// </summary>
/// <remarks>
/// <para>Gehört ein Objekt zwei Personen, zählt bei jeder nur ihr Anteil — am Wert und an der
/// Schuld. Dazu kommt eine vierte Größe: die <b>Forderung an Beteiligte</b>. Ohne sie zählte
/// das Vermögen dessen, der mehr eingebracht hat, um genau diesen Betrag zu wenig.</para>
/// <para><b>Zwei Schuldgrößen, zwei Namen.</b> Die Bilanz zeigt den Haftungsanteil, der
/// Tilgungsplan die ganze Restschuld. Eine Größe umzudefinieren und ihren Namen zu lassen
/// verschiebt den Widerspruch nur — vorher zeigte die Bilanz zu viel, danach der
/// Darlehensschirm zu wenig.</para>
/// </remarks>
public sealed class NetWorthShareTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 9, 1);
    private readonly int haushalt;
    private readonly int oliver;
    private readonly int sabine;

    public NetWorthShareTests()
    {
        haushalt = database.AddHousehold("Testhaushalt");

        using var context = database.Context(haushalt);

        var a = new User
        {
            HouseholdId = haushalt, Name = "Oliver W.", Email = "o@test.de", PasswordHash = "-",
            Role = HouseholdRole.Owner, CreatedAt = clock.Now,
        };

        var b = new User
        {
            HouseholdId = haushalt, Name = "Sabine K.", Email = "s@test.de", PasswordHash = "-",
            Role = HouseholdRole.Member, CreatedAt = clock.Now,
        };

        context.Users.AddRange(a, b);
        context.SaveChanges();

        oliver = a.Id;
        sabine = b.Id;
    }

    private DashboardService Service(int? alsBenutzer)
    {
        var context = database.Context(haushalt);

        return new DashboardService(
            context,
            new AccountService(context),
            TestDatabase.Portfolio(context),
            new LoanService(context),
            new BudgetService(context, clock),
            clock,
            TestDatabase.SignedIn(alsBenutzer));
    }

    /// <summary>Ein Objekt mit Darlehen, wahlweise geteilt.</summary>
    private void Objekt(
        decimal marktwert = 420000m,
        decimal restschuld = 280000m,
        bool geteilt = true,
        decimal eigenkapitalOliver = 90000m,
        decimal eigenkapitalSabine = 50000m)
    {
        using var context = database.Context(haushalt);

        var darlehen = new Loan
        {
            Name = "Immobiliendarlehen",
            Lender = "Sparkasse",
            RemainingDebt = restschuld,
            InterestRatePercent = 1.84m,
            Installment = 1500m,
            NextPaymentDate = new DateOnly(2026, 10, 1),
        };

        context.Loans.Add(darlehen);
        context.SaveChanges();

        var objekt = new Property
        {
            Name = "Haus zu zweit",
            MarketValue = marktwert,
            PurchaseDate = new DateOnly(2026, 4, 1),
            LoanId = darlehen.Id,
        };

        if (geteilt)
        {
            objekt.Shares.Add(new PropertyShare { UserId = oliver, Percent = 50m, Equity = eigenkapitalOliver });
            objekt.Shares.Add(new PropertyShare { UserId = sabine, Percent = 50m, Equity = eigenkapitalSabine });
        }

        context.Properties.Add(objekt);
        context.SaveChanges();
    }

    // ── Die Quote ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wert und Schuld zählen nur zum eigenen Anteil — die vollen Größen bleiben daneben.
    /// </summary>
    [Fact]
    public async Task Das_geteilte_Objekt_zaehlt_nur_zur_Haelfte()
    {
        Objekt();

        var vermoegen = (await Service(oliver).GetAsync()).NetWorth;

        Assert.Equal(210000m, vermoegen.TangibleAssets);
        Assert.Equal(420000m, vermoegen.TangibleTotal);
        Assert.Equal(140000m, vermoegen.Liabilities);
        Assert.Equal(280000m, vermoegen.LiabilitiesTotal);
    }

    /// <summary>
    /// Die Forderung ist die vierte Größe und geht in die Summe ein.
    /// </summary>
    /// <remarks>
    /// Bei Oliver +20.000, bei Sabine −20.000. Ohne sie zählte Olivers Vermögen 20.000 € zu
    /// wenig und Sabines 20.000 € zu viel.
    /// </remarks>
    [Fact]
    public async Task Die_Forderung_zaehlt_ins_Vermoegen()
    {
        Objekt();

        var meines = (await Service(oliver).GetAsync()).NetWorth;
        var ihres = (await Service(sabine).GetAsync()).NetWorth;

        Assert.Equal(20000m, meines.Receivables);
        Assert.Equal(-20000m, ihres.Receivables);

        // 210.000 Anteil Wert − 140.000 Anteil Schuld + 20.000 Forderung, plus Finanzvermögen.
        Assert.Equal(meines.FinancialAssets + 90000m, meines.Net);
        Assert.Equal(ihres.FinancialAssets + 50000m, ihres.Net);
    }

    /// <summary>
    /// Die beiden Vermögen zusammen ergeben das ganze Objekt — nicht mehr und nicht weniger.
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe der Quote: die Forderungen heben sich auf, die Anteile ergänzen sich.
    /// Zusammen 420.000 Wert minus 280.000 Schuld.
    /// </remarks>
    [Fact]
    public async Task Beide_Anteile_ergeben_zusammen_das_ganze_Objekt()
    {
        Objekt();

        var meines = (await Service(oliver).GetAsync()).NetWorth;
        var ihres = (await Service(sabine).GetAsync()).NetWorth;

        Assert.Equal(420000m, meines.TangibleAssets + ihres.TangibleAssets);
        Assert.Equal(280000m, meines.Liabilities + ihres.Liabilities);
        Assert.Equal(0m, meines.Receivables + ihres.Receivables);
    }

    /// <summary>
    /// Ohne Anteile bleibt alles, wie es war.
    /// </summary>
    /// <remarks>
    /// Der Regelfall darf sich nicht ändern: ein Objekt, das dem Haushalt allein gehört, zählt
    /// ganz — und eine Forderung gibt es dort nicht.
    /// </remarks>
    [Fact]
    public async Task Ohne_Anteile_zaehlt_das_Objekt_ganz()
    {
        Objekt(geteilt: false);

        var vermoegen = (await Service(oliver).GetAsync()).NetWorth;

        Assert.Equal(420000m, vermoegen.TangibleAssets);
        Assert.Equal(420000m, vermoegen.TangibleTotal);
        Assert.Equal(280000m, vermoegen.Liabilities);
        Assert.Equal(0m, vermoegen.Receivables);
    }

    /// <summary>
    /// Ein Darlehen ohne Objekt wird nicht gequotet.
    /// </summary>
    /// <remarks>
    /// Die Quote gehört zum Objekt. Ein Anschaffungsdarlehen ohne Immobilie trägt der Haushalt
    /// allein — es zur Hälfte zu zählen wäre eine Schuld, die niemand hat.
    /// </remarks>
    [Fact]
    public async Task Ein_Darlehen_ohne_Objekt_bleibt_ganz()
    {
        Objekt();

        using (var context = database.Context(haushalt))
        {
            context.Loans.Add(new Loan
            {
                Name = "Autokredit",
                Lender = "Bank",
                RemainingDebt = 12000m,
                InterestRatePercent = 3.9m,
                Installment = 250m,
                NextPaymentDate = new DateOnly(2026, 10, 1),
            });

            context.SaveChanges();
        }

        var vermoegen = (await Service(oliver).GetAsync()).NetWorth;

        // 140.000 Haftungsanteil am Haus plus die ganzen 12.000 des Autokredits.
        Assert.Equal(152000m, vermoegen.Liabilities);
        Assert.Equal(292000m, vermoegen.LiabilitiesTotal);
    }

    /// <summary>
    /// Wer nicht beteiligt ist, zählt das Objekt gar nicht.
    /// </summary>
    /// <remarks>
    /// Es gehört ihm nicht. Die Schuld dazu auch nicht — sonst stünde bei ihm eine Haftung für
    /// ein Haus, an dem er keinen Anteil hat.
    /// </remarks>
    [Fact]
    public async Task Wer_nicht_beteiligt_ist_zaehlt_das_Objekt_nicht()
    {
        Objekt();

        var vermoegen = (await Service(alsBenutzer: null).GetAsync()).NetWorth;

        Assert.Equal(0m, vermoegen.TangibleAssets);
        Assert.Equal(420000m, vermoegen.TangibleTotal);
        Assert.Equal(0m, vermoegen.Liabilities);
        Assert.Equal(0m, vermoegen.Receivables);
    }

    public void Dispose() => database.Dispose();
}
