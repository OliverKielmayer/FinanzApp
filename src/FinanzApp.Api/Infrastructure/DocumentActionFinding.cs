using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Das lokale Sprachmodell, das Schreiben durchsieht.
/// </summary>
/// <remarks>
/// <para>Standardmäßig aus, wie die Texterkennung. Ohne Modell läuft die Anwendung weiter, nur
/// ohne die zweite Schicht — gefunden wird dann, was die Leseregeln finden.</para>
/// <para><b>Nur lokal.</b> Der Inhaber hat Cloud-Texterkennung verworfen; ein Sprachmodell in
/// der Cloud wäre dieselbe Entscheidung, nur größer: Beim Bild ginge ein Blatt hinaus, hier der
/// ganze gelesene Text — Name, Anschrift, Kontonummer, Gehalt. Die Adresse zeigt deshalb auf
/// den eigenen Rechner, und wer sie woandershin stellt, trifft diese Entscheidung bewusst.</para>
/// <para><b>Die Größe des Modells entscheidet.</b> Am 06.11.2026 gegen hundertvierzig echte,
/// eingescannte Schreiben gemessen: Ein Modell mit drei Milliarden Gewichten (qwen2.5:3b)
/// richtet sich nach der <em>Form</em> der Frage statt nach dem Text — dieselben Belege,
/// vier Fragefassungen, Ergebnisse zwischen „alles verlangt etwas“ und „nichts verlangt
/// etwas“. Wer diese Schicht einschaltet, sollte ein größeres Modell nehmen und das Ergebnis
/// an den eigenen Belegen ansehen, bevor er ihm glaubt.</para>
/// </remarks>
public sealed class LanguageModelOptions
{
    public const string SectionName = "Sprachmodell";

    /// <summary>Ob Schreiben überhaupt durch das Modell gehen.</summary>
    public bool Enabled { get; set; }

    /// <summary>Adresse des Modellservers. Ollama hört ab Werk hier.</summary>
    public string Url { get; set; } = "http://localhost:11434";

    /// <summary>Welches Modell.</summary>
    public string Model { get; set; } = "qwen2.5:7b";

    /// <summary>
    /// Wie lange auf eine Antwort gewartet wird.
    /// </summary>
    /// <remarks>
    /// Großzügig, weil ein Modell beim ersten Aufruf erst in den Speicher geladen wird — das
    /// dauert Sekunden, danach ist es schnell. Läuft die Zeit ab, bleibt der Beleg ohne
    /// Forderung, und das ist derselbe Zustand wie vorher.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Wie viele Zeichen des Belegs überhaupt angesehen werden.
    /// </summary>
    /// <remarks>
    /// Großzügig, denn gelesen wird daraus nur die Vorauswahl — eine Forderung kann auch auf
    /// Seite drei stehen, etwa in der Schlusszeile eines Bescheids.
    /// </remarks>
    public int MaxCharacters { get; set; } = 20000;
}

/// <summary>
/// Findet den Satz, in dem ein Schreiben etwas verlangt.
/// </summary>
/// <remarks>
/// <para>Die Schnittstelle gibt eine <em>Zeile</em> zurück und keinen Wert. Das ist der ganze
/// Kniff: Ein Modell erfindet Beträge und Daten, und zwar überzeugend. Nach der Fundstelle
/// gefragt, kann es ebenso erfinden — aber eine erfundene Fundstelle steht nicht im Text, und
/// das ist nachprüfbar. <see cref="ActionEvidence"/> prüft es, bevor irgendetwas gespeichert
/// wird.</para>
/// <para>Deshalb steht die Prüfung auch nicht in der Implementierung. Sie gehört vor sie: Eine
/// zweite Anbindung, die sie vergäße, dürfte sonst unbemerkt erfundene Sätze ablegen.</para>
/// </remarks>
public interface IDocumentActionFinder
{
    /// <summary>
    /// Ob überhaupt etwas dahintersteht.
    /// </summary>
    /// <remarks>
    /// Ohne diese Frage wäre „nichts gefunden“ zweideutig: Es hieße <em>das Schreiben verlangt
    /// nichts</em> und <em>es hat nie jemand nachgesehen</em>. Der Bestand bekäme beim ersten
    /// Durchlauf ohne Modell überall den Vermerk „geprüft, nichts dabei“, und ein später
    /// eingerichtetes Modell sähe keinen Beleg mehr.
    /// </remarks>
    bool Available { get; }

