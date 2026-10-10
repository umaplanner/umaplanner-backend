using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UmaPlanner.Api.Endpoints;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Infrastructure.Data.Admin;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AdminEndpointRoutingTests
{
    [Fact]
    public async Task Event_stats_and_general_stats_accept_the_same_admin_session()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:AdminDiscordIds"] = "admin-discord"
        });
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();

        await using var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new UserOptions
            {
                Id = "admin-user",
                DiscordId = "admin-discord",
                Username = "admin",
                CreatedAt = "2026-10-10T00:00:00Z"
            });
            db.DailyStats.AddRange(
                new DailyStat
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    Event = string.Empty,
                    UserCount = 1
                },
                new DailyStat
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    Event = "CM 1",
                    UserCount = 1,
                    BuildCount = 1,
                    TeamCount = 1
                });
            await db.SaveChangesAsync();
        }

        app.UseSession();
        app.Use(async (context, next) =>
        {
            context.Session.SetString("authenticated_user_id", "admin-user");
            await next(context);
        });
        app.MapAdminEndpoints();
        await app.StartAsync();

        try
        {
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var generalResponse = await client.GetAsync("/admin/stats?compareTo=3");
            var eventResponse = await client.GetAsync("/admin/stats/events?compareTo=3");

            Assert.Equal(System.Net.HttpStatusCode.OK, generalResponse.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, eventResponse.StatusCode);
            using var generalJson = System.Text.Json.JsonDocument.Parse(
                await generalResponse.Content.ReadAsStringAsync());
            Assert.Equal(
                new[] { "buildsPercentChange", "teamsPercentChange", "usersPercentChange" },
                generalJson.RootElement
                    .GetProperty("comparison")
                    .EnumerateObject()
                    .Select(property => property.Name)
                    .Order()
                    .ToArray());
            Assert.All(
                generalJson.RootElement.GetProperty("comparison").EnumerateObject(),
                property => Assert.Equal(
                    System.Text.Json.JsonValueKind.Null,
                    property.Value.ValueKind));

            using var eventJson = System.Text.Json.JsonDocument.Parse(
                await eventResponse.Content.ReadAsStringAsync());
            var eventComparison = eventJson.RootElement
                .GetProperty("comparison")
                .GetProperty("CM 1");
            Assert.Equal(
                new[] { "buildsPercentChange", "teamsPercentChange", "usersPercentChange" },
                eventComparison.EnumerateObject()
                    .Select(property => property.Name)
                    .Order()
                    .ToArray());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Event_stats_can_filter_to_one_saved_event()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:AdminDiscordIds"] = "admin-discord"
        });
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new UserOptions
            {
                Id = "admin-user",
                DiscordId = "admin-discord",
                Username = "admin",
                CreatedAt = "2026-10-10T00:00:00Z"
            });
            db.DailyStats.AddRange(
                new DailyStat { Date = today, Event = string.Empty },
                new DailyStat { Date = today.AddDays(-3), Event = string.Empty },
                new DailyStat { Date = today, Event = "CM 1", UserCount = 2 },
                new DailyStat { Date = today, Event = "LoH 1", UserCount = 4 },
                new DailyStat { Date = today.AddDays(-3), Event = "CM 1", UserCount = 1 },
                new DailyStat { Date = today.AddDays(-3), Event = "LoH 1", UserCount = 3 });
            await db.SaveChangesAsync();
        }

        app.UseSession();
        app.Use(async (context, next) =>
        {
            context.Session.SetString("authenticated_user_id", "admin-user");
            await next(context);
        });
        app.MapAdminEndpoints();
        await app.StartAsync();

        try
        {
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            var response = await client.GetAsync("/admin/stats/events?event=CM%201&compareTo=3");

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            var events = json.RootElement.GetProperty("events");
            Assert.Equal("CM 1", Assert.Single(events.EnumerateObject()).Name);
            Assert.Equal(
                2,
                events.GetProperty("CM 1")[0].GetProperty("users").GetInt64());
            var comparison = json.RootElement.GetProperty("comparison");
            Assert.Equal(
                new[] { "buildsPercentChange", "teamsPercentChange", "usersPercentChange" },
                comparison.EnumerateObject()
                    .Select(property => property.Name)
                    .Order()
                    .ToArray());
            Assert.Equal(100, comparison.GetProperty("usersPercentChange").GetDouble());
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
