using System.Net.Http.Json;
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

public sealed class ResultEndpointStatsTests
{
    [Fact]
    public async Task Saving_results_refreshes_current_admin_ticket_counts()
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
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
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
            await db.SaveChangesAsync();
        }

        app.UseSession();
        app.Use(async (context, next) =>
        {
            context.Session.SetString("authenticated_user_id", "admin-user");
            await next(context);
        });
        app.MapResultEndpoints();
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

            var saveResponse = await client.PostAsJsonAsync(
                "/results",
                new[]
                {
                    new
                    {
                        @event = "CM 1",
                        data = new
                        {
                            lastUpdate = 1,
                            round1Day1 = new Dictionary<string, object>
                            {
                                ["1"] = new
                                {
                                builds = new[]
                                {
                                    new { buildId = "a", wins = 2 },
                                    new { buildId = "b", wins = 1 },
                                    new { buildId = "c", wins = 1 }
                                }
                                },
                                ["2"] = new
                                {
                                builds = new[]
                                {
                                    new { buildId = "d", wins = 3 },
                                    new { buildId = "e", wins = 1 }
                                }
                                }
                            }
                        }
                    }
                });

            Assert.Equal(System.Net.HttpStatusCode.OK, saveResponse.StatusCode);

            var updateResponse = await client.PostAsJsonAsync(
                "/results",
                new[]
                {
                    new
                    {
                        @event = "CM 1",
                        data = new
                        {
                            lastUpdate = 2,
                            round1Day1 = new Dictionary<string, object>
                            {
                                ["1"] = new { builds = Array.Empty<object>() },
                                ["2"] = new { builds = Array.Empty<object>() },
                                ["3"] = new { builds = Array.Empty<object>() }
                            }
                        }
                    }
                });
            Assert.Equal(System.Net.HttpStatusCode.OK, updateResponse.StatusCode);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                Assert.Equal(1, await db.Results.LongCountAsync(
                    result => result.UserId == "admin-user" && result.Event == "CM 1"));
                var eventStat = await db.DailyStats.SingleAsync(
                    stat => stat.Date == today && stat.Event == "CM 1");
                eventStat.ResultCount = 0;
                eventStat.TicketCount = 0;
                await db.SaveChangesAsync();
            }

            var statsResponse = await client.GetAsync("/admin/stats/events?event=CM%201");
            Assert.Equal(System.Net.HttpStatusCode.OK, statsResponse.StatusCode);
            using var statsJson = System.Text.Json.JsonDocument.Parse(
                await statsResponse.Content.ReadAsStringAsync());
            var dailyStat = statsJson.RootElement
                .GetProperty("events")
                .GetProperty("CM 1")[0];

            Assert.Equal(1, dailyStat.GetProperty("results").GetInt64());
            Assert.Equal(3, dailyStat.GetProperty("tickets").GetInt64());
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
