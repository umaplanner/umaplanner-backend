using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class RaceEventCacheTests
{
    [Fact]
    public async Task GetAllAsync_returns_a_copy_of_the_cached_snapshot()
    {
        var cache = new RaceEventCache();
        var original = new List<UmaRaceEvent>
        {
            new() { EventTitle = "CM 1", Name = "Champion Meet" }
        };

        await cache.UpdateAsync(original);
        var result = await cache.GetAllAsync();
        result[0].EventTitle = "Changed";
        result.Add(new UmaRaceEvent { EventTitle = "CM 2" });

        var secondRead = await cache.GetAllAsync();

        Assert.Single(secondRead);
        Assert.Equal("Changed", secondRead[0].EventTitle);
        Assert.NotSame(result, secondRead);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_complete_snapshot()
    {
        var cache = new RaceEventCache();

        await cache.UpdateAsync(
        [
            new UmaRaceEvent { EventTitle = "CM 1" },
            new UmaRaceEvent { EventTitle = "CM 2" }
        ]);
        await cache.UpdateAsync([new UmaRaceEvent { EventTitle = "LoH 1" }]);

        var result = await cache.GetAllAsync();

        var eventItem = Assert.Single(result);
        Assert.Equal("LoH 1", eventItem.EventTitle);
    }

    [Fact]
    public async Task UpdateAsync_null_replaces_snapshot_with_empty_list()
    {
        var cache = new RaceEventCache();

        await cache.UpdateAsync([new UmaRaceEvent { EventTitle = "CM 1" }]);
        await cache.UpdateAsync(null!);

        Assert.Empty(await cache.GetAllAsync());
    }
}
