using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Infrastructure;

/// <summary>
/// Ein Vorschlag, wohin ein Dokument gehört, das keine Leseregel erkannt hat.
/// </summary>
/// <param name="Label">Was vorgeschlagen wird, im Klartext: „Arztrechnung“.</param>
/// <param name="Area">Der Bereich, in den es gehört.</param>
/// <param name="TypeName">Der Dokumenttyp, unter dem es abgelegt gehört.</param>
/// <param name="Hits">Die Wörter, die den Ausschlag gegeben haben.</param>
public readonly record struct FilingSuggestion(
    string Label,
    DocumentArea Area,
    string TypeName,
    IReadOnlyList<string> Hits);

/// <summary>
/// Erkennt am Text, was für ein Schreiben vorliegt — und schlägt eine Ablage vor.
/// </summary>
/// <remarks>
/// <para><b>Ein Vorschlag, keine Zuordnung.</b> Die Leseregeln in
/// <see cref="DocumentKindLibrary"/> holen Werte aus einem Beleg und tragen sie in Verträge ein;
/// sie müssen genau sein. Hier geht es nur um die Schublade, und ein falscher Vorschlag kostet
/// einen Klick. Deshalb Schlagwörter und keine Muster mit Fangarmen.</para>
/// <para>Gewichtet wird in zwei Stufen. Ein <em>starkes</em> Wort benennt die Art fast allein
/// („Entgeltabrechnung“, „Versicherungsschein“), ein <em>schwaches</em> stützt sie nur
/// („Brutto“, „IBAN“). Zwei Punkte sind die Schwelle: Ein einzelnes schwaches Wort entscheidet
/// nichts — „Brutto“ steht auf jeder zweiten Rechnung.</para>
/// <para>Die allgemeine Rechnung steht zuletzt und greift nur, wenn nichts Genaueres die
/// Schwelle erreicht. Sonst schlüge sie bei jeder Arztrechnung zuerst an, denn darauf steht
/// auch „Rechnungsnummer“.</para>
/// </remarks>
public static class FilingSuggestions
{
    /// <summary>Ab wie vielen Punkten ein Vorschlag gemacht wird.</summary>
    private const int Threshold = 2;

    private sealed record Rule(
        string Label,
        DocumentArea Area,
        string TypeName,
        string[] Strong,
        string[] Weak,
        bool IsFallback = false);

