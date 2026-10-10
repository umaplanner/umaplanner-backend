namespace UmaPlanner.Infrastructure.Data.Admin;

public sealed class DailyStat
{
    public DateOnly Date { get; set; }
    public string Event { get; set; } = string.Empty;
    public long UserCount { get; set; }
    public long BuildCount { get; set; }
    public long TeamCount { get; set; }
}
