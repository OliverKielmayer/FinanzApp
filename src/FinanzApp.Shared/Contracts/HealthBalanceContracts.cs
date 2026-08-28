namespace FinanzApp.Shared.Contracts;

/// <summary>
/// Die drei Teile einer Rechnungssumme.
/// </summary>
/// <remarks>
/// <b>Anspruch ist nicht Auszahlung.</b> „Ausgezahlt“ meint Geld, das eingegangen ist,
/// „erwartet“ den offenen Anspruch. Ein Balken ohne eigenes Segment für „erwartet“ behauptet
/// Zahlungen, die nicht stattgefunden haben — darum stehen die drei getrennt im Vertrag und
/// werden nirgends zu zweien zusammengefasst.
/// </remarks>
public sealed record HealthSplitDto
{
    /// <summary>Tatsächlich eingegangene Erstattungen.</summary>
    public required decimal Paid { get; init; }

    /// <summary>Noch offener Anspruch.</summary>
    public required decimal Expected { get; init; }

    /// <summary>Eigenanteil — die einzige der drei Größen, die eine Ausgabe ist.</summary>
    public required decimal OwnShare { get; init; }

    /// <summary>Die Rechnungssumme, aus denselben ungerundeten Teilen.</summary>
    public decimal Total => Paid + Expected + OwnShare;
}

/// <summary>Ein Leistungserbringer mit seiner Bilanz.</summary>
public sealed record HealthProviderDto
{
    public required string Provider { get; init; }
    public required int BillCount { get; init; }
    public required HealthSplitDto Split { get; init; }

    /// <summary>Anteil der Auszahlung am Anspruch. <c>null</c>, wenn es keinen gab.</summary>
    public required decimal? PaidSharePercent { get; init; }
}

/// <summary>Ein noch nicht abgeschlossener Vorgang.</summary>
public sealed record HealthOpenBillDto
{
    public required int Id { get; init; }
    public required string Provider { get; init; }
    public required DateOnly BillDate { get; init; }
    public required decimal Expected { get; init; }

    /// <summary>Wann eingereicht. <c>null</c> heißt: noch nicht eingereicht.</summary>
    public required DateOnly? SubmittedOn { get; init; }

    /// <summary>Tage seit der Einreichung. <c>null</c>, solange nicht eingereicht.</summary>
    public required int? WaitingDays { get; init; }

    /// <summary>
    /// Ob dieser Vorgang länger wartet als der eigene Schnitt.
    /// </summary>
    /// <remarks>
    /// Gemessen am eigenen Durchschnitt, nicht an einer erfundenen Frist: was „lange“ ist,
    /// weiß nur diese Versicherung, und sie sagt es durch ihre abgeschlossenen Fälle.
    /// </remarks>
    public required bool AboveAverage { get; init; }
}

/// <summary>Ein Jahr im Filter mit seinen Zahlen.</summary>
public sealed record HealthYearDto(int? Year, int BillCount, decimal Total);

/// <summary>
/// Die PKV-Bilanz — v5-Handoff, Abschnitt 12.
/// </summary>
/// <remarks>
/// Zeitraum ist das <b>Kalenderjahr</b>, nicht der Berichtsrahmen der übrigen Auswertungen:
/// Eigenanteile und Beiträge zählen steuerlich jahresweise.
/// </remarks>
public sealed record HealthBalanceDto
{
    public required int? Year { get; init; }
    public required IReadOnlyList<HealthYearDto> Years { get; init; }

    public required HealthSplitDto Split { get; init; }
    public required int BillCount { get; init; }
    public required int CompletedCount { get; init; }

    /// <summary>Anteil des Eigenanteils an der Rechnungssumme.</summary>
    public required decimal? OwnSharePercent { get; init; }

    /// <summary>Anteil der Auszahlung am Anspruch — „ausgezahlt“ meint Geld, das da ist.</summary>
    public required decimal? PaidSharePercent { get; init; }

    /// <summary>Gesamter Anspruch: ausgezahlt plus noch erwartet.</summary>
    public decimal Claim => Split.Paid + Split.Expected;

    /// <summary>
    /// Durchschnittliche Bearbeitungsdauer in Tagen. <c>null</c> ohne abgeschlossenen Vorgang.
    /// </summary>
    /// <remarks>
    /// Gerechnet aus Einreich- und Zahldatum, nicht gesetzt. Derselbe Wert entscheidet, welcher
    /// offene Vorgang „über dem Schnitt“ wartet.
    /// </remarks>
    public required decimal? AverageDays { get; init; }

    /// <summary>
    /// Jahresbeitrag der privaten Krankenversicherung.
    /// </summary>
    /// <remarks>
    /// Getrennt ausgewiesen: der Beitrag ist Absicherung, keine Behandlungskosten. In eine
    /// Summe mit den Eigenanteilen geworfen wäre er beides und keines. <c>null</c> im Blick über
    /// alle Jahre — ein Jahresbeitrag hat dort keinen Bezug.
    /// </remarks>
    public required decimal? YearlyPremium { get; init; }

    public required IReadOnlyList<HealthProviderDto> Providers { get; init; }
    public required IReadOnlyList<HealthOpenBillDto> OpenBills { get; init; }

    /// <summary>
    /// Was sich steuerlich ansetzen ließe: Eigenanteile plus Beiträge.
    /// </summary>
    /// <remarks>
    /// „Potenziell“ ist kein Füllwort. Ob und wie viel davon zählt, entscheidet die
    /// Steuererklärung — die Anwendung nennt die Summe, nicht das Ergebnis. <c>null</c> ohne
    /// Jahresbezug: Eigenanteile mehrerer Jahre plus ein Jahresbeitrag ergäben nichts.
    /// </remarks>
    public decimal? Deductible => YearlyPremium is { } beitrag ? Split.OwnShare + beitrag : null;
}
