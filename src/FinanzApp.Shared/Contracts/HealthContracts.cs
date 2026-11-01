namespace FinanzApp.Shared.Contracts;

public sealed record MedicalBillListItemDto
{
    public required int Id { get; init; }
    public required string Provider { get; init; }
    public required DateOnly BillDate { get; init; }
    public required decimal GrossAmount { get; init; }
    public required decimal OwnShare { get; init; }
    public required decimal ExpectedReimbursement { get; init; }

    /// <summary>Noch offene Erstattung. Der Eigenanteil steckt hier nie drin.</summary>
    public required decimal OpenAmount { get; init; }

    public required MedicalBillStatus Status { get; init; }

    /// <summary>Tage seit der Einreichung, solange keine Zahlung da ist.</summary>
    public int? DaysWaiting { get; init; }
}

/// <summary>Eine Station der Statuskette im Vorgang.</summary>
public sealed record MedicalBillStepDto
{
    public required MedicalBillStatus Status { get; init; }
    public required string Label { get; init; }
    public required bool Done { get; init; }

    /// <summary>Die Station, an der der Vorgang gerade steht.</summary>
    public required bool Current { get; init; }

    public DateOnly? At { get; init; }
}

public sealed record MedicalBillDetailDto
{
    public required int Id { get; init; }
    public required string Provider { get; init; }
    public required DateOnly BillDate { get; init; }
    public string? BillNumber { get; init; }
    public required decimal GrossAmount { get; init; }
    public required decimal OwnShare { get; init; }
    public required decimal ExpectedReimbursement { get; init; }
    public decimal? ActualReimbursement { get; init; }
    public required decimal OpenAmount { get; init; }
    public required MedicalBillStatus Status { get; init; }
    public int? DaysWaiting { get; init; }

    /// <summary>Übliche Bearbeitungsdauer der Versicherung in Tagen — Maßstab für „überfällig“.</summary>
    public required int UsualProcessingDays { get; init; }

    public string? Notes { get; init; }
    public required IReadOnlyList<MedicalBillStepDto> Steps { get; init; }
    public required IReadOnlyList<DocumentListItemDto> Documents { get; init; }
    public int? ReimbursementTransactionId { get; init; }

    /// <summary>Nächster sinnvoller Schritt, als Beschriftung der Primäraktion.</summary>
    public string? NextActionLabel { get; init; }

    /// <summary>Status, den die Primäraktion setzt.</summary>
    public MedicalBillStatus? NextStatus { get; init; }

    public bool IsOverdue => DaysWaiting is { } days && days > UsualProcessingDays;
}

public sealed record CreateMedicalBillRequest
{
    public required string Provider { get; init; }
    public required DateOnly BillDate { get; init; }
    public string? BillNumber { get; init; }
    public required decimal GrossAmount { get; init; }
    public required decimal OwnShare { get; init; }
    public decimal? ExpectedReimbursement { get; init; }
    public string? Notes { get; init; }

    /// <summary>Dokument, das beim Scannen entstanden ist.</summary>
    public int? DocumentId { get; init; }

    /// <summary>
    /// Ob es eine Zahnbehandlung war — sie zählt nicht gegen den Jahresdeckel.
    /// </summary>
    public bool IsDental { get; init; }
}

public sealed record AdvanceMedicalBillRequest
{
    public required MedicalBillStatus Status { get; init; }
}

/// <summary>
/// Ein Zahlungsvorschlag zu einem Vorgang. Die Bewertung schlägt vor — bestätigt wird von Hand.
/// </summary>
public sealed record PaymentCandidateDto
{
    public required int TransactionId { get; init; }
    public required DateOnly BookingDate { get; init; }
    public required string Payee { get; init; }
    public required decimal Amount { get; init; }
    public required string AccountName { get; init; }

    /// <summary>0…100. Aus Betrag, Datum und Verwendungszweck.</summary>
    public required int Score { get; init; }

    /// <summary>Warum dieser Satz vorgeschlagen wird — oder warum er abweicht.</summary>
    public required string Reason { get; init; }

    /// <summary>Der beste Treffer, im Akzent hervorgehoben.</summary>
    public required bool IsBestMatch { get; init; }
}

public sealed record LinkPaymentRequest
{
    public required int TransactionId { get; init; }

    /// <summary>Tatsächlich erstatteter Betrag. Ohne Angabe der Betrag der Buchung.</summary>
    public decimal? ActualAmount { get; init; }
}

/// <summary>
/// Was ein Beleg-Erkenner aus einer Datei herausliest. Alle Felder dürfen leer bleiben — ohne
/// Erkennung ist die Maske dieselbe, nur unausgefüllt.
/// </summary>
public sealed record ExtractedBillDto
{
    public string? Provider { get; init; }
    public DateOnly? BillDate { get; init; }
    public string? BillNumber { get; init; }
    public decimal? GrossAmount { get; init; }
    public decimal? OwnShare { get; init; }

    /// <summary>Ob überhaupt etwas erkannt wurde.</summary>
    public required bool HasContent { get; init; }

    // ── Woher die Werte stammen ───────────────────────────────────────────────────────────
    //
    // Jeder gelesene Wert bringt seine Zeile mit. Das ist der Unterschied zwischen Lesen und
    // Raten: Ein Betrag mit Quelle ist in einer Sekunde geprüft; einer ohne ist eine
    // Behauptung, die niemand mehr nachsieht. Bei einer Erstattung ist das die falsche Stelle
    // dafür — die Zahl wandert in eine Forderung gegen die Kasse.

    /// <summary>Die Zeile, aus der <see cref="Provider"/> stammt.</summary>
    public string? ProviderSource { get; init; }

    /// <summary>Die Zeile, aus der <see cref="BillDate"/> stammt.</summary>
    public string? BillDateSource { get; init; }

    /// <summary>Die Zeile, aus der <see cref="BillNumber"/> stammt.</summary>
    public string? BillNumberSource { get; init; }

    /// <summary>Die Zeile, aus der <see cref="GrossAmount"/> stammt.</summary>
    public string? GrossAmountSource { get; init; }

    /// <summary>
    /// Ob der Text auf eine Zahnbehandlung hindeutet.
    /// </summary>
    /// <remarks>
    /// Keine Nebensächlichkeit: Fällt Zahnbehandlung aus dem Jahresdeckel der
    /// Selbstbeteiligung heraus, hängt der vorgeschlagene Eigenanteil daran. Deshalb mit
    /// Quellzeile wie jeder andere gelesene Wert — und in der Maske zum Umschalten.
    /// </remarks>
    public bool IsDental { get; init; }

    /// <summary>Die Zeile, aus der <see cref="IsDental"/> stammt.</summary>
    public string? IsDentalSource { get; init; }
}

/// <summary>
/// Was die Maske für einen neuen Erstattungsvorgang vorschlagen kann.
/// </summary>
/// <remarks>
/// Zwei Quellen, die nichts voneinander wissen: Aus dem Beleg kommt, was draufsteht; aus dem
/// Vertrag kommt die Selbstbeteiligung. Getrennt gehalten, weil sie getrennt stimmen oder
/// fehlen können — ein lesbarer Beleg ohne gepflegte Regel ist genauso üblich wie umgekehrt.
/// </remarks>
public sealed record BillPrefillDto
{
    /// <summary>Was im Beleg steht.</summary>
    public required ExtractedBillDto Bill { get; init; }

    /// <summary>Die Selbstbeteiligungsregel; <c>null</c>, wenn keine gepflegt ist.</summary>
    public OwnShareRuleDto? OwnShare { get; init; }
}
