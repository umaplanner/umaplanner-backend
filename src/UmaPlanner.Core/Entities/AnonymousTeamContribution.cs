namespace UmaPlanner.Core.Entities;

public sealed class AnonymousTeamContribution
{
    public string AnonymousId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string TeamData { get; set; } = "{}";
    public string BuildsData { get; set; } = "[]";
    public long LastUpdate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
