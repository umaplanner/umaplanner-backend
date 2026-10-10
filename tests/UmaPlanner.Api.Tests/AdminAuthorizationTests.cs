using Microsoft.Extensions.Configuration;
using UmaPlanner.Api.Endpoints;
using UmaPlanner.Core.Entities;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AdminAuthorizationTests
{
    [Fact]
    public void Admin_discord_ids_are_matched_exactly_after_trimming_configuration()
    {
        var user = new UserOptions { DiscordId = "admin-1" };
        var configuration = CreateConfiguration(" admin-1 , admin-2 ");

        Assert.True(AdminAuthorization.IsAdmin(user, configuration));
        Assert.False(AdminAuthorization.IsAdmin(
        new UserOptions { DiscordId = "not-admin-1" },
        configuration));
    }

    [Fact]
    public void Admin_access_is_based_only_on_configured_discord_ids()
    {
        var user = new UserOptions { DiscordId = "admin-1" };
        var configuration = CreateConfiguration("admin-1");

        Assert.True(AdminAuthorization.IsAdmin(user, configuration));
    }

    [Fact]
    public void No_configured_admin_does_not_grant_admin_access()
    {
        var regularUser = new UserOptions { DiscordId = "admin-1" };
        var configuration = CreateConfiguration(" ");

        Assert.False(AdminAuthorization.IsAdmin(regularUser, configuration));
    }

    private static IConfiguration CreateConfiguration(string adminIds) =>
        new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:AdminDiscordIds"] = adminIds
        })
        .Build();
}
