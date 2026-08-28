using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FinanzApp.Api.Data;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Ein eingelesener Kontoauszug, unabhängig vom Dateiformat.</summary>
public sealed record ParsedStatement
{
    public required string FileName { get; init; }

    /// <summary>Das erkannte Format, so genau wie die Datei es hergibt — z. B. „CAMT.052.001.08“.</summary>
    public required string Format { get; init; }

    public string? BankName { get; init; }

    /// <summary>IBAN des Kontos, auf das sich der Auszug bezieht. Ordnet das Zielkonto zu.</summary>
    public string? Iban { get; init; }

    /// <summary>Schlusssaldo, sofern die Datei einen nennt.</summary>
    public decimal? ClosingBalance { get; init; }

    /// <summary>
    /// Aus wie vielen Auszugsdateien die Sätze stammen. 1 bei einer einzelnen Datei.
    /// </summary>
    /// <remarks>
    /// Ein Archiv wird zu einer Vorschau zusammengelegt. Ohne diese Zahl stünde im Kopf nur der
    /// Archivname, und die Frage „sind wirklich alle acht drin?“ bliebe unbeantwortet.
    /// </remarks>
    public int SourceCount { get; init; } = 1;

    /// <summary>
    /// Alle Sätze der Datei — auch die, aus denen keine Buchung wird. Die tragen dann ein
    /// <see cref="ImportRecord.Problem"/> und stehen in der Liste, statt zu verschwinden.
    /// </summary>
    public required IReadOnlyList<ImportRecord> Records { get; init; }
}

/// <summary>Liest eine Auszugsdatei in Sätze.</summary>
/// <remarks>
/// Eine Schnittstelle, weil das Dateiformat den Fachcode nichts angeht: Vorschau, Duplikatprüfung
/// und Übernahme arbeiten auf <see cref="ImportRecord"/> — ob die aus CAMT, CSV oder MT940 kommen,
/// ändert daran nichts.
/// </remarks>
public interface IStatementParser
{
    /// <summary>Ob dieser Leser die Datei überhaupt anfassen will.</summary>
    bool CanRead(string fileName);

    /// <summary>
    /// Liest die Datei. Wirft <see cref="StatementFormatException"/>, wenn sie nicht zum Format passt.
    /// </summary>
    Task<ParsedStatement> ParseAsync(Stream content, string fileName, CancellationToken ct = default);
}

/// <summary>Die Datei ist nicht das, was sie sein sollte. Die Meldung geht an den Benutzer.</summary>
public sealed class StatementFormatException(string message) : Exception(message);

/// <summary>
/// Liest ISO-20022-Auszüge: camt.052 (Umsatzabfrage) und camt.053 (Kontoauszug).
/// </summary>
/// <remarks>
/// <para>Beide Formate sind unterhalb von <c>Ntry</c> gleich aufgebaut; sie unterscheiden sich nur
/// im Namen des Wurzelberichts (<c>BkToCstmrAcctRpt/Rpt</c> gegenüber <c>BkToCstmrStmt/Stmt</c>).
/// Deshalb liest derselbe Leser beide — 053 zusätzlich zu unterstützen kostet einen Elementnamen.</para>
/// <para>Gesucht wird ausschließlich über <em>lokale</em> Elementnamen. Die Namensräume tragen die
/// Version (<c>…camt.052.001.02</c> bis <c>…001.08</c>), und die Banken liefern unterschiedliche.
/// Ein Leser, der auf einen Namensraum festgenagelt ist, versteht die Datei der nächsten Bank nicht
/// mehr.</para>
/// </remarks>
public sealed class CamtStatementParser : IStatementParser
{
    /// <summary>Eine Auszugsdatei ist Text; alles jenseits davon ist keine.</summary>
    public const int MaxBytes = 20 * 1024 * 1024;

