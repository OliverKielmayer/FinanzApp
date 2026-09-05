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
    /// Abschnitte, in denen gesucht wird — leer heißt: im ganzen Dokument.
    /// </summary>
    /// <remarks>
    /// <para>Der Anker, ohne den der Statusreport nicht auszulesen wäre: „Gesamtleistung“ steht
    /// dort dreimal mit drei verschiedenen Beträgen, je einmal unter Ablauf, Beitragsfreistellung
    /// und Todesfall. Ohne Abschnitt träfe die Suche den erstbesten.</para>
    /// <para><b>Mehrere, weil dieselbe Stelle über die Jahre anders heißt.</b> Derselbe
    /// Versicherer schrieb bis 2018 „Leistung im Erlebensfall“ und schreibt seit 2019 „Leistung
    /// im Erlebensfall zum Ablauf“. Gesucht wird im ersten Abschnitt, den das Dokument
    /// tatsächlich führt — die Reihenfolge hier ist die Rangfolge.</para>
    /// </remarks>
    public string[] Sections { get; init; } = [];

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

    /// <summary>
    /// Warum diese Probe gemacht wird — im Klartext, für die Anzeige vor der Übernahme.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>Erlaubte Abweichung. Banken runden je Position, das ist keine Unstimmigkeit.</summary>
    public decimal Tolerance { get; init; } = 0.01m;
}

/// <summary>
/// Eine Gruppe gleichartiger Zeilen in einem Dokument — v5-Handoff, Abschnitt 17.2.
/// </summary>
/// <remarks>
/// <para>Ohne sie las der Extraktor je Feld den ersten Treffer, und eine Aufstellung mit drei
/// Fonds ergab eine Position. Die Gruppe zerlegt das Dokument an einem <see cref="Anchor"/> in
/// Blöcke und liest die Felder <em>innerhalb</em> eines Blocks — dieselbe Zuordnung wie sonst,
/// nur auf einem Ausschnitt.</para>
/// <para>Ein Block ist keine Zeile: in der realen Aufstellung stehen Stückzahl, ISIN,
/// Bezeichnung und Verwahrart auf sechs Zeilen untereinander. Der Anker eröffnet den Block, die
/// nächste Ankerzeile beendet ihn.</para>
/// </remarks>
public sealed record DocumentRepeatRule
{
    /// <summary>Wie die Gruppe im Prüfschritt überschrieben ist.</summary>
    public required string Title { get; init; }

    /// <summary>Muster, das einen Block eröffnet.</summary>
    public required string Anchor { get; init; }

    /// <summary>Die Felder je Block.</summary>
    public required IReadOnlyList<DocumentFieldRule> Fields { get; init; }

    /// <summary>Feldschlüssel des Werts, den eine Zeile beiträgt.</summary>
    public required string ValueField { get; init; }

    /// <summary>Feldschlüssel, unter dem der Zeilenname steht.</summary>
    public required string NameField { get; init; }

    /// <summary>
    /// Feld im Kopf, das die Summe aller Zeilen ausweist.
    /// </summary>
    /// <remarks>
    /// Gegen sie wird die zweite Stufe der Rechenprobe geführt: stimmt die Summe der Zeilen mit
    /// dem ausgewiesenen Gesamtwert, sitzt jede Zeile richtig.
    /// </remarks>
    public string? TotalField { get; init; }

    /// <summary>
    /// Erlaubte Abweichung der Summe vom ausgewiesenen Gesamtwert.
    /// </summary>
    /// <remarks>
    /// Ein Cent voreingestellt: das deckt die Rundung <em>einer</em> Zeile. Wo der Absender die
    /// ungerundeten Zeilenwerte summiert und die gerundeten ausweist, braucht die Probe je Zeile
    /// einen halben Cent mehr — sonst meldet sie eine Unstimmigkeit, die auf dem Papier steht.
    /// </remarks>
    public decimal TotalTolerance { get; init; } = 0.01m;

    /// <summary>Die Probe je Zeile.</summary>
    public DocumentCheck? RowCheck { get; init; }
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
    /// <summary>
    /// Der Platzhalter im Ablagepfad, solange das Objekt nicht feststeht.
    /// </summary>
    /// <remarks>
    /// Er steht hier und nicht als Zeichenkette in der Ablage, weil eine zweite Stelle ihn
    /// wiedererkennen muss: wird das Objekt später im Scaneingang nachgetragen, wandert die
    /// Datei aus diesem Ordner heraus. Zwei Schreibweisen desselben Wortes hießen, dass sie für
    /// immer unter „Unbekannt“ läge.
    /// </remarks>
    public const string UnknownTarget = "Unbekannt";

