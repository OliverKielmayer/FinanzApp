namespace FinanzApp.Shared.Contracts;

/// <summary>Art einer Buchung. Umbuchungen sind bewusst eine eigene Art und dürfen in
/// Auswertungen weder als Einnahme noch als Ausgabe gezählt werden.</summary>
public enum TransactionKind
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
}

/// <summary>Für welche Buchungsrichtung eine Kategorie angeboten wird.</summary>
public enum CategoryDirection
{
    Expense = 0,
    Income = 1,
}

/// <summary>
/// Wer ein Konto sehen darf.
/// </summary>
/// <remarks>
/// „Nur ich“ ist eigentümerrelativ und darf nie global ausgewertet werden: die Sichtbarkeit
/// entscheidet sich immer am angemeldeten Benutzer.
/// </remarks>
public enum AccountSharing
{
    /// <summary>Alle Mitglieder des Haushalts.</summary>
    Household = 0,

    /// <summary>Privat — nur der Eigentümer.</summary>
    Private = 1,

    /// <summary>Namentlich für einzelne Mitglieder freigegeben.</summary>
    Named = 2,
}

/// <summary>Art einer Immobilie. Steht in der Zeile unter dem Namen.</summary>
public enum PropertyKind
{
    House = 0,
    Apartment = 1,
    Land = 2,
    Other = 9,
}

public enum BudgetPeriod
{
    Month = 0,
    Quarter = 1,
    Year = 2,
}
