using System.Globalization;
using System.Text.RegularExpressions;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Wie ein gelesener Wert zu verstehen ist.</summary>
public enum DocumentValueKind
{
    Text,
    Money,

    /// <summary>Stückzahl — ganzzahlig oder mit Bruchteilen, aber ohne Währung.</summary>
    Quantity,

    /// <summary>Wertpapierkurs. Mehr Nachkommastellen als Geld, deshalb eigen.</summary>
    Price,
    Date,
}

/// <summary>Wo ein Feld im Text steht.</summary>
public enum DocumentLocator
{
    /// <summary>Beschriftung links, Wert rechts in derselben Zeile.</summary>
    Label,

    /// <summary>Der Wert steckt in einer Zeile und wird per Muster herausgeschnitten.</summary>
    Pattern,

    /// <summary>Der Wert steht in der Zeile <em>nach</em> einer Beschriftung.</summary>
    NextLine,
}

/// <summary>Womit eine Rechenprobe prüft.</summary>
public enum DocumentCheckKind
{
    /// <summary>Ergebnis = Summe der Teile.</summary>
    Sum,

    /// <summary>Ergebnis = Produkt der Teile.</summary>
    Product,
}

/// <summary>
/// Ein Feld eines Dokumenttyps.
/// </summary>
/// <remarks>
/// <see cref="Lead"/> und <see cref="Soft"/> sind die beiden Kennzeichen aus Abschnitt 14.2 des
/// Handoffs: das erste sagt, dass der Wert ins Objekt übernommen wird, das zweite, dass er nicht
/// garantiert ist. Ein <c>soft</c>-Wert darf nie <c>lead</c> sein — Bewertungsreserven gehören in
/// keine Vermögenssumme.
/// </remarks>
public sealed record DocumentFieldRule
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required DocumentValueKind Kind { get; init; }
    public DocumentLocator Locator { get; init; } = DocumentLocator.Label;

    /// <summary>
    /// Abschnitt, in dem gesucht wird — <c>null</c> heißt: im ganzen Dokument.
    /// </summary>
    /// <remarks>
    /// Der Anker, ohne den der Statusreport nicht auszulesen wäre: „Gesamtleistung“ steht dort
    /// dreimal mit drei verschiedenen Beträgen, je einmal unter Ablauf, Beitragsfreistellung und
    /// Todesfall. Ohne Abschnitt träfe die Suche den erstbesten.
    /// </remarks>
    public string? Section { get; init; }

    /// <summary>Beschriftungen, unter denen der Wert steht. Der erste Treffer gewinnt.</summary>
    public string[] Labels { get; init; } = [];

    /// <summary>Muster mit genau einer Fanggruppe.</summary>
    public string? Pattern { get; init; }

    /// <summary>Wird ins Zielobjekt übernommen.</summary>
    public bool Lead { get; init; }

    /// <summary>Ohne Garantie — nie Teil einer Vermögenssumme.</summary>
    public bool Soft { get; init; }

    /// <summary>
    /// Ein zweites Feld, das in derselben Zeile mit angezeigt wird.
    /// </summary>
    /// <remarks>
    /// „ISIN · WKN“ ist für den Menschen eine Angabe und für die Ablage zwei. Gespeichert werden
    /// beide getrennt, gezeigt wird eine Zeile — sonst zählte der Nutzer zehn Felder, wo der
    /// Handoff acht nennt.
    /// </remarks>
    public string? PairedWith { get; init; }
}

/// <summary>
/// Eine Rechenprobe über gelesene Werte.
/// </summary>
/// <remarks>
/// <para>Der eigentliche Schutz gegen falsch zugeordnete Beträge. Ein Formular verrät nicht, ob
/// eine Zahl in der richtigen Zeile gelandet ist — seine eigene Arithmetik schon: passt
/// Rückkaufswert + Ansammlungsguthaben zur ausgewiesenen Gesamtleistung, stimmt die Zuordnung.
/// Passt sie nicht, ist etwas verrutscht, und das gehört gesagt statt gespeichert.</para>
/// <para>Fehlt das Ergebnisfeld im Dokument, wird es aus den Teilen <em>abgeleitet</em>. Ein
/// gerechneter Wert ist besser als ein leeres Feld — solange dransteht, dass er gerechnet ist.</para>
/// </remarks>
public sealed record DocumentCheck
{
    public required string Result { get; init; }
    public required string[] Parts { get; init; }
    public required DocumentCheckKind Kind { get; init; }

