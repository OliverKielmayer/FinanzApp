using System.Linq.Expressions;
using FinanzApp.Api.Data.Entities;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Application;

/// <summary>
/// Welche Buchungsarten in Einnahmen, Ausgaben, Sparquote und Liquidität zählen.
/// </summary>
/// <remarks>
/// <para>An einer Stelle beantwortet, weil die Frage an vielen gestellt wird: Monatskennzahlen,
/// Liquidität, Triage, Kategoriezuweisung. Vorher stand überall <c>Kind != Transfer</c>. Mit der
/// zweiten nicht zählenden Art — der <see cref="TransactionKind.Deposit"/> — wären daraus
/// zehnmal <c>!= Transfer &amp;&amp; != Deposit</c> geworden, und beim nächsten Mal hätte eine
/// Stelle gefehlt. Genau diese Drift beschreibt der Handoff als seinen häufigsten Fehler.</para>
/// <para><b>Was zählt, ist Zufluss oder Abfluss von außen.</b> Eine Umbuchung verschiebt Geld
/// zwischen eigenen Konten, eine Einlage zwischen Beteiligten desselben Objekts — beide sind
/// keine Einnahme und keine Ausgabe, sonst zählte dieselbe Zahl zweimal.</para>
/// </remarks>
public static class BookingKinds
{
    /// <summary>
    /// Als Ausdruck für Abfragen — so übersetzt EF Core den Filter in SQL.
    /// </summary>
    /// <remarks>
    /// Eine Methode ließe sich nicht übersetzen; der Ausdruck wird deshalb an
    /// <c>Where</c> übergeben statt eines Lambdas mit Methodenaufruf.
    /// </remarks>
    public static readonly Expression<Func<Transaction, bool>> Counting =
        t => t.Kind == TransactionKind.Expense || t.Kind == TransactionKind.Income;

    /// <summary>Dieselbe Frage für schon geladene Zeilen.</summary>
    public static bool Counts(TransactionKind kind)
        => kind is TransactionKind.Expense or TransactionKind.Income;

    /// <summary>
    /// Ob eine Buchung dieser Art eine Kategorie trägt.
    /// </summary>
    /// <remarks>
    /// Umbuchung und Einlage nicht: bei ihnen wäre eine Kategorie eine Aussage über einen
    /// Verbrauch, den es nicht gab. Der Erfassen-Weg überspringt den Schritt bei beiden.
    /// </remarks>
    public static bool TakesCategory(TransactionKind kind) => Counts(kind);

    /// <summary>Wie die Art im Klartext heißt.</summary>
    public static string Label(TransactionKind kind) => kind switch
    {
        TransactionKind.Income => "Einnahme",
        TransactionKind.Transfer => "Umbuchung",
        TransactionKind.Deposit => "Einlage",
        _ => "Ausgabe",
    };
}
