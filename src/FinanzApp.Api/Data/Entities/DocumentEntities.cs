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
    /// Der gelesene Text des Dokuments, für die Suche.
    /// </summary>
    /// <remarks>
    /// <para>Die Anwendung las ihn längst — aus der Textebene eines PDF, seit der Texterkennung
    /// auch aus Fotos und Scans —, zog ihre Werte heraus und warf ihn weg. Wer danach ein
    /// Schreiben suchte, fand es nur über Titel, Dateiname, Typ oder verknüpftes Objekt: über
    /// das, was jemand vorher von Hand gepflegt hatte.</para>
    /// <para>Dass er aus einer Erkennung stammen kann, steht an <see cref="TextSource"/>.
    /// Verlesene Wörter sind in einer Suche kein Schaden — sie finden eben nicht —, aber wer
    /// nichts findet, soll wissen, ob er schlecht gesucht oder die Maschine schlecht gelesen
    /// hat.</para>
    /// <para><c>null</c> heißt: noch nicht gelesen. <see cref="string.Empty"/> heißt: gelesen
    /// und nichts gefunden. Die Unterscheidung verhindert, dass ein bildhaftes PDF bei jedem
    /// Aufruf erneut durch die Erkennung läuft.</para>
    /// </remarks>
    public string? ContentText { get; set; }

    /// <summary>Woher der Text stammt — Textebene, Bild ohne Erkennung, oder Erkennung.</summary>
    public TextSource? TextSource { get; set; }

    /// <summary>
    /// Wer das Dokument sehen darf. Vorgabe: der ganze Haushalt.
    /// </summary>
    public DocumentSharing Sharing { get; set; } = DocumentSharing.Household;

    /// <summary>
    /// Wem es gehört — nur bei <see cref="DocumentSharing.Private"/> von Bedeutung.
    /// </summary>
    /// <remarks>
    /// Ohne Eigentümer sähe ein privates Dokument niemand mehr, auch der nicht, der es abgelegt
    /// hat. Wer ein Dokument auf „nur ich“ stellt, wird deshalb im selben Zug sein Eigentümer.
    /// </remarks>
    public int? OwnerUserId { get; set; }

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

    /// <summary>
    /// Der Abdruck des Dateiinhalts — SHA-256, klein geschrieben, 64 Zeichen.
    /// </summary>
    /// <remarks>
    /// <para>Er beantwortet genau eine Frage: <em>Ist das Byte für Byte dieselbe Datei?</em> Der
    /// Dateiname beantwortet sie nicht — derselbe Scan heißt beim zweiten Mal anders, weil die
    /// Ablage einem besetzten Namen ausweicht —, der Pfad erst recht nicht, und die gelesenen
    /// Werte sagen nur, dass zwei Belege dasselbe <em>berichten</em>.</para>
    /// <para><c>null</c> bei allem, was vor dieser Spalte abgelegt wurde. Nachgetragen wird der
    /// Abdruck, sobald jemand danach sucht, und nicht in einer Wanderung: die Datenbank kommt an
    /// den Dokumentordner nicht heran — den kennt erst die Anwendung.</para>
    /// </remarks>
    public string? ContentHash { get; set; }

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