    /// <summary>Wie die Probe im Klartext heißt, etwa „Rückkaufswert + Ansammlungsguthaben“.</summary>
    public required string Note { get; init; }

    /// <summary>Erlaubte Abweichung. Banken runden je Position, das ist keine Unstimmigkeit.</summary>
    public decimal Tolerance { get; init; } = 0.01m;
}

/// <summary>Woran ein Dokument seinem Typ zugeordnet wird.</summary>
/// <param name="Text">Zeichenfolge, die im Dokument vorkommen muss.</param>
public sealed record DocumentMarker(string Text);

/// <summary>Worauf ein Dokumenttyp zielt.</summary>
public enum DocumentTargetKind
{
    Policy,
    Depot,
}

/// <summary>
/// Ein unterstützter Dokumenttyp — Abschnitt 14.2 des Handoffs.
/// </summary>
/// <remarks>
/// <para>Der Datensatz, aus dem Vorschlag, Werteprüfung, Ablagepfad, Bestätigung und
/// Speicherlogik entstehen. Eine dritte Art — Beitragsrechnung, Steuerbescheid — ist damit ein
/// Eintrag in <see cref="DocumentKindLibrary"/> und kein neuer Bildschirm.</para>
/// <para>Was <em>nicht</em> hier steht: wie gespeichert wird. Der Typ nennt sein Zielobjekt und
/// seine Leitwerte; was ein Vertrag mit einem erreichten Wert anfängt, weiß der Vertrag.</para>
/// </remarks>
public sealed record DocumentKind
{
    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>Ablagebereich — er bestimmt den obersten Ordner.</summary>
    public required DocumentArea Area { get; init; }

    public required DocumentTargetKind Target { get; init; }

    /// <summary>Wie das Zielobjekt in einem Satz heißt, etwa „Vertrag“ oder „Depot“.</summary>
    public required string TargetNoun { get; init; }

    /// <summary>Beschriftung des Knopfes, der zum Zielobjekt führt.</summary>
    public required string TargetLink { get; init; }

    /// <summary>
    /// Unterordner unter dem Bereichsordner. <c>{ziel}</c> und <c>{jahr}</c> werden eingesetzt.
    /// </summary>
    public required string FolderTemplate { get; init; }

    /// <summary>Dateiname-Vorlage mit <c>{stichtag}</c>.</summary>
    public required string FileTemplate { get; init; }

    /// <summary>Zeichenfolgen, die alle vorkommen müssen, damit der Typ passt.</summary>
    public required IReadOnlyList<DocumentMarker> Markers { get; init; }

    /// <summary>Abschnittsüberschriften, an denen das Dokument zerlegt wird.</summary>
    public IReadOnlyList<string> Sections { get; init; } = [];

    /// <summary>Feld mit dem fachlichen Stichtag.</summary>
    public required string AsOfField { get; init; }

    /// <summary>Feld mit dem Datum des Schreibens.</summary>
    public required string DocumentDateField { get; init; }

    /// <summary>Feld mit der Nummer, über die das Zielobjekt gefunden wird.</summary>
    public required string TargetNumberField { get; init; }

    public required IReadOnlyList<DocumentFieldRule> Fields { get; init; }

    public IReadOnlyList<DocumentCheck> Checks { get; init; } = [];

