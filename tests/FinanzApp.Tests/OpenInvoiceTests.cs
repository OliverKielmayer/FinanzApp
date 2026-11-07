using FinanzApp.Api.Application;
using FinanzApp.Api.Data;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;
using FinanzApp.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanzApp.Tests;

/// <summary>
/// Der offene Posten — vom Beleg bis zur belegten Zahlung.
/// </summary>
/// <remarks>
/// <para>Eine Rechnung kommt ins Haus und ist damit eine Verpflichtung. Was danach zählt, ist
/// nicht die Ablage, sondern die Frage, ob sie beglichen wurde — und ob man das belegen kann.</para>
/// <para>Den Apparat dahinter gab es schon: Zahlungssuche, Vorgang, Fälligkeit. Nur entstand nie
/// eine Rechnung außerhalb des Beispielbestands, und so lief er leer.</para>
/// <para>Die Belege hier sind erfunden.</para>
/// </remarks>
public sealed class OpenInvoiceTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly IClock clock = TestDatabase.ClockAt(2026, 11, 2);

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "finanzapp-tests", Guid.NewGuid().ToString("N"));

    private const string Rechnung = """
        Musterelektro Handels GmbH, Musterstr. 22, 11111
        Rechnung Nr.: VR5000012345
        Belegdatum 14. Juli 2026
        Endsumme inkl. MwSt 1.204,00 EUR
        """;

    private DocumentService Documents(FinanzAppDbContext context)
        => new(context, TestDatabase.PathService(root), new ObjectLabelService(context), clock,
            new PdfPigTextReader(), NullLogger<DocumentService>.Instance);

    private CreateFormService Forms(FinanzAppDbContext context)
        => new(context, clock, Documents(context), new ObjectLabelService(context),
            new NoPolicyDocumentAnalyzer());

    private PropertyService Properties(FinanzAppDbContext context)
        => new(context, Documents(context), clock,
            new ParticipationService(context, TestDatabase.SignedIn(null)));

    private TaskService Tasks(FinanzAppDbContext context) => new(context, clock);

    private static Dictionary<string, string?> Felder(
        string faellig = "2026-08-14", string? empfaenger = "Musterelektro Handels GmbH")
        => new()
        {
            ["subject"] = "Rechnung Musterelektro",
            ["payee"] = empfaenger,
            ["amount"] = "1204,00",
            ["issued"] = "2026-07-14",
            ["due"] = faellig,
            ["number"] = "VR5000012345",
        };

    /// <summary>Eine Buchung, die zur Rechnung passen könnte.</summary>
    private int Buchung(
        FinanzAppDbContext context, decimal betrag, DateOnly tag, string payee,
        string zweck = "Rechnung VR5000012345")
    {
        var konto = context.Accounts.FirstOrDefault();
        if (konto is null)
        {
            konto = new Account
            {
                Name = "Musterbank Giro", ShortName = "Giro", BankName = "Musterbank",
                Kind = AccountKind.Checking,
            };
            context.Accounts.Add(konto);
            context.SaveChanges();
        }

        var buchung = new Transaction
        {
            AccountId = konto.Id,
            BookingDate = tag,
            Amount = -betrag,
            Kind = TransactionKind.Expense,
            Payee = payee,
            Purpose = zweck,
        };

        context.Transactions.Add(buchung);
        context.SaveChanges();
        return buchung.Id;
    }

    // ── Der Einstieg ───────────────────────────────────────────────────────────────────────

    /// <summary>Ein offener Posten entsteht über denselben Anlegeweg wie jedes Objekt.</summary>
    [Fact]
    public async Task Ein_offener_Posten_entsteht_als_Verknuepfungsziel()
    {
        using var context = database.Context();

        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());

        Assert.True(ergebnis.Ok);
        Assert.Equal(LinkTargetType.Invoice, ergebnis.LinkTarget);

        var posten = context.Invoices.Single();
        Assert.Equal(1204.00m, posten.Amount);
        Assert.Equal("Musterelektro Handels GmbH", posten.Payee);
        Assert.Equal(InvoiceStatus.Open, posten.Status);
        Assert.Null(posten.PaidOn);
    }

    /// <summary>
    /// Der gelesene Beleg füllt das Formular vor — bis auf die Fälligkeit.
    /// </summary>
    /// <remarks>
    /// Sie steht auf dem Blatt als Zahlungsziel und in jeder Rechnung anders; der Belegleser
    /// kennt sie nicht. Ein geratenes Datum ließe den Posten am Tag seiner Erfassung überfällig
    /// aussehen — und an ihm misst sich die ganze Nachverfolgung.
    /// </remarks>
    [Fact]
    public async Task Die_Rechnung_fuellt_den_Posten_vor_aber_nicht_die_Faelligkeit()
    {
        using var context = database.Context();

        var dokument = new Document
        {
            Title = "Scannen",
            RelativePath = "Sonstiges/Scannen.pdf",
            FileName = "Scannen.pdf",
            Extension = ".pdf",
            Area = DocumentArea.Other,
            ContentText = Rechnung,
            CreatedAt = clock.Now,
            UpdatedAt = clock.Now,
        };

        context.Documents.Add(dokument);
        context.SaveChanges();

        var form = await Forms(context).GetFormAsync(
            CreateObjectType.Invoice, null, dokument.Id);

        Assert.NotNull(form);

        string? Vorgabe(string key) => form.Fields.FirstOrDefault(f => f.Key == key)?.DefaultValue;

        Assert.Equal("Musterelektro Handels GmbH", Vorgabe("payee"));
        Assert.Equal("Rechnung Musterelektro Handels GmbH", Vorgabe("subject"));
        Assert.Equal("VR5000012345", Vorgabe("number"));
        Assert.Equal("2026-07-14", Vorgabe("issued"));
        Assert.Equal("1204,00", Vorgabe("amount"));

        Assert.True(string.IsNullOrEmpty(Vorgabe("due")));
    }

    /// <summary>Eine Fälligkeit vor dem Rechnungsdatum ist ein Vertipper, keine Frist.</summary>
    /// <remarks>
    /// Stillschweigend genommen zeigte sie den Posten am Tag seiner Erfassung als überfällig —
    /// und der Benutzer suchte den Fehler bei der Rechnung statt bei seiner Eingabe.
    /// </remarks>
    [Fact]
    public async Task Eine_Faelligkeit_vor_dem_Rechnungsdatum_wird_abgewiesen()
    {
        using var context = database.Context();

        var ergebnis = await Forms(context).CreateAsync(
            CreateObjectType.Invoice, Felder(faellig: "2026-07-01"));

        Assert.False(ergebnis.Ok);
        Assert.Equal("due", ergebnis.FieldKey);
        Assert.Empty(context.Invoices);
    }

    // ── Die Nachverfolgung ─────────────────────────────────────────────────────────────────

    /// <summary>Solange der Posten offen ist, steht er unter den Vorgängen.</summary>
    [Fact]
    public async Task Ein_offener_Posten_steht_unter_den_Vorgaengen()
    {
        using var context = database.Context();
        await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());

        var liste = await Tasks(context).GetListAsync();

        Assert.Contains(liste.Items, t =>
            t.Source == TaskSource.InvoiceDue && t.SourceType == LinkTargetType.Invoice);
    }

    /// <summary>Ist er bezahlt, verschwindet der Vorgang von selbst.</summary>
    /// <remarks>
    /// Niemand hakt ihn ab. Der Vorgang ist eine Ansicht auf den Bestand, kein zweiter
    /// Datensatz, der gepflegt werden müsste — sonst bliebe er stehen und niemand wüsste, ob
    /// die Rechnung offen ist oder nur die Aufgabe.
    /// </remarks>
    [Fact]
    public async Task Nach_der_Zahlung_schliesst_sich_der_Vorgang()
    {
        using var context = database.Context();
        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());
        var dienst = Tasks(context);

        await dienst.GetListAsync();
        await Properties(context).PayAsync(ergebnis.Id!.Value, new PayInvoiceRequest());

        var offen = await dienst.GetListAsync(TaskState.Open);

        Assert.DoesNotContain(offen.Items, t =>
            t.Source == TaskSource.InvoiceDue && t.SourceId == ergebnis.Id);
    }

    // ── Die Zahlung ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die passende Buchung wird gefunden — auch ohne Vertrag.
    /// </summary>
    /// <remarks>
    /// Der Anbietername stand bisher nur am Vertrag. Eine Rechnung vom Prüfplatz hat keinen;
    /// ohne den Empfänger an ihr selbst fände sie ihre Buchung nur über den Betrag.
    /// </remarks>
    [Fact]
    public async Task Die_Zahlungssuche_findet_die_Buchung_ueber_den_Empfaenger()
    {
        using var context = database.Context();
        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());
        var buchung = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12), "Musterelektro Handels GmbH");

        var kandidaten = await Properties(context).GetPaymentCandidatesAsync(ergebnis.Id!.Value);

        var treffer = Assert.Single(kandidaten, k => k.TransactionId == buchung);
        Assert.True(treffer.IsBestMatch);
        Assert.Contains("Anbieter passt", treffer.Reason, StringComparison.Ordinal);
    }

    /// <summary>Zugeordnet wird erst auf Bestätigung — und mit dem Buchungstag als Zahltag.</summary>
    /// <remarks>
    /// Nicht der Tag des Klicks. Das Geld ist geflossen, als die Bank es gebucht hat; wer eine
    /// Zahlung drei Wochen später zuordnet, hat sie nicht drei Wochen später geleistet.
    /// </remarks>
    [Fact]
    public async Task Die_zugeordnete_Buchung_bestimmt_den_Zahltag()
    {
        using var context = database.Context();
        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());
        var buchung = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12), "Musterelektro Handels GmbH");

        var danach = await Properties(context).PayAsync(
            ergebnis.Id!.Value, new PayInvoiceRequest { TransactionId = buchung });

        Assert.NotNull(danach);
        Assert.Equal(InvoiceStatus.Paid, danach.Status);
        Assert.Equal(new DateOnly(2026, 8, 12), danach.PaidOn);
        Assert.True(danach.IsProven);
    }

    /// <summary>
    /// Eine Barzahlung gilt — bleibt aber als Aussage erkennbar.
    /// </summary>
    /// <remarks>
    /// Der Fall, für den es den zweiten Weg gibt: bar bezahlt, verrechnet, von einem Konto, das
    /// die Anwendung nicht führt. Sie zählt wie jede andere Zahlung; was sie von einer
    /// zugeordneten Buchung unterscheidet, ist, dass niemand sie nachprüfen kann. Genau das muss
    /// an der Rechnung stehen bleiben.
    /// </remarks>
    [Fact]
    public async Task Eine_bestaetigte_Barzahlung_gilt_bleibt_aber_unbelegt()
    {
        using var context = database.Context();
        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());

        var danach = await Properties(context).PayAsync(
            ergebnis.Id!.Value,
            new PayInvoiceRequest { PaidOn = new DateOnly(2026, 7, 20), Note = "bar bezahlt" });

        Assert.NotNull(danach);
        Assert.Equal(InvoiceStatus.Paid, danach.Status);
        Assert.Equal(new DateOnly(2026, 7, 20), danach.PaidOn);
        Assert.Equal("bar bezahlt", danach.PaymentNote);

        // Bezahlt, aber nicht belegt — und das bleibt sichtbar.
        Assert.Null(danach.TransactionId);
        Assert.False(danach.IsProven);
    }

    /// <summary>Ohne Angabe gilt der heutige Tag — nicht gar keiner.</summary>
    [Fact]
    public async Task Ohne_Datum_gilt_der_heutige_Tag()
    {
        using var context = database.Context();
        var ergebnis = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());

        var danach = await Properties(context).PayAsync(
            ergebnis.Id!.Value, new PayInvoiceRequest());

        Assert.Equal(clock.Today, danach!.PaidOn);
    }

    // ── Zwei Buchungen, die gleich aussehen ────────────────────────────────────────────────

    /// <summary>
    /// Der Vorschlag nennt den Verwendungszweck.
    /// </summary>
    /// <remarks>
    /// Zwei Bescheide desselben Absenders über denselben Betrag am selben Tag ergeben zwei
    /// Vorschläge, die Zeile für Zeile gleich aussehen: Betrag, Tag, Konto, Empfänger, sogar
    /// die Begründung. Unterscheiden lassen sie sich allein am Verwendungszweck — dort steht
    /// die Steuernummer. Ohne ihn ist die Wahl ein Münzwurf.
    /// </remarks>
    [Fact]
    public async Task Ein_Vorschlag_nennt_den_Verwendungszweck()
    {
        using var context = database.Context();

        var posten = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());
        Buchung(context, 1204.00m, new DateOnly(2026, 8, 12), "Musterelektro Handels GmbH",
            "Steuernummer 11111/22222");

        var vorschlag = Assert.Single(
            await Properties(context).GetPaymentCandidatesAsync(posten.Id!.Value));

        Assert.Equal("Steuernummer 11111/22222", vorschlag.Purpose);
    }

    /// <summary>
    /// Eine vergebene Buchung steht in der Liste, beschriftet und nicht hervorgehoben.
    /// </summary>
    /// <remarks>
    /// Sie verschwinden zu lassen wäre das Nächstliegende und das Falsche: Wer die Abbuchung
    /// sucht, die er im Konto mit eigenen Augen gesehen hat, und sie hier nicht findet, sucht
    /// weiter oder glaubt der Anwendung nicht mehr.
    /// </remarks>
    [Fact]
    public async Task Eine_vergebene_Buchung_steht_beschriftet_und_nicht_hervorgehoben()
    {
        using var context = database.Context();
        var (erster, zweiter) = await ZweiPostenAsync(context);

        var eine = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12),
            "Musterelektro Handels GmbH", "Steuernummer 11111/22222");
        var andere = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12),
            "Musterelektro Handels GmbH", "Steuernummer 33333/44444");

        await Properties(context).PayAsync(erster, new PayInvoiceRequest { TransactionId = eine });

        var vorschlaege = await Properties(context).GetPaymentCandidatesAsync(zweiter);

        var vergeben = vorschlaege.Single(v => v.TransactionId == eine);
        Assert.Equal("Rechnung Musterelektro", vergeben.AssignedTo);
        Assert.False(vergeben.IsBestMatch);

        var frei = vorschlaege.Single(v => v.TransactionId == andere);
        Assert.Null(frei.AssignedTo);
        Assert.True(frei.IsBestMatch);
    }

    /// <summary>
    /// Dieselbe Buchung kann nicht zwei Posten begleichen.
    /// </summary>
    /// <remarks>
    /// Die Liste beschriftet eine vergebene Buchung bereits, und der Knopf daran ist tot. Das
    /// hier ist die Sperre dahinter: Eine Beschriftung ist keine Regel. Ohne sie stünde eine
    /// Zahlung doppelt in den Büchern und eine andere gar nicht — und beide Posten sähen
    /// bezahlt aus.
    /// </remarks>
    [Fact]
    public async Task Dieselbe_Buchung_kann_nicht_zwei_Posten_begleichen()
    {
        using var context = database.Context();
        var (erster, zweiter) = await ZweiPostenAsync(context);

        var eine = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12),
            "Musterelektro Handels GmbH", "Steuernummer 11111/22222");

        await Properties(context).PayAsync(erster, new PayInvoiceRequest { TransactionId = eine });

        var fehler = await Assert.ThrowsAsync<RuleViolationException>(
            () => Properties(context).PayAsync(zweiter, new PayInvoiceRequest { TransactionId = eine }));

        Assert.Contains("bereits", fehler.Message);

        // Abgewiesen heißt unverändert: Der zweite Posten steht weiter offen. Geprüft wird am
        // verfolgten Datensatz — so fällt auch auf, wenn etwas geändert und nur nicht
        // gespeichert wurde.
        var unberuehrt = context.Invoices.Single(i => i.Id == zweiter);
        Assert.Equal(InvoiceStatus.Open, unberuehrt.Status);
        Assert.Null(unberuehrt.TransactionId);
    }

    /// <summary>
    /// Eine vergebene Buchung wird nicht von schwachen Vorschlägen verdrängt.
    /// </summary>
    /// <remarks>
    /// <para>Der erste Versuch sortierte vergebene Buchungen ans Ende — und damit aus der
    /// Liste, denn sie zeigt sechs. Im Probelauf gegen den echten Bestand war die gesuchte
    /// Abbuchung danach spurlos weg, während ein halbes Dutzend Vorschläge mit zehn Punkten
    /// dastand. Zwei Tests mit zwei Buchungen hatten das nicht gezeigt: Dort griff die Grenze
    /// nie.</para>
    /// <para>Die Punktzahl sagt, wie gut eine Buchung passt. Ob sie frei ist, sagt die
    /// Beschriftung.</para>
    /// </remarks>
    [Fact]
    public async Task Eine_vergebene_Buchung_wird_nicht_verdraengt()
    {
        using var context = database.Context();
        var (erster, zweiter) = await ZweiPostenAsync(context);

        var passend = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12),
            "Musterelektro Handels GmbH", "Steuernummer 11111/22222");

        // Acht Buchungen, die nur über das Datum punkten — mehr als die Liste zeigt.
        for (var i = 1; i <= 8; i++)
        {
            Buchung(context, 10m * i, new DateOnly(2026, 8, 20), $"Musterhandel {i}", $"Einkauf {i}");
        }

        await Properties(context).PayAsync(erster, new PayInvoiceRequest { TransactionId = passend });

        var vorschlaege = await Properties(context).GetPaymentCandidatesAsync(zweiter);

        var vergeben = vorschlaege.Single(v => v.TransactionId == passend);
        Assert.Equal("Rechnung Musterelektro", vergeben.AssignedTo);
        Assert.False(vergeben.IsBestMatch);
    }

    /// <summary>Ein Posten darf seine eigene Buchung behalten.</summary>
    /// <remarks>
    /// Die Sperre fragt nach <em>anderen</em> Posten. Fragte sie nach allen, ließe sich eine
    /// einmal zugeordnete Zahlung nie wieder bestätigen.
    /// </remarks>
    [Fact]
    public async Task Ein_Posten_darf_seine_eigene_Buchung_behalten()
    {
        using var context = database.Context();
        var posten = (await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder())).Id!.Value;

        var eine = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12), "Musterelektro Handels GmbH");

        await Properties(context).PayAsync(posten, new PayInvoiceRequest { TransactionId = eine });
        var nochmal = await Properties(context).PayAsync(posten, new PayInvoiceRequest { TransactionId = eine });

        Assert.Equal(eine, nochmal!.TransactionId);
    }

    /// <summary>
    /// Ein stornierter Posten hält seine Buchung nicht fest.
    /// </summary>
    /// <remarks>
    /// Storniert heißt: Diesen Posten gibt es nicht mehr. Hielte er die Buchung weiter, wäre
    /// eine Zahlung für immer vergeben, ohne dass noch eine Forderung dahinterstünde.
    /// </remarks>
    [Fact]
    public async Task Ein_stornierter_Posten_gibt_seine_Buchung_frei()
    {
        using var context = database.Context();
        var (erster, zweiter) = await ZweiPostenAsync(context);

        var eine = Buchung(context, 1204.00m, new DateOnly(2026, 8, 12), "Musterelektro Handels GmbH");
        await Properties(context).PayAsync(erster, new PayInvoiceRequest { TransactionId = eine });

        context.Invoices.Single(i => i.Id == erster).Status = InvoiceStatus.Cancelled;
        context.SaveChanges();

        var vorschlag = Assert.Single(await Properties(context).GetPaymentCandidatesAsync(zweiter));
        Assert.Null(vorschlag.AssignedTo);

        var danach = await Properties(context).PayAsync(zweiter, new PayInvoiceRequest { TransactionId = eine });
        Assert.Equal(eine, danach!.TransactionId);
    }

    /// <summary>Zwei offene Posten über denselben Betrag, unterscheidbar am Betreff.</summary>
    private async Task<(int Erster, int Zweiter)> ZweiPostenAsync(FinanzAppDbContext context)
    {
        var erster = await Forms(context).CreateAsync(CreateObjectType.Invoice, Felder());

        var zweiteFelder = Felder();
        zweiteFelder["subject"] = "Rechnung Musterelektro, zweites Gerät";
        zweiteFelder["number"] = "VR5000012346";

        var zweiter = await Forms(context).CreateAsync(CreateObjectType.Invoice, zweiteFelder);

        return (erster.Id!.Value, zweiter.Id!.Value);
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
