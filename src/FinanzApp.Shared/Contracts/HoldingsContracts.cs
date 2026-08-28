namespace FinanzApp.Shared.Contracts;

/// <summary>Die Objektklassen des Bestands.</summary>
public enum HoldingClass
{
    Accounts = 0,
    Depot = 1,
    Pension = 2,
    Protection = 3,
    Housing = 4,
    Vehicles = 5,
    Loans = 6,
    Work = 7,
}

/// <summary>Ein Klassenfilter mit seinem Zähler.</summary>
public sealed record HoldingClassCountDto(HoldingClass? Class, string Label, int Count);

/// <summary>
/// Die Kopfkennzahl, die der Klassenfilter setzt.
/// </summary>
/// <remarks>
/// <para>Sie trägt Zahlen, keine fertigen Sätze. Ein hier formatierter Euro-Betrag käme an
/// „Beträge verbergen“ vorbei, und die Wortwahl gehört ohnehin in die Anzeige.</para>
/// <para>Ohne diese Kennzahl wäre die Zusammenlegung der sieben Bereiche ein Verlust: jeder
/// Einzelbereich hatte seine eigene Summe oben, und eine Liste ohne sie wäre nur länger.</para>
/// </remarks>
public sealed record HoldingsHeadDto
{
    /// <summary><c>null</c> für „Alle“.</summary>
    public required HoldingClass? Class { get; init; }

    /// <summary>Die Hauptsumme: Wert oder Jahreskosten, je nach Klasse.</summary>
    public required decimal Value { get; init; }

    /// <summary>Die Sachwerte — nur bei „Alle“ von Belang.</summary>
    public required decimal TangibleAssets { get; init; }

    /// <summary>Verbindlichkeiten, positiv geführt.</summary>
    public required decimal Liabilities { get; init; }

    /// <summary>Finanzvermögen plus Sachwerte minus Verbindlichkeiten.</summary>
    public required decimal Net { get; init; }

    /// <summary>Wie viele Zeilen der Filter zeigt.</summary>
    public required int Count { get; init; }

    /// <summary>
    /// Eine zweite Zählung, wenn die Klasse zweierlei enthält.
    /// </summary>
    /// <remarks>
    /// Wohnen führt Objekte <em>und</em> Verträge. „5 Objekte“ wäre falsch, „5 Zeilen“ nichtssagend.
    /// Bei „Arbeit“ trennt sie laufend von beendet — ein nackter Zähler, der etwas anderes
    /// zählt als der Chip daneben, ist ein Fehler; hier zählt jede Zahl mit ihrem Wort.
    /// </remarks>
    public required int SecondaryCount { get; init; }

    /// <summary>Wie viele Posten eine laufende Frist haben.</summary>
    public required int UrgentCount { get; init; }

    /// <summary>Die Rate — nur beim Darlehen.</summary>
    public required decimal? Installment { get; init; }

    /// <summary>Der nächste Zahltag — nur beim Darlehen.</summary>
    public required DateOnly? NextPayment { get; init; }
}

/// <summary>
/// Eine Zeile des Bestands.
/// </summary>
/// <remarks>
/// <para>Sie trägt <b>entweder</b> einen Wert <b>oder</b> Jahreskosten. Verträge haben keinen
/// Vermögenswert; ihnen einen zu geben wäre eine erfundene Zahl in einer Summe, der man glauben
/// soll. Zwei Spaltenbedeutungen in einer Liste sind zulässig, solange die Einheit an der Zahl
/// steht — darum die Unterscheidung im Vertrag und nicht erst in der Anzeige.</para>
/// <para><see cref="Meta"/> entsteht aus den Rohfeldern des Objekts, nie aus einem
/// Anzeigefeld. Was ein Objekt nicht hat, steht nicht da — auch nicht als „ohne Konto“.</para>
/// </remarks>
public sealed record HoldingRowDto
{
    public required HoldingClass Class { get; init; }
    public required string ClassLabel { get; init; }
    public required string Name { get; init; }
    public required string Meta { get; init; }

    /// <summary>Vermögenswert. <c>null</c> bei Posten, die keinen tragen.</summary>
    public required decimal? Value { get; init; }

    /// <summary>Jahreskosten. <c>null</c> bei Posten, die einen Wert tragen.</summary>
    public required decimal? YearlyCost { get; init; }

    /// <summary>
    /// Jahreseinkommen. <c>null</c> bei allem, was keines abwirft.
    /// </summary>
    /// <remarks>
    /// Eine dritte Spaltenbedeutung, weil es die drei Bedeutungen wirklich gibt: ein Gehalt ist
    /// weder ein Vermögenswert noch eine Kosten­last. Es unter <see cref="YearlyCost"/> zu
    /// führen hieße, Einnahmen als Ausgaben zu buchen — im Vertrag und damit überall.
    /// Bei einem beendeten Verhältnis steht auch hier <c>null</c>: es trägt keine Jahreslast
    /// mehr, und die Zeile zeigt „—“.
    /// </remarks>
    public required decimal? YearlyIncome { get; init; }

    /// <summary>Ob der Wert ein Sachwert ist — er zählt in eine andere Summe.</summary>
    public required bool IsTangible { get; init; }

    /// <summary>Stichtag oder Notiz, rechts unter dem Wert.</summary>
    public required string? Note { get; init; }

    /// <summary>Eine laufende Frist. Die Zeile steht dann im Akzentmuster.</summary>
    public required bool Urgent { get; init; }

    /// <summary>Wohin die Zeile führt.</summary>
    public required string Route { get; init; }
}

public sealed record HoldingsDto
{
    public required IReadOnlyList<HoldingClassCountDto> Classes { get; init; }
    public required HoldingsHeadDto Head { get; init; }
    public required IReadOnlyList<HoldingRowDto> Rows { get; init; }

    /// <summary>Die Klasse, in der die „+“-Zeile anlegt. <c>null</c> öffnet das Erfassen-Sheet.</summary>
    public required HoldingClass? AddIn { get; init; }
}
