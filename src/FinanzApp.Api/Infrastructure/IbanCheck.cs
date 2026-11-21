using System.Globalization;
using System.Text.RegularExpressions;
using System.Numerics;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Die Prüfziffer einer IBAN.
/// </summary>
/// <remarks>
/// <para><b>Warum das hier steht und nicht bei der Bank.</b> Eine falsch abgetippte IBAN nimmt
/// die Bank an, wenn sie formal stimmt — und überweist an jemand anderen. Zurückholen lässt
/// sich das nur mit dessen Einverständnis. Die Prüfziffer fängt genau den häufigsten Fehler
/// ab: zwei vertauschte Ziffern.</para>
/// <para>Sie ist <b>keine Zusicherung, dass es das Konto gibt</b>. Sie sagt nur, dass die
/// Zeichenfolge in sich stimmt. Wer die IBAN seines Nachbarn abtippt, kommt damit durch.</para>
/// <para>Das Verfahren steht in ISO 13616: Die ersten vier Zeichen wandern ans Ende, Buchstaben
/// werden zu Zahlen (A=10 … Z=35), und der Rest der Division durch 97 muss 1 sein.</para>
/// </remarks>
public static class IbanCheck
{
    /// <summary>Die kürzeste und die längste IBAN, die es gibt.</summary>
    /// <remarks>
    /// Norwegen hat fünfzehn Zeichen, Malta vierunddreißig. Kürzer oder länger ist keine IBAN,
    /// und ohne diese Schranke liefe die Rechnung unten über eine Zeichenfolge, die keine ist.
    /// </remarks>
    private const int MinLength = 15;

    private const int MaxLength = 34;