    /// <summary>
    /// Die Analyseschritte, die der Oberfläche als Kette angezeigt werden.
    /// </summary>
    /// <remarks>
    /// Sichtbare Kette statt Wartesymbol: bricht ein Schritt ab, ist erkennbar welcher. Die
    /// Platzhalter <c>{seiten}</c>, <c>{absender}</c>, <c>{ziel}</c> und <c>{werte}</c> füllt die
    /// Analyse mit dem, was sie tatsächlich gefunden hat.
    /// </remarks>
    public required IReadOnlyList<string> Steps { get; init; }
}

/// <summary>
/// Die unterstützten Dokumenttypen.
/// </summary>
/// <remarks>
/// Beide Datensätze sind an den echten PDFs des Nutzers entstanden, nicht an Beispielen: an
/// einem Statusreport der Heidelberger Leben zum 31.07.2025 und an einer Quartalsaufstellung der
/// Baader Bank zum 30.06.2026.
/// </remarks>
public static class DocumentKindLibrary
{
    /// <summary>
    /// Statusreport Lebensversicherung — Abschnitt 14.3, zehn Felder.
    /// </summary>
    /// <remarks>
    /// <para>Die drei Leistungsszenarien des Dokuments (Ablauf, Beitragsfreistellung, Todesfall)
    /// tragen teils dieselben Beträge und dürfen nicht vermischt werden. Deshalb hängt jedes
    /// Feld an seinem Abschnitt, und der Vermögenswert kommt aus „Wert der Versicherung“ — nicht
    /// aus „Leistung im Erlebensfall“, die eine Prognose auf 2031 ist.</para>
    /// <para>Bewertungsreserven und Schlussüberschüsse stehen als <c>soft</c> dabei. Das Dokument
    /// erklärt in drei Fußnoten, warum sie nicht garantiert sind; sie in eine Vermögenssumme zu
    /// nehmen hieße, dem Nutzer 566,21 € zu versprechen, die niemand versprochen hat.</para>
    /// </remarks>
    public static readonly DocumentKind Statusreport = new()
    {
        Key = "statusreport-lv",
        Label = "Statusreport Lebensversicherung",
        Area = DocumentArea.Insurance,
        Target = DocumentTargetKind.Policy,
        TargetNoun = "Vertrag",
        TargetLink = "Zum Vertrag",
        FolderTemplate = "Lebensversicherung/{ziel}/{jahr}",
        FileTemplate = "Statusreport_{stichtag}",

        Markers = [new("Statusreport"), new("Wert der Versicherung")],

        Sections =
        [
            "Leistung im Erlebensfall zum Ablauf",
            "Leistung im Erlebensfall bei Beitragsfreistellung",
            "Leistung im Todesfall",
            "Wert der Versicherung",
            "Leistung bei Berufsunfähigkeit",
        ],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "vertragsnummer",

        Fields =
        [
            new()
            {
                Key = "rueckkauf", Label = "Rückkaufswert", Kind = DocumentValueKind.Money,
                Section = "Wert der Versicherung", Labels = ["Rückkaufswert"],
            },
            new()
            {
                Key = "ansammlung", Label = "Ansammlungsguthaben", Kind = DocumentValueKind.Money,
                Section = "Wert der Versicherung",
                Labels = ["erreichter Wert der Überschussbeteiligung"],
            },

            // Der Vermögenswert. Nicht der Rückkaufswert allein und nicht die Ablaufleistung.
            new()
            {
                Key = "gesamt", Label = "Erreichter Wert gesamt", Kind = DocumentValueKind.Money,
                Section = "Wert der Versicherung", Labels = ["Gesamtleistung"], Lead = true,
            },

            new()
            {
                Key = "garantie", Label = "Garantierte Erlebensfallleistung", Kind = DocumentValueKind.Money,
                Section = "Leistung im Erlebensfall zum Ablauf",
                Labels = ["garantierte Erlebensfallleistung"],
            },
            new()
            {
                Key = "ablaufwert", Label = "Gesamtleistung bei Ablauf", Kind = DocumentValueKind.Money,
                Section = "Leistung im Erlebensfall zum Ablauf", Labels = ["Gesamtleistung"],
            },

            // Das Ablaufdatum steht in der Abschnittsüberschrift selbst, nicht in einer Zeile
            // darunter: „Leistung im Erlebensfall zum Ablauf 01.08.2031“.
            new()
            {
                Key = "ablauf", Label = "Ablauf", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,
                Pattern = @"zum Ablauf\s+(\d{1,2}\.\d{1,2}\.\d{4})",
            },

            new()
            {
                Key = "todesfall", Label = "Todesfallleistung", Kind = DocumentValueKind.Money,
                Section = "Leistung im Todesfall", Labels = ["Gesamtleistung"],
            },
            new()
            {
                Key = "bu", Label = "Monatliche BU-Rente", Kind = DocumentValueKind.Money,
                Section = "Leistung bei Berufsunfähigkeit",
                Labels = ["monatliche Berufsunfähigkeitsrente"],
            },

            new()
            {
                Key = "reserven", Label = "Bewertungsreserven", Kind = DocumentValueKind.Money,
                Section = "Wert der Versicherung",
                Labels = ["Für die Zukunft nicht garantierte Bewertungsreserven"], Soft = true,
            },
            new()
            {
                Key = "schluss", Label = "Schlussüberschüsse", Kind = DocumentValueKind.Money,
                Section = "Wert der Versicherung",
                Labels = ["Für die Zukunft nicht garantierte Schlussüberschüsse"], Soft = true,
            },

            // Kopfdaten. Sie stehen nicht in der Werteliste, tragen aber Vorschlag und Ablage.
            new()
            {
                Key = "stichtag", Label = "Stichtag", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Vertragsstand zum\s+(\d{1,2}\.\d{1,2}\.\d{4})",
            },
            new()
            {
                Key = "dokumentdatum", Label = "Dokumentdatum", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,
                Pattern = @"^[A-Za-zÄÖÜäöüß.\- ]+,\s+(\d{1,2}\.\d{1,2}\.\d{4})$",
            },
            new()
            {
                Key = "vertragsnummer", Label = "Versicherungsnummer", Kind = DocumentValueKind.Text,
                Labels = ["Versicherungsnummer"],
            },
            new()
            {
                Key = "absender", Label = "Absender", Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.Pattern,
                Pattern = @"^([A-ZÄÖÜ][\w.\-äöüß]*(?: [\w.\-äöüß]+)*? (?:Lebensversicherung|Leben) AG)$",
            },
        ],

        // Das Dokument rechnet selbst vor: 18.373,87 + 2.107,65 = 20.481,52. Stimmt die Probe,
        // sitzt jeder Betrag in seiner Zeile.
        Checks =
        [
            new()
            {
                Result = "gesamt", Parts = ["rueckkauf", "ansammlung"], Kind = DocumentCheckKind.Sum,
                Note = "Rückkaufswert + Ansammlungsguthaben",
            },
        ],

        Steps =
        [
            "Text gelesen ({seiten} Seiten)",
            "Absender: {absender}",
            "Typ: Statusreport",
            "{ziel}",
            "{werte} Werte gelesen",
        ],
    };

