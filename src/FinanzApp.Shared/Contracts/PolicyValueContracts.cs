namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Wie die Vermögensübersicht heißt und wo ihr Prüfschritt liegt.
/// </summary>
/// <remarks>
/// Im gemeinsamen Vertrag, weil drei Stellen dieselbe Zeichenkette brauchen: die Einlieferung
/// kennzeichnet das Dokument damit, der Scaneingang erkennt es daran wieder, und die Oberfläche
/// baut daraus den Weg zum Prüfschritt. Drei Kopien einer Zeichenkette liefen beim ersten
/// Umbenennen auseinander — und der Schalter im Eingang zeigte danach ins Leere.
/// </remarks>
public static class AssetSheetKind
{
    /// <summary>Der Wert, der als <c>ScanKind</c> am Dokument steht.</summary>
    public const string Key = "vermoegensuebersicht";

    public const string Label = "Vermögensübersicht";

    /// <summary>Der Prüfschritt zu einer abgelegten Übersicht.</summary>
    public static string Href(int documentId) => $"/vorsorge/werte/{documentId}";
}

/// <summary>
/// Was mit einem Vertrag aus der Vermögensübersicht geschehen soll.
/// </summary>
/// <remarks>
/// Der Vorschlag der Anwendung, nicht die Entscheidung: die Oberfläche zeigt ihn an und schickt
/// zurück, was der Mensch daraus gemacht hat. Ohne diese Trennung übernähme eine Datei, die
/// monatlich unbeaufsichtigt hereinkommt, ihre eigenen Zahlen.
/// </remarks>
public enum PolicyValueAction
{
    /// <summary>Die Vertragsnummer passt zu einem Vertrag im Bestand — die Stände gehen dorthin.</summary>
    Match = 0,

    /// <summary>Kein Vertrag mit dieser Nummer. Er ließe sich aus den Angaben der Datei anlegen.</summary>
    Create = 1,

    /// <summary>
    /// Die Zeile gehört nicht hierher — Konto, Depot oder eine Art, die die Datei nicht benennt.
    /// </summary>
    Skip = 2,
}

/// <summary>Wie sich ein gemeldeter Stand zum Bestand verhält.</summary>
public enum PolicyValueState
{
    /// <summary>Diesen Stichtag gibt es am Vertrag noch nicht.</summary>
    New = 0,

    /// <summary>Der Stichtag ist bekannt und trägt denselben Wert — die Übernahme ändert nichts.</summary>
    Unchanged = 1,

    /// <summary>Der Stichtag ist bekannt, der Wert weicht ab — die Übernahme berichtigt ihn.</summary>
    Changed = 2,
}

/// <summary>
/// Die gelesene Vermögensübersicht, bevor irgendetwas übernommen ist.
/// </summary>
/// <remarks>
/// <para>Dieselbe Vorschau für beide Wege: die Monatsdatei, die der Ordnerdienst hereinreicht,
/// und die einmalige Historie aus der Tabellenkalkulation. Es ist dasselbe Format und darum
/// derselbe Schirm — eine Datei mit 264 Ständen unterscheidet sich von einer mit acht nur in der
/// Anzahl.</para>
/// <para>Die Datei liegt zu diesem Zeitpunkt bereits als Dokument in der Ablage, die
/// <em>Werte</em> nicht. Gelesen wird sie bei jedem Aufruf neu — sie ändert sich nicht, und ein
/// zwischengespeicherter Auszug liefe der Datei irgendwann hinterher.</para>
/// </remarks>
public sealed record PolicyValueImportDto
{
    /// <summary>Das abgelegte Dokument, aus dem gelesen wurde.</summary>
    public required int DocumentId { get; init; }

    public required string FileName { get; init; }

    /// <summary>Woher die Datei kam — der Pfad im überwachten Ordner, wo es einen gibt.</summary>
    public string? Source { get; init; }

    /// <summary>Zeitraum der Stichtage in der Datei.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    /// <summary>Zeilen in der Datei — alle, auch die nicht übernommenen.</summary>
    public required int LineCount { get; init; }

    /// <summary>Wie viele Stände tatsächlich neu wären.</summary>
    public required int NewCount { get; init; }
    public required int ChangedCount { get; init; }
    public required int UnchangedCount { get; init; }

    /// <summary>Die Verträge der Datei, jeder mit seinen Ständen.</summary>
    public required IReadOnlyList<PolicyValueContractDto> Contracts { get; init; }

    /// <summary>
    /// Zeilen, aus denen kein Vertragsstand wird, mit dem Grund.
    /// </summary>
    /// <remarks>
    /// Sie stehen im Schirm und werden nicht stillschweigend übersprungen. Wer eine Datei mit
    /// dreizehn Zeilen einliest und acht Verträge sieht, soll die fünf anderen wiederfinden —
    /// sonst sucht er nach seinem Girokonto.
    /// </remarks>
    public required IReadOnlyList<PolicyValueSkippedDto> Skipped { get; init; }

    /// <summary>Was der Übernahme im Weg steht — <c>null</c>, wenn nichts.</summary>
    public string? Blocker { get; init; }
}

/// <summary>Ein Vertrag der Datei mit allen Ständen, die für ihn darin stehen.</summary>
public sealed record PolicyValueContractDto
{
    public required string ContractNumber { get; init; }
    public required string Company { get; init; }