    /// <summary>
    /// Was dieses Schreiben verlangt — als Zeile aus seinem eigenen Text, oder <c>null</c>.
    /// </summary>
    Task<string?> FindActionLineAsync(string text, CancellationToken ct = default);
}

/// <summary>Findet nichts und sagt das auch. Der eingebaute Stand ohne Modell.</summary>
/// <remarks>
/// Bewusst kein Platzhalter, der etwas errät: Ein erfundener Vorgang im Vorgänge-Tab wäre
/// schlimmer als keiner — er verlangt Aufmerksamkeit für etwas, das es nicht gibt.
/// </remarks>
public sealed class NoDocumentActionFinder : IDocumentActionFinder
{
    public bool Available => false;

    public Task<string?> FindActionLineAsync(string text, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// Die Vorauswahl, die Frage und die Prüfung — alles, was nicht am Modell hängt.
/// </summary>
/// <remarks>
/// <para>Steht hier und nicht in der Anbindung, damit es prüfbar ist, ohne dass ein Modell
/// läuft. Was ein bestimmtes Modell antwortet, kann kein Test festhalten; dass aus der Antwort
/// nichts entsteht, was nicht im Papier steht, schon.</para>
/// <para><b>Die Form ist aus Messungen entstanden, nicht aus Überlegung.</b> Am 06.11.2026
/// gegen echte, eingescannte Schreiben gemessen. „Schreib den Satz ab“ liefert erfundene
/// Sätze oder gar keine; „ja oder nein je Zeile“ liefert immer nein; alle hundert Zeilen eines
/// OCR-Textes vorzulegen führt dazu, dass ein kleines Modell ins Blaue zählt. Was am besten
/// trug: eine kurze Liste vorausgewählter Zeilen, durchnummeriert, mit vier gelösten
/// Beispielen davor — und als Antwort nur eine Zahl.</para>
/// </remarks>
public static class ActionEvidence
{
    /// <summary>Kürzer belegt nichts — „Zahlung“ allein ist keine Forderung, sondern ein Wort.</summary>
    private const int MinimumLength = 15;

    /// <summary>
    /// Länger ist kein Satz mehr.
    /// </summary>
    /// <remarks>
    /// Ohne obere Grenze könnte ein Modell den ganzen Beleg zurückgeben: Der käme durch jede
    /// Prüfung, denn er steht natürlich im Beleg, und bewiese nichts.
    /// </remarks>
    private const int MaximumLength = 250;

    /// <summary>Ein Termin mehr als zehn Jahre entfernt ist keine Frist.</summary>
    private const int PlausibleYears = 10;

    /// <summary>
    /// Wie viele Zeilen dem Modell höchstens vorgelegt werden.
    /// </summary>
    /// <remarks>
    /// Je länger die Liste, desto eher zählt ein kleines Modell daneben. Zwanzig Zeilen decken
    /// auch einen mehrseitigen Bescheid ab — die Vorauswahl wirft das meiste schon weg.
    /// </remarks>
    private const int MaximumCandidates = 20;

    private static readonly Regex Leerraum = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex Datum =
        new(@"\b\d{1,2}\s*\.\s*\d{1,2}\s*\.\s*\d{2,4}\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Wörter, an denen eine Zeile überhaupt eine Forderung sein <em>kann</em>.
    /// </summary>
    /// <remarks>
    /// <para>Bewusst breit und bewusst nicht die Liste aus <see cref="BillTextReader"/>. Jene
    /// trifft eine Entscheidung und muss deshalb eng sein; diese wirft nur vor, und was sie zu
    /// viel durchlässt, sortiert das Modell aus. Zu eng wäre sie dagegen nicht zu heilen: Was
    /// hier herausfällt, sieht das Modell nie.</para>
    /// <para>Eine Zeile mit Datum kommt ohnehin mit. Eine Frist ohne Datum gibt es nicht, und
    /// die Formulierungen, die ein Datum begleiten, sind zu viele zum Aufzählen.</para>
    /// </remarks>
    private static readonly string[] Forderungswoerter =
    [
        "bitte", "wir bitten", "senden sie", "schicken sie", "überweisen", "ueberweisen",
        "zahlen sie", "zahlbar", "fällig", "faellig", "stellen sie", "teilen sie",
        "reichen sie", "unterschreiben", "widerspruch", "kündigen", "kuendigen", "frist",
        "bis zum", "innerhalb von", "veranlassen", "lastschrift", "zahlungsziel",
        "antworten sie", "melden sie", "beachten sie",
    ];

    /// <summary>Der geprüfte Satz und, falls er einen nennt, sein Termin.</summary>
    public readonly record struct Finding(string Line, DateOnly? Due);

    /// <summary>
    /// Eine Frist, die kein Datum nennt, sondern eine Spanne.
    /// </summary>
    /// <remarks>
    /// <para>„Bitte überweisen Sie den Rechnungsbetrag innerhalb von 14 Tagen“ setzt eine
    /// Frist, nennt aber keinen Tag. Bisher blieb der Vorgang dadurch ohne Termin: Er stand in
    /// der Liste, wurde nie dringend und sortierte sich ans Ende — ausgerechnet bei Rechnungen,
    /// die am ehesten eine Zahlung verlangen.</para>
    /// <para><b>Drei Formen, an den echten Schreiben gemessen</b> (08.11.2026, über den
    /// gelesenen Text von hundertneun Belegen): „innerhalb von 14 Tagen“, „innerhalb 8 Tagen“
    /// ohne das <em>von</em>, und „binnen“ als drittes Wort, das dasselbe meint. Mehr kam nicht
    /// vor. „Zahlbar innerhalb 8 Tagen nach Rechnungseingang“ fällt unter dieselbe Form; der
    /// Nachsatz verschiebt den Anfang um die Postlaufzeit, und die kennt niemand.</para>
    /// <para><b>„Sofort“ bleibt ohne Termin.</b> Acht Belege sagen es, und es ist keine Frist,
    /// sondern eine Haltung. Daraus ein Datum zu machen hieße, eine Dringlichkeit zu erfinden,
    /// die das Papier nicht hergibt — dieselbe Linie wie bei einem Satz ganz ohne Termin.</para>
    /// </remarks>
    private static readonly Regex Spanne =
        new(@"\b(?:innerhalb\s+von|innerhalb|binnen)\s+(\d{1,3})\s*Tag(?:e|en)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Längere Zahlungsziele gibt es, aber keine über ein Jahr.</summary>
    private const int PlausibleDays = 365;

    /// <summary>
    /// Die Zeilen, die überhaupt eine Forderung sein können.
    /// </summary>
    /// <remarks>
    /// Ein eingescanntes Schreiben hat hundert Zeilen und mehr, die meisten davon Anschrift,
    /// Tabellenreste und Fußzeile. Sie alle vorzulegen hieß, das Modell nach einer Nadel in
    /// einem Haufen zu fragen, den es selbst nicht überblickt.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(string? text, int maxCharacters = 20000)
    {
        if (text is not { Length: > 0 })
        {
            return [];
        }

        var gefunden = new List<string>();

        foreach (var roh in text[..Math.Min(text.Length, maxCharacters)].Split('\n'))
        {
            var zeile = Leerraum.Replace(roh, " ").Trim();
            if (zeile.Length is < MinimumLength or > MaximumLength)
            {
                continue;
            }

            var klein = zeile.ToLowerInvariant();
            if (Datum.IsMatch(zeile) || Forderungswoerter.Any(w => klein.Contains(w, StringComparison.Ordinal)))
            {
                gefunden.Add(zeile);
            }

            if (gefunden.Count == MaximumCandidates)
            {
                break;
            }
        }

        return gefunden;
    }

    /// <summary>
    /// Die Frage an das Modell: eine nummerierte Liste, vier gelöste Beispiele, eine Zahl zurück.
    /// </summary>
    /// <remarks>
    /// <para>Die Beispiele sind nicht Zierde. Ohne sie antwortet ein kleines Modell auf dieselbe
    /// Liste entweder immer mit einer Zahl oder immer mit null — es folgt der Form der Frage
    /// statt dem Text. Zwei Beispiele mit Antwort und zwei ohne zeigen ihm, dass beides
    /// vorkommt.</para>
    /// <para>Deutsch, weil die Belege deutsch sind. Eine englische Frage über deutschem Text
    /// bringt kleine Modelle dazu, die Antwort zu übersetzen.</para>
    /// </remarks>
    public static string PromptFor(IReadOnlyList<string> candidates)
    {
        var liste = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
        {
            liste.Append(i + 1).Append(": ").AppendLine(candidates[i]);
        }

        return $"""
                Aufgabe: Aus einem Brief an einen Privathaushalt sind Zeilen herausgesucht. Nenne
                die Nummer der Zeile, die vom Empfänger eine Handlung verlangt. Verlangt keine
                etwas, antworte 0.

                Beispiel 1:
                1: Ihr Guthaben zum 31.12.2025 beträgt 12.345,67 EUR.
                2: Die Überschussbeteiligung betrug 2,1 Prozent.
                3: Diese Mitteilung dient Ihrer Information.
                Antwort: 0

                Beispiel 2:
                1: Rechnungsdatum 04.04.2026
                2: Zahlbar innerhalb von 14 Tagen ohne Abzug bis zum 18.04.2026.
                3: Vielen Dank für Ihren Auftrag.
                Antwort: 2

                Beispiel 3:
                1: Ihr Vertrag wurde zum 01.10.2026 auf den neuen Betreiber übertragen.
                2: Bitte stellen Sie Ihren Lastschrifteinzug auf das neue Konto um.
                3: Ihre Kundennummer bleibt unverändert.
                Antwort: 2

                Beispiel 4:
                1: Kontostand am 31.10.2026: 4.120,55 EUR
                2: Dauerauftrag Miete fällig am 01.11.2026
                3: Bitte beachten Sie unsere neuen Öffnungszeiten.
                Antwort: 0

                Jetzt die echte Aufgabe:
                {liste}Antwort:
                """;
    }

    /// <summary>
    /// Welche Zeile das Modell gemeint hat — oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Eine Zahl außerhalb der Liste ist kein Treffer und kein Fehler, sondern dasselbe wie
    /// null: Das Modell hat sich verzählt, und geraten wird hier nicht.
    /// </remarks>
    public static string? LineFrom(string? answer, IReadOnlyList<string> candidates)
    {
        if (answer is not { Length: > 0 })
        {
            return null;
        }

        var erste = answer.Trim().Split('\n')[0];
        var ziffern = new string([.. erste.Where(char.IsDigit)]);

        return int.TryParse(ziffern, NumberStyles.None, CultureInfo.InvariantCulture, out var nummer)
               && nummer >= 1
               && nummer <= candidates.Count
            ? candidates[nummer - 1]
            : null;
    }

    /// <summary>
    /// Gibt den geprüften Satz zurück — oder <c>null</c>, wenn er nicht im Beleg steht.
    /// </summary>
    /// <remarks>
    /// <para>Hier fällt Erfundenes heraus, strukturell und nicht nach Gefühl. Was das Modell
    /// nennt, muss im gespeicherten Text vorkommen; sonst wird es verworfen, ohne dass jemand
    /// entscheiden müsste, wie glaubwürdig es klingt.</para>
    /// <para>Bei der Anbindung über nummerierte Zeilen kann das gar nicht fehlschlagen — die
    /// Zeilen stammen aus dem Beleg. Die Prüfung bleibt trotzdem: Sie gilt für jede Anbindung,
    /// auch für eine, die später einmal frei antwortet.</para>
    /// <para><b>Verglichen wird mit zusammengezogenen Leerräumen.</b> Ein PDF trennt Wörter mit
    /// Zeilenumbrüchen und doppelten Leerzeichen, wo ein Mensch eines sieht. Die Eigenschaft,
    /// auf die es ankommt — <em>die Wörter stammen aus diesem Beleg</em> —, bleibt erhalten.</para>
    /// </remarks>
    /// <param name="paperDate">
    /// Das Datum des Belegs. Nur dafür da, eine Spanne wie „innerhalb von 14 Tagen“ in einen
    /// Tag umzurechnen — ohne es bleibt ein solcher Satz ohne Termin, denn eine Spanne ohne
    /// Anfang ist keine Frist.
    /// </param>
    public static Finding? Verify(
        string? text, string? claimed, DateOnly today, DateOnly? paperDate = null)
    {
        if (text is not { Length: > 0 } || claimed is not { Length: > 0 })
        {
            return null;
        }

        var satz = Leerraum.Replace(claimed, " ").Trim();
        if (satz.Length is < MinimumLength or > MaximumLength)
        {
            return null;
        }

        var beleg = Leerraum.Replace(text, " ");
        var stelle = beleg.IndexOf(satz, StringComparison.OrdinalIgnoreCase);
        if (stelle < 0)
        {
            return null;
        }

        var ausDemBeleg = beleg.Substring(stelle, satz.Length);

        // Erst das genannte Datum, dann die Spanne. Nie umgekehrt: Wo ein Tag dasteht, ist er
        // die Frist, auch wenn im selben Satz noch eine Zahlungsfrist in Tagen steht.
        var termin = BillTextReader.DateInLine(ausDemBeleg) ?? AusSpanne(ausDemBeleg, paperDate);

        if (termin is { } tag
            && (tag < today.AddYears(-PlausibleYears) || tag > today.AddYears(PlausibleYears)))
        {
            termin = null;
        }

        return new Finding(ausDemBeleg, termin);
    }

    /// <summary>
    /// Ob dieser Satz seinen Termin selbst nennt.
    /// </summary>
    /// <remarks>
    /// <para>Die Gegenfrage zu <see cref="AusSpanne"/>, und der Grund, warum am Beleg kein
    /// Kennzeichen dafür liegt: Ob eine gespeicherte Frist abgelesen oder gerechnet wurde,
    /// steht in der Zeile, die daneben liegt. Eine zweite Spalte könnte von ihr abweichen —
    /// diese Frage kann es nicht.</para>
    /// <para>Wer eine gerechnete Frist anzeigt, soll es dazusagen: Sie steht so nicht auf dem
    /// Papier, und wer sie nachschlägt, findet dort nur die Spanne.</para>
    /// </remarks>
    public static bool StatesDate(string? line)
        => line is { Length: > 0 } && BillTextReader.DateInLine(line) is not null;

    /// <summary>
    /// Rechnet eine Spanne wie „innerhalb von 14 Tagen“ in einen Tag um.
    /// </summary>
    /// <remarks>
    /// Ohne Belegdatum gibt es nichts zu rechnen, und geraten wird nicht: Die Spanne beginnt am
    /// Papier, und wenn dessen Datum fehlt, bleibt der Vorgang ohne Termin stehen — so wie
    /// jeder Satz, der gar keine Frist nennt.
    /// </remarks>
    private static DateOnly? AusSpanne(string line, DateOnly? paperDate)
    {
        if (paperDate is not { } ab || Spanne.Match(line) is not { Success: true } treffer)
        {
            return null;
        }

        if (!int.TryParse(
                treffer.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                out var tage)
            || tage is < 1 or > PlausibleDays)
        {
            return null;
        }

        return ab.AddDays(tage);
    }

    /// <summary>Für Protokollzeilen: der Satz, auf eine Zeile gekürzt.</summary>
    public static string ForLog(string? line)
    {
        if (line is not { Length: > 0 })
        {
            return "(nichts)";
        }

        var kurz = Leerraum.Replace(line, " ").Trim();
        return kurz.Length > 60 ? string.Concat(kurz.AsSpan(0, 60), "…") : kurz;
    }

    /// <summary>Hilfsformat für Protokolle.</summary>
    public static string Format(DateOnly? date)
        => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "ohne Termin";
}
