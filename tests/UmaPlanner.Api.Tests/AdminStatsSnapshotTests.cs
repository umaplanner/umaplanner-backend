using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Infrastructure.Data.Admin;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AdminStatsSnapshotTests
{
    [Fact]
    public async Task Capture_saves_overall_and_per_event_daily_counts()
    {
        await using var db = CreateContext();
        db.Users.AddRange(
            CreateUser("user-1"),
            CreateUser("user-2"),
            CreateUser("user-3"));
        db.UmaBuilds.AddRange(
            new UserUmaBuild { UserId = "user-1", Event = "CM 1", Id = "build-1" },
            new UserUmaBuild
            {
                UserId = "user-1",
                Event = "CM 1",
                Id = "build-2",
                DeletedAt = DateTimeOffset.UtcNow
            },
            new UserUmaBuild { UserId = "user-2", Event = "CM 2", Id = "build-3" });
        db.Teams.AddRange(
            new UserTeam
            {
                UserId = "user-1",
                Event = "CM 1",
                Data = """{"uma1":"build-1"}"""
            },
            new UserTeam
            {
                UserId = "user-2",
                Event = "CM 1",
                Data = """{"uma2":"build-2"}"""
            },
            new UserTeam
            {
                UserId = "user-3",
                Event = "CM 1",
                Data = """{"uma1":null,"uma2":"","uma3":"  "}"""
            });
        db.Results.AddRange(
            new UserResult
            {
                UserId = "user-1",
                Event = "CM 1",
                Data = """
                    {
                      "lastUpdate":1791716200000,
                      "round1Day1":{
                        "1":{"builds":[{"buildId":"a","wins":2}]},
                        "2":{"builds":[{"buildId":"b","wins":1}]}
                      },
                      "round1Day2":{},
                      "finals":{"placement":2,"builds":[{"buildId":"a","placement":2}]}
                    }
                    """
            },
            new UserResult
            {
                UserId = "user-2",
                Event = "CM 1",
                Data = """{"round2Day3":{"1":{"builds":[{"buildId":"c","wins":4}]}}}"""
            },
            new UserResult
            {
                UserId = "user-2",
                Event = "CM 2",
                Data = """{"round1Day1":{"1":{"builds":[]}}}"""
            });
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 10, 10);
        await StatsSnapshot.CaptureAsync(db, date, CancellationToken.None);

        var stats = await db.DailyStats.AsNoTracking().ToListAsync();
        var general = Assert.Single(stats, stat => stat.Event == string.Empty);
        Assert.Equal(3, general.UserCount);
        Assert.Equal(3, general.BuildCount);
        Assert.Equal(2, general.TeamCount);
        Assert.Equal(3, general.ResultCount);
        Assert.Equal(4, general.TicketCount);

        var cm1 = Assert.Single(stats, stat => stat.Event == "CM 1");
        Assert.Equal(2, cm1.UserCount);
        Assert.Equal(2, cm1.BuildCount);
        Assert.Equal(2, cm1.TeamCount);
        Assert.Equal(2, cm1.ResultCount);
        Assert.Equal(3, cm1.TicketCount);

        var cm2 = Assert.Single(stats, stat => stat.Event == "CM 2");
        Assert.Equal(1, cm2.UserCount);
        Assert.Equal(1, cm2.BuildCount);
        Assert.Equal(0, cm2.TeamCount);
        Assert.Equal(1, cm2.ResultCount);
        Assert.Equal(1, cm2.TicketCount);
    }

    [Fact]
    public async Task Recapturing_a_day_updates_counts_and_keeps_known_events()
    {
        await using var db = CreateContext();
        db.Users.Add(CreateUser("user-1"));
        db.UmaBuilds.Add(new UserUmaBuild
        {
            UserId = "user-1",
            Event = "CM 1",
            Id = "build-1"
        });
        db.Results.Add(new UserResult
        {
            UserId = "user-1",
            Event = "CM 1",
            Data = """{"round1Day1":{"1":{},"2":{}}}"""
        });
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 10, 10);
        await StatsSnapshot.CaptureAsync(db, date, CancellationToken.None);
        db.UmaBuilds.RemoveRange(db.UmaBuilds);
        db.Results.Single().Data = """{"round1Day1":{"1":{}}}""";
        await db.SaveChangesAsync();
        await StatsSnapshot.CaptureAsync(db, date, CancellationToken.None);

        var general = Assert.Single(await db.DailyStats
            .Where(stat => stat.Event == string.Empty)
            .ToListAsync());
        var eventStat = Assert.Single(await db.DailyStats
            .Where(stat => stat.Event == "CM 1")
            .ToListAsync());
        Assert.Equal(0, general.BuildCount);
        Assert.Equal(1, general.ResultCount);
        Assert.Equal(1, general.TicketCount);
        Assert.Equal(1, eventStat.UserCount);
        Assert.Equal(0, eventStat.BuildCount);
        Assert.Equal(1, eventStat.ResultCount);
        Assert.Equal(1, eventStat.TicketCount);
    }

    [Fact]
    public async Task Capture_creates_zero_count_rows_when_there_are_no_datapoints()
    {
        await using var db = CreateContext();

        var date = new DateOnly(2026, 10, 10);
        await StatsSnapshot.CaptureAsync(db, date, CancellationToken.None);

        var general = Assert.Single(await db.DailyStats.ToListAsync());
        Assert.Equal(date, general.Date);
        Assert.Equal(string.Empty, general.Event);
        Assert.Equal(0, general.UserCount);
        Assert.Equal(0, general.BuildCount);
        Assert.Equal(0, general.TeamCount);
        Assert.Equal(0, general.ResultCount);
        Assert.Equal(0, general.TicketCount);
    }

    private static UserOptions CreateUser(string id) =>
        new()
        {
            Id = id,
            DiscordId = $"discord-{id}",
            Username = id,
            CreatedAt = "2026-10-10T00:00:00Z"
        };

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
