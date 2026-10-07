namespace UmaPlanner.Core.Entities;

public sealed class UserResult
{
    public string UserId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Data { get; set; } = "{}";
}
