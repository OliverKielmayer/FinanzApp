namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Wie ein Vertrag seine eigenen Größen nennt — v5-Handoff, Abschnitt 19.7.
/// </summary>
/// <remarks>
/// <para>Die Bezeichnung ist eine <b>Funktion der Vertragsart</b>, keine Konstante. Vorher hieß
/// bei allen Arten alles „Rückkaufswert“ — ein Bausparvertrag hat keinen, eine Riesterrente
/// auch nicht. Produktfremde Texte machen aus einer richtigen Zahl eine falsche Aussage.</para>
/// <para>Geteilt zwischen Server und Oberfläche, damit Formularfeld, Wertblock und
/// Sektionsüberschrift dieselbe Bezeichnung tragen. Zwei Stellen mit zwei Namen für dieselbe
/// Größe wären derselbe Fehler eine Ebene tiefer.</para>
/// </remarks>
public static class PolicyValueNaming
{
    /// <summary>Wie der Hauptbestandteil des erreichten Werts heißt.</summary>
    public static string BaseValueLabel(PolicyKind kind) => kind switch
    {
        PolicyKind.CapitalLife => "Rückkaufswert",
        PolicyKind.BuildingSociety => "Sparguthaben",
        _ => "Deckungskapital",
    };

    /// <summary>
    /// Ob der Vertrag eine Überschussbeteiligung führt, die getrennt ausgewiesen wird.
    /// </summary>
    /// <remarks>
    /// Beim Bausparen gibt es keine — dort besteht der erreichte Wert aus einem Teil, und eine
    /// Summe aus einem Summanden ist keine.
    /// </remarks>
    public static bool HasAccruedBonus(PolicyKind kind)
        => kind is PolicyKind.CapitalLife or PolicyKind.Pension
            or PolicyKind.Riester or PolicyKind.OccupationalPension;

    /// <summary>Wie der Bericht heißt, aus dem die Werte stammen.</summary>
    /// <remarks>
    /// „Statusreport“ nur, wo es einen gibt. Ein Bausparvertrag bekommt einen Jahresauszug, und
    /// ihn Statusreport zu nennen hieße, ein Dokument zu behaupten, das nie kam.
    /// </remarks>
    public static string ReportLabel(PolicyKind kind)
        => kind is PolicyKind.CapitalLife or PolicyKind.Pension or PolicyKind.OccupationalPension
            ? "Statusreport"
            : "Auszug";

    /// <summary>Wie mehrere davon heißen.</summary>
    /// <remarks>
    /// Eigene Stelle statt eines angehängten „e“: aus „Auszug“ würde sonst „Auszuge“. Der Plural
    /// steht im Schirm an zwei Stellen und wäre an beiden falsch.
    /// </remarks>
    public static string ReportPlural(PolicyKind kind)
        => ReportLabel(kind) == "Statusreport" ? "Statusreporte" : "Auszüge";

    /// <summary>Wie die Verlaufssektion überschrieben ist.</summary>
    public static string HistoryLabel(PolicyKind kind)
        => ReportLabel(kind) == "Statusreport"
            ? "Verlauf aus Statusreports"
            : "Verlauf aus Jahresauszügen";

    /// <summary>
    /// Ob der Hinweis zu Bewertungsreserven und Schlussüberschüssen überhaupt gilt.
    /// </summary>
    /// <remarks>
    /// Nur bei der Kapitallebensversicherung: diese Posten gibt es bei Bausparen und Riester
    /// nicht, und ohne Statusreport gibt es auch keinen Bericht, auf den sich der Satz berufen
    /// könnte.
    /// </remarks>
    public static bool MentionsUnguaranteed(PolicyKind kind) => kind == PolicyKind.CapitalLife;
}
