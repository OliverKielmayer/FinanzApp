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
            var rein = Normalise(treffer.Value);

            if (IsValid(rein) && !gefunden.Contains(rein))
            {
                gefunden.Add(rein);
            }
        }

        return gefunden;
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