    public bool CanRead(string fileName)
        => fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
           || fileName.EndsWith(".camt", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedStatement> ParseAsync(
        Stream content, string fileName, CancellationToken ct = default)
    {
        var document = await LoadAsync(content, ct);
        var root = document.Root
                   ?? throw new StatementFormatException("Die Datei enthält kein XML-Dokument.");

        // 052 heißt BkToCstmrAcctRpt und führt Rpt, 053 heißt BkToCstmrStmt und führt Stmt.
        var reports = Descend(root, "BkToCstmrAcctRpt").SelectMany(x => Children(x, "Rpt"))
            .Concat(Descend(root, "BkToCstmrStmt").SelectMany(x => Children(x, "Stmt")))
            .ToList();

        if (reports.Count == 0)
        {
            throw new StatementFormatException(
                "Kein camt.052 oder camt.053: die Datei enthält weder BkToCstmrAcctRpt noch BkToCstmrStmt.");
        }

        var records = new List<ImportRecord>();

        foreach (var report in reports)
        {
            var statementId = Text(Child(report, "Id"));

            foreach (var entry in Children(report, "Ntry"))
            {
                ct.ThrowIfCancellationRequested();
                Read(entry, records, statementId);
            }
        }

        var account = reports.Select(r => Child(r, "Acct")).FirstOrDefault(a => a is not null);

        return new ParsedStatement
        {
            FileName = fileName,
            Format = FormatOf(root),
            BankName = BankNameOf(account),
            Iban = Text(Child(Child(account, "Id"), "IBAN")),
            ClosingBalance = ClosingBalanceOf(reports),
            Records = records,
        };
    }

    /// <summary>
    /// Lädt das Dokument ohne DTD und ohne externe Auflösung.
    /// </summary>
    /// <remarks>
    /// Eine Auszugsdatei kommt von außen. Mit erlaubter DTD ließe sich über eine externe Entität
    /// jede Datei des Servers in die Antwort ziehen; <c>Prohibit</c> lässt eine solche Datei
    /// scheitern, statt sie zu verarbeiten.
    /// </remarks>
    private static async Task<XDocument> LoadAsync(Stream content, CancellationToken ct)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            Async = true,
        };