    /// <summary>
    /// Entfernt Leerzeichen und macht Großbuchstaben daraus.
    /// </summary>
    /// <remarks>
    /// Auf einem Beleg steht die IBAN in Vierergruppen. Wer sie abtippt, tippt sie so ab — und
    /// eine Prüfung, die daran scheitert, prüft die Schreibweise statt der Nummer.
    /// </remarks>
    public static string Normalise(string? iban)
        => new((iban ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c))
            .Select(char.ToUpperInvariant)
            .ToArray());

    /// <summary>Ob die Prüfziffer aufgeht.</summary>
    public static bool IsValid(string? iban)
    {
        var rein = Normalise(iban);

        if (rein.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        // Zwei Buchstaben Land, zwei Ziffern Prüfziffer, dann die Kontokennung.
        if (!char.IsAsciiLetterUpper(rein[0]) || !char.IsAsciiLetterUpper(rein[1])
            || !char.IsAsciiDigit(rein[2]) || !char.IsAsciiDigit(rein[3]))
        {
            return false;
        }

        if (rein.Skip(4).Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            return false;
        }

        // Die ersten vier ans Ende, dann jeder Buchstabe zu seiner Zahl.
        var gedreht = string.Concat(rein.AsSpan(4), rein.AsSpan(0, 4));

        var ziffern = string.Concat(gedreht.Select(c => char.IsAsciiDigit(c)
            ? c.ToString()
            : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture)));

        // Die Zahl wird bis zu vierzig Stellen lang — das trägt kein long. BigInteger rechnet
        // sie in einem Zug; stückweise Modulo wäre dasselbe Ergebnis mit mehr Gelegenheit,
        // sich zu vertun.
        return BigInteger.Parse(ziffern, CultureInfo.InvariantCulture) % 97 == 1;
    }

    /// <summary>
    /// Sucht IBANs in einem Fliesstext — nur die, deren Prüfziffer aufgeht.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Prüfziffer ist hier nicht Beiwerk, sondern der Filter.</b> Auf einer
    /// Rechnung steht mehr, was wie eine IBAN aussieht, als IBANs: Die Gläubiger-Identifikation
    /// einer Lastschrift beginnt mit dem Ländercode und zwei Ziffern wie eine IBAN
    /// (<c>DE98ZZZ…</c>) und ist keine. Eine maskierte eigene IBAN (<c>XX1234…XXX789</c>) sieht
    /// ebenso aus. Am gemessenen Beleg fielen beide an der Prüfziffer durch — und nur an
    /// ihr.</para>
    /// <para><b>Die Reihenfolge bleibt die des Papiers.</b> Wer zwei Vorschläge sieht, soll sie
    /// in der Reihenfolge sehen, in der sie auf dem Blatt stehen; eine andere Sortierung wäre
    /// eine Behauptung darüber, welche die richtige ist.</para>
    /// </remarks>
    public static IReadOnlyList<string> FindAll(string? text)
    {
        if (text is not { Length: > 0 })
        {
            return [];
        }

        List<string> gefunden = [];

        foreach (Match treffer in Kandidat.Matches(text))
        {
            var rein = Beschnitten(Normalise(treffer.Value));

            if (IsValid(rein) && !gefunden.Contains(rein))
            {
                gefunden.Add(rein);
            }
        }

        return gefunden;
    }


    /// <summary>
    /// Wie lang eine IBAN in diesem Land ist.
    /// </summary>
    /// <remarks>
    /// <para><b>Sie steht fest</b> — ISO 13616 gibt je Land genau eine Länge vor. Deutschland
    /// hat zweiundzwanzig Zeichen, Österreich zwanzig, Frankreich siebenundzwanzig.</para>
    /// <para>Hier stehen die Länder des SEPA-Raums. Ein Land, das nicht dabei ist, bekommt
    /// keine Schranke: Dann gilt, was das Muster gefunden hat, und die Prüfziffer entscheidet
    /// allein. Das ist die sichere Seite — eine geratene Länge schnitte eine richtige IBAN
    /// mitten durch.</para>
    /// </remarks>
    private static readonly Dictionary<string, int> LaengeJeLand = new(StringComparer.Ordinal)
    {
        ["AD"] = 24, ["AT"] = 20, ["BE"] = 16, ["BG"] = 22, ["CH"] = 21, ["CY"] = 28,
        ["CZ"] = 24, ["DE"] = 22, ["DK"] = 18, ["EE"] = 20, ["ES"] = 24, ["FI"] = 18,
        ["FR"] = 27, ["GB"] = 22, ["GR"] = 27, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22,
        ["IS"] = 26, ["IT"] = 27, ["LI"] = 21, ["LT"] = 20, ["LU"] = 20, ["LV"] = 21,
        ["MC"] = 27, ["MT"] = 31, ["NL"] = 18, ["NO"] = 15, ["PL"] = 28, ["PT"] = 25,
        ["RO"] = 24, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27,
    };

    /// <summary>
    /// Kürzt einen Fund auf die Länge, die sein Land vorschreibt.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Grund steht auf jeder Handwerkerrechnung.</b> Dort führt der Briefbogen
    /// eine Tabelle: Bank, IBAN, BIC — nebeneinander in einer Zeile. Das Muster läuft über die
    /// IBAN hinaus in die BIC hinein, weil die auch nur Großbuchstaben mit Leerzeichen ist,
    /// und die Prüfziffer fällt durch. An einem gemessenen Beleg mit zwei Bankverbindungen
    /// wurde so <em>keine einzige</em> IBAN gefunden.</para>
    /// <para><b>Nur kürzen, nie verlängern und nie reparieren.</b> Derselbe Beleg trug eine
    /// zweite IBAN, in der die Texterkennung zwei Nullen zu großen O gemacht hatte. Sie fällt
    /// an der Prüfziffer durch und bleibt draußen — richtig so: Wer ein O zur Null erklärt,
    /// errechnet eine Kontonummer, die auf keinem Papier steht.</para>
    /// </remarks>
    private static string Beschnitten(string rein)
    {
        if (rein.Length < 2 || !LaengeJeLand.TryGetValue(rein[..2], out var laenge))
        {
            return rein;
        }

        return rein.Length <= laenge ? rein : rein[..laenge];
    }
    /// <summary>
    /// Was überhaupt als IBAN in Frage kommt.
    /// </summary>
    /// <remarks>
    /// Zwei Buchstaben, zwei Ziffern, dann Gruppen aus Buchstaben und Ziffern mit oder ohne
    /// Leerzeichen dazwischen — so steht sie auf Papier. Was hier hängen bleibt, ist ein
    /// Kandidat und kein Fund; darüber entscheidet <see cref="IsValid"/>.
    /// </remarks>
    private static readonly Regex Kandidat = new(
        @"\b[A-Z]{2}[0-9]{2}(?:[ ]?[A-Z0-9]){10,30}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Ob eine BIC formal stimmt — leer gilt als in Ordnung.
    /// </summary>
    /// <remarks>
    /// Im SEPA-Raum ist die BIC seit 2016 entbehrlich; die Bank findet das Institut über die
    /// IBAN. Eine leere BIC ist deshalb kein Mangel. Eine falsch abgetippte sehr wohl — sie
    /// hat acht oder elf Zeichen, nie neun.
    /// </remarks>
    public static bool BicIsValid(string? bic)
    {
        var rein = Normalise(bic);

        if (rein.Length == 0)
        {
            return true;
        }

        if (rein.Length is not (8 or 11))
        {
            return false;
        }

        return rein.All(char.IsAsciiLetterOrDigit)
               && rein.Take(6).All(char.IsAsciiLetterUpper);
    }
}
