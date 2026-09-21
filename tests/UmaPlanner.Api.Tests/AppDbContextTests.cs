using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AppDbContextTests
{
    [Fact]
    public async Task Users_can_be_saved_and_loaded()
    {
        using var db = CreateContext();
        db.Users.Add(new UserOptions
        {
            Id = "user-1",
            DiscordId = "discord-1",
            Username = "trainer",
            CreatedAt = "2026-09-21T00:00:00Z"
        });

        await db.SaveChangesAsync();

        var user = await db.Users.SingleAsync();

        Assert.Equal("user-1", user.Id);
        Assert.Equal("discord-1", user.DiscordId);
        Assert.Equal("trainer", user.Username);
    }

    [Fact]
    public async Task DiscordId_must_be_unique()
    {
        await using var db = CreateContext();
        var index = db.Model
            .FindEntityType(typeof(UserOptions))!
            .GetIndexes()
            .Single(index => index.Properties.Single().Name == nameof(UserOptions.DiscordId));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Users_are_mapped_to_the_users_table()
    {
        using var db = CreateContext();
        var userEntity = db.Model.FindEntityType(typeof(UserOptions))!;

        Assert.Equal("users", userEntity.GetTableName());
        Assert.Equal(nameof(UserOptions.Id), userEntity.FindProperty(nameof(UserOptions.Id))!.Name);
        Assert.Equal(nameof(UserOptions.DiscordId), userEntity.FindProperty(nameof(UserOptions.DiscordId))!.Name);
        Assert.Equal(nameof(UserOptions.Username), userEntity.FindProperty(nameof(UserOptions.Username))!.Name);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
