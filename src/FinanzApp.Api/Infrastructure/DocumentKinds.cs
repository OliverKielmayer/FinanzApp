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

    /// <summary>
    /// Beschädigte Trennzeichen dürfen berichtigt werden.
    /// </summary>
    /// <remarks>
    /// <para>Eingescannte Tabellen verlieren Punkt und Komma: aus <c>5.135,57</c> wird
    /// <c>4.220^35</c>, aus <c>2.776,40</c> kommt der Tausenderpunkt als zweites Komma zurück,
    /// und <c>14.791,94</c> erscheint als <c>12,480.75</c>. Ist das gesetzt, gilt <b>das letzte
    /// Trennzeichen als Dezimaltrenner</b>, und alle übrigen fallen weg.</para>
    /// <para><b>Nur dort setzen, wo das Papier die Probe mitliefert.</b> Die Berichtigung ist
    /// eine Vermutung; sie darf nur stehen bleiben, wenn eine Rechnung sie bestätigt. In der
    /// Fondstabelle tut das der ausgewiesene Gesamtwert — geht die Summe nicht auf, wird der
    /// Wert als zweifelhaft ausgewiesen und niemand übernimmt ihn blind. An einem einzelnen
    /// Feld ohne Gegenprobe wäre dieselbe Vermutung eine Behauptung: die Beitragssumme
    /// <c>39 415.80</c> aus dem Scan von 2023 bleibt deshalb ungelesen.</para>
    /// </remarks>
    public bool ScanRepair { get; init; }

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

    /// <summary>
    /// Teile, die <em>abgezogen</em> werden.
    /// </summary>
    /// <remarks>
    /// <para>Eine Vorsorgeabrechnung addiert nicht nur, sie behält ein: „Kapital zum
    /// Jahresbeginn + Beiträge + Zulagen + Wertentwicklung − Kosten = Kapital zum Jahresende“ ist
    /// die Rechnung, die das Papier selbst vorführt. Ohne Abzug ließe sie sich nicht
    /// aufschreiben, und die einzige Probe eines Riester-Berichts fiele aus.</para>
    /// <para><b>Ein Abzug darf fehlen und zählt dann als null</b> — anders als ein Teil aus
    /// <see cref="Parts"/>, ohne den die Probe ganz ausfällt. Der Grund ist die Richtung des
    /// Irrtums: was nicht ausgewiesen ist, wurde meist nicht einbehalten (bis 2017 gab es keine
    /// Zeile „Fondskosten“), und ein Abzug, der auf dem Papier steht und ungelesen bleibt, lässt
    /// die Probe <em>scheitern</em>. Beides ist die sichere Seite.</para>
    /// <para>Nur bei <see cref="DocumentCheckKind.Sum"/>. Ein Produkt hat keine Abzüge.</para>
    /// </remarks>
    public string[] Minus { get; init; } = [];

    /// <summary>
    /// Ein Teil, dessen <em>Vorzeichen</em> das Papier nicht hergibt.
    /// </summary>
    /// <remarks>
    /// <para>Der Riester-Bericht weist das Ergebnis des Jahres mit einem Zeichen davor aus, und
    /// die Texterkennung setzt dieses Zeichen auf eine eigene Zeile: aus „+ 941,97 EUR“ wird
    /// eine Zeile mit dem Betrag und eine mit dem Plus. Am Betrag selbst ist nicht zu erkennen,
    /// ob das Jahr gewonnen oder verloren hat — 2018 sind es <em>minus</em> 41,20 EUR.</para>
    /// <para>Die Probe versucht deshalb beide Richtungen. Geht eine auf, ist das Vorzeichen
    /// damit <b>bewiesen</b> und nicht geraten: die Rechnung des Papiers lässt nur eine zu. Der
    /// gelesene Wert bekommt es danach mitgeteilt, und der Prüfschritt zeigt einen Verlust als
    /// Verlust.</para>
    /// <para>Gehen beide nicht auf, gilt die Probe als gescheitert — gemeldet mit der Richtung,
    /// die das Papier anzeigt.</para>
    /// </remarks>
    public string? Unsigned { get; init; }

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
/// <summary>
/// Ein Kennzeichen, an dem eine Dokumentart erkannt wird.
/// </summary>
/// <param name="Text">Die Schreibweise, wie sie auf dem heilen Papier steht.</param>
/// <param name="Also">
/// Weitere Schreibweisen desselben Wortes, die ein Scan hergibt.
/// </param>
/// <remarks>
/// Der Grund ist ein einzelner Buchstabe: die Texterkennung eines Jahrgangs schreibt
/// „Anteiisguthaben“ mit i statt l, und daran hing die ganze Art — der Bericht wurde nicht
/// erkannt, obwohl er vollständig lesbar war. Ein Kennzeichen zu kürzen, bis es beide Formen
/// trifft, wäre der schlechtere Weg: „Antei“ steht auch in „Anteile“, der Spaltenüberschrift
/// derselben Tabelle. Die zweite Schreibweise ausdrücklich zu nennen sagt, was sie ist — ein
/// Lesefehler eines bestimmten Scans und keine Regel über die Sprache.
/// </remarks>
public sealed record DocumentMarker(string Text, params string[] Also)
{
    public bool Matches(string text)
        => text.Contains(Text, StringComparison.OrdinalIgnoreCase)
           || Also.Any(a => text.Contains(a, StringComparison.OrdinalIgnoreCase));
}

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

                // Berichtigt werden darf hier, weil das Papier die Gegenprobe mitliefert:
                // Rückkaufswert + Ansammlungsguthaben = Gesamtleistung. Der Scan von 2023
                // schreibt „24.310.55“ mit Punkt statt Komma, und ohne Berichtigung bliebe der
                // Wert ungelesen — die Probe hätte nichts zu prüfen.
                ScanRepair = true,
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
                Sections = ["Wert der Versicherung"],

                // „Gesam“ als zweite Beschriftung: der Scan von 2023 schreibt
                // „Gesamileistung“, und ein verlesenes t nimmt dem Bericht sonst seinen
                // Leitwert. Kurz genug, um beides zu treffen, und im Abschnitt „Wert der
                // Versicherung“ eindeutig — dort beginnt keine andere Zeile so.
                Labels = ["Gesamtleistung", "Gesam"],
                Lead = true, ScanRepair = true,
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
                //
                // Drei Zugeständnisse an eingescannte Jahrgänge, alle drei an echten Berichten
                // gelernt: „Vertragsstandzum 31.10.2023“ ohne Leerzeichen (2023); und ein
                // Umbruch mitten im Satz, der das Datum allein auf eine Zeile stellt — dann
                // beginnt sie mit „zum“ (2012). Das „zum“ muss dabei die Zeile <em>eröffnen</em>:
                // „Leistung im Erlebensfall zum Ablauf 01.11.2036“ ist kein Stichtag, und ein
                // freies „zum“ träfe irgendwann genau das.
                Pattern = @"(?:Vertragsstand\s*zum|^zum)\s+"
                          + @"(\d{1,2}\.\s*(?:\d{1,2}\.|[A-Za-zÄÖÜäöüß]+\s+)\d{4})",
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
    /// „39 415.80“. Der Stichtag kommt in dem Fall aus der Betreffzeile — deshalb nennt das
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
        Markers = [new("Statusreport"), new("Anteilsguthaben", "Anteiisguthaben")],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "vertragsnummer",

        // Je Fonds eine Zeile: WKN, Bezeichnung, Anteile, Anteilspreis, Wert. Der ältere
        // Jahrgang trägt dazwischen noch die Fondswährung — die Muster greifen die Zahlen
        // deshalb der Reihe nach ab und nicht nach Spaltennummer.
        Repeat = new DocumentRepeatRule
        {
            Title = "Fonds in diesem Statusreport",

            // Ein Kennzeichen am Zeilenanfang, dann ein Name, dann Anteile und Anteilspreis.
            //
            // Das Kennzeichen ist die WKN — aber nicht auf sechs Großbuchstaben festgenagelt:
            // der Scan von 2016 liest „120547“ als „12054;r“, und die ganze Zeile fiel heraus.
            // Ihre 705,40 EUR fehlten danach in der Summe, ohne dass die Meldung sagen konnte,
            // warum.
            //
            // Erkannt wird die Zeile an <b>zwei durch Leerraum getrennten Dezimalzahlen</b> und
            // nicht am Betrag am Ende. Der Betrag ist genau das, was ein beschädigter Scan
            // verliert: im Jahrgang 2023 steht dort „1 845 60“, und hinge die Zeile daran, wäre
            // sie unsichtbar — nicht einmal von Hand nachzutragen, weil nichts dastünde, das
            // man antippen könnte. Anteile und Preis überstehen dieselbe Beschädigung meist.
            //
            // Zwei Zahlen mit Leerraum dazwischen, nicht bloß zwei Zahlen: „25.115,92“ trägt
            // sonst beide in sich („25.1“ und „15,92“), und der Satz „reichten Anteilsguthabens
            // 25.115,92 EUR ggf. reduziert …“ eröffnete einen Block ohne Wert. Die Fußzeile
            // „665463 · Fax: +49 40 21995 6999 · … · 5014“ trägt gar keine Dezimalzahl.
            Anchor = @"^[0-9A-Za-z;:.]{5,8}\s+\D.*?\d+[.,^]\d+\s+.*\d+[.,^]\d+",
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
                    Locator = DocumentLocator.Pattern, Pattern = @"^([0-9A-Za-z;:.]{5,8})\s",
                },
                new()
                {
                    Key = "fonds", Label = "Fonds", Kind = DocumentValueKind.Text,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Za-z;:.]{5,8}\s+(.+?)\s+\d+[.,^]\d+\s",
                },
                new()
                {
                    Key = "anteile", Label = "Anteile", Kind = DocumentValueKind.Quantity,
                    Locator = DocumentLocator.Pattern, ScanRepair = true,
                    Pattern = @"^[0-9A-Za-z;:.]{5,8}\s+.+?\s(\d+[.,^]\d+)\s",
                },
                new()
                {
                    Key = "anteilspreis", Label = "Anteilspreis", Kind = DocumentValueKind.Price,
                    Locator = DocumentLocator.Pattern, ScanRepair = true,
                    Pattern = @"^[0-9A-Za-z;:.]{5,8}\s+.+?\s\d+[.,^]\d+\s+(\d+[.,^]\d+)\b",
                },
                new()
                {
                    Key = "fondswert", Label = "Wert der Anteile", Kind = DocumentValueKind.Money,
                    Locator = DocumentLocator.Pattern,
                    Pattern = @"^[0-9A-Za-z;:.]{5,8}\s.*?(\d{1,3}(?:[.,^]\d{3})*[.,^]\d{2})$",
                    Lead = true, ScanRepair = true,
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

                // Beide Schreibweisen, aus demselben Grund wie beim Kennzeichen: ein Jahrgang
                // schreibt „Anteiisguthaben“. Ohne die zweite bliebe der Bericht erkannt und
                // ohne Wert — schlimmer als gar nicht erkannt.
                Labels = ["Anteilsguthaben", "Anteiisguthaben"],
                Lead = true,
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
    /// <summary>
    /// Statusreport einer staatlich geförderten Altersvorsorge (Riester).
    /// </summary>
    /// <remarks>
    /// <para>Ein anderer Bericht als die beiden Lebensversicherungen, obwohl derselbe Absender
    /// ihn schickt: er rechnet ein <b>Jahr</b> vor statt einen Stand aufzuschlüsseln — Kapital
    /// zum Jahresbeginn, Beiträge, Zulagen, Kosten, Wertentwicklung, Kapital zum Jahresende.
    /// Damit bringt er seine Rechenprobe selbst mit, und sogar zwei: die Jahresbewegung und die
    /// Aufteilung des Endkapitals in garantiertes Deckungskapital und Anteilsguthaben.</para>
    /// <para><b>Der erreichte Wert ist „Kapital zum Jahresende“</b> und nicht „Gebildetes
    /// Kapital zum Jahresende“, das im neueren Jahrgang darunter steht: letzteres enthält
    /// Bewertungsreserven und Schlussüberschüsse, und die weist das Papier selbst als nicht
    /// garantiert aus. Dieselbe Entscheidung wie beim klassischen Statusreport — in einer
    /// Vermögenssumme steht nur, was jemand versprochen hat. Im älteren Jahrgang (bis 2016) gibt
    /// es nur die eine Zeile; sie trägt dort dieselbe Bedeutung.</para>
    /// <para><b>Fast alles über Muster statt über Beschriftungen.</b> Die Berichte sind
    /// eingescannt, und die Texterkennung zerlegt die Wörter links: „Einbehal ene
    /// Verwaltungskosten“, „Insgesannt zugeflossene“, „Gebildet?js Kapital“,
    /// „Verstehendngsnummer“. Was durchhält, ist der <em>hintere</em> Teil der Zeile — deshalb
    /// hängen die Muster dort und nicht am Anfang der Beschriftung. Gelernt an zwölf echten
    /// Berichten, 2012 bis 2025.</para>
    /// </remarks>
    public static readonly DocumentKind RiesterStatusreport = new()
    {
        Key = "statusreport-riester",
        Label = "Statusreport staatlich geförderte Altersvorsorge",

        // Dieselbe Schublade wie die anderen beiden: für die Ablage ist es ein Statusreport.
        TypeName = "Statusreport",
        Area = DocumentArea.Insurance,
        Target = DocumentTargetKind.Policy,
        TargetNoun = "Vertrag",
        TargetLink = "Zum Vertrag",
        FolderTemplate = "Lebensversicherung/{ziel}/{jahr}",
        FileTemplate = "Statusreport_{stichtag}",

        // „Kapital zum Jahresende“ trennt diese Art von den beiden anderen — es ist die Zeile,
        // die es dort nicht gibt. Der Produktname wäre die naheliegende Wahl und die schlechtere:
        // er bindet die Regel an einen Vertrag statt an einen Aufbau.
        //
        // Nicht „staatlich geförderte Altersvorsorge“: im Jahrgang 2015 bricht die Zeile
        // zwischen „staatlich“ und „geförderten“ um, und der Vergleich läuft über den Text mit
        // Zeilenumbrüchen.
        Markers = [new("Statusreport"), new("Kapital zum Jahresende")],

        AsOfField = "stichtag",
        DocumentDateField = "dokumentdatum",
        TargetNumberField = "vertragsnummer",

        Fields =
        [
            // Der erreichte Wert. Das Muster greift beide Schreibweisen — „Kapital zum
            // Jahresende“ und „Gebildetes Kapital zum Jahresende“ —, und im neueren Jahrgang
            // gewinnt die erste Zeile, weil sie oben steht. Genau die ist gemeint.
            //
            // Kein Treffer im Fließtext: „Das Kapital zum Jahresende setzt sich zusammen aus …“
            // trägt hinter „Jahresende“ kein Betrag, und das Muster verlangt einen.
            new()
            {
                Key = "kapitalEnde", Label = "Kapital zum Jahresende",
                Kind = DocumentValueKind.Money, Lead = true,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Kapital zum Jahresende\s*=?\s*([\d.]+,\d{2})",
            },

            new()
            {
                Key = "kapitalAnfang", Label = "Kapital zum Jahresbeginn",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Kapital zum Jahresbeginn\s*([\d.]+,\d{2})",
            },

            // Der Betrag steht meist in derselben Zeile, im Jahrgang 2014 in der nächsten („+
            // 787,56 EUR“ allein darunter). Beide Wege stehen deshalb bereit — erst der Blick
            // in die Zeile, dann der in die folgende.
            new()
            {
                Key = "eigenbeitraege", Label = "Eigenbeiträge im Jahr",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Eigenbeitr[äa]ge [Ii]m abgelaufenen Jahr\s*\+?\s*([\d.]+,\d{2})",
            },
            // Zweites Feld für dieselbe Größe, und kein Schönheitsfehler: im Jahrgang 2014
            // steht der Betrag eine Zeile tiefer („+ 787,56 EUR“ allein darunter). Ein Feld
            // kann nur einen Weg gehen, und beide Wege schließen sich aus — wo der eine trifft,
            // findet der andere nichts. Die Jahresprobe steht deshalb in zwei Fassungen da.
            new()
            {
                Key = "eigenbeitraegeZeileTiefer", Label = "Eigenbeiträge im Jahr",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.NextLine,
                Labels = ["Gezahlte Eigenbeiträge", "Geleistete Eigenbeiträge"],
            },

            // Die Zulagen: das Muster hängt am hinteren Teil der Zeile. Vorn steht je nach
            // Jahrgang „Insgesamt zugeflossene“, „insgesamt zugefiossene“ oder „Insgesannt
            // zugeflossene“ — die Texterkennung trifft dort nichts zuverlässig.
            new()
            {
                Key = "zulagen", Label = "Staatliche Zulagen im Jahr",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"staatliche Zulagen im abgelaufenen Jahr\s*\+?\s*([\d.]+,\d{2})",
            },

            // Die drei Abzüge. Jedes Muster verlangt einen Betrag <em>direkt</em> hinter dem
            // Wort — die Fußnoten nennen dieselben Wörter im Fließtext („Die Betrachtung der
            // Höhe der Kosten …“, „Die Fondskosten haben wir mit den …“) und tragen dort keine
            // Zahl. Ohne diese Bedingung läse der Bericht seine eigene Fußnote als Kosten.
            new()
            {
                Key = "abschlusskosten", Label = "Einbehaltene Abschluss- und Vertriebskosten",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Vertriebskosten\*?\s+([\d.]+,\d{2})",
            },
            new()
            {
                Key = "verwaltungskosten", Label = "Einbehaltene Verwaltungskosten",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Verwaltungskosten\*{0,2}\s+([\d.]+,\d{2})",
            },
            new()
            {
                Key = "fondskosten", Label = "Fondskosten",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Fondskosten\*?\s+([\d.]+,\d{2})",
            },

            // Zwei Zeilen, die es nur in einem Jahr gibt: was der Versorgungsausgleich aus dem
            // Vertrag genommen hat, und was er gekostet hat. Gefunden hat sie die Rechenprobe —
            // sie fiel im Jahrgang 2024 um genau 9.420,50 EUR auseinander, und das ist die
            // Summe dieser beiden. Ohne sie hätte der Bericht als fehlerhaft dagestanden,
            // obwohl er stimmt.
            new()
            {
                Key = "entnahme", Label = "Entnahme Versorgungsausgleich",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Entnahme Versorgungsausgleich[^\d]*([\d.]+,\d{2})",
            },
            new()
            {
                Key = "entnahmekosten", Label = "Kosten Versorgungsausgleich",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Kosten Versorgungsausgleich[^\d]*([\d.]+,\d{2})",
            },

            // Bis 2016 „Ergebnis im abgelaufenen Jahr inkl. Fondsentwicklung“, ab 2017
            // „Wertentwicklung des Anteilsguthabens und Erträge im klassischen Deckungskapital“.
            // Dieselbe Größe, zwei Namen — ein Feld, denn die Probe braucht sie an einer Stelle.
            new()
            {
                Key = "wertentwicklung", Label = "Ergebnis im Jahr",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"(?:Ergebnis im abgelaufenen Jahr|Wertentwicklung des Anteilsguthabens)"
                          + @"[^\d]*([\d.]+,\d{2})",
            },

            // Nicht garantiert — und deshalb nie Teil des erreichten Werts. Das Papier erklärt
            // es selbst in einem eigenen Absatz.
            new()
            {
                Key = "bewertungsreserven", Label = "Bewertungsreserven",
                Kind = DocumentValueKind.Money, Soft = true,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Beteiligung an den Bewertungsreserven\s*\+?\s*([\d.]+,\d{2})",
            },
            new()
            {
                Key = "schlussueberschuesse", Label = "Schlussüberschüsse",
                Kind = DocumentValueKind.Money, Soft = true,
                Locator = DocumentLocator.Pattern,

                // „Schlussüberschüsse“ und „Schfussüberschüsse“ — der Scan von 2023 verliert das
                // l. Gesucht wird deshalb um die Bruchstelle herum.
                Pattern = @"Nicht garantierte Sch\w*bersch[üu]sse[^\d]*([\d.]+,\d{2})",
            },

            // Das gebildete Kapital: erreichter Wert plus die beiden nicht garantierten Posten.
            // Es steht hier, weil das Papier es ausweist und die zweite Probe es prüft — aber es
            // ist nicht der Wert, der ins Vermögen geht.
            new()
            {
                Key = "gebildetesKapital", Label = "Gebildetes Kapital zum Jahresende",
                Kind = DocumentValueKind.Money, Soft = true,
                Locator = DocumentLocator.Pattern,
                Pattern = @"Gebildet.{0,4} Kapital zum Jahresende\s*=?\s*([\d.]+,\d{2})",
            },

            // Die beiden Bestandteile des Endkapitals. Sie stehen im Fließtext („… setzt sich
            // zusammen aus dem garantierten Deckungskapital von 10.205,30 EUR und einem
            // Anteilsguthaben von 10.210,30 EUR“) und im Jahrgang 2016 mitten im Wort
            // umgebrochen — „einem An“ / „teilsguthaben von …“. Das Muster fängt deshalb hinten
            // an.
            new()
            {
                Key = "deckungskapital", Label = "Garantiertes Deckungskapital",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"(?:Deckungskapital|Garantiekapital)\s+(?:von\s+)?([\d.]+,\d{2})",
            },
            new()
            {
                Key = "anteilsguthaben", Label = "Anteilsguthaben",
                Kind = DocumentValueKind.Money,
                Locator = DocumentLocator.Pattern,
                Pattern = @"(?:teilsguthaben von|Gesamtwert aller Anteile:)\s+([\d.]+,\d{2})",
            },

            // Kopfdaten.
            new()
            {
                Key = "stichtag", Label = "Stichtag", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,

                // Die Betreffzeile, nicht der Fließtext: „Ihr Statusreport zum 31.12.2020“. Sie
                // steht in jedem Jahrgang und trägt beide Schreibweisen des Datums — der ältere
                // schreibt den Monat aus.
                Pattern = @"Statusreport zum\s+(\d{1,2}[.,]\s*(?:\d{1,2}[.,]|[A-Za-zÄÖÜäöüß]+\s+)\d{4})",
            },
            new()
            {
                Key = "dokumentdatum", Label = "Dokumentdatum", Kind = DocumentValueKind.Date,
                Locator = DocumentLocator.Pattern,
                Pattern = @"^(?:[A-Za-zÄÖÜäöüß.\- ]+,\s+)?(\d{1,2}\.\d{1,2}\.\d{4})$",
            },

            // Über das Muster und nicht über die Beschriftung: der Scan von 2023 schreibt
            // „Verstehendngsnummer“. Die Nummer selbst ist unversehrt, und ihre Gestalt ist
            // eindeutig — das R vorweg trägt jeder Riester-Vertrag dieses Hauses.
            new()
            {
                Key = "vertragsnummer", Label = "Versicherungsnummer",
                Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.Pattern,
                Pattern = @"\b(R-[0-9A-Z]{6,12}-[0-9]{2})\b",
            },
            new()
            {
                Key = "absender", Label = "Absender", Kind = DocumentValueKind.Text,
                Locator = DocumentLocator.Pattern,
                Pattern = @"^([A-ZÄÖÜ][\w.\-äöüß]*(?: [\w.\-äöüß]+)*? (?:Lebensversicherung|Leben) AG)\b",
            },
        ],

        // Zwei Proben, die das Papier selbst vorrechnet.
        Checks =
        [
            new()
            {
                Result = "kapitalEnde",
                Parts = ["kapitalAnfang", "eigenbeitraege", "zulagen", "wertentwicklung"],
                Minus =
                [
                    "abschlusskosten", "verwaltungskosten", "fondskosten",
                    "entnahme", "entnahmekosten",
                ],
                Unsigned = "wertentwicklung",
                Kind = DocumentCheckKind.Sum,
                Note = "Jahresbeginn + Beiträge + Zulagen + Ergebnis − Kosten − Entnahmen",
                Why = "Der Bericht rechnet das Jahr vor. Geht die Zeile nicht auf, sitzt ein "
                      + "Betrag in der falschen Zeile — oder es fehlt eine Bewegung, die das "
                      + "Papier nicht als solche ausweist.",
            },
            new()
            {
                // Dieselbe Probe für den Jahrgang, dessen Beitrag eine Zeile tiefer steht. Es
                // erscheint immer nur eine der beiden: das jeweils andere Feld bleibt leer.
                Result = "kapitalEnde",
                Parts = ["kapitalAnfang", "eigenbeitraegeZeileTiefer", "zulagen", "wertentwicklung"],
                Minus =
                [
                    "abschlusskosten", "verwaltungskosten", "fondskosten",
                    "entnahme", "entnahmekosten",
                ],
                Unsigned = "wertentwicklung",
                Kind = DocumentCheckKind.Sum,
                Note = "Jahresbeginn + Beiträge + Zulagen + Ergebnis − Kosten − Entnahmen",
                Why = "Der Bericht rechnet das Jahr vor. Geht die Zeile nicht auf, sitzt ein "
                      + "Betrag in der falschen Zeile — oder es fehlt eine Bewegung, die das "
                      + "Papier nicht als solche ausweist.",
            },
            new()
            {
                Result = "kapitalEnde",
                Parts = ["deckungskapital", "anteilsguthaben"],
                Kind = DocumentCheckKind.Sum,
                Note = "Deckungskapital + Anteilsguthaben",
                Why = "Das Endkapital besteht aus dem garantierten Deckungskapital und dem "
                      + "Anteilsguthaben. Die zweite Probe hängt an anderen Zeilen als die erste "
                      + "— stimmen beide, steht der Wert doppelt geprüft da.",
            },
            new()
            {
                Result = "gebildetesKapital",
                Parts = ["kapitalEnde", "bewertungsreserven", "schlussueberschuesse"],
                Kind = DocumentCheckKind.Sum,
                Note = "Kapital zum Jahresende + Bewertungsreserven + Schlussüberschüsse",
                Why = "Sie zeigt, dass der erreichte Wert und die nicht garantierten Posten "
                      + "richtig getrennt sind. Was hier oben steht, geht ins Vermögen; was "
                      + "dazukommt, nicht.",
            },
        ],

        Steps =
        [
            "Text gelesen ({seiten} Seiten)",
            "Absender: {absender}",
            "Typ: Statusreport Altersvorsorge",
            "{ziel}",
            "{werte} Werte gelesen",
        ],
    };

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
    [
        // Die Reihenfolge ist die Rangfolge. Der Riester-Bericht nennt in einem Nebensatz auch
        // ein „Anteilsguthaben“ und trüge damit das Kennzeichen der fondsgebundenen
        // Lebensversicherung; er steht deshalb vor ihr. Umgekehrt fehlt jener die Zeile „Kapital
        // zum Jahresende“, sodass er ihm nichts wegnehmen kann.
        RiesterStatusreport,
        Statusreport,
        FundStatusreport,
        QuarterlyStatement,
    ];

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

        return All.FirstOrDefault(art => art.Markers.All(m => m.Matches(text)));
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

    /// <summary>
    /// Nicht gelesen, sondern von einem Menschen eingetragen.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zu <see cref="Derived"/> ist nicht kosmetisch: ein gerechneter Wert lässt
    /// sich herleiten, ein eingetragener steht für sich. Er darf in einer Rechenprobe mitzählen —
    /// aber die Probe muss dann sagen, dass sie ihn nicht vom Papier hat, sonst bestätigt sie
    /// scheinbar eine Lesung, die es nie gab.
    /// </remarks>
    public bool ByHand { get; init; }

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
    /// <param name="rowValues">
    /// Von Hand berichtigte Zeilenwerte, nach Zeilennummer ab null. Für den Fall, dass die
    /// Textebene eine Zeile nicht hergibt: aus einem beschädigten Scan wird sonst nie eine
    /// aufgehende Probe, und der ausgewiesene Wert bliebe für immer zweifelhaft. Gerechnet wird
    /// mit den berichtigten Zeilen — und die Probe sagt, wie viele davon von Hand kamen.
    /// </param>
    /// <param name="values">
    /// Von Hand eingetragene Einzelwerte, nach Feldschlüssel. Sie ersetzen den gelesenen Wert —
    /// und die Rechenproben rechnen mit ihnen. Sonst stünde unter berichtigten Zahlen eine Probe
    /// über die alten, und niemand wüsste, welche der beiden gilt.
    /// </param>
    public ExtractionResult Read(
        DocumentKind kind,
        PdfContent content,
        IReadOnlyDictionary<int, string>? rowValues = null,
        IReadOnlyDictionary<string, string>? values = null)
    {
        var basis = content.TextIsInvisible ? Behind : Sure;
        var abschnitte = Sections(kind, content);

        var werte = new Dictionary<string, ReadValue>();

        foreach (var regel in kind.Fields)
        {
            // Was ein Mensch eingetragen hat, gilt vor dem, was auf dem Papier steht. Gelesen
            // wird es mit derselben Regel: sonst hieße „1.845,60“ je nach Weg etwas anderes.
            // Ohne ScanRepair — was jemand tippt, ist nicht beschädigt.
            if (values?.TryGetValue(regel.Key, out var eingetragen) == true
                && eingetragen is { Length: > 0 })
            {
                werte[regel.Key] = (Read(regel with { ScanRepair = false }, eingetragen)
                                    ?? new ReadValue { Rule = regel, Raw = eingetragen.Trim() })
                    with
                    {
                        Confidence = basis,
                        ByHand = true,

                        // Eine Eingabe, aus der keine Zahl wird, verschwindet nicht: sie steht
                        // da und sagt, was ihr fehlt. Verschluckt wäre sie das Schlimmste — der
                        // Nutzer hätte etwas eingetragen und sähe wieder den alten Wert.
                        Warning = Read(regel with { ScanRepair = false }, eingetragen) is null
                                  && regel.Kind != DocumentValueKind.Text
                            ? "keine Zahl — bitte so eintragen, wie es auf dem Beleg steht"
                            : null,
                    };

                continue;
            }

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
            ? Repeat(gruppe, content, basis, proben, rowValues)
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
        DocumentRepeatRule gruppe, PdfContent content, double basis, List<ProofResult> proben,
        IReadOnlyDictionary<int, string>? rowValues = null)
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

            // Von Hand eingetragen: der Wert ersetzt den gelesenen — oder tritt an seine Stelle,
            // wo die Textebene gar keinen hergab. Die Zeilenprobe darüber bleibt unberührt: sie
            // hat geprüft, was auf dem Papier stand, und das ändert eine Eingabe nicht.
            if (rowValues?.TryGetValue(i, out var eingetragen) == true
                && Read(ValueRule(gruppe), eingetragen) is { } vonHand)
            {
                werte[gruppe.ValueField] = vonHand with { Confidence = basis, ByHand = true };
            }

            zeilen.Add(new ReadRow
            {
                Values = [.. gruppe.Fields.Where(f => werte.ContainsKey(f.Key)).Select(f => werte[f.Key])],
            });
        }

        return zeilen;
    }

    /// <summary>
    /// Die Feldregel, nach der der Wert einer Zeile gelesen wird.
    /// </summary>
    /// <remarks>
    /// Eine von Hand eingetragene Zahl geht durch dieselbe Regel wie eine gelesene. Sonst hinge
    /// an der Eingabe eine zweite Vorstellung davon, was ein Betrag ist — und „1.845,60“ hieße
    /// je nach Weg etwas anderes. Ohne <see cref="DocumentFieldRule.ScanRepair"/>: was ein Mensch
    /// tippt, ist nicht beschädigt.
    /// </remarks>
    private static DocumentFieldRule ValueRule(DocumentRepeatRule gruppe)
        => gruppe.Fields.First(f => f.Key == gruppe.ValueField) with { ScanRepair = false };

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

        // Zeilen ohne lesbaren Betrag. Vorher fiel die Probe dann ganz aus — und über einem
        // beschädigten Scan stand gar nichts, wo eine Auskunft am nötigsten ist. Jetzt zählt
        // sie die Zeile mit, sagt, dass ihr Betrag fehlt, und macht damit den Weg auf: eintragen
        // und noch einmal rechnen.
        var stumm = teile.Count(t => t is null);

        var summe = decimal.Round(teile.Where(t => t is not null).Sum(t => t!.Value), 2);
        var stimmt = stumm == 0 && Math.Abs(summe - ausgewiesen) <= gruppe.TotalTolerance;

        // Wie viele Zeilen ein Mensch beigesteuert hat. Ohne diesen Zusatz stünde unter einem
        // beschädigten Scan eine Probe, die aussieht, als hätte das Papier sie getragen.
        var vonHand = zeilen.Count(z => z[gruppe.ValueField]?.ByHand == true);

        var zusatz = vonHand == 0
            ? string.Empty
            : $" ({(vonHand == 1 ? "eine Zeile" : $"{vonHand} Zeilen")} von Hand eingetragen)";

        var fehlt = stumm == 0
            ? string.Empty
            : $" ({(stumm == 1 ? "eine Zeile trägt" : $"{stumm} Zeilen tragen")} keinen lesbaren "
              + "Betrag — bitte eintragen)";

        if (!stimmt)
        {
            werte[schluessel] = gesamt with
            {
                Confidence = Doubtful,
                Warning = stumm > 0
                    ? $"{(stumm == 1 ? "Eine Zeile trägt" : $"{stumm} Zeilen tragen")} keinen "
                      + "lesbaren Betrag — bitte eintragen"
                    : $"Die {zeilen.Count} {(zeilen.Count == 1 ? "Zeile ergibt" : "Zeilen ergeben")} "
                      + $"{Format(gesamt.Rule, summe)} — bitte prüfen",
            };
        }

        proben.Add(new ProofResult
        {
            Line = $"{zeilen.Count} {(zeilen.Count == 1 ? "Zeile" : "Zeilen")} = {Format(gesamt.Rule, summe)}"
                   + zusatz
                   + fehlt
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
        for (var z = 0; z < zeilen.Count; z++)
        {
            var zeile = zeilen[z];

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

                    // Nichts dahinter, und die Beschriftung steht allein in ihrer Zeile: dann
                    // liegt der Wert daneben. Im Scan von 2017 kippt die Wertspalte gegen die
                    // Beschriftungen — „erreichter Wert der“ / „18.120,65 Euro“ /
                    // „Gesamtleistung“, der Betrag also eine Zeile <em>über</em> seinem Namen.
                    //
                    // Erst die Zeile danach, dann die davor, und nur solange keine von beiden
                    // eine Zahl hergibt. Weiter reicht die Suche nicht: eine Beschriftung, deren
                    // Wert zwei Zeilen entfernt steht, ist keine Zuordnung mehr, sondern Raten.
                    if (zeile.Cells.Count == 1)
                    {
                        foreach (var versatz in (int[])[1, -1])
                        {
                            var nachbar = z + versatz;

                            if (nachbar < 0 || nachbar >= zeilen.Count)
                            {
                                continue;
                            }

                            if (Read(regel, zeilen[nachbar].Text) is { } daneben)
                            {
                                return daneben with
                                {
                                    Page = zeilen[nachbar].Page,
                                    Confidence = basis,
                                };
                            }
                        }
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

    /// <summary>
    /// Der Wert steht in der Zeile nach der Beschriftung.
    /// </summary>
    /// <remarks>
    /// Die Beschriftung muss eine <em>Zelle</em> beginnen und nicht die Zeile — dieselbe Regel
    /// wie bei <see cref="ByLabel"/>, und aus demselben Grund: die älteren Berichte setzen links
    /// eine Druckmarke davor. „06 · Gezahlte Eigenbeiträge im abgelaufenen Jahr“ mit dem Betrag
    /// in der Zeile darunter wurde deshalb nicht gefunden.
    /// </remarks>
    private ReadValue? ByNextLine(DocumentFieldRule regel, IReadOnlyList<PdfLine> zeilen, double basis)
    {
        for (var i = 0; i < zeilen.Count - 1; i++)
        {
            if (!regel.Labels.Any(
                    l => zeilen[i].Cells.Any(c => c.StartsWith(l, StringComparison.OrdinalIgnoreCase))))
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

        // Fehlende Abzüge zählen als null: bis 2017 wies der Bericht keine Fondskosten aus,
        // und eine Probe, die daran ausfällt, prüft die Jahrgänge nie, um die es geht.
        var abzug = probe.Minus.Sum(m => werte.TryGetValue(m, out var w) ? w.Number ?? 0m : 0m);

        var soll = probe.Kind == DocumentCheckKind.Sum
            ? teile.Sum(t => t!.Value) - abzug
            : teile.Aggregate(1m, (a, t) => a * t!.Value);

        // Ein Teil ohne lesbares Vorzeichen: geht die Rechnung nur mit dem anderen auf, war es
        // das andere. Der gelesene Wert bekommt es mitgeteilt, damit ein Verlust als Verlust
        // dasteht — im Prüfschritt und in der Probe.
        if (probe.Unsigned is { } offen
            && werte.TryGetValue(offen, out var ohneZeichen)
            && ohneZeichen.Number is { } betrag
            && werte.TryGetValue(probe.Result, out var ziel)
            && ziel.Number is { } steht0
            && Math.Abs(steht0 - soll) > probe.Tolerance
            && Math.Abs(steht0 - (soll - 2 * betrag)) <= probe.Tolerance)
        {
            werte[offen] = ohneZeichen with { Number = -betrag };
            soll -= 2 * betrag;
        }

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

        // Die Abzüge hinten, mit Minus davor. „a + b − c = d“ liest sich wie die Zeile auf dem
        // Papier; eine Probe, die ihre Abzüge verschweigt, wäre eine Rechnung ohne Rechenweg.
        var abzuege = probe.Minus
            .Where(werte.ContainsKey)
            .Select(m =>
            {
                var teil = felder.FirstOrDefault(f => f.Key == m);

                return " \u2212 " + (teil?.Kind == DocumentValueKind.Money && werte[m].Number is { } zahl
                    ? Format(teil, zahl)
                    : werte[m].Raw);
            });

        return string.Join(zeichen, teile) + string.Concat(abzuege) + " = " + Format(regel, ergebnis);
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

        // Ein beschädigter Scan, aber nur wo die Regel es zulässt — und dort steht eine
        // Rechenprobe dahinter, die die Vermutung bestätigt oder verwirft.
        if (regel.ScanRepair)
        {
            zahl = RepairScan(zahl);
        }

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

    /// <summary>Was in einer Zahl als Trennzeichen durchgehen kann.</summary>
    /// <remarks>
    /// <para>Das Dach kommt aus der Texterkennung: sie liest ein Komma mitunter als <c>^</c>. Es
    /// steht hier, weil es an dieser Stelle nichts anderes sein <em>kann</em> — mitten in einer
    /// Zahl gibt es kein Dach.</para>
    /// <para>Das Leerzeichen steht <b>nicht</b> hier, obwohl der Scan von 2023 auch <c>1 845 60</c>
    /// schreibt. Am Rand desselben Papiers stehen verirrte Zahlen aus dem Briefkopf: aus
    /// „<c>152,4300 300 870,45</c>“ würde mit Leerzeichen der Betrag 300.870,45 statt 870,45, und
    /// der Jahrgang 2020 läse sich um zweihunderttausend Euro zu hoch. Eine Regel, die einen
    /// kaputten Beleg rettet und einen heilen zerstört, ist keine.</para>
    /// </remarks>
    private static readonly char[] Trennzeichen = ['.', ',', '^'];

    /// <summary>
    /// Eine Zahl aus einem beschädigten Scan: das letzte Trennzeichen trennt die Nachkommastellen.
    /// </summary>
    /// <remarks>
    /// <para>Aus <c>4.220^35</c> wird <c>5135,57</c>, aus <c>3,145,60</c> ein <c>2776,40</c>,
    /// aus <c>12,480.75</c> ein <c>14791,94</c> und aus <c>29.1198</c> die Stückzahl
    /// <c>32,6050</c>. Die Regel ist derselbe Satz für alle vier Fälle: <em>hinten steht der
    /// Rest, davor die ganze Zahl</em> — welches Zeichen die Texterkennung dazwischengesetzt
    /// hat, ist gleichgültig.</para>
    /// <para>Was das Muster durchgelassen hat, hat die Gestalt eines Betrags. Ob die gelesene
    /// Zahl auch die richtige ist, entscheidet nicht diese Stelle, sondern die Summenprobe gegen
    /// den ausgewiesenen Gesamtwert.</para>
    /// </remarks>
    private static string RepairScan(string roh)
    {
        // Nur Ziffern und Trennzeichen. Ohne diesen Riegel wird aus jedem Text mit einer Zahl
        // darin eine Zahl: „Überschussbeteiligung 1.180,40“ ergäbe 1.180,40, und im Scan von
        // 2017 stand dieser Betrag danach als Rückkaufswert da — die Beschriftung eine Zeile
        // weiter, der Wert im falschen Feld. In der Fondstabelle fiel das nie auf, weil dort ein
        // Muster den Betrag schon herausgeschnitten hatte; über eine Beschriftung kommt die
        // ganze Zelle herein.
        if (roh.Any(c => !char.IsAsciiDigit(c) && !Trennzeichen.Contains(c)))
        {
            return roh;
        }

        var trenner = roh.LastIndexOfAny(Trennzeichen);
        if (trenner < 0)
        {
            return roh;
        }

        var ganz = new string([.. roh[..trenner].Where(char.IsAsciiDigit)]);
        var rest = new string([.. roh[(trenner + 1)..].Where(char.IsAsciiDigit)]);

        return ganz.Length == 0 || rest.Length == 0 ? roh : ganz + "," + rest;
    }

    /// <summary>
    /// Ob die Zeichenfolge im Deutschen überhaupt eine Zahl ist.
    /// </summary>
    /// <remarks>
    /// <para>Ein Punkt mit einer oder zwei Stellen dahinter ist keine: als Tausenderpunkt
    /// bräuchte er drei Stellen, als Dezimaltrenner ein Komma. Die deutsche Kultur liest ihn
    /// trotzden — sie prüft die Gruppengröße nicht — und macht aus dem eingescannten
    /// „39 415.80“ den Betrag <b>3.941.580</b>, also das Hundertfache.</para>
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
