using FinanzApp.Api.Application;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace FinanzApp.Tests;

/// <summary>
/// Die Auszugsfelder: gelesen, gespeichert, wiederauffindbar.
/// </summary>
/// <remarks>
/// Zwei Regeln aus dem Handoff, an denen der Prototyp zuerst gescheitert ist: die Anzeige liest
/// ausschließlich die an der Buchung gespeicherten Felder — nie eine Nachschlagetabelle über den
/// Empfängernamen —, und was nicht behalten wurde, bleibt null statt Leerstring.
/// </remarks>
public sealed class ImportDetailTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 8, 26);
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 50_000 });
    private readonly CurrentUser anonymous = new(new HttpContextAccessor());

    private readonly int account;

    public ImportDetailTests()
    {
        using var context = database.Context();

        var giro = new Account
        {
            Name = "Sparkasse Giro", ShortName = "Sparkasse", BankName = "Sparkasse",
            Kind = AccountKind.Checking, Iban = "DE44 6725 0020 0034 8891 02",
            BalanceAsOf = new DateOnly(2026, 8, 26),
        };

        context.Accounts.Add(giro);
        context.SaveChanges();
        account = giro.Id;
    }

    private ImportService Service()
        => new(database.Context(), clock, new CamtStatementParser(), cache, anonymous);

    /// <summary>Ein Satz mit allen Feldern, die der Handoff im Panel auflistet.</summary>
    private const string Auszug = """
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.08">
          <BkToCstmrAcctRpt><Rpt>
            <Id>1387C522026082608145515</Id>
            <Acct><Id><IBAN>DE44672500200034889102</IBAN></Id>
              <Svcr><FinInstnId><Nm>Sparkasse</Nm></FinInstnId></Svcr></Acct>
            <Ntry>
              <Amt Ccy="EUR">47.98</Amt><CdtDbtInd>DBIT</CdtDbtInd><Sts><Cd>BOOK</Cd></Sts>
              <BookgDt><Dt>2026-01-02</Dt></BookgDt><ValDt><Dt>2026-01-03</Dt></ValDt>
              <AcctSvcrRef>2025123112385825000</AcctSvcrRef>
              <BkTxCd><Domn><Cd>PMNT</Cd><Fmly><Cd>RDDT</Cd><SubFmlyCd>ESDD</SubFmlyCd></Fmly></Domn>
                <Prtry><Cd>NDDT+105+00931</Cd><Issr>DK</Issr></Prtry></BkTxCd>
              <NtryDtls><TxDtls>
                <RltdPties>
                  <Cdtr><Pty><Nm>Vodafone GmbH</Nm></Pty></Cdtr>
                  <CdtrAcct><Id><IBAN>DE13380700590045335700</IBAN></Id></CdtrAcct>
                </RltdPties>
                <RltdAgts><CdtrAgt><FinInstnId><BICFI>DEUTDEDK380</BICFI></FinInstnId></CdtrAgt></RltdAgts>
                <RmtInf><Ustrd>12/2025 K-NR. 934834184 Ihre Rechnung</Ustrd></RmtInf>
              </TxDtls></NtryDtls>
              <AddtlNtryInf>Lastschrift</AddtlNtryInf>
            </Ntry>
          </Rpt></BkToCstmrAcctRpt>
        </Document>
        """;

    private async Task<ImportPreviewDto> ReadAsync()
    {
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Auszug));
        return await Service().ReadAsync(content, "auszug.xml");
    }

    [Fact]
    public async Task Die_Vorschau_traegt_alle_Felder_des_Auszugs()
    {
        var preview = await ReadAsync();
        var details = preview.Rows.Single().Details;

        Assert.NotNull(details);
        Assert.Equal(new DateOnly(2026, 1, 3), details.ValueDate);
        Assert.Equal("EUR", details.Currency);
        Assert.Equal("DE13380700590045335700", details.CounterpartyIban);
        Assert.Equal("DEUTDEDK380", details.CounterpartyBic);
        Assert.Equal("12/2025 K-NR. 934834184 Ihre Rechnung", details.Purpose);
        Assert.Equal("Lastschrift", details.BookingText);
        Assert.Equal("PMNT-RDDT-ESDD", details.BankTransactionCode);
        Assert.Equal("NDDT+105+00931", details.ProprietaryCode);
        Assert.Equal("1387C522026082608145515", details.StatementId);
    }

    [Fact]
    public async Task Was_der_Auszug_nicht_liefert_bleibt_null()
    {
        // Kein ValDt, kein Gegenkonto, kein BkTxCd. Ein Leerstring würde „steht nicht drin“ von
        // „steht drin, ist leer“ ununterscheidbar machen.
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.08">
              <BkToCstmrAcctRpt><Rpt><Id>1</Id><Ntry>
                <Amt Ccy="EUR">5.00</Amt><CdtDbtInd>DBIT</CdtDbtInd>
                <BookgDt><Dt>2026-08-20</Dt></BookgDt><AcctSvcrRef>X1</AcctSvcrRef>
                <NtryDtls><TxDtls><RltdPties><Cdtr><Nm>Kiosk</Nm></Cdtr></RltdPties></TxDtls></NtryDtls>
              </Ntry></Rpt></BkToCstmrAcctRpt>
            </Document>
            """));

        var preview = await Service().ReadAsync(content, "karg.xml");
        var details = preview.Rows.Single().Details;

        Assert.Null(details!.ValueDate);
        Assert.Null(details.CounterpartyIban);
        Assert.Null(details.CounterpartyBic);
        Assert.Null(details.BankTransactionCode);
        Assert.Null(details.Purpose);
    }

    [Fact]
    public async Task Beim_Import_landen_die_Felder_an_der_Buchung()
    {
        var preview = await ReadAsync();

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id, AccountId = account, Indexes = [0],
        });

        using var context = database.Context();
        var gebucht = context.Transactions.Single();

        Assert.Equal(new DateOnly(2026, 1, 3), gebucht.ValueDate);
        Assert.Equal("DE13380700590045335700", gebucht.CounterpartyIban);
        Assert.Equal("DEUTDEDK380", gebucht.CounterpartyBic);
        Assert.Equal("12/2025 K-NR. 934834184 Ihre Rechnung", gebucht.Purpose);
        Assert.Equal("CAMT:2025123112385825000", gebucht.ImportReference);
    }

    [Fact]
    public async Task Abgewaehlte_Felder_werden_nicht_gespeichert()
    {
        var preview = await ReadAsync();

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = account,
            Indexes = [0],
            Keep = new ImportKeepFields(Purpose: false, Counterparty: false, Reference: true),
        });

        using var context = database.Context();
        var gebucht = context.Transactions.Single();

        Assert.Null(gebucht.Purpose);
        Assert.Null(gebucht.CounterpartyIban);
        Assert.Null(gebucht.CounterpartyBic);

        // Die übrigen Felder hängen nicht an den drei Schaltern.
        Assert.Equal("Lastschrift", gebucht.BookingText);
        Assert.Equal("CAMT:2025123112385825000", gebucht.ImportReference);
    }

    [Fact]
    public async Task Ohne_Referenz_bleibt_nur_der_Notnagel()
    {
        var preview = await ReadAsync();

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = account,
            Indexes = [0],
            Keep = new ImportKeepFields(Reference: false),
        });

        using var context = database.Context();
        Assert.Null(context.Transactions.Single().ImportReference);

        // Derselbe Auszug noch einmal: ohne Referenz greift nur noch Tag, Empfänger und Betrag.
        var zweite = await ReadAsync();
        Assert.Equal(ImportRowState.Duplicate, zweite.Rows.Single().State);
    }

    [Fact]
    public async Task Ein_einzelner_Satz_darf_abweichen()
    {
        var preview = await ReadAsync();

        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id,
            AccountId = account,
            Indexes = [0],
            Keep = new ImportKeepFields(),
            KeepOverrides = [new ImportKeepOverride(0, new ImportKeepFields(Purpose: false))],
        });

        using var context = database.Context();
        var gebucht = context.Transactions.Single();

        Assert.Null(gebucht.Purpose);
        Assert.Equal("DE13380700590045335700", gebucht.CounterpartyIban);
    }

    [Fact]
    public async Task Der_Verwendungszweck_wird_mitdurchsucht()
    {
        var preview = await ReadAsync();
        await Service().CommitAsync(new ImportCommitRequest
        {
            PreviewId = preview.Id, AccountId = account, Indexes = [0],
        });

        var service = new TransactionService(database.Context(), clock);

        // Der Zweck ist oft das Einzige, was einen Einkauf benennt — „K-NR.“ steht nirgends sonst.
        var treffer = await service.GetPageAsync("K-NR.");
        Assert.Single(treffer.Items);

        var daneben = await service.GetPageAsync("gibt-es-nicht");
        Assert.Empty(daneben.Items);
    }

    [Fact]
    public async Task Eine_von_Hand_erfasste_Buchung_traegt_keine_Auszugsdaten()
    {
        using (var context = database.Context())
        {
            context.Transactions.Add(new Transaction
            {
                BookingDate = new DateOnly(2026, 8, 5), Payee = "Vodafone GmbH",
                Kind = TransactionKind.Expense, Amount = -20m,
                AccountId = account, CreatedAt = new DateTime(2026, 8, 5),
            });

            context.SaveChanges();
        }

        var seite = await new TransactionService(database.Context(), clock).GetPageAsync(search: null);
        var vonHand = seite.Items.Single(t => t.Amount == -20m);

        // Nie über den Empfängernamen nachschlagen: sonst trüge diese Buchung plötzlich die
        // Auszugsdaten der gleichnamigen importierten — samt erfundener Referenz.
        Assert.Null(vonHand.Details);
        Assert.Null(vonHand.ImportReference);
        Assert.False(vonHand.HasStatementData);
    }

    public void Dispose()
    {
        cache.Dispose();
        database.Dispose();
    }
}