    public required string Key { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// Der Dokumenttyp, unter dem ein Beleg dieser Art abgelegt gehört.
    /// </summary>
    /// <remarks>
    /// <para>Getrennt von <see cref="Label"/>, weil beides verschiedene Fragen beantwortet: die
    /// Bezeichnung sagt, <em>was die Anwendung liest</em> („Statusreport fondsgebundene
    /// Lebensversicherung“), der Typ, <em>wie der Haushalt seine Ablage beschriftet</em>
    /// („Statusreport“). Vorher suchte die Einlieferung einen Typ mit dem Namen der Bezeichnung —
    /// und der klassische und der fondsgebundene Bericht brauchten zwei Typen, die sich nur im
    /// Zusatz unterscheiden.</para>
    /// <para>Mehrere Arten dürfen sich einen Typ teilen. Er ist die Schublade, nicht die
    /// Leseregel.</para>
    /// </remarks>
    public required string TypeName { get; init; }

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

    /// <summary>Die Wiederholgruppe, wenn der Typ eine hat.</summary>
    public DocumentRepeatRule? Repeat { get; init; }

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
    /// Statusreport Lebensversicherung — Abschnitt 14.3.
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
        TypeName = "Statusreport",
        Area = DocumentArea.Insurance,
        Target = DocumentTargetKind.Policy,
        TargetNoun = "Vertrag",
        TargetLink = "Zum Vertrag",
        FolderTemplate = "Lebensversicherung/{ziel}/{jahr}",
        FileTemplate = "Statusreport_{stichtag}",

        Markers = [new("Statusreport"), new("Wert der Versicherung")],

        // Die Reihenfolge ist die Rangfolge: die genauere Schreibweise zuerst. „Leistung im
        // Erlebensfall“ steht deshalb hinten — sonst schluckte sie in den neueren Berichten die
        // beiden Szenarien, die mit ihr anfangen, und der Ablaufabschnitt käme nie zustande.
        Sections =
        [
            "Leistung im Erlebensfall zum Ablauf",
            "Leistung im Erlebensfall bei Beitragsfreistellung",
            "Leistung im Todesfall",
            "Wert der Versicherung",
            "Leistung bei Berufsunfähigkeit",
            "Leistung im Erlebensfall",
        ],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "vertragsnummer",

        Fields =
        [
            new()
            {
                Key = "rueckkauf", Label = "Rückkaufswert", Kind = DocumentValueKind.Money,
                Sections = ["Wert der Versicherung"], Labels = ["Rückkaufswert"],
            },
            new()
            {
                Key = "ansammlung", Label = "Ansammlungsguthaben", Kind = DocumentValueKind.Money,
                Sections = ["Wert der Versicherung"],
                Labels = ["erreichter Wert der Überschussbeteiligung"],
            },

            // Der Vermögenswert. Nicht der Rückkaufswert allein und nicht die Ablaufleistung.
            new()
            {
                Key = "gesamt", Label = "Erreichter Wert gesamt", Kind = DocumentValueKind.Money,
                Sections = ["Wert der Versicherung"], Labels = ["Gesamtleistung"], Lead = true,
            },

            new()
            {
                Key = "garantie", Label = "Garantierte Erlebensfallleistung", Kind = DocumentValueKind.Money,
                Sections = ["Leistung im Erlebensfall zum Ablauf", "Leistung im Erlebensfall"],
                Labels = ["garantierte Erlebensfallleistung"],
            },
            new()
            {
                Key = "ablaufwert", Label = "Gesamtleistung bei Ablauf", Kind = DocumentValueKind.Money,
                Sections = ["Leistung im Erlebensfall zum Ablauf", "Leistung im Erlebensfall"],
                Labels = ["Gesamtleistung"],
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
                Sections = ["Leistung im Todesfall"], Labels = ["Gesamtleistung"],
            },
            new()
            {
                Key = "bu", Label = "Monatliche BU-Rente", Kind = DocumentValueKind.Money,
                Sections = ["Leistung bei Berufsunfähigkeit"],

                // Beide Schreibweisen desselben Absenders: bis 2021 stand „garantierte“ mit im
                // Namen der Zeile.
                Labels = ["monatliche Berufsunfähigkeitsrente", "monatliche garantierte Berufsunfähigkeitsrente"],
            },

            // Eigenes Feld und nicht dasselbe: mehrere Jahrgänge weisen die Rente **jährlich**
            // aus. Sie in das Monatsfeld zu lesen wäre eine falsche Zahl in einem richtigen Feld
            // — zwölfmal zu hoch, und niemand sähe es der Zeile an.
            new()
            {
                Key = "bujahr", Label = "Jährliche BU-Rente", Kind = DocumentValueKind.Money,
                Sections = ["Leistung bei Berufsunfähigkeit"],
                Labels = ["jährliche Berufsunfähigkeitsrente", "jährliche garantierte Berufsunfähigkeitsrente"],
            },

            new()
            {
                Key = "reserven", Label = "Bewertungsreserven", Kind = DocumentValueKind.Money,
                Sections = ["Wert der Versicherung"],
                Labels = ["Für die Zukunft nicht garantierte Bewertungsreserven"], Soft = true,
            },
            new()
            {
                Key = "schluss", Label = "Schlussüberschüsse", Kind = DocumentValueKind.Money,
                Sections = ["Wert der Versicherung"],
                Labels = ["Für die Zukunft nicht garantierte Schlussüberschüsse"], Soft = true,
            },

            // Kopfdaten. Sie stehen nicht in der Werteliste, tragen aber Vorschlag und Ablage.
            new()
            {
                Key = "stichtag", Label = "Stichtag", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,

                // Beide Schreibweisen: „zum 31.07.2025“ und „zum 31. Juli 2014“ — der ältere
                // Jahrgang schreibt den Monat aus, und manchmal fehlt das Leerzeichen dahinter.
                // Ohne den Stichtag wird kein Wert übernommen; er ist die Pflichtangabe.
                Pattern = @"Vertragsstand zum\s+(\d{1,2}\.\s*(?:\d{1,2}\.|[A-Za-zÄÖÜäöüß]+\s+)\d{4})",
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
                // Kein Zeilenende erzwingen: in den älteren Berichten klebt die Anschrift
                // ohne Leerzeichen hinter der Firma („… AG•Postfach103969“).
                Pattern = @"^([A-ZÄÖÜ][\w.\-äöüß]*(?: [\w.\-äöüß]+)*? (?:Lebensversicherung|Leben) AG)\b",
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
                Why = "Rückkaufswert plus Ansammlungsguthaben muss die ausgewiesene "
                      + "Gesamtleistung ergeben. Die Probe fängt Zeilenversatz in der Textebene "
                      + "ab — dort steht die Wertspalte stellenweise um eine Zeile verschoben.",
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
    /// Statusreport einer fondsgebundenen Lebensversicherung.
    /// </summary>
    /// <remarks>
    /// <para>Derselbe Absender, ein anderes Papier: dieser Vertrag trägt keinen Rückkaufswert und
    /// keine Überschussbeteiligung, sondern <b>Fondsanteile</b>. Sein erreichter Wert ist das
    /// <b>Anteilsguthaben</b> — die Summe der Fondszeilen —, und garantierte Leistungen gibt es
    /// nicht: das Dokument sagt das ausdrücklich in einem Satz. Deshalb eine eigene Art und keine
    /// Felder, die hier leer bleiben müssten.</para>
    /// <para>Entstanden an <b>zwölf Jahresberichten desselben Vertrags, 2012 bis 2025</b>. Sie
    /// zerfallen in zwei Jahrgänge: bis 2017 „Ihr aktueller Vertragsstand“, „Todesfallschutz“ und
    /// Beträge in „Euro“; ab 2018 „Ihr Vertragsstand“, „Mindesttodesfallschutz“ neben „Aktuelle
    /// Leistung im Todesfall“ und „EUR“. Beide Schreibweisen stehen deshalb nebeneinander in den
    /// Beschriftungen.</para>
    /// <para>Der Jahrgang 2023 ist ein Scan: dort steht „Vortragsstand“, „31,12.2023“ und
    /// „43 866.12“. Der Stichtag kommt in dem Fall aus der Betreffzeile — deshalb nennt das
    /// Muster beide Überschriften. Die verunglückten Beträge werden <em>nicht</em> geraten; das
    /// Anteilsguthaben selbst steht dort unbeschädigt.</para>
    /// </remarks>
    public static readonly DocumentKind FundStatusreport = new()
    {
        Key = "statusreport-fonds-lv",
        Label = "Statusreport fondsgebundene Lebensversicherung",

        // Derselbe Typ wie beim klassischen Bericht: für die Ablage ist beides ein Statusreport,
        // gelesen wird es verschieden.
        TypeName = "Statusreport",
        Area = DocumentArea.Insurance,
        Target = DocumentTargetKind.Policy,
        TargetNoun = "Vertrag",
        TargetLink = "Zum Vertrag",
        FolderTemplate = "Lebensversicherung/{ziel}/{jahr}",
        FileTemplate = "Statusreport_{stichtag}",

        // „Anteilsguthaben“ trennt diese Art von der klassischen: dort steht „Wert der
        // Versicherung“, hier eine Fondstabelle. Beide Kennzeichen stehen in allen zwölf
        // Berichten.
        Markers = [new("Statusreport"), new("Anteilsguthaben")],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "vertragsnummer",

        // Je Fonds eine Zeile: WKN, Bezeichnung, Anteile, Anteilspreis, Wert. Der ältere
        // Jahrgang trägt dazwischen noch die Fondswährung — die Muster greifen die Zahlen
        // deshalb der Reihe nach ab und nicht nach Spaltennummer.
        Repeat = new DocumentRepeatRule
        {
            Title = "Fonds in diesem Statusreport",

            // Sechsstellige WKN, dann ein Name, und am Ende ein Betrag. Ohne den Betrag am Ende
            // eröffnete die Fußzeile „665463 · Fax: +49 40 21995 6999 · … · 5014“ einen Block,
            // dem jeder Wert fehlt — und die Summenprobe fiel dann ganz aus, statt zu prüfen.
            Anchor = @"^[0-9A-Z]{6}\s+\D.*[\d.]+,\d{2}$",
            ValueField = "fondswert",
            NameField = "fonds",
            TotalField = "anteilsguthaben",

            // Fünf Cent Luft: der Bericht summiert die ungerundeten Zeilenwerte, ausgewiesen sind
            // die gerundeten. Bei sechs Fonds gehen so zwei Cent Unterschied auf das Papier
            // (Jahrgang 2024) — eine fehlende Zeile dagegen fehlt in Hunderten.
            TotalTolerance = 0.05m,

            Fields =
            [
                new()
                {
                    Key = "wkn", Label = "WKN", Kind = DocumentValueKind.Text,
                    Locator = DocumentLocator.Pattern, Pattern = @"^([0-9A-Z]{6})\s",
                },
                new()
                {
                    Key = "fonds", Label = "Fonds", Kind = DocumentValueKind.Text,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Z]{6}\s+(.+?)\s+[\d.]+,\d+\s",
                },
                new()
                {
                    Key = "anteile", Label = "Anteile", Kind = DocumentValueKind.Quantity,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Z]{6}\s+.+?\s([\d.]+,\d+)\s",
                },
                new()
                {
                    Key = "anteilspreis", Label = "Anteilspreis", Kind = DocumentValueKind.Price,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Z]{6}\s+.+?\s[\d.]+,\d+\s+([\d.]+,\d+)\b",
                },
                new()
                {
                    Key = "fondswert", Label = "Wert der Anteile", Kind = DocumentValueKind.Money,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Z]{6}\s.*?([\d.]+,\d{2})$", Lead = true,
                },
            ],

            // Keine Probe je Zeile: der ältere Jahrgang weist den Anteilspreis auf zwei Stellen
            // aus, und Anteile × Preis weicht dann um Cent ab. Das wäre eine Probe über die
            // Rundung und nicht über die Zuordnung. Geprüft wird die Summe — sie steht im
            // Dokument selbst.
        },

