using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Benutzerisolierung über die neuen Entitäten. Der Handoff nennt das ausdrücklich als Testfall —
/// ohne den Mandantenfilter wird aus dem Mehrbenutzerbetrieb ein Datenleck.
/// </summary>
public sealed class HouseholdIsolationTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly int mine;
    private readonly int theirs;

    public HouseholdIsolationTests()
    {
        mine = database.AddHousehold("Haushalt A");
        theirs = database.AddHousehold("Haushalt B");
        Seed(theirs);
    }

    [Fact]
    public void Fremde_Dokumente_sind_unsichtbar()
    {
        using var context = database.Context(mine);

        Assert.Empty(context.Documents);
        Assert.Empty(context.DocumentTypes);
        Assert.Empty(context.DocumentLinks);
    }

    [Fact]
    public void Fremde_Fachobjekte_sind_unsichtbar()
    {
        using var context = database.Context(mine);

        Assert.Empty(context.MedicalBills);
        Assert.Empty(context.Insurances);
        Assert.Empty(context.Properties);
        Assert.Empty(context.Contracts);
        Assert.Empty(context.Invoices);
        Assert.Empty(context.TaskItems);
    }

    [Fact]
    public void Eigener_Haushalt_sieht_seine_Daten()
    {
        using var context = database.Context(theirs);

        Assert.Single(context.Insurances);
        Assert.Single(context.MedicalBills);
        Assert.Single(context.Documents);
    }

    [Fact]
    public void Ohne_gesetzten_Haushalt_ist_nichts_sichtbar()
    {
        // Der Standardfall ist „nichts sichtbar“, nicht „alles sichtbar“.
        using var context = database.Context(0);

        Assert.Empty(context.Documents);
        Assert.Empty(context.Insurances);
        Assert.Empty(context.MedicalBills);
    }

    [Fact]
    public async Task Fremdes_Objekt_laesst_sich_nicht_verknuepfen()
    {
        using var foreignContext = database.Context(theirs);
        var foreignInsuranceId = foreignContext.Insurances.Select(i => i.Id).Single();

        using var context = database.Context(mine);
        var labels = new ObjectLabelService(context);

        // Aus dem eigenen Haushalt heraus ist das fremde Ziel schlicht nicht auffindbar.
        Assert.False(await labels.ExistsAsync(LinkTargetType.Insurance, foreignInsuranceId));
    }

    [Fact]
    public async Task Neue_Datensaetze_bekommen_den_Haushalt_automatisch()
    {
        using var context = database.Context(mine);
        var labels = new ObjectLabelService(context);
        var paths = TestDatabase.PathService(Path.GetTempPath());
        var documents = new DocumentService(
            context, paths, labels, TestDatabase.ClockAt(2026, 8, 23), NullLogger<DocumentService>.Instance);

        context.Insurances.Add(new Insurance { Name = "Neu", Insurer = "Test", Premium = 10m });
        await context.SaveChangesAsync();

        using var check = database.Context(mine);
        Assert.Single(check.Insurances);

        using var other = database.Context(theirs);
        Assert.DoesNotContain(other.Insurances, i => i.Name == "Neu");

        _ = documents;
    }

    private void Seed(int householdId)
    {
        using var context = database.Context(householdId);

        var type = new DocumentType { Name = "Versicherungsschein", Area = DocumentArea.Insurance };
        context.DocumentTypes.Add(type);

        var insurance = new Insurance
        {
            Name = "Hausrat",
            Insurer = "HUK",
            Premium = 156m,
            PremiumInterval = PremiumInterval.Yearly,
        };
        context.Insurances.Add(insurance);
        context.SaveChanges();

        var document = new Document
        {
            Title = "Schein",
            DocumentTypeId = type.Id,
            Area = DocumentArea.Insurance,
            RelativePath = "Versicherungen/Schein.pdf",
            FileName = "Schein.pdf",
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = new DateTime(2026, 1, 1),
        };
        context.Documents.Add(document);
        context.SaveChanges();

        context.DocumentLinks.Add(new DocumentLink
        {
            DocumentId = document.Id,
            TargetType = LinkTargetType.Insurance,
            TargetId = insurance.Id,
            CreatedAt = new DateTime(2026, 1, 1),
        });

        context.MedicalBills.Add(new MedicalBill
        {
            Provider = "Dr. Fremd",
            BillDate = new DateOnly(2026, 7, 1),
            GrossAmount = 100m,
            OwnShare = 20m,
            ExpectedReimbursement = 80m,
            CreatedAt = new DateTime(2026, 7, 1),
        });

        var property = new Property { Name = "Haus B", MarketValue = 100000m };
        context.Properties.Add(property);
        context.SaveChanges();

        var contract = new Contract
        {
            PropertyId = property.Id,
            Name = "Strom",
            Provider = "Werke",
            MonthlyAmount = 50m,
        };
        context.Contracts.Add(contract);
        context.SaveChanges();

        context.Invoices.Add(new Invoice
        {
            ContractId = contract.Id,
            Subject = "Abschlag",
            IssuedOn = new DateOnly(2026, 8, 1),
            DueOn = new DateOnly(2026, 9, 1),
            Amount = 50m,
        });

        context.TaskItems.Add(new TaskItem
        {
            Title = "Fremde Aufgabe",
            State = TaskState.Open,
            Source = TaskSource.Manual,
            CreatedAt = new DateTime(2026, 8, 1),
        });

        context.SaveChanges();
    }

    public void Dispose() => database.Dispose();
}
