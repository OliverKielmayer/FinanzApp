using System.Globalization;
using System.Text.RegularExpressions;

using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Liest die Eckdaten einer Rechnung aus ihrem Text.
/// </summary>
/// <remarks>
/// <para><b>An Beschriftungen und nicht an Positionen.</b> Wo auf dem Blatt der Betrag steht,
/// ist bei jedem Absender anders; dass daneben „Rechnungsbetrag“ steht, ist es nicht. Deshalb
/// sucht dieser Leser Wörter und nimmt den Wert, der dabeisteht — und nicht „die Zahl unten
/// rechts“.</para>
/// <para><b>Jeder Wert nennt seine Zeile.</b> Das ist der Unterschied zwischen Lesen und Raten:
/// Ein gelesener Betrag, der seine Quelle mitbringt, ist in einer Sekunde geprüft. Einer ohne
/// ist eine Behauptung, die niemand mehr nachsieht — und bei einer Erstattung ist das die
/// falsche Stelle dafür.</para>
/// <para><b>Lieber nichts als etwas Erfundenes.</b> Findet eine Regel ihren Wert nicht, bleibt
/// das Feld leer. Ein leeres Feld fragt; ein falsch gefülltes antwortet.</para>
/// <para>Die Erkennung verliest sich. „RECHNU G“ statt „RECHNUNG“ ist hier schon vorgekommen,
/// und deshalb hängt kein Wert an einem einzigen Wort: Für Betrag, Nummer und Datum gibt es
/// mehrere Beschriftungen, und was zweimal auf der Seite steht, gilt als bestätigt.</para>
/// </remarks>
public static class BillTextReader
{
    /// <summary>Ein Geldbetrag deutscher Schreibweise: 1.234,56 oder 99,00.</summary>
    private static readonly Regex Geld =
        new(@"(?<!\d)(\d{1,3}(?:[.  ]\d{3})+|\d+),(\d{2})(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// Ein Betrag, den die Erkennung mit Punkt statt Komma gelesen hat.
    /// </summary>
    /// <remarks>
    /// „EUR 12.34“ stand so auf einem Laborbeleg. Ein Punkt ist im Deutschen sonst der
    /// Tausenderpunkt, und „1.234“ als 1,23 zu lesen wäre schlimmer als gar nichts. Deshalb
    /// nur unmittelbar neben einem Währungszeichen: Dort kann es kein Tausenderpunkt sein,
    /// weil hinter ihm drei Ziffern stünden und nicht zwei.
    /// </remarks>
    private static readonly Regex GeldMitPunkt =
        new(@"(?:(?:EUR|€)\s*(\d+)\.(\d{2})(?!\d))|(?:(?<!\d)(\d+)\.(\d{2})\s*(?:EUR|€))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Datum =
        new(@"(?<!\d)(\d{1,2})\.\s?(\d{1,2})\.\s?(\d{4}|\d{2})(?!\d)", RegexOptions.Compiled);

    /// <summary>
    /// Ein Datum mit ausgeschriebenem Monat: „22. August 2026“.
    /// </summary>
    /// <remarks>
    /// Händlerrechnungen schreiben den Monat gern aus, Arztrechnungen fast nie — deshalb fehlte
    /// die Form bisher. Ein Beleg über einen Einbaukühlschrank trug sein Belegdatum so, und die
    /// Beschriftung griff, während das Datum daneben ungelesen blieb.
    /// </remarks>
    private static readonly Regex DatumMitMonatswort =
        new(@"(?<!\d)(\d{1,2})\.\s*([A-Za-zÄÖÜäöü]{3,9})\.?\s+(\d{4})(?!\d)",
            RegexOptions.Compiled);

    /// <summary>
    /// Monatsnamen über ihre ersten drei Buchstaben.
    /// </summary>
    /// <remarks>
    /// Drei Buchstaben decken den ausgeschriebenen Namen und die Abkürzung in einem ab. Die
    /// Schreibweisen ohne Umlaut und die englischen stehen mit dabei: Eine Erkennung, die „ä“
    /// nicht sauber liest, macht aus „März“ gern „Marz“.
    /// </remarks>
    private static readonly Dictionary<string, int> Monate = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jan"] = 1, ["feb"] = 2, ["mär"] = 3, ["mar"] = 3, ["apr"] = 4, ["mai"] = 5,
        ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8, ["sep"] = 9, ["okt"] = 10,
        ["oct"] = 10, ["nov"] = 11, ["dez"] = 12, ["dec"] = 12,
    };

    /// <summary>Eine Belegnummer: enthält mindestens eine Ziffer und ist kein Datum.</summary>
    private static readonly Regex Belegnummer =
        new(@"(?<![\w/-])(?=[\w/-]*\d)[A-Za-z0-9][A-Za-z0-9/-]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// „Rechnung Nr. 4711“, auch wenn die Erkennung das Wort zerlegt hat.
    /// </summary>
    /// <remarks>
    /// <para>Auf einem echten Beleg stand groß „RECHNUNG Nr.“, und die Erkennung lieferte
    /// „RECHNU G Nr.“ — ein verschlucktes N. Das trifft gerade das größte Wort der Seite, weil
    /// es oft gesperrt oder in einer Zierschrift steht.</para>
    /// <para>Deshalb ein Rückfall, der den Wortanfang nimmt und ein verirrtes Bruchstück
    /// davor duldet — aber erst, wenn keine ausdrückliche Beschriftung gegriffen hat. Als
    /// erste Regel wäre er zu grob.</para>
    /// </remarks>
    private static readonly Regex ZerleseneRechnungsnummer =
        new(@"\brech\w*(?:\s+\w{1,2})?\s*nr\.?\s*:?\s*([A-Za-z0-9/-]{3,})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Eine Zeile, die mit „Datum“ beginnt.
    /// </summary>
    /// <remarks>
    /// Der Rückfall für Belege ohne „Rechnungsdatum“. Am Zeilenanfang und nicht irgendwo:
    /// „Labordatum“, „Geb.Datum“ und „Aufn.-Dat.“ stehen auf denselben Blättern und meinen
    /// etwas anderes.
    /// </remarks>
    private static readonly Regex DatumZeilenanfang =
        new(@"^datum\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Wo eine Anschrift beginnt — danach gehört nichts mehr zum Namen.</summary>
    private static readonly Regex Anschrift =
        new(@"\b([A-Za-zÄÖÜäöüß.]*(?:stra(?:ß|ss)e|str\.?|weg|platz|allee|ring|gasse)\s*\d"
            + @"|\d{5}\s+[A-ZÄÖÜ])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Beschriftungen des Rechnungsbetrags, die wichtigste zuerst.
    /// </summary>
    /// <remarks>
    /// Die Reihenfolge entscheidet nur im Streitfall. „Rechnungsbetrag“ meint den Betrag der
    /// Rechnung; „Gesamt“ kann auch eine Zwischensumme einer Position sein.
    /// </remarks>
    private static readonly string[] BetragWoerter =
    [
        "rechnungsbetrag", "zu zahlender betrag", "zahlbetrag", "endbetrag", "rechnungssumme",
        "gesamtbetrag", "gesamtsumme", "bruttobetrag", "summe brutto", "gesamt brutto",

        // Händlerrechnungen stellen Netto und Brutto untereinander und nennen beide „Endsumme“.
        // Die ausdrückliche Brutto-Schreibweise steht deshalb vor der bloßen.
        "endsumme inkl", "endsumme brutto", "gesamtsumme inkl", "summe inkl", "endsumme",
    ];

    /// <summary>
    /// Woran eine Zeile zu erkennen ist, die <em>nicht</em> den Bruttobetrag trägt.
    /// </summary>
    /// <remarks>
    /// „Endsumme ohne MwSt“ enthält „Endsumme“ und steht auf dem Blatt über der Endsumme mit
    /// MwSt. Ohne diese Sperre wäre der übernommene Preis um die Umsatzsteuer zu niedrig — und
    /// eine Zahl, die fast stimmt, fällt niemandem auf.
    /// </remarks>
    private static readonly string[] NichtBrutto =
    [
        "ohne mwst", "ohne mehrwertsteuer", "ohne ust", "netto", "zzgl", "exkl", "zuzüglich",
    ];

    private static readonly string[] NummerWoerter =
    [
        "rechnungsnummer", "rechnungs-nr", "rechnungsnr", "rechnung nr", "rechnung-nr",
        "rechn-nr", "rechn.-nr", "re-nr", "re.-nr", "r-nummer", "belegnummer", "beleg-nr",
        "invoice no",
    ];

    private static readonly string[] DatumWoerter =
    [
        "rechnungsdatum", "re-datum", "re.-datum", "r-datum", "belegdatum",
        "datum der rechnung", "rechnung vom",
    ];

    /// <summary>
    /// Rechtsformen — sie stehen am vollständigen Namen des Absenders.
    /// </summary>
    /// <remarks>
    /// Als ganze Wörter und nicht als Teilstrings. „ se“ für die SE fand sonst das „Se“ in
    /// „Seite“, und der Absender hieß fortan „Endrechnung/ ambulant Seite“.
    /// </remarks>
    private static readonly Regex Rechtsform =
        new(@"\b(?:g?gmbh|mbh|aktiengesellschaft|e\.\s?v\.|gbr|ohg|kgaa|kg|ag|se)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Woran eine Zahnbehandlung zu erkennen ist.
    /// </summary>
    /// <remarks>
    /// Die GOZ ist der sicherste Marker: Die Gebührenordnung für Zahnärzte steht auf jeder
    /// zahnärztlichen Liquidation und auf keiner anderen. „Zahn“ allein wäre zu grob — es
    /// steckt auch in Namen und Anschriften.
    /// </remarks>
    private static readonly string[] Zahnwoerter =
    [
        "goz", "gebührenordnung für zahnärzte", "zahnarzt", "zahnärzt", "zahnmedizin",
        "zahnbehandlung", "zahnersatz", "kieferorthopäd", "kieferchirurg", "dentalhygien",
    ];

    /// <summary>Betriebsarten — der Rückfall, wenn keine Rechtsform dasteht.</summary>
    private static readonly string[] Betriebsarten =
    [
        "klinikum", "klinik", "krankenhaus", "praxis", "mvz", "labor", "apotheke", "zentrum",
        "sanitätshaus", "physiotherapie", "radiologie",
    ];

    /// <summary>
    /// Liest, was dasteht — oder gibt zu, nichts gefunden zu haben.
    /// </summary>
    public static ExtractedBillDto Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ExtractedBillDto { HasContent = false };
        }

        var zeilen = text
            .Split('\n')
            .Select(z => z.Replace('\r', ' ').Trim())
            .Where(z => z.Length > 0)
            .ToArray();

        if (zeilen.Length == 0)
        {
            return new ExtractedBillDto { HasContent = false };
        }

        var (betrag, betragZeile) = Betrag(zeilen);
        var (nummer, nummerZeile) = Nummer(zeilen);
        var (datum, datumZeile) = Rechnungsdatum(zeilen);
        var (absender, absenderZeile) = Absender(zeilen);
        var (zahn, zahnZeile) = Zahnbehandlung(zeilen);

        return new ExtractedBillDto
        {
            IsDental = zahn,
            IsDentalSource = zahnZeile,
            HasContent = betrag is not null || nummer is not null
                         || datum is not null || absender is not null,
            Provider = absender,
            ProviderSource = absenderZeile,
            BillNumber = nummer,
            BillNumberSource = nummerZeile,
            BillDate = datum,
            BillDateSource = datumZeile,
            GrossAmount = betrag,
            GrossAmountSource = betragZeile,

            // Was eine Rechnung selbst trägt, ist ihr Betrag. Wie viel davon der Versicherte
            // trägt, steht auf der Abrechnung der Kasse und nicht hier — und wäre geraten.
            OwnShare = null,
        };
    }

    /// <summary>
    /// Ob der Text eine Zahnbehandlung nennt — und in welcher Zeile.
    /// </summary>
    private static (bool Wert, string? Zeile) Zahnbehandlung(string[] zeilen)
    {
        foreach (var zeile in zeilen)
        {
            foreach (var wort in Zahnwoerter)
            {
                if (zeile.Contains(wort, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, zeile);
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Der Rechnungsbetrag.
    /// </summary>
    /// <remarks>
    /// Auf der Zeile mit der Beschriftung steht der Betrag rechts, meist als letzte Zahl —
    /// Positionspreise stehen davor. Steht auf der Zeile gar keine Zahl, ist sie eine
    /// Spaltenüberschrift, und der Wert folgt darunter.
    /// </remarks>
    private static (decimal? Wert, string? Zeile) Betrag(string[] zeilen)
    {
        foreach (var wort in BetragWoerter)
        {
            foreach (var (zeile, index) in zeilen.Select((z, i) => (z, i)))
            {
                if (!zeile.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    || NichtBrutto.Any(n => zeile.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (LetzteZahl(zeile) is { } hier)
                {
                    return (hier, zeile);
                }

                if (Spaltenkopf(zeile) && index + 1 < zeilen.Length
                    && LetzteZahl(zeilen[index + 1]) is { } darunter)
                {
                    return (darunter, zeile + " → " + zeilen[index + 1]);
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Ob eine Zeile eine Spaltenüberschrift ist — nur dann steht der Wert darunter.
    /// </summary>
    /// <remarks>
    /// Ein Kasten mit „R-Nummer/Debitor R-Datum“ trägt seine Werte in der Zeile darunter. Ein
    /// Fließsatz wie „Bitte überweisen Sie den Rechnungsbetrag unter Angabe der
    /// Rechnungsnummer auf unser Konto.“ trägt gar keinen — und darunter stand die
    /// Bankverbindung. So wurde aus einer Rechnungsnummer einmal „DE88“.
    ///
    /// Eine Überschrift ist kurz und enthält keine Ziffern; ein Satz ist lang.
    /// </remarks>
    private static bool Spaltenkopf(string zeile)
        => zeile.Length <= 60 && !zeile.Any(char.IsDigit);

    private static decimal? LetzteZahl(string zeile)
    {
        var treffer = Geld.Matches(zeile);
        if (treffer.Count == 0)
        {
            return MitPunkt(zeile);
        }

        var letzter = treffer[^1];
        var roh = letzter.Groups[1].Value.Replace(".", string.Empty)
                      .Replace(" ", string.Empty).Replace(" ", string.Empty)
                  + "." + letzter.Groups[2].Value;

        return decimal.TryParse(roh, NumberStyles.Any, CultureInfo.InvariantCulture, out var wert)
            ? wert
            : null;
    }

    /// <summary>Ein Betrag, den die Erkennung mit Punkt statt Komma gelesen hat.</summary>
    private static decimal? MitPunkt(string zeile)
    {
        var treffer = GeldMitPunkt.Matches(zeile);
        if (treffer.Count == 0)
        {
            return null;
        }

        var letzter = treffer[^1];
        var ganz = letzter.Groups[1].Success ? letzter.Groups[1].Value : letzter.Groups[3].Value;
        var rest = letzter.Groups[1].Success ? letzter.Groups[2].Value : letzter.Groups[4].Value;

        return decimal.TryParse(
            ganz + "." + rest, NumberStyles.Any, CultureInfo.InvariantCulture, out var wert)
            ? wert
            : null;
    }

    /// <summary>
    /// Die Rechnungsnummer.
    /// </summary>
    /// <remarks>
    /// Sie steht hinter ihrer Beschriftung, und wo ein Kasten zwei Spalten hat
    /// („R-Nummer/Debitor“), trägt die Zeile darunter beide Werte — der erste ist die Nummer.
    /// Ein Datum ist keine Nummer, auch wenn es aus Ziffern besteht.
    /// </remarks>
    private static (string? Wert, string? Zeile) Nummer(string[] zeilen)
    {
        foreach (var wort in NummerWoerter)
        {
            foreach (var (zeile, index) in zeilen.Select((z, i) => (z, i)))
            {
                var stelle = zeile.IndexOf(wort, StringComparison.OrdinalIgnoreCase);
                if (stelle < 0)
                {
                    continue;
                }

                if (ErsteNummer(zeile[(stelle + wort.Length)..], zeile) is { } hier)
                {
                    return (hier, zeile);
                }

                if (Spaltenkopf(zeile) && index + 1 < zeilen.Length
                    && ErsteNummer(zeilen[index + 1], zeilen[index + 1]) is { } darunter)
                {
                    return (darunter, zeile + " → " + zeilen[index + 1]);
                }
            }
        }

        // Zuletzt: das große Wort, so wie der Scanner es übrig gelassen hat.
        foreach (var zeile in zeilen.Take(25))
        {
            if (ZerleseneRechnungsnummer.Match(zeile) is { Success: true } treffer
                && ErsteNummer(treffer.Groups[1].Value, zeile) is { } geborgen)
            {
                return (geborgen, zeile);
            }
        }

        return (null, null);
    }

    private static string? ErsteNummer(string rest, string ganzeZeile)
    {
        // Auf einer Zeile mit Bankverbindung steht keine Rechnungsnummer, sondern eine IBAN.
        if (ganzeZeile.Contains("iban", StringComparison.OrdinalIgnoreCase)
            || ganzeZeile.Contains("bic", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (Match treffer in Belegnummer.Matches(rest))
        {
            var wert = treffer.Value.Trim('/', '-');

            // Ein Datum ist keine Belegnummer, und eine Jahreszahl auch nicht. Aber nur eine
            // echte: „4711“ ist vierstellig und trotzdem eine Rechnungsnummer — jede
            // vierstellige Zahl zu verwerfen hat genau die verschluckt.
            if (Datum.IsMatch(treffer.Value) || wert.Length < 3 || Jahreszahl(wert))
            {
                continue;
            }

            // „4711/880022“ ist Nummer und Debitor in einem Feld. Die Nummer steht vorn.
            var geteilt = wert.Split('/')[0];
            return geteilt.Length >= 3 ? geteilt : wert;
        }

        return null;
    }

    /// <summary>Ob ein Wert als Jahreszahl durchgeht — dann ist er keine Belegnummer.</summary>
    private static bool Jahreszahl(string wert)
        => wert.Length == 4 && wert.All(char.IsDigit)
           && int.Parse(wert, CultureInfo.InvariantCulture) is >= 1900 and <= 2100;

    private static (DateOnly? Wert, string? Zeile) Rechnungsdatum(string[] zeilen)
    {
        foreach (var wort in DatumWoerter)
        {
            foreach (var (zeile, index) in zeilen.Select((z, i) => (z, i)))
            {
                if (!zeile.Contains(wort, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LetztesDatum(zeile) is { } hier)
                {
                    return (hier, zeile);
                }

                if (Spaltenkopf(zeile) && index + 1 < zeilen.Length
                    && LetztesDatum(zeilen[index + 1]) is { } darunter)
                {
                    return (darunter, zeile + " → " + zeilen[index + 1]);
                }
            }
        }

        // Zuletzt: eine Zeile, die schlicht mit „Datum“ anfängt.
        foreach (var zeile in zeilen.Take(25))
        {
            if (DatumZeilenanfang.IsMatch(zeile) && LetztesDatum(zeile) is { } schlicht)
            {
                return (schlicht, zeile);
            }
        }

        return (null, null);
    }

    private static DateOnly? LetztesDatum(string zeile)
    {
        var treffer = Datum.Matches(zeile);
        if (treffer.Count == 0)
        {
            // Erst die Ziffernform, dann die mit Monatswort. Andersherum fände ein Blatt, das
            // beide trägt, das seltenere zuerst.
            return LetztesDatumMitMonatswort(zeile);
        }

        var letzter = treffer[^1];
        var tag = int.Parse(letzter.Groups[1].Value, CultureInfo.InvariantCulture);
        var monat = int.Parse(letzter.Groups[2].Value, CultureInfo.InvariantCulture);
        var jahrRoh = int.Parse(letzter.Groups[3].Value, CultureInfo.InvariantCulture);
        var jahr = letzter.Groups[3].Value.Length == 2 ? 2000 + jahrRoh : jahrRoh;

        return Tag(jahr, monat, tag);
    }

    /// <summary>„22. August 2026“ — nur mit einem Monatsnamen, den es gibt.</summary>
    /// <remarks>
    /// Das Wort muss in der Liste stehen. Alles andere wäre geraten: „12. Rate 2026“ sieht
    /// genauso aus und ist kein Datum.
    /// </remarks>
    private static DateOnly? LetztesDatumMitMonatswort(string zeile)
    {
        var treffer = DatumMitMonatswort.Matches(zeile);

        for (var i = treffer.Count - 1; i >= 0; i--)
        {
            var wort = treffer[i].Groups[2].Value;
            if (wort.Length < 3 || !Monate.TryGetValue(wort[..3], out var monat))
            {
                continue;
            }

            if (Tag(
                    int.Parse(treffer[i].Groups[3].Value, CultureInfo.InvariantCulture),
                    monat,
                    int.Parse(treffer[i].Groups[1].Value, CultureInfo.InvariantCulture))
                is { } gefunden)
            {
                return gefunden;
            }
        }

        return null;
    }

    /// <summary>Ein Datum, das es im Kalender gibt — oder keins.</summary>
    private static DateOnly? Tag(int jahr, int monat, int tag)
        => monat is < 1 or > 12 || jahr is < 1900 or > 2200
           || tag < 1 || tag > DateTime.DaysInMonth(jahr, monat)
            ? null
            : new DateOnly(jahr, monat, tag);

    /// <summary>
    /// Der Absender.
    /// </summary>
    /// <remarks>
    /// <para>Das schwächste der vier Felder, und das sagt es auch: Ein Briefkopf hat keine
    /// Beschriftung. Die Rechtsform ist der beste Anker, den ein Blatt von sich aus hergibt —
    /// sie steht am vollständigen Namen und sonst nirgends. Fehlt sie, trägt die Betriebsart;
    /// fehlt auch die, bleibt die erste Zeile.</para>
    /// <para>Nur im Briefkopf gesucht, nicht auf der ganzen Seite: Weiter unten stehen die
    /// Banken des Absenders und gelegentlich der eigene Arbeitgeber.</para>
    /// </remarks>
    private static (string? Wert, string? Zeile) Absender(string[] zeilen)
    {
        var kopf = zeilen.Take(10).ToArray();

        foreach (var zeile in kopf)
        {
            if (Rechtsform.IsMatch(zeile))
            {
                return (OhneAnschrift(zeile), zeile);
            }
        }

        foreach (var zeile in kopf)
        {
            if (Betriebsarten.Any(w => zeile.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                return (OhneAnschrift(zeile), zeile);
            }
        }

        // Zuletzt die erste Zeile, die überhaupt einen Namen tragen kann.
        //
        // Ein Briefkopf bricht den Firmennamen gern über mehrere Zeilen um, und eine Erkennung
        // liefert die Bruchstücke einzeln: „Die“, „Schreiner“, „Eberhard Gronbach“. Die erste
        // davon ist kein Absender, sondern ein Artikel. Zwei Buchstaben und ein Artikel allein
        // sagen nichts — die nächste Zeile ist die bessere Antwort und selten eine schlechtere.
        foreach (var zeile in kopf)
        {
            var name = OhneAnschrift(zeile);
            if (name.Length > 3 && !Fuellwort.IsMatch(name))
            {
                return (name, zeile);
            }
        }

        return (OhneAnschrift(kopf[0]), kopf[0]);
    }

    /// <summary>Ein Wort, das allein auf einer Zeile keinen Absender ergibt.</summary>
    private static readonly Regex Fuellwort =
        new(@"^(?:der|die|das|den|dem|ihr|ihre|herr|frau|firma|seite)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Schneidet die Anschrift ab, die im Briefkopf oft in derselben Zeile steht.
    /// </summary>
    private static string OhneAnschrift(string zeile)
    {
        var treffer = Anschrift.Match(zeile);
        var name = (treffer.Success ? zeile[..treffer.Index] : zeile).Trim(' ', ',', '·', '•', '-');

        return name.Length > 0 ? name : zeile.Trim();
    }
}