    /// <summary>Vertragsart, wie die Datei sie nennt.</summary>
    public required string ContractKind { get; init; }

    public required PolicyValueAction Action { get; init; }

    /// <summary>Der getroffene Vertrag im Bestand. <c>null</c>, wenn es noch keinen gibt.</summary>
    public int? PolicyId { get; init; }
    public string? PolicyName { get; init; }

    /// <summary>Die Art, unter der ein neuer Vertrag angelegt würde.</summary>
    public PolicyKind? SuggestedKind { get; init; }

    /// <summary>Der Name, unter dem er angelegt würde.</summary>
    public string? SuggestedName { get; init; }

    /// <summary>
    /// Monatlicher Beitrag laut Datei.
    /// </summary>
    /// <remarks>
    /// Übernommen wird er nur, wo am Vertrag keiner steht. Weicht er von einem vorhandenen ab,
    /// bleibt der vorhandene — die Vorschau nennt beide nebeneinander, und geändert wird der
    /// Beitrag am Vertrag.
    /// </remarks>
    public decimal? MonthlyRate { get; init; }

    /// <summary>Der Beitrag, der heute am Vertrag steht. 0, solange keiner eingetragen ist.</summary>
    public decimal? CurrentPremium { get; init; }

    /// <summary>Was heute am Vertrag steht.</summary>
    public decimal? CurrentValue { get; init; }
    public DateOnly? CurrentAsOf { get; init; }

    /// <summary>Der jüngste Stand aus der Datei — das, was nach der Übernahme dort stünde.</summary>
    public required decimal LatestValue { get; init; }
    public required DateOnly LatestAsOf { get; init; }

    public required int NewCount { get; init; }
    public required int ChangedCount { get; init; }
    public required int UnchangedCount { get; init; }

    /// <summary>Vorschlag: angehakt, solange etwas dabei herauskommt.</summary>
    public required bool PreSelected { get; init; }

    /// <summary>Die einzelnen Stände, neueste zuerst.</summary>
    public required IReadOnlyList<PolicyValueStandDto> Stands { get; init; }
}

/// <summary>Ein gemeldeter Stand aus der Datei.</summary>
public sealed record PolicyValueStandDto
{
    public required DateOnly AsOf { get; init; }
    public required decimal Value { get; init; }
    public required PolicyValueState State { get; init; }

    /// <summary>Der Wert, der bisher zu diesem Stichtag am Vertrag steht.</summary>
    public decimal? Previous { get; init; }

    /// <summary>Was in der Spalte „Hinweis“ der Datei steht.</summary>
    public string? Note { get; init; }
}

/// <summary>Eine Zeile, aus der kein Vertragsstand wird.</summary>
public sealed record PolicyValueSkippedDto
{
    /// <summary>Zeilennummer in der Datei — damit sie sich dort wiederfinden lässt.</summary>
    public required int Line { get; init; }

    public required string Label { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Was übernommen werden soll.
/// </summary>
/// <remarks>
/// Gewählt wird je <b>Vertrag</b> und nicht je Stand: bei 264 Ständen wäre alles andere eine
/// Zumutung, und ein einzelner Stand lässt sich am Vertrag wieder entfernen. Ein Vertrag, der
/// hier nicht steht, bleibt unberührt.
/// </remarks>
public sealed record ConfirmPolicyValuesRequest
{
    public required int DocumentId { get; init; }
    public required IReadOnlyList<PolicyValueChoice> Contracts { get; init; }
}

/// <param name="ContractNumber">Die Vertragsnummer aus der Datei.</param>
/// <param name="PolicyId">
/// Der Vertrag, in den die Stände gehen. <c>null</c> zusammen mit <see cref="Create"/> heißt:
/// erst anlegen. Ein hier genannter Vertrag hat Vorrang vor dem Vorschlag — so lässt sich eine
/// Nummer von Hand einem vorhandenen Vertrag zuordnen.
/// </param>
/// <param name="Create">Den Vertrag aus den Angaben der Datei anlegen.</param>
public sealed record PolicyValueChoice(string ContractNumber, int? PolicyId, bool Create);

/// <summary>Was die Übernahme bewirkt hat.</summary>
public sealed record PolicyValueResultDto
{
    public required int PolicyCount { get; init; }
    public required int CreatedCount { get; init; }
    public required int NewCount { get; init; }
    public required int ChangedCount { get; init; }

    /// <summary>Je Vertrag eine Zeile mit der Wirkung — Bausteine, keine fertigen Sätze.</summary>
    public required IReadOnlyList<PolicyValueEffectDto> Effects { get; init; }
}

/// <summary>
/// Die Wirkung an einem Vertrag.
/// </summary>
/// <remarks>
/// Beträge gehen als Zahl hinaus. Ein hier formatierter Euro-Betrag ließe sich von „Beträge
/// verbergen“ nicht mehr maskieren — die Maske greift nur, wo die Oberfläche selbst formatiert.
/// </remarks>
public sealed record PolicyValueEffectDto
{
    public required int PolicyId { get; init; }
    public required string PolicyName { get; init; }
    public required bool Created { get; init; }
    public required int StandCount { get; init; }
    public required decimal Value { get; init; }
    public required DateOnly AsOf { get; init; }

    /// <summary>Veränderung gegenüber dem Wert, der vorher am Vertrag stand.</summary>
    public decimal? Delta { get; init; }
    public DateOnly? PreviousAsOf { get; init; }
}
