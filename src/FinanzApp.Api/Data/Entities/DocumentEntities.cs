using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Data.Entities;

/// <summary>
/// Art eines Dokuments — Versicherungsschein, Lohnabrechnung, Arztrechnung und so fort.
/// </summary>
/// <remarks>
/// Bewusst eine Tabelle und kein Enum: welche Arten es gibt, entscheidet der Haushalt, nicht der
/// Quelltext. Eine neue Art anzulegen darf keine neue Fassung der Anwendung brauchen.
/// </remarks>
public class DocumentType : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public DocumentArea Area { get; set; }
    public int SortOrder { get; set; }

    /// <summary>
    /// Nicht mehr gepflegt — der Typ ist aus der Verwaltung verschwunden, seine Zeile nicht.
    /// </summary>
    /// <remarks>
    /// Ein echtes Löschen setzt die Typ-Kennung der Dokumente auf null: die Historie zerreißt,
    /// und ein abgelegter Beleg weiß nicht mehr, was er ist. Er behält sie deshalb, und der Typ
    /// verschwindet nur aus der Pflegeliste.
    /// </remarks>
    public bool IsRetired { get; set; }

    public List<Document> Documents { get; set; } = [];
}

/// <summary>
/// Ein abgelegtes Dokument. Das zentrale Modell — alle Bereiche hängen ihre Dateien hier an.
/// </summary>
public class Document : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public required string Title { get; set; }

    public int? DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }

    public DocumentArea Area { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Pfad unterhalb des konfigurierten Dokumentordners, etwa
    /// <c>Versicherungen/Risikoleben/Police_2026.pdf</c>.
    /// </summary>
    /// <remarks>
    /// Bewusst <em>relativ</em>. Ein absoluter Pfad bindet die Datenbank an einen Rechner: nach
    /// einem Umzug oder auf einem zweiten Gerät zeigt er ins Leere. Zusammengesetzt wird erst beim
    /// Öffnen, gegen den aktuell konfigurierten Wurzelordner.
    /// </remarks>
    public required string RelativePath { get; set; }

    public required string FileName { get; set; }

    /// <summary>Kleingeschrieben, mit Punkt: <c>.pdf</c>.</summary>
    public string? Extension { get; set; }

    /// <summary>Datum auf dem Dokument, nicht der Ablagezeitpunkt.</summary>
    public DateOnly? DocumentDate { get; set; }

    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidUntil { get; set; }

    public DocumentStatus Status { get; set; }

    /// <summary>Freie Schlagworte, durch Komma getrennt abgelegt.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Der beim Einlesen erkannte Dokumenttyp, etwa <c>statusreport-lv</c>.
    /// </summary>
    /// <remarks>
    /// Nicht zu verwechseln mit <see cref="DocumentTypeId"/>: das ist die gepflegte Typenliste
    /// des Nutzers, dies hier die maschinelle Erkennung aus dem Text. Sie steht am Dokument und
    /// nicht am einzelnen gelesenen Wert, weil sie das Dokument beschreibt — und weil die
    /// Übernahme sonst raten müsste, nach welchen Regeln die gespeicherten Werte entstanden sind.
    /// </remarks>
    public string? ScanKind { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<DocumentLink> Links { get; set; } = [];
}

/// <summary>
/// Verknüpfung eines Dokuments mit einem Fachobjekt.
/// </summary>
/// <remarks>
/// Polymorph über <see cref="TargetType"/> und <see cref="TargetId"/> statt über je einen
/// Fremdschlüssel: ein neuer Zieltyp kostet dann einen Aufzählungswert und keine Schemaänderung.
/// Den Preis — keine Fremdschlüsselprüfung durch die Datenbank — trägt der Anwendungsdienst, der
/// vor dem Anlegen prüft, ob das Ziel im eigenen Haushalt existiert.
/// </remarks>
public class DocumentLink : IHouseholdOwned
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }

    public int DocumentId { get; set; }
    public Document? Document { get; set; }

    public LinkTargetType TargetType { get; set; }
    public int TargetId { get; set; }

    public DateTime CreatedAt { get; set; }
}