    /// <summary>
    /// Quartalsaufstellung MiFID II — Abschnitt 14.4, acht Felder.
    /// </summary>
    /// <remarks>
    /// <para>Sie <em>belegt</em> den Depotbestand zum Stichtag und ersetzt ihn nicht: der
    /// Depotwert entsteht weiter aus den importierten Ausführungen. Die Aufstellung geht in den
    /// Bestandsabgleich aus Abschnitt 11.3, der beide Seiten je ISIN gegenüberstellt.</para>
    /// <para><b>Eine Position je Aufstellung.</b> Die Feldliste beschreibt eine Bestandszeile,
    /// und der Extraktor nimmt je Feld den ersten Treffer — ein Depot mit drei Fonds läse nur
    /// den ersten. Für mehrere Positionen bräuchte der Typ eine Wiederholgruppe; das ist
    /// vorgesehen, aber nicht gebaut, weil das reale Beispiel eine Position führt. Bis dahin
    /// bleibt die Erfassung von Hand aus Abschnitt 11.2 der Weg für Aufstellungen mit
    /// mehreren Werten.</para>
    /// </remarks>
    public static readonly DocumentKind QuarterlyStatement = new()
    {
        Key = "quartalsaufstellung",
        Label = "Quartalsaufstellung MiFID II",
        Area = DocumentArea.Finance,
        Target = DocumentTargetKind.Depot,
        TargetNoun = "Depot",
        TargetLink = "Zum Depot",
        FolderTemplate = "Depot/{ziel}/{jahr}",
        FileTemplate = "Quartalsaufstellung_{stichtag}",

        Markers = [new("Quartalsaufstellung"), new("MIFID II")],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "depotnummer",

        Fields =
        [
            // Die Bestandszeile ist eine Tabellenzeile, keine Beschriftung mit Wert:
            // „Stück · 763 · WKN: A0RPWH · EUR 125,240 · 95.558,12 · EUR“.
            new()
            {
                Key = "nominale", Label = "Nominale", Kind = DocumentValueKind.Quantity,
                Locator = DocumentLocator.Pattern, Pattern = @"^Stück\s+([\d.]+(?:,\d+)?)\b",
                Lead = true,
            },
            new()
            {
                Key = "kurs", Label = "Kurs", Kind = DocumentValueKind.Price,
                Locator = DocumentLocator.Pattern, Pattern = @"^Stück\s.*?\bEUR\s+([\d.]+,\d+)",
            },
            new()
            {
                Key = "kurswert", Label = "Kurswert", Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern, Pattern = @"^Stück\s.*?([\d.]+,\d{2})\s+EUR$",
                Lead = true,
            },

            new()
            {
                Key = "isin", Label = "ISIN · WKN", Kind = DocumentValueKind.Text,
                Labels = ["ISIN"], PairedWith = "wkn",
            },
            new()
            {
                Key = "wkn", Label = "WKN", Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.Pattern, Pattern = @"\bWKN:\s*([A-Z0-9]{6})\b",
            },

            // Die Bezeichnung trägt keine Beschriftung; sie steht unter der ISIN.
            new()
            {
                Key = "papier", Label = "Wertpapier", Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.NextLine, Labels = ["ISIN"],
            },

            new()
            {
                Key = "verwahrart", Label = "Verwahrart · Lagerland", Kind = DocumentValueKind.Text,
                Labels = ["Verwahrart"], PairedWith = "lagerland",
            },
            new()
            {
                Key = "lagerland", Label = "Lagerland", Kind = DocumentValueKind.Text,
                Labels = ["Lagerland"],
            },
            new()
            {
                Key = "lagerstelle", Label = "Lagerstelle", Kind = DocumentValueKind.Text,
                Labels = ["Lagerstelle"],
            },
            new()
            {
                Key = "referenz", Label = "Referenz-Nr.", Kind = DocumentValueKind.Text,
                Labels = ["Referenz-Nr."],
            },

            new()
            {
                Key = "stichtag", Label = "Stichtag", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern, Pattern = @"per\s+(\d{1,2}\.\d{1,2}\.\d{4})",
            },

            // Das Schreiben datiert im Briefkopf, allein auf einer Zeile unter dem Absendeort.
            new()
            {
                Key = "dokumentdatum", Label = "Dokumentdatum", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern, Pattern = @"^(\d{1,2}\.\d{1,2}\.\d{4})$",
            },
            new()
            {
                Key = "depotnummer", Label = "Depot-Nr.", Kind = DocumentValueKind.Text,
                Labels = ["Depot-Nr."],
            },
            new()
            {
                Key = "absender", Label = "Absender", Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.Pattern, Pattern = @"^([\w.\-äöüß ]+ Bank AG)\b",
            },
        ],

        // 763 × 125,240 = 95.558,12. Geht die Probe auf, stehen Stück, Kurs und Kurswert in den
        // Spalten, in die sie gehören.
        Checks =
        [
            new()
            {
                Result = "kurswert", Parts = ["nominale", "kurs"], Kind = DocumentCheckKind.Product,
                Note = "Nominale × Kurs",
            },
        ],

        Steps =
        [
            "Text gelesen ({seiten} Seiten)",
            "Absender: {absender}",
            "Typ: Quartalsaufstellung",
            "{ziel}",
            "{werte} Werte gelesen",
        ],
    };