    /// <summary>
    /// Die Arten, die auf dem Postweg ankommen. Eine weitere ist ein Eintrag mehr in dieser
    /// Liste — kein neuer Bildschirm und keine neue Tabelle.
    /// </summary>
    private static readonly Rule[] Rules =
    [
        new("Arztrechnung", DocumentArea.Health, "Arztrechnung",
            Strong: ["arztrechnung", "zahnarzt", "goä", "gebührenordnung für ärzte", "liquidation",
                     "honorarrechnung", "gebührenordnung für zahnärzte"],
            Weak: ["praxis", "behandlung", "diagnose", "patient", "befund", "steigerungsfaktor"]),

        new("Handwerkerrechnung", DocumentArea.Housing, "Handwerkerrechnung",
            Strong: ["handwerkerleistung", "lohnanteil", "arbeitslohn", "§ 35a", "35a estg",
                     "monteurstunden"],
            Weak: ["monteur", "montage", "meisterbetrieb", "stundenlohn", "anfahrt", "material"]),

        new("Steuerbescheid", DocumentArea.Finance, "Steuerbescheid",
            Strong: ["steuerbescheid", "bescheid für", "festsetzung", "einkommensteuerbescheid"],
            Weak: ["finanzamt", "steuernummer", "veranlagung", "solidaritätszuschlag", "kirchensteuer"]),

        new("Gehaltsabrechnung", DocumentArea.Work, "Gehaltsabrechnung",
            Strong: ["entgeltabrechnung", "gehaltsabrechnung", "verdienstabrechnung", "lohnabrechnung"],
            Weak: ["lohnsteuer", "sozialversicherung", "brutto", "netto", "steuerklasse", "gesamtbrutto"]),

        new("Kontoauszug", DocumentArea.Finance, "Kontoauszug",
            Strong: ["kontoauszug", "auszug nr", "umsatzübersicht"],
            Weak: ["kontostand", "iban", "alter kontostand", "neuer kontostand", "buchungstag"]),

        // Der Typ heißt in der Anwendung „Versicherungspolice“ — der Vorschlag muss auf einen
        // Typ zeigen, den es gibt, sonst schickt er den Benutzer zum Anlegen eines Synonyms.
        new("Versicherungsschein", DocumentArea.Insurance, "Versicherungspolice",
            Strong: ["versicherungsschein", "nachtrag zum versicherungsschein", "policennummer"],
            Weak: ["versicherungsnummer", "beitragsrechnung", "versicherungsjahr", "tarif",
                   "versicherungsbeginn"]),

        // Zuletzt und nur, wenn nichts Genaueres greift.
        //
        // Die schwachen Wörter sind an einer eingescannten Rechnung gewählt und nicht am
        // Schreibtisch. Dort stand „RECHNU G Nr.“ — die Erkennung hatte ein N verschluckt, und
        // eine Regel, die auf „rechnung“ wartet, wartete vergeblich. Was ein Scan heil
        // überliefert, sind die kurzen Feldbeschriftungen ringsum: Kunden-Nr., Auftrags-Nr.,
        // MwSt, Warenwert. Sie stehen auf jeder Rechnung und auf wenig sonst.
        new("Rechnung", DocumentArea.Other, "Rechnung",
            Strong: ["rechnungsnummer", "rechnungsdatum", "rechnungs-nr", "zahlungsziel"],
            Weak:
            [
                "rechnung", "zahlbar", "ust-idnr", "umsatzsteuer", "gesamtbetrag", "nettobetrag",
                "kunden-nr", "auftrags-nr", "mwst", "warenwert", "zwischensumme", "einzelpreis",
                "lieferschein", "lieferdat",
            ],
            IsFallback: true),
    ];

    /// <summary>
    /// Der beste Vorschlag zu einem gelesenen Text — oder keiner.
    /// </summary>
    /// <remarks>
    /// Kein Vorschlag ist eine gültige Antwort und besser als ein schlechter: Wer „Sonstiges“
    /// vorgeschlagen bekommt, klickt es weg; wer „Arztrechnung“ vorgeschlagen bekommt und eine
    /// Autowerkstattrechnung in der Hand hält, traut dem nächsten Vorschlag nicht mehr.
    /// </remarks>
    public static FilingSuggestion? For(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var klein = text.ToLowerInvariant();

        FilingSuggestion? bester = null;
        var bestePunkte = 0;

        foreach (var regel in Rules)
        {
            var (punkte, treffer) = Score(klein, regel);
            if (punkte < Threshold)
            {
                continue;
            }

            // Die allgemeine Rechnung tritt nur an, wenn noch nichts gefunden ist.
            if (regel.IsFallback && bester is not null)
            {
                continue;
            }

            if (punkte > bestePunkte)
            {
                bestePunkte = punkte;
                bester = new FilingSuggestion(regel.Label, regel.Area, regel.TypeName, treffer);
            }
        }

        return bester;
    }

    private static (int Punkte, List<string> Treffer) Score(string klein, Rule regel)
    {
        var treffer = new List<string>();
        var punkte = 0;

        foreach (var wort in regel.Strong)
        {
            if (klein.Contains(wort, StringComparison.Ordinal))
            {
                punkte += 2;
                treffer.Add(wort);
            }
        }

        foreach (var wort in regel.Weak)
        {
            if (klein.Contains(wort, StringComparison.Ordinal))
            {
                punkte += 1;
                treffer.Add(wort);
            }
        }

        return (punkte, treffer);
    }
}
