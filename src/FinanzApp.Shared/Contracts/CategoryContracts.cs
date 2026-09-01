namespace FinanzApp.Shared.Contracts;

public sealed record CategoryDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required CategoryDirection Direction { get; init; }
}

/// <summary>
/// Eine Kategorie samt dem, was an ihr hängt.
/// </summary>
/// <remarks>
/// Der Verwendungsnachweis ist die Entscheidungsgrundlage vor dem Löschen — gezählt, nicht
/// behauptet. Wer nicht sieht, was dranhängt, löscht entweder blind oder gar nicht.
/// </remarks>
public sealed record CategoryUsageDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required CategoryDirection Direction { get; init; }

    /// <summary>
    /// Wie Buchungen dieser Kategorie steuerlich zählen.
    /// </summary>
    /// <remarks>
    /// Sie stand im Modell, war aber nirgends sichtbar — und ohne eingeordnete Kategorie
    /// blieben Handwerkerleistungen und Werbungskosten im Steuerjahr leer, ohne dass jemand
    /// sagen konnte warum.
    /// </remarks>
    public TaxCategory TaxCategory { get; init; }

    /// <summary>
    /// Ob Ausgaben dieser Kategorie zum Objekt gehören — Handoff „Gemeinsame Immobilie“, 3.4.
    /// </summary>
    /// <remarks>
    /// Trennt Hauskosten von Lebenshaltung. Ohne die Trennung wäre jede €/m²-Zahl falsch, weil
    /// Lebensmittel vom selben Konto abgehen wie der Strom für das Haus.
    /// </remarks>
    public bool PropertyRelated { get; init; }

    public required int TransactionCount { get; init; }
    public required int RuleCount { get; init; }
    public required bool HasBudget { get; init; }

    public bool IsUsed => TransactionCount > 0 || RuleCount > 0 || HasBudget;
}

/// <summary>
/// Eine Kategorie, die es jetzt gibt — neu angelegt oder schon vorhanden.
/// </summary>
/// <remarks>
/// Der Unterschied gehört in die Antwort, nicht in einen Fehler: wer im Import eine fehlende
/// Kategorie anlegt und dabei einen Namen trifft, den es schon gibt, hat nichts falsch gemacht.
/// Die Meldung sagt dann „bestand bereits“ und ordnet trotzdem zu.
/// </remarks>
public sealed record CategoryEnsureResultDto
{
    public required CategoryDto Category { get; init; }
    public required bool Created { get; init; }
}

public sealed record CategoryNameRequest
{
    public required string Name { get; init; }
    public CategoryDirection Direction { get; init; }
}

/// <summary>Welche steuerliche Einordnung eine Kategorie bekommt.</summary>
public sealed record CategoryTaxRequest
{
    public required TaxCategory TaxCategory { get; init; }
}

/// <summary>Ob eine Kategorie zum Objekt gehört.</summary>
public sealed record CategoryPropertyRequest
{
    public required bool PropertyRelated { get; init; }
}

/// <summary>Was das Umbenennen oder Löschen tatsächlich angefasst hat.</summary>
public sealed record CategoryChangeResultDto
{
    public required int TransactionCount { get; init; }
    public required int RuleCount { get; init; }
    public required bool HadBudget { get; init; }
}

public sealed record CategorizationRuleDto
{
    public required int Id { get; init; }

    /// <summary>Präfix des Empfängers, auf das die Regel greift.</summary>
    public required string PayeePattern { get; init; }

    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }

    /// <summary>
    /// Wann die Regel gelernt wurde — <c>null</c> für die, die von Anfang an dabei waren.
    /// </summary>
    public DateOnly? LearnedOn { get; init; }
}