        Fields =
        [
            // Der erreichte Wert. Er ist der Lead: aus ihm entsteht der Stand im Vertrag.
            new()
            {
                Key = "anteilsguthaben", Label = "Anteilsguthaben", Kind = DocumentValueKind.Money,
                Labels = ["Anteilsguthaben"], Lead = true,
            },

            // Die Beschriftung bricht im älteren Jahrgang um: „Beitragssumme“ steht allein in
            // einer Zeile, der Betrag hinter der Klammer in der nächsten. Beide Anfänge stehen
            // deshalb hier.
            new()
            {
                Key = "beitragssumme", Label = "Beitragssumme", Kind = DocumentValueKind.Money,
                Labels = ["Beitragssumme", "(entspricht den über die Laufzeit"],
            },

            // Zwei Größen, zwei Felder: der Mindestschutz entspricht der Beitragssumme, die
            // aktuelle Leistung im Todesfall wächst mit dem Vertragskapital. Sie in ein Feld zu
            // legen hieße, im einen Jahrgang das eine und im anderen das andere zu zeigen.
            new()
            {
                Key = "mindesttodesfall", Label = "Mindesttodesfallschutz",
                Kind = DocumentValueKind.Money, Labels = ["Mindesttodesfallschutz"],
            },
            new()
            {
                Key = "todesfall", Label = "Leistung im Todesfall", Kind = DocumentValueKind.Money,
                Labels = ["Aktuelle Leistung im Todesfall", "Todesfallschutz"],
            },

            // Kein Betrag, sondern eine Auskunft: der Vertrag befreit im Fall der
            // Berufsunfähigkeit von den Beiträgen und zahlt keine Rente. Als Geldfeld bliebe es
            // leer, und niemand wüsste, ob die Angabe fehlt oder keine ist.
            new()
            {
                Key = "bu", Label = "Leistungen bei Berufsunfähigkeit",
                Kind = DocumentValueKind.Text, Labels = ["Leistungen bei Berufsunfähigkeit"],
            },

            // Kopfdaten wie beim klassischen Statusreport — mit einem Unterschied: als zweite
            // Quelle für den Stichtag zählt die Betreffzeile. Im Scan von 2023 ist die
            // Vertragsstandzeile verlesen, die Betreffzeile aber nicht.
            new()
            {
                Key = "stichtag", Label = "Stichtag", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,
                Pattern = @"(?:Vertragsstand|Statusreport) zum\s+(\d{1,2}[.,]\s*(?:\d{1,2}[.,]|[A-Za-zÄÖÜäöüß]+\s+)\d{4})",
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
                Pattern = @"^([A-ZÄÖÜ][\w.\-äöüß]*(?: [\w.\-äöüß]+)*? (?:Lebensversicherung|Leben) AG)\b",
            },
        ],

