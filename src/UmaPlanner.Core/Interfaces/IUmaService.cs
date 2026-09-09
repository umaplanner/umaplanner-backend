using UmaPlanner.Core.Entities;

namespace UmaPlanner.Core.Interfaces;

public interface IUmaService
{
    Task<IReadOnlyList<BaseUma>> GetBaseAsync();
    Task<IReadOnlyList<VariantUma>> GetVariantsAsync();
}
