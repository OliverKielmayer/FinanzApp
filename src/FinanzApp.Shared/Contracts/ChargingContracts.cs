namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Eine Ablesung, wie die Liste sie zeigt — mit dem, was sich aus ihrer Vorgängerin ergibt.
/// </summary>
/// <remarks>
/// <para><b>Verbrauch, Satz und Betrag stehen hier, obwohl keines davon gespeichert ist.</b>
/// Sie entstehen beim Lesen aus dem vorigen Stand und dem Satz, der am Ablesetag galt. Der
/// Bildschirm rechnet sie nicht selbst: Zwei Rechnungen für eine Aussage laufen auseinander,
/// und der Tag, an dem es passiert, fällt niemandem auf.</para>
/// <para>Bei der ersten Ablesung einer Reihe sind sie <c>null</c>. Es gibt nichts, wovon sich
/// abziehen ließe — und eine Null wäre die Behauptung, es sei nichts geladen worden.</para>
/// </remarks>
public sealed record ChargingReadingDto
{
    public int Id { get; init; }

    /// <summary>Der Tag der Ablesung.</summary>
    public DateOnly ReadOn { get; init; }

    /// <summary>Der abgelesene Stand in kWh.</summary>
    public decimal MeterKwh { get; init; }

    /// <summary>Was seit der vorigen Ablesung geladen wurde. <c>null</c> bei der ersten.</summary>
    public decimal? Kwh { get; init; }

    /// <summary>Der Tag der vorigen Ablesung — der Anfang des Zeitraums.</summary>
    public DateOnly? Since { get; init; }

    /// <summary>
    /// Der Satz, der am Ablesetag galt. <c>null</c>, wenn für diesen Tag keiner vereinbart ist.
    /// </summary>
    public decimal? EuroPerKwh { get; init; }

    /// <summary>Verbrauch mal Satz. <c>null</c>, sobald eines von beiden fehlt.</summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Ob im Zeitraum ein neuer Satz zu gelten begann.
    /// </summary>
    /// <remarks>
    /// Gerechnet wird dann mit dem Satz des Ablesetags — die Kilowattstunden auf die Tage zu
    /// verteilen hieße, eine Ladekurve zu erfinden, die niemand gemessen hat. Dass der Zeitraum
    /// darüber lief, muss der Mensch sehen können: Wer es genau will, liest am Tag des Wechsels
    /// ab, und dann gibt es die Frage nicht mehr.
    /// </remarks>
    public bool RateChangedInPeriod { get; init; }

    public int? VehicleId { get; init; }
    public string? VehicleName { get; init; }

    /// <summary>Der Beleg mit dem Foto vom Display.</summary>
    public int? DocumentId { get; init; }
    public string? DocumentName { get; init; }

    public string? Note { get; init; }
}

/// <summary>Ein vereinbarter Erstattungssatz, gültig ab einem Tag.</summary>
public sealed record ChargingRateDto
{
    public int Id { get; init; }
    public DateOnly ValidFrom { get; init; }
    public decimal EuroPerKwh { get; init; }
    public string? Note { get; init; }

    /// <summary>Bis wann er gilt — der Tag vor dem nächsten Satz. <c>null</c> beim jüngsten.</summary>
    /// <remarks>
    /// Abgeleitet und nicht gespeichert: Ein Bis-Datum neben dem Ab-Datum wäre dieselbe Aussage
    /// zweimal, und gepflegt würde irgendwann nur eines.
    /// </remarks>
    public DateOnly? ValidUntil { get; init; }
}

/// <summary>
/// Eine Abrechnung für einen Zeitraum — das Blatt, das zum Arbeitgeber geht.
/// </summary>
/// <remarks>
/// Jede Zahl darin ist festgeschrieben und wird nicht neu gerechnet. Was der Arbeitgeber
/// gesehen hat, bleibt stehen, auch wenn später ein anderer Satz vereinbart wird.
/// </remarks>
public sealed record ChargingStatementDto
{
    public int Id { get; init; }

    public DateOnly FromOn { get; init; }
    public decimal FromMeterKwh { get; init; }
    public DateOnly ToOn { get; init; }
    public decimal ToMeterKwh { get; init; }

    public decimal Kwh { get; init; }

    /// <summary>Der Satz, wenn im Zeitraum nur einer galt. Sonst <c>null</c>.</summary>
    public decimal? EuroPerKwh { get; init; }

