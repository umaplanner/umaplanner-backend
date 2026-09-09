namespace UmaPlanner.Core.Entities;

public sealed class BaseUma
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public IDictionary<string, object?> Raw { get; init; } = new Dictionary<string, object?>();
}
