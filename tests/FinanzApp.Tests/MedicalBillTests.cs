using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Die Regel, die im ganzen PKV-Bereich durchschlägt: der Eigenanteil ist eine gebuchte Ausgabe,
/// keine offene Forderung. Sie steht deshalb hier als Test und nicht nur als Kommentar.
/// </summary>
public sealed class MedicalBillTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 23);
    private readonly string root = Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Eigenanteil_zaehlt_nicht_als_offene_Forderung()
    {
        var bill = await CreateBillAsync(gross: 850m, ownShare: 187.60m);

        Assert.Equal(662.40m, bill.ExpectedReimbursement);
        Assert.Equal(662.40m, bill.OpenAmount);

        // Der Eigenanteil taucht im offenen Betrag nirgends auf.
        Assert.NotEqual(bill.GrossAmount, bill.OpenAmount);
        Assert.Equal(bill.GrossAmount - bill.OwnShare, bill.OpenAmount);
    }

    [Fact]
    public async Task Abgeschlossener_Vorgang_hat_nichts_mehr_offen()
    {
        var service = CreateService();
        var bill = await CreateBillAsync(gross: 500m, ownShare: 100m);

        var completed = await service.AdvanceAsync(bill.Id, MedicalBillStatus.Completed);

        Assert.NotNull(completed);
        Assert.Equal(0m, completed.OpenAmount);
    }

    [Fact]
    public async Task Abgelehnter_Vorgang_fordert_nichts_mehr()
    {
        var service = CreateService();
        var bill = await CreateBillAsync(gross: 300m, ownShare: 0m);

        var rejected = await service.AdvanceAsync(bill.Id, MedicalBillStatus.Rejected);

        Assert.NotNull(rejected);
        Assert.Equal(0m, rejected.OpenAmount);
    }

    [Fact]
    public async Task Eigenanteil_groesser_als_Rechnung_wird_abgewiesen()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateMedicalBillRequest
        {
            Provider = "Dr. Test",
            BillDate = new DateOnly(2026, 8, 1),
            GrossAmount = 100m,
            OwnShare = 150m,
        }));
    }

    [Fact]
    public async Task Zahlung_unter_der_Erwartung_gilt_als_teilweise_erstattet()
    {
        var service = CreateService();
        // Erwartet werden 700 (800 minus 100 Eigenanteil), eingegangen sind nur 600.
        var bill = await CreateBillAsync(gross: 800m, ownShare: 100m);
        Assert.Equal(700m, bill.ExpectedReimbursement);

        var transactionId = AddIncome(600m, new DateOnly(2026, 8, 20), "Erstattung");

        var linked = await service.LinkPaymentAsync(bill.Id, new LinkPaymentRequest
        {
            TransactionId = transactionId,
        });

        Assert.NotNull(linked);
        Assert.Equal(MedicalBillStatus.PartiallyReimbursed, linked.Status);
        Assert.Equal(600m, linked.ActualReimbursement);
    }

    [Fact]
    public async Task Passende_Buchung_steht_als_bester_Treffer_oben()
    {
        var service = CreateService();
        var bill = await CreateBillAsync(gross: 780m, ownShare: 100m, billNumber: "R-2026-098",
            submittedAt: new DateTime(2026, 7, 25, 9, 0, 0, DateTimeKind.Local));

        AddIncome(612.40m, new DateOnly(2026, 8, 19), "Irgendein Eingang");
        var expected = AddIncome(680m, new DateOnly(2026, 8, 21), "Erstattung PKV R-2026-098");

        var candidates = await service.GetPaymentCandidatesAsync(bill.Id);

        Assert.NotEmpty(candidates);
        Assert.Equal(expected, candidates[0].TransactionId);
        Assert.True(candidates[0].IsBestMatch);
        Assert.True(candidates[0].Score > candidates.Skip(1).Select(c => c.Score).DefaultIfEmpty(0).Max());
    }

    private MedicalBillService CreateService()
    {
        var context = database.Context();
        var labels = new ObjectLabelService(context);
        var paths = TestDatabase.PathService(root);
        var documents = new DocumentService(context, paths, labels, clock, NullLogger<DocumentService>.Instance);
        return new MedicalBillService(context, documents, clock);
    }

    private async Task<MedicalBillDetailDto> CreateBillAsync(
        decimal gross, decimal ownShare, string? billNumber = null, DateTime? submittedAt = null)
    {
        var service = CreateService();
        var bill = await service.CreateAsync(new CreateMedicalBillRequest
        {
            Provider = "Dr. Meyer, Zahnarzt",
            BillDate = new DateOnly(2026, 7, 18),
            BillNumber = billNumber,
            GrossAmount = gross,
            OwnShare = ownShare,
        });

        if (submittedAt is { } at)
        {
            using var context = database.Context();
            var entity = context.MedicalBills.Single(b => b.Id == bill.Id);
            entity.Status = MedicalBillStatus.Submitted;
            entity.SubmittedAt = at;
            context.SaveChanges();

            return (await CreateService().GetAsync(bill.Id))!;
        }

        return bill;
    }

    private int AddIncome(decimal amount, DateOnly date, string payee)
    {
        using var context = database.Context();
        var account = context.Accounts.FirstOrDefault();
        if (account is null)
        {
            account = new Account
            {
                Name = "Sparkasse Giro",
                ShortName = "Sparkasse",
                BankName = "Sparkasse",
                Kind = AccountKind.Checking,
                BalanceAsOf = new DateOnly(2026, 8, 23),
            };
            context.Accounts.Add(account);
            context.SaveChanges();
        }

        var transaction = new Transaction
        {
            BookingDate = date,
            Payee = payee,
            Kind = TransactionKind.Income,
            Amount = amount,
            AccountId = account.Id,
            CreatedAt = new DateTime(2026, 8, 23),
        };

        context.Transactions.Add(transaction);
        context.SaveChanges();
        return transaction.Id;
    }

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
