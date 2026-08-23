namespace FinanzApp.Shared.Contracts;

public sealed record CategoryDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required CategoryDirection Direction { get; init; }
}

public sealed record CategorizationRuleDto
{
    public required int Id { get; init; }

    /// <summary>Präfix des Empfängers, auf das die Regel greift.</summary>
    public required string PayeePattern { get; init; }

    public required int CategoryId { get; init; }
    public required string CategoryName { get; init; }
}
