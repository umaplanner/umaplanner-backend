namespace UmaPlanner.Core.Entities;

public class UserOptions
{
    public string Id { get; set; } = string.Empty;
    public string DiscordId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? TrainerId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