    public static readonly IReadOnlyList<DocumentKind> All = [Statusreport, QuarterlyStatement];

    /// <summary>
    /// Welcher Typ zu einem gelesenen Text passt.
    /// </summary>
    /// <remarks>
    /// Aus dem Inhalt, nie aus dem Dateinamen — die echte Datei des Nutzers heißt „statusreport
    /// 2024“ und meint den Stand zum 31.07.2025. Passt nichts, ist das Ergebnis <c>null</c>: die
    /// Datei wird trotzdem abgelegt, die Werte trägt ein Mensch ein.
    /// </remarks>
    public static DocumentKind? Detect(PdfContent content)
    {
        var text = string.Join("\n", content.Lines.Select(z => z.Text));

        return All.FirstOrDefault(art => art.Markers.All(
            m => text.Contains(m.Text, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>Ein gelesener Wert mit seiner Herkunft.</summary>
public sealed record ReadValue
{
    public required DocumentFieldRule Rule { get; init; }

    /// <summary>Der Text, wie er im Dokument steht.</summary>
    public required string Raw { get; init; }

    public decimal? Number { get; init; }
    public DateOnly? Date { get; init; }

    public int? Page { get; init; }
    public double Confidence { get; init; }

    /// <summary>Nicht gelesen, sondern aus anderen Feldern gerechnet.</summary>
    public bool Derived { get; init; }

    /// <summary>Wenn etwas nicht stimmt: was.</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// Zieht die Felder eines Dokumenttyps aus gelesenem Text.
/// </summary>
/// <remarks>
/// <para>Eine Zuordnung je Typ, ein Mechanismus für alle — Abschnitt 14.6. Der Extraktor kennt
/// keinen einzigen Feldnamen; er kennt Abschnitte, Beschriftungen, Muster und Rechenproben.</para>
/// <para>Wörtlich am echten Dokument entwickelt: der Statusreport verschiebt in seiner
/// Textebene stellenweise die Wertspalte um eine Zeile gegen die Beschriftungen. Wer Zeilen
/// zählt, liest dort den Rückkaufswert als Überschrift. Deshalb zählt hier nichts Zeilen — es
/// gilt der Abschnitt, die Beschriftung und am Ende die Rechenprobe.</para>
/// </remarks>
public sealed class DocumentFieldExtractor
{
    /// <summary>Textebene sichtbar: der Wert steht so auf dem Papier.</summary>
    private const double Sure = 1.0;

    /// <summary>Textebene hinter Seitenbildern: lesbar, aber nicht das Sichtbare.</summary>
    private const double Behind = 0.85;

    /// <summary>Gerechnet statt gelesen.</summary>
    private const double Calculated = 0.9;

    /// <summary>Die Rechenprobe geht nicht auf — hier muss jemand hinsehen.</summary>
    private const double Doubtful = 0.4;

    public IReadOnlyList<ReadValue> Extract(DocumentKind kind, PdfContent content)
    {
        var basis = content.TextIsInvisible ? Behind : Sure;
        var abschnitte = Sections(kind, content);

        var werte = new Dictionary<string, ReadValue>();

        foreach (var regel in kind.Fields)
        {
            if (Find(regel, abschnitte, content, basis) is { } wert)
            {
                werte[regel.Key] = wert;
            }
        }

        foreach (var probe in kind.Checks)
        {
            Verify(probe, kind, werte, basis);
        }

        return [.. kind.Fields.Where(f => werte.ContainsKey(f.Key)).Select(f => werte[f.Key])];
    }

    // ── Abschnitte ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Das Dokument in seine Abschnitte zerlegt.
    /// </summary>
    /// <remarks>
    /// Ein Abschnitt läuft von seiner Überschrift bis zur nächsten. Kennt der Typ keine
    /// Überschriften, ist das ganze Dokument ein Abschnitt.
    /// </remarks>
    private static Dictionary<string, List<PdfLine>> Sections(DocumentKind kind, PdfContent content)
    {
        var abschnitte = new Dictionary<string, List<PdfLine>>(StringComparer.OrdinalIgnoreCase);
        List<PdfLine>? laufend = null;

        foreach (var zeile in content.Lines)
        {
            var ueberschrift = kind.Sections.FirstOrDefault(
                s => zeile.Text.StartsWith(s, StringComparison.OrdinalIgnoreCase));

            if (ueberschrift is not null)
            {
                // Kommt eine Überschrift zweimal vor, gewinnt der erste Auftritt: eine
                // Wiederholung ist im Briefverkehr fast immer eine Zusammenfassung.
                if (!abschnitte.TryGetValue(ueberschrift, out laufend))
                {
                    laufend = [];
                    abschnitte[ueberschrift] = laufend;
                }
                else
                {
                    laufend = null;
                }

                continue;
            }

            laufend?.Add(zeile);
        }

        return abschnitte;
    }

    // ── Suchen ─────────────────────────────────────────────────────────────────────────────

    private ReadValue? Find(
        DocumentFieldRule regel,
        Dictionary<string, List<PdfLine>> abschnitte,
        PdfContent content,
        double basis)
    {
        var zeilen = regel.Section is { } abschnitt
            ? abschnitte.TryGetValue(abschnitt, out var treffer) ? treffer : []
            : content.Lines;

        return regel.Locator switch
        {
            DocumentLocator.Label => ByLabel(regel, zeilen, basis),
            DocumentLocator.Pattern => ByPattern(regel, zeilen, basis),
            DocumentLocator.NextLine => ByNextLine(regel, zeilen, basis),
            _ => null,
        };
    }

    /// <summary>
    /// Beschriftung links, Wert rechts.
    /// </summary>
    /// <remarks>
    /// Die Beschriftung muss am <em>Anfang</em> der Zeile stehen. Das schließt Fließtext aus, in
    /// dem dasselbe Wort mitten im Satz vorkommt — „Die Gesamtleistung Ihrer Versicherung setzt
    /// sich…“ ist keine Wertzeile und darf keine werden.
    /// </remarks>
    private ReadValue? ByLabel(DocumentFieldRule regel, IReadOnlyList<PdfLine> zeilen, double basis)
    {
        foreach (var zeile in zeilen)
        {
            foreach (var beschriftung in regel.Labels)
            {
                var kopf = zeile.Cells[0];
                if (!kopf.StartsWith(beschriftung, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Steht alles in einer Zelle, ist der Wert der Rest dahinter; sonst die letzte
                // Zelle der Zeile.
                var roh = zeile.Cells.Count > 1
                    ? zeile.Cells[^1]
                    : kopf[beschriftung.Length..].TrimStart(':', ' ');

                if (Read(regel, roh) is { } wert)
                {
                    return wert with { Page = zeile.Page, Confidence = basis };
                }
            }
        }

        return null;
    }

    private ReadValue? ByPattern(DocumentFieldRule regel, IReadOnlyList<PdfLine> zeilen, double basis)
    {
        if (regel.Pattern is not { } muster)
        {
            return null;
        }

        var regex = new Regex(muster, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        foreach (var zeile in zeilen)
        {
            var treffer = regex.Match(zeile.Text);
            if (treffer.Success && Read(regel, treffer.Groups[1].Value) is { } wert)
            {
                return wert with { Page = zeile.Page, Confidence = basis };
            }
        }

        return null;
    }

    private ReadValue? ByNextLine(DocumentFieldRule regel, IReadOnlyList<PdfLine> zeilen, double basis)
    {
        for (var i = 0; i < zeilen.Count - 1; i++)
        {
            if (!regel.Labels.Any(
                    l => zeilen[i].Cells[0].StartsWith(l, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Read(regel, zeilen[i + 1].Text) is { } wert)
            {
                return wert with { Page = zeilen[i + 1].Page, Confidence = basis };
            }
        }

        return null;
    }

    // ── Rechenprobe ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Prüft eine Rechenprobe und leitet das Ergebnis ab, wo das Dokument es nicht nennt.
    /// </summary>
    private static void Verify(
        DocumentCheck probe, DocumentKind kind, Dictionary<string, ReadValue> werte, double basis)
    {
        var teile = probe.Parts.Select(p => werte.TryGetValue(p, out var w) ? w.Number : null).ToList();
        if (teile.Any(t => t is null))
        {
            return;
        }

        var soll = probe.Kind == DocumentCheckKind.Sum
            ? teile.Sum(t => t!.Value)
            : teile.Aggregate(1m, (a, t) => a * t!.Value);

        if (!werte.TryGetValue(probe.Result, out var ergebnis))
        {
            // Das Dokument nennt den Wert nicht. Dann rechnen wir ihn — und sagen es.
            var regel = kind.Fields.FirstOrDefault(f => f.Key == probe.Result);
            if (regel is null)
            {
                return;
            }

            var gerundet = decimal.Round(soll, 2);
            werte[probe.Result] = new ReadValue
            {
                Rule = regel,
                Raw = Format(regel, gerundet),
                Number = gerundet,
                Page = werte[probe.Parts[0]].Page,
                Confidence = Math.Min(Calculated, basis),
                Derived = true,
                Warning = $"gerechnet: {probe.Note}",
            };

            return;
        }

        if (ergebnis.Number is not { } steht || Math.Abs(steht - soll) <= probe.Tolerance)
        {
            return;
        }

        // Die Zuordnung ist verrutscht — genau der Fall, für den die Probe da ist.
        werte[probe.Result] = ergebnis with
        {
            Confidence = Doubtful,
            Warning = $"{probe.Note} ergibt {Format(ergebnis.Rule, decimal.Round(soll, 2))} — bitte prüfen",
        };
    }

    // ── Werte lesen ────────────────────────────────────────────────────────────────────────

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Macht aus dem Rohtext einen Wert der erwarteten Art — oder nichts.
    /// </summary>
    /// <remarks>
    /// Öffentlich, weil die Übernahme dieselbe Regel braucht: ein von Hand berichtigter Betrag
    /// muss genauso gelesen werden wie ein erkannter, sonst hinge das Ergebnis daran, wer ihn
    /// eingetragen hat.
    /// </remarks>
    /// <remarks>
    /// Dass eine Zahl auch als Zahl lesbar sein muss, ist die zweite Sicherung neben der
    /// Rechenprobe: eine Beschriftung, hinter der Fließtext steht, liefert keinen Betrag und
    /// damit auch keinen falschen.
    /// </remarks>
    public static ReadValue? Read(DocumentFieldRule regel, string roh)
    {
        var text = roh.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (regel.Kind == DocumentValueKind.Text)
        {
            return new ReadValue { Rule = regel, Raw = text };
        }

        if (regel.Kind == DocumentValueKind.Date)
        {
            return DateOnly.TryParseExact(text, "d.M.yyyy", German, DateTimeStyles.None, out var tag)
                ? new ReadValue { Rule = regel, Raw = text, Date = tag }
                : null;
        }

        var zahl = text.Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("Stück", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(zahl, NumberStyles.Number, German, out var wert)
            ? new ReadValue { Rule = regel, Raw = text, Number = wert }
            : null;
    }

    /// <summary>Wie ein Wert dieser Art im Dokument aussähe.</summary>
    private static string Format(DocumentFieldRule regel, decimal wert) => regel.Kind switch
    {
        DocumentValueKind.Money => wert.ToString("N2", German) + " EUR",
        DocumentValueKind.Price => wert.ToString("0.00##", German),
        DocumentValueKind.Quantity => wert.ToString("0.####", German),
        _ => wert.ToString(German),
    };
}
