using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Tests;

/// <summary>
/// Der Datenqualitätsbericht aus Abschnitt 10b.
/// </summary>
/// <remarks>
/// Er zählt keine Beträge, sondern Lücken — und zwar so, dass jede Zahl eine Folge und ein
/// Ziel hat. Geprüft wird vor allem, was <em>nicht</em> als Lücke gilt: eine Umbuchung ohne
/// Kategorie ist keine, und ein belegter Vertrag auch nicht.
/// </remarks>
public sealed class DataQualityTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 20);

    private readonly int konto;
    private readonly int kategorie;

    public DataQualityTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, BalanceAsOf = new DateOnly(2026, 8, 20),
        };
        var kat = new Category { Name = "Wohnen", Direction = CategoryDirection.Expense };

        context.Accounts.Add(giro);
        context.Categories.Add(kat);
        context.SaveChanges();

        konto = giro.Id;
        kategorie = kat.Id;
    }

    private Task<DataQualityDto> QualityAsync() => database.Reports(clock).GetDataQualityAsync();

    /// <summary>
    /// Findet eine Zeile über ihren Wortstamm.
    /// </summary>
    /// <remarks>
    /// Die Beschriftung richtet sich nach der Zahl — „Dokument“ oder „Dokumente“. Der Test
    /// soll die Zeile finden, nicht die Form vorschreiben; welche Form richtig ist, prüft ein
    /// eigener Test.
    /// </remarks>
    private static DataQualityRowDto Row(DataQualityDto bericht, string stamm)
        => bericht.Rows.Single(r => r.Label.StartsWith(stamm, StringComparison.Ordinal));

    private void Buchung(TransactionKind art, int? kat)
    {
        using var context = database.Context();
        context.Transactions.Add(new Transaction
        {
            BookingDate = new DateOnly(2026, 8, 5), Payee = "Laden", Kind = art,
            Amount = art == TransactionKind.Income ? 100m : -100m,
            AccountId = konto, CategoryId = kat,
            CreatedAt = new DateTime(2026, 8, 5, 6, 0, 0, DateTimeKind.Local),
        });
        context.SaveChanges();
    }

    // ── Was als Lücke zählt und was nicht ──────────────────────────────────────────────────

    /// <summary>
    /// Eine Umbuchung ohne Kategorie ist keine Lücke.
    /// </summary>
    /// <remarks>
    /// Sie trägt zu Recht keine: Geld wechselt das Konto, es wird nichts ausgegeben. Sie
    /// mitzuzählen hieße, den Nutzer zu einer Zuordnung aufzufordern, die es nicht gibt.
    /// </remarks>
    [Fact]
    public async Task Umbuchungen_ohne_Kategorie_sind_keine_Luecke()
    {
        Buchung(TransactionKind.Transfer, null);
        Buchung(TransactionKind.Expense, kategorie);

        Assert.Equal(0, Row(await QualityAsync(), "Buchung").Count);
    }

    [Fact]
    public async Task Ausgaben_und_Einnahmen_ohne_Kategorie_zaehlen()
    {
        Buchung(TransactionKind.Expense, null);
        Buchung(TransactionKind.Income, null);

        var zeile = Row(await QualityAsync(), "Buchung");

        Assert.Equal(2, zeile.Count);
        Assert.Equal("/konten?offen=true", zeile.Route);
    }

    /// <summary>
    /// Die Zahl gilt dem ganzen Bestand — und sagt es.
    /// </summary>
    /// <remarks>
    /// Der Kostentrend nennt dieselbe Sache für seinen Zeitraum. Zwei verschiedene Zahlen unter
    /// derselben Überschrift wären der Fehler, vor dem der Handoff warnt; verschieden benannte
    /// Ausschnitte sind es nicht. Also steht der Ausschnitt dabei.
    /// </remarks>
    [Fact]
    public async Task Der_Ausschnitt_steht_dabei()
    {
        Buchung(TransactionKind.Expense, null);

        Assert.Contains(
            "über den ganzen Bestand",
            Row(await QualityAsync(), "Buchung").Consequence);
    }

    [Fact]
    public async Task Ein_belegter_Vertrag_ist_keine_Luecke()
    {
        using (var context = database.Context())
        {
            var vertrag = new Contract { Name = "Strom", Provider = "Stadtwerke" };
            var ohne = new Contract { Name = "Wasser", Provider = "Stadtwerke" };
            context.Contracts.AddRange(vertrag, ohne);
            context.SaveChanges();

            var beleg = new Document
            {
                Title = "Stromvertrag", RelativePath = "Wohnen/strom.pdf", FileName = "strom.pdf",
            };
            context.Documents.Add(beleg);
            context.SaveChanges();

            context.DocumentLinks.Add(new DocumentLink
            {
                DocumentId = beleg.Id, TargetType = LinkTargetType.Contract, TargetId = vertrag.Id,
                CreatedAt = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Local),
            });
            context.SaveChanges();
        }

        var zeile = Row(await QualityAsync(), "Vertrag");

        Assert.Equal(1, zeile.Count);
        Assert.Equal("/wohnen", zeile.Route);
    }

    [Fact]
    public async Task Policen_ohne_Beitrag_stehen_als_eigene_Luecke()
    {
        using (var context = database.Context())
        {
            context.Policies.AddRange(
                new Policy
                {
                    Name = "Kapital-LV", Provider = "Allianz", Premium = 0m,
                    IsCapitalForming = true, Kind = PolicyKind.CapitalLife,
                },
                new Policy
                {
                    Name = "Haftpflicht", Provider = "HUK", Premium = 89m,
                    Kind = PolicyKind.Liability,
                });
            context.SaveChanges();
        }

        var bericht = await QualityAsync();

        Assert.Equal(1, Row(bericht, "Police ohne Beitrag").Count);
        Assert.Equal("fehlt in den Fixkosten", Row(bericht, "Police ohne Beitrag").Consequence);
    }

    [Fact]
    public async Task Ein_alter_Kontostand_faellt_auf_nach_drei_Tagen()
    {
        using (var context = database.Context())
        {
            context.Accounts.Add(new Account
            {
                Name = "Tagesgeld", ShortName = "Tagesgeld", BankName = "Raiffeisen",
                Kind = AccountKind.Savings, BalanceAsOf = new DateOnly(2026, 8, 10),
            });
            context.SaveChanges();
        }

        var zeile = Row(await QualityAsync(), "Konto");

        // Das Girokonto steht auf heute und zählt nicht mit.
        Assert.Equal(1, zeile.Count);
        Assert.Contains("älter als 3 Tage", zeile.Consequence);
    }

    // ── Kopf ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ohne_Luecken_heisst_es_vollstaendig()
    {
        var bericht = await QualityAsync();

        Assert.Equal(0, bericht.OpenCount);
        Assert.Equal("vollständig", bericht.Headline);
        Assert.Equal("Alle Auswertungen rechnen auf vollständigen Daten.", bericht.Line);
    }

    [Fact]
    public async Task Der_Kopf_zaehlt_alle_Zeilen_zusammen()
    {
        Buchung(TransactionKind.Expense, null);
        Buchung(TransactionKind.Income, null);

        using (var context = database.Context())
        {
            context.Contracts.Add(new Contract { Name = "Wasser", Provider = "Stadtwerke" });
            context.SaveChanges();
        }

        var bericht = await QualityAsync();

        Assert.Equal(3, bericht.OpenCount);
        Assert.Equal("3 Lücken", bericht.Headline);
        Assert.Contains("bleiben die Summen darüber unvollständig", bericht.Line);
        Assert.Equal(3, bericht.Rows.Sum(r => r.Count));
    }

    /// <summary>Erledigte Zeilen bleiben stehen — nur hinten.</summary>
    [Fact]
    public async Task Erledigte_Zeilen_verschwinden_nicht()
    {
        Buchung(TransactionKind.Expense, null);

        var bericht = await QualityAsync();

        Assert.Equal(6, bericht.Rows.Count);
        Assert.Equal("Buchung ohne Kategorie", bericht.Rows[0].Label);
        Assert.All(bericht.Rows, r => Assert.NotEmpty(r.Route));
        Assert.All(bericht.Rows, r => Assert.NotEmpty(r.Consequence));
    }

    /// <summary>
    /// Kopf und Beschriftung stehen in der Einzahl, wenn es eine ist.
    /// </summary>
    /// <remarks>
    /// „1 Dokumente ohne Datei“ ist ein Zahlwort, das seinem eigenen Substantiv widerspricht.
    /// </remarks>
    [Fact]
    public async Task Eine_einzelne_Luecke_steht_in_der_Einzahl()
    {
        Buchung(TransactionKind.Expense, null);

        var bericht = await QualityAsync();

        Assert.Equal("1 Lücke", bericht.Headline);
        Assert.Equal("Buchung ohne Kategorie", bericht.Rows[0].Label);
    }

    [Fact]
    public async Task Mehrere_stehen_in_der_Mehrzahl()
    {
        Buchung(TransactionKind.Expense, null);
        Buchung(TransactionKind.Income, null);

        var bericht = await QualityAsync();

        Assert.Equal("2 Lücken", bericht.Headline);
        Assert.Equal("Buchungen ohne Kategorie", bericht.Rows[0].Label);
    }

    public void Dispose() => database.Dispose();
}
