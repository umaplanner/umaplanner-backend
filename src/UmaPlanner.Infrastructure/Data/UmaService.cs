using UmaPlanner.Core.Entities;
using UmaPlanner.Core.Interfaces;
using UmaPlanner.Infrastructure.Mdb;

namespace UmaPlanner.Infrastructure.Data;

public sealed class UmaService : IUmaService
{
    private readonly string _mdbPath;

    public UmaService(string mdbPath)
    {
        _mdbPath = mdbPath;
    }

    public async Task<IReadOnlyList<BaseUma>> GetBaseAsync()
    {
        var (baseUmas, _) = await MdbReader.ReadAllUmasAsync(_mdbPath);
        return baseUmas;
    }

    public async Task<IReadOnlyList<VariantUma>> GetVariantsAsync()
    {
        var (_, variants) = await MdbReader.ReadAllUmasAsync(_mdbPath);
        return variants;
    }
}