        try
        {
            using var reader = XmlReader.Create(content, settings);
            return await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        }
        catch (XmlException ex)
        {
            throw new StatementFormatException($"Die Datei ist kein gültiges XML: {ex.Message}");
        }
    }

    /// <summary>Ein einzelner Umsatz.</summary>
    /// <remarks>
    /// Ein <c>Ntry</c> kann mehrere <c>TxDtls</c> tragen (ein Sammler, etwa ein Lastschrifteinzug).
    /// Tragen die Einzelposten eigene Beträge, werden sie einzeln übernommen — sonst bliebe die
    /// Zuordnung zu Empfängern und Kategorien Raten. Sonst zählt der Sammelbetrag.
    /// </remarks>
    private static void Read(XElement entry, List<ImportRecord> records, string? statementId)
    {
        // Ein vorgemerkter Umsatz ist keine Buchung. Er verschwindet trotzdem nicht, sondern
        // steht mit Grund in der Liste — sonst fehlten am Ende Sätze, die niemand erklärt hat.
        var status = StatusOf(entry);
        var problem = status is null or "BOOK"
            ? null
            : $"nur vorgemerkt ({status}), noch nicht gebucht";

        var sign = SignOf(entry);
        var date = DateOf(entry);
        var details = Descend(entry, "TxDtls").ToList();

        var split = details.Count > 1 && details.All(d => Child(d, "Amt") is not null);
        if (split)
        {
            foreach (var detail in details)
            {
                records.Add(Build(
                    entry, detail, date, DirectionOf(detail) ?? sign, Amount(detail), problem, statementId));
            }

            return;
        }

        records.Add(Build(
            entry, details.FirstOrDefault(), date, sign, Amount(entry), problem, statementId));
    }

    private static ImportRecord Build(
        XElement entry, XElement? detail, DateOnly? date, int sign, decimal? amount,
        string? problem, string? statementId)
    {
        var payee = PayeeOf(entry, detail, sign);

        return new ImportRecord(
            Reference: ReferenceOf(entry, detail, date, payee, amount),
            BookingDate: date,
            Payee: payee,
            Amount: amount is { } value ? sign * value : null,
            Problem: problem,
            Details: DetailsOf(entry, detail, sign, statementId));
    }

    /// <summary>
    /// Die übrigen Felder des Auszugs.
    /// </summary>
    /// <remarks>
    /// Jedes Feld bleibt <c>null</c>, wenn die Datei es nicht liefert. Der Unterschied zwischen
    /// „steht nicht im Auszug“ und „steht drin, ist leer“ geht sonst verloren — und die Anzeige
    /// verspricht, genau diesen Unterschied zu zeigen.
    /// </remarks>
    private static StatementDetails DetailsOf(
        XElement entry, XElement? detail, int sign, string? statementId)
    {
        var parties = Child(detail, "RltdPties");
        var agents = Child(detail, "RltdAgts");
        var gegen = sign < 0 ? "Cdtr" : "Dbtr";

        return new StatementDetails(
            ValueDate: DateIn(Child(entry, "ValDt")),
            Currency: Attribute(Child(detail, "Amt") ?? Child(entry, "Amt"), "Ccy"),
            CounterpartyIban: Text(Child(Child(Child(parties, gegen + "Acct"), "Id"), "IBAN")),
            CounterpartyBic: BicOf(Child(agents, gegen + "Agt")),
            Purpose: Purpose(detail),
            BookingText: Text(Child(entry, "AddtlNtryInf")),
            BankTransactionCode: DomainCodeOf(Child(detail, "BkTxCd") ?? Child(entry, "BkTxCd")),
            ProprietaryCode: Text(Child(Child(entry, "BkTxCd"), "Prtry", "Cd"))
                             ?? Text(Child(Child(detail, "BkTxCd"), "Prtry", "Cd")),
            StatementId: statementId);
    }

    /// <summary>Der Geschäftsvorfall nach ISO, zusammengesetzt als <c>PMNT-RDDT-ESDD</c>.</summary>
    private static string? DomainCodeOf(XElement? code)
    {
        var domain = Child(code, "Domn");
        if (domain is null)
        {
            return null;
        }

        var teile = new[]
        {
            Text(Child(domain, "Cd")),
            Text(Child(Child(domain, "Fmly"), "Cd")),
            Text(Child(Child(domain, "Fmly"), "SubFmlyCd")),
        }.Where(t => t is not null);

        var zusammen = string.Join("-", teile);

        return zusammen.Length == 0 ? null : zusammen;
    }

    /// <summary>Neuere Fassungen schreiben <c>BICFI</c>, ältere <c>BIC</c>.</summary>
    private static string? BicOf(XElement? agent)
        => Text(Child(Child(agent, "FinInstnId"), "BICFI"))
           ?? Text(Child(Child(agent, "FinInstnId"), "BIC"));

    private static string? Attribute(XElement? element, string name)
    {
        var wert = element?.Attribute(name)?.Value.Trim();
        return string.IsNullOrEmpty(wert) ? null : wert;
    }

    /// <summary>
    /// Der Betrag steht in der Datei immer <b>ohne</b> Vorzeichen; die Richtung trägt
    /// <c>CdtDbtInd</c>.
    /// </summary>
    /// <remarks>
    /// Das ist die Stelle, an der ein Auszug am leisesten kippt: wer den Betrag nimmt und den
    /// Indikator übersieht, bucht jede Abbuchung als Eingang und die Bilanz sieht großartig aus.
    /// </remarks>
    private static int SignOf(XElement element) => DirectionOf(element) ?? 1;

    /// <summary><c>null</c>, wenn das Element gar keine Richtung nennt — dann gilt die des Sammlers.</summary>
    private static int? DirectionOf(XElement element)
    {
        var indicator = Text(Child(element, "CdtDbtInd"));

        return indicator is null
            ? null
            : indicator.Equals("DBIT", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
    }

    private static decimal? Amount(XElement element)
        => decimal.TryParse(
            Text(Child(element, "Amt")), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Gebucht oder vorgemerkt. Ältere Fassungen schreiben den Code direkt, neuere in <c>Cd</c>.</summary>
    private static string? StatusOf(XElement entry)
    {
        var status = Child(entry, "Sts");
        if (status is null)
        {
            return null;
        }

        var code = Text(Child(status, "Cd")) ?? Text(status);
        return string.IsNullOrWhiteSpace(code) ? null : code.ToUpperInvariant();
    }

    /// <summary>Buchungstag; ersatzweise die Wertstellung.</summary>
    private static DateOnly? DateOf(XElement entry)
        => DateIn(Child(entry, "BookgDt")) ?? DateIn(Child(entry, "ValDt"));

    private static DateOnly? DateIn(XElement? holder)
    {
        if (holder is null)
        {
            return null;
        }

        var text = Text(Child(holder, "Dt")) ?? Text(Child(holder, "DtTm"));
        if (text is null)
        {
            return null;
        }

        // DtTm bringt noch die Uhrzeit mit; für eine Buchung zählt der Tag.
        return DateOnly.TryParseExact(
            text.Length > 10 ? text[..10] : text, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>
    /// Wer auf der anderen Seite steht — bei einer Abbuchung der Empfänger, bei einem Eingang der
    /// Zahler.
    /// </summary>
    /// <remarks>
    /// Immer den Gläubiger zu nehmen wäre falsch herum: bei einer Gutschrift sind wir selbst der
    /// Gläubiger, und in der Liste stünde dann der eigene Name.
    /// </remarks>
    private static string PayeeOf(XElement entry, XElement? detail, int sign)
    {
        var parties = Child(detail, "RltdPties");
        var counterpart = sign < 0 ? "Cdtr" : "Dbtr";

        var genannt = Text(Child(Child(parties, counterpart), "Nm"))
                      ?? Text(Child(Child(parties, counterpart), "Pty", "Nm"));

        var zweck = Purpose(detail);
        var haendler = MerchantIn(zweck) ?? ShopIn(zweck);

        // Steht im Zweck ein Händler und heißt der Genannte anders, ist der Genannte der
        // Zahlungsdienstleister. Dann zählt der Händler.
        if (haendler is not null
            && (genannt is null || !Categorization.Matches(haendler, Categorization.RulePatternFor(genannt))))
        {
            return Shorten(haendler)!;
        }

        return Shorten(genannt ?? zweck ?? Text(Child(entry, "AddtlNtryInf"))) ?? "Ohne Empfänger";
    }

    /// <summary>
    /// Der Händler aus dem Verwendungszweck einer Kartenzahlung.
    /// </summary>
    /// <remarks>
    /// <para>Bei einer Kartenzahlung nennt das Gläubigerfeld oft nicht den Laden, sondern den
    /// Zahlungsdienstleister — „PAYONE GmbH“, „DZ BANK AG“ — oder gleich den Platzhalter
    /// „Lastschrift aus Kartenzahlung“. Der Laden steht dann im Zweck, und ohne ihn fallen
    /// Dutzende Einkäufe an ganz verschiedenen Orten unter einen nichtssagenden Namen.</para>
    /// <para>Der Zweck folgt dem ELV-Muster: Name, Straße und Ort, dann Datum und Uhrzeit. Zwei
    /// Schreibweisen kommen vor — <c>…/DE 31.12.2025 um 19:08:01 Uhr</c> und
    /// <c>…/D02.01.2026 / 18:58 Ortszeit</c>. Beide enden den Kopf am Datum; abgeschnitten wird
    /// dort und sonst nirgends, denn Händlernamen enthalten selbst Schrägstriche
    /// („Setzer 24/7 Vell.“).</para>
    /// </remarks>
    private static string? MerchantIn(string? purpose)
    {
        if (purpose is null)
        {
            return null;
        }

        var treffer = CardPurpose.Match(purpose);
        if (!treffer.Success)
        {
            return null;
        }

        var kopf = treffer.Groups[1].Value.Trim().TrimEnd('/');

        return kopf.Length < 3 ? null : kopf;
    }

    /// <summary>
    /// Der Laden hinter einem Zahlungsdienstleister, der ihn im Zweck nennt.
    /// </summary>
    /// <remarks>
    /// PayPal bucht unter eigenem Namen und schreibt den Laden in den Zweck: <c>… Ihr Einkauf bei
    /// LaVita GmbH EREF: …</c>. Ohne das lägen alle PayPal-Zahlungen unter einem Namen, obwohl
    /// dahinter Apotheke, Bahnfahrt und Tierbedarf stehen.
    ///
    /// Oft bleibt die Stelle allerdings leer — in einer echten Datei bei 21 von 36 Sätzen. Dann
    /// bleibt es beim Dienstleister, und das ist ehrlicher als ein geratener Name.
    /// </remarks>
    private static string? ShopIn(string? purpose)
    {
        if (purpose is null)
        {
            return null;
        }

        var treffer = ShopPurpose.Match(purpose);

        return treffer.Success ? WithoutDate(treffer.Groups[1].Value.Trim()) : null;
    }

    private static readonly Regex ShopPurpose = new(
        @"Ihr Einkauf bei\s+(\S[^,]{2,60}?)\s+EREF:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Streicht ein angehängtes Buchungsdatum aus dem Namen.
    /// </summary>
    /// <remarks>
    /// Manche Häuser schreiben <c>Ihr Einkauf bei EDEKA Möller vom 30.12.2025</c>. Bliebe das
    /// Datum stehen, wäre jeder Einkauf ein eigener Empfänger — aus einer Gruppe mit 42 Sätzen
    /// würden 42 Gruppen, und der Nutzer beantwortete zweiundvierzigmal dieselbe Frage. Ein Name,
    /// der den Tag der Buchung trägt, ist keiner.
    /// </remarks>
    private static string WithoutDate(string name)
    {
        var ohne = TrailingDate.Replace(name, string.Empty).TrimEnd();

        return ohne.Length < 3 ? name : ohne;
    }

    private static readonly Regex TrailingDate = new(
        @"\s+(?:vom\s+)?\d{2}\.\d{2}\.\d{2,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Alles vor dem Zahlungsdatum; Länderkürzel und Terminalziffer davor fallen weg.
    /// </summary>
    /// <remarks>
    /// Drei Schreibweisen sind in einer einzigen Bankdatei belegt:
    /// <c>…/Wolpertshausen/DE 31.12.2025 um 19:08:01 Uhr</c>,
    /// <c>…/Schwaebisch H/D02.01.2026 / 18:58 Ortszeit</c> und
    /// <c>…/SchwaebischHa/DE/0 16.01.2026 / 16:01 Ortszeit</c>. Sie unterscheiden sich allein
    /// zwischen Ort und Datum — dort wird geschnitten und sonst nirgends.
    /// </remarks>
    private static readonly Regex CardPurpose = new(
        @"^(.{3,70}?)/D[E]?(?:/\d+)?\s?\d{2}\.\d{2}\.\d{4}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? Purpose(XElement? detail)
    {
        var parts = Descend(Child(detail, "RmtInf"), "Ustrd")
            .Select(x => x.Value.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>Verwendungszwecke werden lang; die Liste zeigt eine Zeile.</summary>
    private static string? Shorten(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= 80 ? trimmed : trimmed[..79].TrimEnd() + "…";
    }

    /// <summary>
    /// Die Referenz, an der ein bereits gebuchter Satz wiedererkannt wird.
    /// </summary>
    /// <remarks>
    /// <para>Die Reihenfolge ist nicht beliebig: erst was die <b>Bank</b> vergeben hat —
    /// <c>AcctSvcrRef</c>, <c>TxId</c>, dann die hauseigene Umsatz-Id aus <c>Refs/Prtry/Ref</c>
    /// (die Sparkassen tragen dort <c>FI-UMSATZ-ID</c> ein und lassen <c>AcctSvcrRef</c> auf
    /// <c>NONREF</c> stehen). Sie ist pro Buchung vergeben und über Wiederholungen desselben
    /// Auszugs stabil, und genau das trägt die Duplikatprüfung.</para>
    /// <para>Erst danach kommt, was der <b>Zahler</b> mitgegeben hat. Die <c>EndToEndId</c>
    /// gehört ihm: sie steht oft auf <c>NOTPROVIDED</c>, und ein Dauerauftrag schickt Monat für
    /// Monat dieselbe. Als Wiedererkennungsmerkmal wäre sie dann derselbe Fehler, den
    /// <c>NONREF</c> gemacht hat — aus zwölf Buchungen würde eine.</para>
    /// <para>Fehlt alles, wird aus Tag, Empfänger und Betrag ein Fingerabdruck gebildet —
    /// derselbe Satz ergibt dieselbe Referenz, ein anderer eine andere.</para>
    /// </remarks>
    private static string ReferenceOf(
        XElement entry, XElement? detail, DateOnly? date, string payee, decimal? amount)
    {
        var references = Child(detail, "Refs");

        var given = Clean(Text(Child(entry, "AcctSvcrRef")))
                    ?? Clean(Text(Child(references, "AcctSvcrRef")))
                    ?? Clean(Text(Child(references, "TxId")))
                    ?? Proprietary(references)
                    ?? Clean(Text(Child(references, "EndToEndId")))
                    ?? Clean(Text(Child(entry, "NtryRef")));

        if (given is not null)
        {
            return "CAMT:" + given;
        }

        var fingerprint = string.Create(
            CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd}|{payee}|{amount:0.00}");

        return "CAMT:~" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..16];
    }

    /// <summary>Die hauseigene Umsatz-Id der Bank, sofern eine dasteht.</summary>
    private static string? Proprietary(XElement? references)
        => Children(references, "Prtry")
            .Select(p => Clean(Text(Child(p, "Ref"))))
            .FirstOrDefault(r => r is not null);

    /// <summary>
    /// Platzhalter, die wie eine Kennung aussehen.
    /// </summary>
    /// <remarks>
    /// <para><c>NONREF</c> fehlte hier. Acht Tagesauszüge der Sparkasse Schwäbisch Hall trugen es
    /// alle, bekamen damit dieselbe Importreferenz, und sobald einer eingelesen war, meldete die
    /// Vorschau die übrigen sieben als „vorhanden“ — sieben echte Buchungen, die sich nicht mehr
    /// importieren ließen. Ein Platzhalter, den man für eine Kennung hält, ist schlimmer als gar
    /// keine: er macht aus verschiedenen Sätzen denselben.</para>
    /// <para>Nur belegte Platzhalter gehören in die Liste. Beide stehen so in den Auszügen, die
    /// hier ankommen. Ein weiterer auf Verdacht wäre der umgekehrte Fehler: eine echte,
    /// kurze Referenz würde weggeworfen, und der Fingerabdruck träte an ihre Stelle.</para>
    /// </remarks>
    private static readonly string[] Placeholders = ["NOTPROVIDED", "NONREF"];

    /// <summary>Ein Platzhalter ist keine Referenz, sondern das Eingeständnis, keine zu haben.</summary>
    private static string? Clean(string? text)
    {
        var trimmed = text?.Trim();

        return string.IsNullOrEmpty(trimmed)
               || Placeholders.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    /// <summary>Der Schlusssaldo (<c>CLBD</c>); ersatzweise der letzte genannte Saldo.</summary>
    private static decimal? ClosingBalanceOf(List<XElement> reports)
    {
        var balances = reports.SelectMany(r => Children(r, "Bal")).ToList();

        var closing = balances.LastOrDefault(b =>
                          string.Equals(
                              Text(Child(Child(Child(b, "Tp"), "CdOrPrtry"), "Cd")),
                              "CLBD", StringComparison.OrdinalIgnoreCase))
                      ?? balances.LastOrDefault();

        if (closing is null || Amount(closing) is not { } value)
        {
            return null;
        }

        return SignOf(closing) * value;
    }

    private static string? BankNameOf(XElement? account)
        => Shorten(Text(Child(Child(Child(account, "Svcr"), "FinInstnId"), "Nm"))
                   ?? Text(Child(Child(Child(account, "Svcr"), "FinInstnId"), "BIC"))
                   ?? Text(Child(Child(Child(account, "Svcr"), "FinInstnId"), "BICFI")));

    /// <summary>Das Format steht im Namensraum: <c>…tech:xsd:camt.052.001.08</c>.</summary>
    private static string FormatOf(XElement root)
    {
        var space = root.Name.NamespaceName;
        var marker = space.LastIndexOf("camt.", StringComparison.OrdinalIgnoreCase);

        return marker < 0 ? "CAMT" : space[marker..].ToUpperInvariant();
    }

    // ── Suche über lokale Namen, damit die Version des Namensraums egal bleibt ──────────────

    private static IEnumerable<XElement> Children(XContainer? parent, string name)
        => parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static IEnumerable<XElement> Descend(XContainer? parent, string name)
        => parent?.Descendants().Where(e => e.Name.LocalName == name) ?? [];

    private static XElement? Child(XContainer? parent, string name)
        => Children(parent, name).FirstOrDefault();

    /// <summary>Zwei Ebenen am Stück — spart die Klammerkaskade an den tiefen Pfaden.</summary>
    private static XElement? Child(XContainer? parent, string name, string then)
        => Child(Child(parent, name), then);

    private static string? Text(XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