        Steps =
        [
            "Text gelesen ({seiten} Seiten)",
            "Absender: {absender}",
            "Typ: Statusreport (fondsgebunden)",
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
        TypeName = "Quartalsaufstellung",
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

        // Je Bestandszeile ein Block: Stückzahl, ISIN, Bezeichnung und Verwahrart stehen im
        // realen Dokument auf sechs Zeilen untereinander, und ein Depot mit drei Fonds
        // wiederholt das dreimal.
        Repeat = new DocumentRepeatRule
        {
            Title = "Positionen in dieser Aufstellung",
            Anchor = @"^Stück\s+[\d.]+(?:,\d+)?\b",
            ValueField = "kurswert",
            NameField = "papier",
            TotalField = "depotwert",

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
            ],

            // Erste Stufe der Probe: je Zeile. Bei einer verrutschten Wertspalte fällt genau
            // eine Zeile heraus — in der Summe allein bliebe das unsichtbar.
            RowCheck = new DocumentCheck
            {
                Result = "kurswert", Parts = ["nominale", "kurs"], Kind = DocumentCheckKind.Product,
                Note = "Nominale × Kurs",
                Why = "Nominale mal Kurs muss den ausgewiesenen Kurswert dieser Zeile ergeben. "
                      + "Stück, Kurs und Kurswert stehen ohne Beschriftung nebeneinander.",
            },
        },

