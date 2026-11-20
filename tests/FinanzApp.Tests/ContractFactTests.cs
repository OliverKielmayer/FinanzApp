using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Kennzahlen eines Vertrags.
/// </summary>
/// <remarks>
/// <para>Sie sind der Grund, aus dem sich zwei Verträge vergleichen lassen: Grundpreis und
/// Arbeitspreis beim Strom, Datenvolumen beim Mobilfunk. Ein Feld am Vertrag könnte das auch —
/// bis zur ersten Preiserhöhung. Dann stünde der neue Wert da und der alte nirgends, und der
/// Vergleich, für den man sie führt, wäre genau in dem Moment unmöglich, in dem er gebraucht
/// wird.</para>
/// <para>Deshalb prüft das meiste hier <b>die Historie</b>: dass eine zweite Vereinbarung die
/// erste begrenzt statt sie zu ersetzen, und dass jede Reihe für sich endet.</para>
/// </remarks>
public sealed class ContractFactTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 6, 15);
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private readonly int vertrag;

    public ContractFactTests()
    {
        using var context = database.Context();

        var contract = new Contract
        {
            Name = "Strom Musterstadt",
            Provider = "Stadtwerke Musterstadt",
            ContractNumber = "SM-4711",
            Amount = 70m,
            PaymentInterval = PremiumInterval.Monthly,
        };

        context.Contracts.Add(contract);
        context.SaveChanges();

        vertrag = contract.Id;
    }

    private PropertyService Dienst()
    {
        var context = database.Context();

        return new PropertyService(
            context,
            new DocumentService(
                context,
                TestDatabase.PathService(root),
                new ObjectLabelService(context),
                clock,
                new PdfPigTextReader(), NullLogger<DocumentService>.Instance),
            clock,
            new ParticipationService(context, TestDatabase.SignedIn(null)));
    }

    private Task<ContractDetailDto> EintragenAsync(
        string schluessel,
        decimal wert,
        DateOnly ab,
        string einheit = "€/kWh",
        ContractFactKind art = ContractFactKind.UnitPrice)
        => Dienst().AddContractFactAsync(vertrag, new AddContractFactRequest
        {
            Key = schluessel,
            Label = schluessel,
            Kind = art,
            Number = wert,
            Unit = einheit,
            ValidFrom = ab,
        });

    // ── Was eine Kennzahl braucht ────────────────────────────────────────────────────────

    /// <summary>
    /// Eine Zahl ohne Einheit kommt nicht durch.
    /// </summary>
    /// <remarks>
    /// Der teuerste Fehler dieses Bereichs, weil er nicht auffällt: Ein Vertrag weist den
    /// Arbeitspreis in Cent aus, der nächste in Euro. Beide Zahlen sehen richtig aus, und der
    /// Vergleich liegt um den Faktor hundert daneben.
    /// </remarks>
    [Fact]
    public async Task Zahl_ohne_Einheit_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1), einheit: ""));

        Assert.Contains("Einheit", fehler.Message);
    }

    /// <summary>Eine Textangabe dagegen hat keine — „ÖkoTarif“ ist in keiner Einheit gemessen.</summary>
    [Fact]
    public async Task Angabe_braucht_keine_Einheit()
    {
        var stand = await Dienst().AddContractFactAsync(vertrag, new AddContractFactRequest
        {
            Key = "tarif",
            Label = "Tarif",
            Kind = ContractFactKind.Text,
            Text = "Musterstrom Basis",
            ValidFrom = new DateOnly(2026, 1, 1),
        });

        var tarif = Assert.Single(stand.Facts);

        Assert.Equal("Musterstrom Basis", tarif.Text);
        Assert.Null(tarif.Unit);
    }

    [Fact]
    public async Task Ohne_Beschriftung_geht_es_nicht()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AddContractFactAsync(vertrag, new AddContractFactRequest
            {
                Key = "arbeitspreis",
                Label = "   ",
                Kind = ContractFactKind.UnitPrice,
                Number = 0.2345m,
                Unit = "€/kWh",
                ValidFrom = new DateOnly(2026, 1, 1),
            }));

        Assert.Contains("Beschriftung", fehler.Message);
    }

    /// <summary>Ein Stichtag als Kennzahl braucht ein Datum und keine Zahl.</summary>
    [Fact]
    public async Task Stichtag_ohne_Datum_wird_abgewiesen()
    {
        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Dienst().AddContractFactAsync(vertrag, new AddContractFactRequest
            {
                Key = "preisgarantie",
                Label = "Preisgarantie bis",
                Kind = ContractFactKind.Date,
                ValidFrom = new DateOnly(2026, 1, 1),
            }));

        Assert.Contains("Datum", fehler.Message);
    }

    // ── Der Schlüssel ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Groß oder klein geschrieben ist derselbe Schlüssel.
    /// </summary>
    /// <remarks>
    /// Sonst stünden „Arbeitspreis“ und „arbeitspreis“ als zwei Reihen nebeneinander, jede mit
    /// ihrer eigenen Historie — und beide gälten.
    /// </remarks>
    [Fact]
    public async Task Schluessel_wird_vereinheitlicht()
    {
        await EintragenAsync("Arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var stand = await EintragenAsync("  arbeitspreis ", 0.2567m, new DateOnly(2026, 7, 1));

        Assert.Equal(2, stand.Facts.Count);
        Assert.All(stand.Facts, f => Assert.Equal("arbeitspreis", f.Key));
    }

    // ── Die Historie ─────────────────────────────────────────────────────────────────────

    /// <summary>Derselbe Tag noch einmal ist eine Berichtigung, keine zweite Zeile.</summary>
    [Fact]
    public async Task Gleicher_Stichtag_berichtigt()
    {
        await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var stand = await EintragenAsync("arbeitspreis", 0.2399m, new DateOnly(2026, 1, 1));

        var preis = Assert.Single(stand.Facts);

        Assert.Equal(0.2399m, preis.Number);
    }

    /// <summary>
    /// Ein späterer Tag legt eine zweite Zeile an und begrenzt die erste.
    /// </summary>
    /// <remarks>
    /// Das ist der ganze Zweck: Nach der Preiserhöhung ist noch nachzulesen, was vorher galt.
    /// </remarks>
    [Fact]
    public async Task Spaeterer_Stichtag_begrenzt_den_vorigen()
    {
        await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var stand = await EintragenAsync("arbeitspreis", 0.2567m, new DateOnly(2026, 4, 1));

        var alt = stand.Facts.Single(f => f.ValidFrom == new DateOnly(2026, 1, 1));
        var neu = stand.Facts.Single(f => f.ValidFrom == new DateOnly(2026, 4, 1));

        Assert.Equal(new DateOnly(2026, 3, 31), alt.ValidUntil);
        Assert.False(alt.IsCurrent);

        Assert.Null(neu.ValidUntil);
        Assert.True(neu.IsCurrent);
    }

    /// <summary>
    /// Jede Reihe endet für sich.
    /// </summary>
    /// <remarks>
    /// Die Falle ohne Gruppierung nach Schlüssel: Der Grundpreis vom Januar bekäme als
    /// Enddatum den Tag vor dem Arbeitspreis vom April — obwohl er unverändert weitergilt.
    /// </remarks>
    [Fact]
    public async Task Eine_fremde_Kennzahl_begrenzt_nicht()
    {
        await EintragenAsync("grundpreis", 7.77m, new DateOnly(2026, 1, 1),
            einheit: "€/Monat", art: ContractFactKind.Money);

        await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var stand = await EintragenAsync("arbeitspreis", 0.2567m, new DateOnly(2026, 4, 1));

        var grund = stand.Facts.Single(f => f.Key == "grundpreis");

        Assert.Null(grund.ValidUntil);
        Assert.True(grund.IsCurrent);
    }

    /// <summary>
    /// Was erst nächstes Jahr gilt, gilt heute nicht.
    /// </summary>
    /// <remarks>
    /// Ein Stichtag in der Zukunft ist erlaubt — anders als bei einer Ablesung, wo er ein
    /// Zahlendreher wäre. Preiserhöhungen werden angekündigt, bevor sie greifen, und das
    /// Schreiben liegt dann schon vor.
    /// </remarks>
    [Fact]
    public async Task Ein_kuenftiger_Stichtag_gilt_noch_nicht()
    {
        await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var stand = await EintragenAsync("arbeitspreis", 0.3101m, new DateOnly(2027, 1, 1));

        var kuenftig = stand.Facts.Single(f => f.ValidFrom == new DateOnly(2027, 1, 1));
        var heute = stand.Facts.Single(f => f.ValidFrom == new DateOnly(2026, 1, 1));

        Assert.True(kuenftig.IsFuture);
        Assert.False(kuenftig.IsCurrent);

        Assert.True(heute.IsCurrent);
    }

    // ── Die Zahl selbst ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vier Nachkommastellen kommen unverändert zurück.
    /// </summary>
    /// <remarks>
    /// Auf Cent gerundet wären aus 0,2345 € je Kilowattstunde 0,23 € — bei dreitausend
    /// Kilowattstunden im Jahr eine Abweichung von über zehn Euro, die niemand sieht.
    /// </remarks>
    [Fact]
    public async Task Vier_Nachkommastellen_bleiben_stehen()
    {
        var stand = await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));

        Assert.Equal(0.2345m, Assert.Single(stand.Facts).Number);
    }

    // ── Entfernen ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Entfernen_nimmt_nur_die_eine_Zeile()
    {
        await EintragenAsync("arbeitspreis", 0.2345m, new DateOnly(2026, 1, 1));
        var zwei = await EintragenAsync("arbeitspreis", 0.2567m, new DateOnly(2026, 4, 1));

        var jung = zwei.Facts.Single(f => f.ValidFrom == new DateOnly(2026, 4, 1));
        var stand = await Dienst().DeleteContractFactAsync(jung.Id);

        var uebrig = Assert.Single(stand.Facts);

        Assert.Equal(new DateOnly(2026, 1, 1), uebrig.ValidFrom);

        // Und die übrige gilt wieder bis auf Weiteres — ihr Ende war die entfernte Zeile.
        Assert.Null(uebrig.ValidUntil);
        Assert.True(uebrig.IsCurrent);
    }

    public void Dispose() => database.Dispose();
}
