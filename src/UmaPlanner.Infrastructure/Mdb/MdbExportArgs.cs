namespace UmaPlanner.Infrastructure.Mdb;

public sealed class MdbExportArgs
{
    public string DatabasePath { get; init; } = "";
    public string OutputPath { get; init; } = "data";
    public string Region { get; init; } = "global";
    public bool CopyMdb { get; init; }
}
