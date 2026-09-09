namespace UmaPlanner.Core.Entities;

public sealed class VariantUma
{
    public int Id { get; init; }
    public int CharaId { get; init; }
    public int VariantNumber { get; init; }
    public string? Name { get; init; }
    public string? OutfitTitle { get; init; }
    public string? BaseCharacterName { get; init; }
    public bool BaseCharacterExistsInCharaData { get; init; }
}
