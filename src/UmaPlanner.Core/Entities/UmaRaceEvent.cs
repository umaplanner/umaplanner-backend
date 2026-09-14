namespace UmaPlanner.Core.Entities;

public class UmaRaceEvent
{
    public string? EventTitle { get; set; }
    public string? Name { get; set; }
    public string? DistanceType { get; set; }
    public string? Racecourse { get; set; }
    public string? Distance { get; set; }
    public string? GroundType { get; set; }
    public string? GroundCondition { get; set; }
    public string? Weather { get; set; }
    public string? Direction { get; set; }
    public string? Season { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public bool IsConfirmed { get; set; }
}
