namespace FinanzApp.Api.Data;

/// <summary>Ein Satz aus einer Importdatei, so wie ihn ein CAMT- oder CSV-Parser liefern würde.</summary>
/// <param name="Reference">Referenz aus der Datei. Trägt die Wiedererkennung bereits importierter Sätze.</param>
/// <param name="BookingDate">Buchungstag. <c>null</c> bei einem unlesbaren Satz.</param>
/// <param name="Payee">Empfänger beziehungsweise Auftraggeber.</param>
/// <param name="Amount">Vorzeichenbehafteter Betrag. <c>null</c> bei einem unlesbaren Satz.</param>
public sealed record ImportRecord(string Reference, DateOnly? BookingDate, string Payee, decimal? Amount);

/// <summary>
/// Steht für die Datei, die im Handoff auf dem Importscreen liegt.
/// </summary>
/// <remarks>
/// Es gibt in diesem Stand noch keinen CAMT-Parser und keinen Datei-Upload. Der Stapel ersetzt
/// beides, damit Vorschau und Übernahme mit echter Logik laufen: Referenzabgleich, Duplikatprüfung
/// und transaktionale Übernahme arbeiten auf diesen Sätzen genauso wie später auf einer echten Datei.
/// Die fünf bereits vorhandenen und die zwei verdächtigen Sätze sind absichtlich so gewählt, dass
/// sie auf die Beispieldaten treffen.
/// </remarks>
public static class DemoImportBatch
{
    /// <summary>Feste Id, damit die Vorschau über mehrere Aufrufe hinweg dieselbe bleibt.</summary>
    public static readonly Guid PreviewId = new("6f1c1f4a-1a2e-4d1f-9d0b-2f5c7a0a1e01");

    public const string FileName = "camt053_2026-08.xml";
    public const string BankName = "Sparkasse";
    public const string Format = "CAMT.053";
    public const string ProfileName = "Sparkasse Standard";

    /// <summary>Konto, auf das der Stapel gebucht wird.</summary>
    public const string AccountName = "Sparkasse Giro";

    public static IReadOnlyList<ImportRecord> Records { get; } = Build();

    private static List<ImportRecord> Build()
    {
        // 34 Sätze, die die Beispieldaten noch nicht kennen.
        (int Day, string Payee, decimal Amount)[] fresh =
        [
            (22, "Kiosk am Bismarckplatz", -4.80m),
            (22, "DB Vertrieb Fahrschein", -18.90m),
            (21, "Parkhaus P7 Altstadt", -6.50m),
            (21, "Netflix Abo", -17.99m),
            (20, "Blumen Nagel", -24.00m),
            (20, "Bahnhofsbuchhandlung", -9.90m),
            (19, "Pizzeria Da Luigi", -38.60m),
            (18, "Baumarkt Hornbach", -73.45m),
            (17, "Friseur Salon Elf", -42.00m),
            (17, "Spotify Abo", -11.99m),
            (16, "Eiscafé San Marco", -13.20m),
            (15, "Rossmann Drogerie", -28.75m),
            (14, "Tierarzt Dr. Kaltenbach", -96.40m),
            (14, "Post Filiale Porto", -7.35m),
            (13, "Getränkemarkt Fristo", -34.90m),
            (12, "Schuhhaus Görtz", -89.95m),
            (11, "Reinigung Textilpflege", -22.50m),
            (11, "Bäckerei Göbes", -8.60m),
            (10, "Optiker Fielmann", -149.00m),
            (9, "Weinhandlung Kurpfalz", -56.80m),
            (9, "Kiosk am Bismarckplatz", -3.40m),
            (8, "Fahrradladen Radschlag", -64.20m),
            (7, "Copyshop Uni", -12.80m),
            (6, "Metzgerei Lang", -31.15m),
            (5, "Theater Heidelberg", -46.00m),
            (5, "Parkhaus P7 Altstadt", -5.00m),
            (4, "Elektromarkt Saturn", -119.00m),
            (3, "Gärtnerei Sommer", -37.70m),
            (3, "Kiosk am Bismarckplatz", -2.90m),
            (2, "Sportstudio Tagesticket", -14.00m),
            (2, "Second Hand Buchladen", -16.50m),
            (1, "Zinsgutschrift Sparkasse", 3.42m),
            (1, "Versicherungskammer Beitrag", -48.30m),
            (1, "Kita Elternbeitrag", -180.00m),
        ];

        var records = fresh
            .Select((row, index) => new ImportRecord(
                Reference: "CAMT-2608-" + (index + 1).ToString("000"),
                BookingDate: new DateOnly(2026, 8, row.Day),
                Payee: row.Payee,
                Amount: row.Amount))
            .ToList();

        // Fünf Sätze, die die Beispieldaten unter derselben Referenz bereits enthalten.
        (string Reference, int Day, string Payee, decimal Amount)[] known =
        [
            ("SEED-4200", 22, "REWE Markt Heidelberg", -68.42m),
            ("SEED-4201", 21, "Shell Tankstelle", -84.10m),
            ("SEED-4202", 20, "Stadtwerke Strom", -96.00m),
            ("SEED-4208", 18, "Spende Tierheim", -80.00m),
            ("SEED-4212", 14, "EDEKA Neckargemünd", -87.20m),
        ];

        records.AddRange(known.Select(row => new ImportRecord(
            row.Reference, new DateOnly(2026, 8, row.Day), row.Payee, row.Amount)));

        // Zwei Sätze mit neuer Referenz, aber Tag, Empfänger und Betrag einer vorhandenen Buchung.
        records.Add(new ImportRecord("CAMT-2608-901", new DateOnly(2026, 8, 13), "ARAL Tankstelle", -92.30m));
        records.Add(new ImportRecord("CAMT-2608-902", new DateOnly(2026, 8, 10), "Amazon Bestellung", -62.71m));

        // Ein Satz mit unlesbarem Betrag — mit Absicht, wie das Dokument ohne Datei. Sonst
        // ließe sich der Zustand „Fehlerhafte Sätze“ nirgends vorführen, und der Handoff
        // verlangt ausdrücklich, dass solche Sätze gezählt und benannt werden statt
        // stillschweigend übersprungen zu werden.
        records.Add(new ImportRecord("CAMT-2608-903", new DateOnly(2026, 8, 16), "Unleserlicher Beleg", null));

        return records;
    }
}