        Fields =
        [
            // Zweite Stufe der Probe: die Summe der Zeilen gegen diesen Wert.
            new()
            {
                Key = "depotwert", Label = "Depotwert gesamt", Kind = DocumentValueKind.Money,
                Labels = ["Depotwert"], Lead = true,
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

        Steps =
        [
            "Text gelesen ({seiten} Seiten)",
            "Absender: {absender}",
            "Typ: Quartalsaufstellung",
            "{ziel}",
            "{werte} Werte gelesen",
        ],
    };

    /// <remarks>
    /// Die klassische Art steht vor der fondsgebundenen: beide führen „Statusreport“, und der
    /// klassische Bericht verlangt zusätzlich „Wert der Versicherung“, den der fondsgebundene
    /// nicht kennt. Die Kennzeichen schließen sich also aus — die Reihenfolge ist trotzdem
    /// festgelegt, damit die Zuordnung nicht davon abhängt, welche Art zufällig zuerst steht.
    /// </remarks>
    public static readonly IReadOnlyList<DocumentKind> All =
        [Statusreport, FundStatusreport, QuarterlyStatement];

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

/// <summary>
/// Das Ergebnis einer Rechenprobe, wie es der Prüfschritt zeigt.
/// </summary>
/// <remarks>
/// Sie steht sichtbar vor der Übernahme und nicht nur im Protokoll — v5-Handoff, Abschnitt
/// 15.6. Wer eine Zahl in sein Vermögen übernimmt, soll sehen, woran sie geprüft wurde.
/// </remarks>
public sealed record ProofResult
{
    /// <summary>Die Rechnung im Klartext: „18.373,87 EUR + 2.107,65 EUR = 20.481,52 EUR“.</summary>
    public required string Line { get; init; }

    /// <summary>Warum diese Probe überhaupt gemacht wird.</summary>
    public required string Why { get; init; }

    /// <summary>Ob sie aufgeht.</summary>
    public required bool Passed { get; init; }
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

/// <summary>Eine gelesene Zeile einer Wiederholgruppe.</summary>
public sealed record ReadRow
{
    /// <summary>Die Felder dieser Zeile, in der Reihenfolge des Typs.</summary>
    public required IReadOnlyList<ReadValue> Values { get; init; }

    public ReadValue? this[string key] => Values.FirstOrDefault(v => v.Rule.Key == key);
}

/// <summary>Was aus einem Dokument herauskam.</summary>
public sealed record ExtractionResult
{
    public required IReadOnlyList<ReadValue> Values { get; init; }
    public required IReadOnlyList<ProofResult> Proofs { get; init; }

    /// <summary>Die Zeilen der Wiederholgruppe. Leer, wenn der Typ keine hat.</summary>
    public IReadOnlyList<ReadRow> Rows { get; init; } = [];
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
        => Read(kind, content).Values;

    /// <summary>
    /// Die gelesenen Werte samt ihren Rechenproben.
    /// </summary>
    /// <remarks>
    /// Die Proben gehen mit hinaus, weil sie vor der Übernahme sichtbar sein müssen. Eine
    /// Prüfung, die nur im Verborgenen stattfindet, überzeugt niemanden von einer Zahl.
    /// </remarks>
    public ExtractionResult Read(DocumentKind kind, PdfContent content)
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

        var proben = new List<ProofResult>();

        foreach (var probe in kind.Checks)
        {
            if (Verify(probe, kind, werte, basis) is { } ergebnis)
            {
                proben.Add(ergebnis);
            }
        }

        var zeilen = kind.Repeat is { } gruppe
            ? Repeat(gruppe, content, basis, proben)
            : [];

        if (kind.Repeat is { } summe)
        {
            Total(summe, kind, zeilen, werte, proben);
        }

        return new ExtractionResult
        {
            Values = [.. kind.Fields.Where(f => werte.ContainsKey(f.Key)).Select(f => werte[f.Key])],
            Proofs = proben,
            Rows = zeilen,
        };
    }

    // ── Wiederholgruppe ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zerlegt das Dokument an den Ankerzeilen und liest jeden Block für sich.
    /// </summary>
    /// <remarks>
    /// Ein Block läuft von seiner Ankerzeile bis zur nächsten. Die Feldzuordnung darin ist
    /// dieselbe wie sonst — nur auf einem Ausschnitt statt auf dem ganzen Dokument. Genau das
    /// fehlte vorher: der Extraktor nahm je Feld den ersten Treffer im ganzen Text, und drei
    /// Fonds wurden zu einer Position.
    /// </remarks>
    private List<ReadRow> Repeat(
        DocumentRepeatRule gruppe, PdfContent content, double basis, List<ProofResult> proben)
    {
        var anker = new Regex(gruppe.Anchor, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var starts = content.Lines
            .Select((zeile, i) => (Index: i, Treffer: anker.IsMatch(zeile.Text)))
            .Where(x => x.Treffer)
            .Select(x => x.Index)
            .ToList();

        var zeilen = new List<ReadRow>();

        for (var i = 0; i < starts.Count; i++)
        {
            var bis = i + 1 < starts.Count ? starts[i + 1] : content.Lines.Count;
            var block = content.Lines.Skip(starts[i]).Take(bis - starts[i]).ToList();

            var werte = new Dictionary<string, ReadValue>();

            foreach (var regel in gruppe.Fields)
            {
                if (Find(regel, [], BlockOf(block), basis) is { } wert)
                {
                    werte[regel.Key] = wert;
                }
            }

            if (gruppe.RowCheck is { } probe && Verify(probe, gruppe.Fields, werte, basis) is { } ergebnis)
            {
                // Je Zeile eine Probe, benannt nach ihrem Papier — sonst wüsste niemand,
                // welche der drei Zeilen nicht aufgeht.
                var name = werte.GetValueOrDefault(gruppe.NameField)?.Raw;
                proben.Add(ergebnis with
                {
                    Line = (name is { Length: > 0 } ? name + ": " : null) + ergebnis.Line,
                });
            }

            zeilen.Add(new ReadRow
            {
                Values = [.. gruppe.Fields.Where(f => werte.ContainsKey(f.Key)).Select(f => werte[f.Key])],
            });
        }

        return zeilen;
    }

    /// <summary>Ein Block, so verpackt, dass die vorhandene Suche darauf läuft.</summary>
    private static PdfContent BlockOf(List<PdfLine> block) => new()
    {
        PageCount = block.Count == 0 ? 0 : block.Max(z => z.Page),
        Lines = block,
        TextIsInvisible = false,
        ImageCount = 0,
    };

    /// <summary>
    /// Die zweite Stufe der Probe: die Summe der Zeilen gegen den ausgewiesenen Gesamtwert.
    /// </summary>
    /// <remarks>
    /// Je Zeile allein genügt nicht — eine fehlende Zeile ginge durch, weil die vorhandenen für
    /// sich stimmen. Erst die Summe zeigt, ob alle da sind.
    /// </remarks>
    private static void Total(
        DocumentRepeatRule gruppe,
        DocumentKind kind,
        List<ReadRow> zeilen,
        Dictionary<string, ReadValue> werte,
        List<ProofResult> proben)
    {
        if (gruppe.TotalField is not { } schluessel
            || !werte.TryGetValue(schluessel, out var gesamt)
            || gesamt.Number is not { } ausgewiesen
            || zeilen.Count == 0)
        {
            return;
        }

        var teile = zeilen.Select(z => z[gruppe.ValueField]?.Number).ToList();
        if (teile.Any(t => t is null))
        {
            return;
        }

        var summe = decimal.Round(teile.Sum(t => t!.Value), 2);
        var stimmt = Math.Abs(summe - ausgewiesen) <= gruppe.TotalTolerance;

        if (!stimmt)
        {
            werte[schluessel] = gesamt with
            {
                Confidence = Doubtful,
                Warning = $"Die {zeilen.Count} {(zeilen.Count == 1 ? "Zeile ergibt" : "Zeilen ergeben")} "
                          + $"{Format(gesamt.Rule, summe)} — bitte prüfen",
            };
        }

        proben.Add(new ProofResult
        {
            Line = $"{zeilen.Count} {(zeilen.Count == 1 ? "Zeile" : "Zeilen")} = {Format(gesamt.Rule, summe)}"
                   + (stimmt
                       ? " — entspricht dem ausgewiesenen Gesamtwert."
                       : $" — ausgewiesen sind {Format(gesamt.Rule, ausgewiesen)}."),
            Why = "Die Summe der Zeilen muss den ausgewiesenen Gesamtwert ergeben. Je Zeile "
                  + "allein genügt nicht: eine fehlende Zeile ginge durch, weil die übrigen für "
                  + "sich stimmen.",
            Passed = stimmt,
        });
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
            // Nicht nur am Zeilenanfang: die älteren Berichte tragen links eine Druckmarke, und
            // aus „Leistung im Todesfall“ wird in der Textebene „06 · Leistung im Todesfall“.
            // Ohne diesen Blick auf jede Zelle beginnt der Abschnitt nie, und jedes Feld darin
            // bleibt leer.
            var ueberschrift = kind.Sections.FirstOrDefault(
                s => zeile.Cells.Any(z => z.StartsWith(s, StringComparison.OrdinalIgnoreCase)));

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
        var zeilen = Zeilen(regel, abschnitte, content);

        return regel.Locator switch
        {
            DocumentLocator.Label => ByLabel(regel, zeilen, basis),
            DocumentLocator.Pattern => ByPattern(regel, zeilen, basis),
            DocumentLocator.NextLine => ByNextLine(regel, zeilen, basis),
            _ => null,
        };
    }

    /// <summary>
    /// Die Zeilen, in denen ein Feld gesucht wird.
    /// </summary>
    /// <remarks>
    /// Nennt die Regel Abschnitte, gewinnt der erste, den das Dokument führt. Führt es keinen
    /// davon, bleibt die Liste leer — dann wird nicht ersatzweise im ganzen Dokument gesucht:
    /// „Gesamtleistung“ steht im Statusreport dreimal, und der erstbeste Treffer wäre eine
    /// falsche Zahl in einem richtigen Feld.
    /// </remarks>
    private static IReadOnlyList<PdfLine> Zeilen(
        DocumentFieldRule regel,
        Dictionary<string, List<PdfLine>> abschnitte,
        PdfContent content)
    {
        if (regel.Sections.Length == 0)
        {
            return content.Lines;
        }

        foreach (var abschnitt in regel.Sections)
        {
            if (abschnitte.TryGetValue(abschnitt, out var treffer))
            {
                return treffer;
            }
        }

        return [];
    }

    /// <summary>
    /// Beschriftung links, Wert rechts.
    /// </summary>
    /// <remarks>
    /// <para>Die Beschriftung muss eine Zelle <em>beginnen</em> — nicht die Zeile. Das schließt
    /// Fließtext aus, in dem dasselbe Wort mitten im Satz vorkommt („Die Gesamtleistung Ihrer
    /// Versicherung setzt sich…“ ist keine Wertzeile), lässt aber die Druckmarke links davor
    /// zu: die älteren Berichte setzen dort eine Nummer, und aus „garantierte
    /// Erlebensfallleistung · 22.550,00 Euro“ wird „01191591 · garantierte
    /// Erlebensfallleistung · 22.550,00 Euro“.</para>
    /// <para>Der Wert steht dann <em>rechts von der Beschriftung</em> und nie links davon.</para>
    /// </remarks>
    private ReadValue? ByLabel(DocumentFieldRule regel, IReadOnlyList<PdfLine> zeilen, double basis)
    {
        foreach (var zeile in zeilen)
        {
            foreach (var beschriftung in regel.Labels)
            {
                var stelle = -1;

                for (var i = 0; i < zeile.Cells.Count; i++)
                {
                    if (zeile.Cells[i].StartsWith(beschriftung, StringComparison.OrdinalIgnoreCase))
                    {
                        stelle = i;
                        break;
                    }
                }

                if (stelle < 0)
                {
                    continue;
                }

                // Steht die Beschriftung in der letzten Zelle, ist der Wert der Rest dahinter.
                if (stelle == zeile.Cells.Count - 1)
                {
                    var rest = zeile.Cells[stelle][beschriftung.Length..].TrimStart(':', ' ');

                    if (Read(regel, rest) is { } einzeln)
                    {
                        return einzeln with { Page = zeile.Page, Confidence = basis };
                    }

                    continue;
                }

                // Sonst von rechts nach links die erste Zelle, die sich lesen lässt. Die letzte
                // ist nicht immer der Wert: „Depotwert · 95.558,12 · EUR“ trägt hinten die
                // Währung, und wer stur die letzte nimmt, findet dort keinen Betrag.
                for (var i = zeile.Cells.Count - 1; i > stelle; i--)
                {
                    if (Read(regel, zeile.Cells[i]) is { } wert)
                    {
                        return wert with { Page = zeile.Page, Confidence = basis };
                    }
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
    private static ProofResult? Verify(
        DocumentCheck probe, DocumentKind kind, Dictionary<string, ReadValue> werte, double basis)
        => Verify(probe, kind.Fields, werte, basis);

    private static ProofResult? Verify(
        DocumentCheck probe,
        IReadOnlyList<DocumentFieldRule> felder,
        Dictionary<string, ReadValue> werte,
        double basis)
    {
        var teile = probe.Parts.Select(p => werte.TryGetValue(p, out var w) ? w.Number : null).ToList();
        if (teile.Any(t => t is null))
        {
            return null;
        }

        var soll = probe.Kind == DocumentCheckKind.Sum
            ? teile.Sum(t => t!.Value)
            : teile.Aggregate(1m, (a, t) => a * t!.Value);

        if (!werte.TryGetValue(probe.Result, out var ergebnis))
        {
            // Das Dokument nennt den Wert nicht. Dann rechnen wir ihn — und sagen es.
            var regel = felder.FirstOrDefault(f => f.Key == probe.Result);
            if (regel is null)
            {
                return null;
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

            return new ProofResult
            {
                Line = Sentence(probe, felder, werte, gerundet)
                       + " — das Dokument nennt die Summe selbst nicht, sie ist gerechnet.",
                Why = probe.Why,
                Passed = true,
            };
        }

        if (ergebnis.Number is not { } steht)
        {
            return null;
        }

        var satz = Sentence(probe, felder, werte, decimal.Round(soll, 2));

        if (Math.Abs(steht - soll) <= probe.Tolerance)
        {
            return new ProofResult
            {
                Line = satz + " — stimmt mit dem ausgewiesenen Wert überein.",
                Why = probe.Why,
                Passed = true,
            };
        }

        // Die Zuordnung ist verrutscht — genau der Fall, für den die Probe da ist.
        werte[probe.Result] = ergebnis with
        {
            Confidence = Doubtful,
            Warning = $"{probe.Note} ergibt {Format(ergebnis.Rule, decimal.Round(soll, 2))} — bitte prüfen",
        };

        return new ProofResult
        {
            Line = satz + $" — ausgewiesen sind {Format(ergebnis.Rule, steht)}.",
            Why = probe.Why,
            Passed = false,
        };
    }

    /// <summary>
    /// Die Rechnung als Satz, mit den Zahlen, die tatsächlich gelesen wurden.
    /// </summary>
    /// <remarks>
    /// Aus den gelesenen Rohtexten und nicht neu formatiert: der Nutzer soll die Zahlen
    /// wiedererkennen, die er auf dem Blatt vor sich hat.
    /// </remarks>
    private static string Sentence(
        DocumentCheck probe,
        IReadOnlyList<DocumentFieldRule> felder,
        Dictionary<string, ReadValue> werte,
        decimal ergebnis)
    {
        var zeichen = probe.Kind == DocumentCheckKind.Sum ? " + " : " \u00D7 ";
        var regel = felder.First(f => f.Key == probe.Result);

        // Beträge einheitlich geschrieben statt im Rohtext des Papiers: der ältere Jahrgang
        // schreibt „6.099,65 Euro“ und setzt stellenweise ein Leerzeichen als Tausenderpunkt.
        // Alles andere bleibt, wie es dort steht — bei einem Kurs trägt die Schreibweise die
        // Genauigkeit („100,500“ sind drei Stellen und keine zwei).
        var teile = probe.Parts.Select(p =>
        {
            var teil = felder.FirstOrDefault(f => f.Key == p);

            return teil?.Kind == DocumentValueKind.Money && werte[p].Number is { } zahl
                ? Format(teil, zahl)
                : werte[p].Raw;
        });

        return string.Join(zeichen, teile) + " = " + Format(regel, ergebnis);
    }

    // ── Werte lesen ────────────────────────────────────────────────────────────────────────

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Wie ein Datum im Papier stehen kann.
    /// </summary>
    /// <remarks>
    /// Ausdrücklich und nicht über die nachsichtige Prüfung der Kultur: <c>d.M.yyyy</c> ist die
    /// heutige Schreibweise, <c>d. MMMM yyyy</c> die des älteren Jahrgangs — und der schreibt das
    /// Leerzeichen nach dem Punkt manchmal nicht.
    /// </remarks>
    private static readonly string[] Datumsformate =
        ["d.M.yyyy", "dd.MM.yyyy", "d. MMMM yyyy", "d.MMMM yyyy"];

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
            // „31,12.2023“ aus dem Scan: ein Komma steht in einem deutschen Datum nie, und die
            // Stelle, an der es steht, lässt keine zweite Lesart zu. Der Rohtext bleibt, wie er
            // auf dem Papier steht — geprüft wird das berichtigte Datum.
            var datum = text.Replace(',', '.');

            return DateOnly.TryParseExact(datum, Datumsformate, German, DateTimeStyles.None, out var tag)
                ? new ReadValue { Rule = regel, Raw = text, Date = tag }
                : null;
        }

        // „Euro“ vor „EUR“: umgekehrt bliebe von „6.099,65 Euro“ ein „6.099,65 o“ übrig, und der
        // ganze Jahrgang bis 2018 wäre unlesbar — er schreibt die Währung aus. Genau daran ist es
        // gescheitert.
        var zahl = text.Replace("Euro", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("€", string.Empty, StringComparison.Ordinal)
            .Replace("Stück", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        // Leerzeichen innerhalb der Zahl fallen weg: die Textebene setzt stellenweise ein
        // Leerzeichen als Tausenderpunkt („24 782,58“). Ein Betrag trägt nie eines.
        zahl = zahl.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00a0", string.Empty, StringComparison.Ordinal);

        if (!IsGermanNumber(zahl))
        {
            return null;
        }

        return decimal.TryParse(zahl, NumberStyles.Number, German, out var wert)
            ? new ReadValue { Rule = regel, Raw = text, Number = wert }
            : null;
    }

    /// <summary>
    /// Ob die Zeichenfolge im Deutschen überhaupt eine Zahl ist.
    /// </summary>
    /// <remarks>
    /// <para>Ein Punkt mit einer oder zwei Stellen dahinter ist keine: als Tausenderpunkt
    /// bräuchte er drei Stellen, als Dezimaltrenner ein Komma. Die deutsche Kultur liest ihn
    /// trotzden — sie prüft die Gruppengröße nicht — und macht aus dem eingescannten
    /// „43 866.12“ den Betrag <b>4.386.612</b>, also das Hundertfache.</para>
    /// <para>Gefunden am Scan von 2023: dort hat die Texterkennung Punkt und Komma vertauscht.
    /// Lieber kein Wert als der hundertfache — die Übernahme verlangt ihn dann von Hand.</para>
    /// </remarks>
    private static bool IsGermanNumber(string zahl)
    {
        if (zahl.Contains(',', StringComparison.Ordinal))
        {
            return true;
        }

        var punkt = zahl.LastIndexOf('.');

        // Ohne Punkt bleibt nichts zu prüfen; mit Punkt müssen genau drei Stellen folgen.
        return punkt < 0 || zahl.Length - punkt - 1 == 3;
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