    public decimal Amount { get; init; }

    public ChargingStatementStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;

    /// <summary>Offen, solange eingereicht und nicht erstattet.</summary>
    public decimal OpenAmount { get; init; }

    public DateTime? SubmittedAt { get; init; }
    public DateTime? PaidAt { get; init; }

    public int? TransactionId { get; init; }
    public string? TransactionLabel { get; init; }

    public string? VehicleName { get; init; }
    public string? VehiclePlate { get; init; }
    public string? Note { get; init; }

    /// <summary>Ob sie sich zurücknehmen lässt — nur die jüngste kann das.</summary>
    /// <remarks>
    /// Jede Abrechnung setzt auf dem Endstand der vorigen auf. Eine aus der Mitte zu entfernen
    /// hinterließe eine Lücke zwischen zwei Ständen, die niemandem auffällt — bis die
    /// Jahressumme nicht mehr aufgeht.
    /// </remarks>
    public bool CanDelete { get; init; }
}

/// <summary>Was eine nächste Abrechnung umfassen würde — oder warum keine möglich ist.</summary>
/// <remarks>
/// Steht als Vorschau an der Schaltfläche. Wer erst nach dem Drücken erfährt, dass nichts
/// abzurechnen ist, hat einen Knopf gedrückt, der nichts tut.
/// </remarks>
public sealed record ChargingNextStatementDto
{
    /// <summary>Ob sich daraus eine Abrechnung bilden lässt.</summary>
    public bool Possible { get; init; }

    /// <summary>Warum nicht, in einem Satz. Leer, wenn es geht.</summary>
    public string? Obstacle { get; init; }

    public DateOnly? FromOn { get; init; }
    public DateOnly? ToOn { get; init; }
    public decimal? Kwh { get; init; }
    public decimal? Amount { get; init; }
}

/// <summary>Der ganze Ladestrom eines Arbeitsverhältnisses, wie die Seite ihn braucht.</summary>
public sealed record ChargingOverviewDto
{
    public int? EmploymentId { get; init; }
    public string? Employer { get; init; }

    public IReadOnlyList<ChargingReadingDto> Readings { get; init; } = [];
    public IReadOnlyList<ChargingRateDto> Rates { get; init; } = [];

    /// <summary>Summe der Verbräuche aller Zeiträume, die einen haben.</summary>
    public decimal TotalKwh { get; init; }

    /// <summary>Summe der Beträge aller Zeiträume, die einen haben.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Die Abrechnungen, jüngste zuerst.</summary>
    public IReadOnlyList<ChargingStatementDto> Statements { get; init; } = [];

    /// <summary>Was die nächste Abrechnung umfassen würde.</summary>
    public ChargingNextStatementDto Next { get; init; } = new();

    /// <summary>Summe dessen, was eingereicht und noch nicht erstattet ist.</summary>
    public decimal OpenAmount { get; init; }

    /// <summary>
    /// Zeiträume, für die kein Satz vereinbart ist.
    /// </summary>
    /// <remarks>
    /// Sie fehlen in <see cref="TotalAmount"/>, und das muss dastehen: Eine Summe, der etwas
    /// fehlt, ohne dass sie es sagt, ist schlimmer als keine.
    /// </remarks>
    public int PeriodsWithoutRate { get; init; }
}

/// <summary>Eine neue Ablesung.</summary>
public sealed record CreateChargingReadingRequest
{
    public int? EmploymentId { get; init; }
    public int? VehicleId { get; init; }
    public DateOnly ReadOn { get; init; }
    public decimal MeterKwh { get; init; }
    public int? DocumentId { get; init; }
    public string? Note { get; init; }
}

/// <summary>Eine Abrechnung bis zur jüngsten Ablesung.</summary>
public sealed record CreateChargingStatementRequest
{
    public int? EmploymentId { get; init; }
    public string? Note { get; init; }
}

/// <summary>Die nächste Station der Abrechnung.</summary>
public sealed record AdvanceChargingStatementRequest
{
    public ChargingStatementStatus Status { get; init; }
}

/// <summary>Ein neuer oder geänderter Erstattungssatz.</summary>
public sealed record CreateChargingRateRequest
{
    public int? EmploymentId { get; init; }
    public DateOnly ValidFrom { get; init; }
    public decimal EuroPerKwh { get; init; }
    public string? Note { get; init; }
}
