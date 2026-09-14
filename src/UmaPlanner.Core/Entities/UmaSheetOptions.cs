namespace UmaPlanner.Core.Entities;

public class UmaSheetOptions
{
    public string SpreadsheetId { get; set; } = string.Empty;
    public string SheetName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int IntervalHours { get; set; } = 12;
}
