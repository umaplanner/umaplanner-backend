using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UmaPlanner.Api.Endpoints;
using UmaPlanner.Infrastructure.Data;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AnonymousTeamEndpointTests
{
    [Fact]
    public async Task Anonymous_teams_are_saved_updated_and_removed_without_authentication()
    {
        var databaseName = Guid.NewGuid().ToString();
        var anonymousId = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

        await using var app = builder.Build();
        app.MapAnonymousTeamEndpoints();
        await app.StartAsync();

        try
        {
            using var client = CreateClient(app);
            var firstResponse = await client.PostAsJsonAsync(
                "/anonymous/teams",
                CreateRequest(anonymousId, "CM 42", "build-1", 1, "uma-1"));
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var saved = await db.AnonymousTeamContributions.SingleAsync();
                Assert.Equal(anonymousId, saved.AnonymousId);
                Assert.Equal("CM 42", saved.Event);
                Assert.Equal(1, saved.LastUpdate);
                Assert.Contains("uma-1", saved.BuildsData);
                Assert.DoesNotContain("Local-only name", saved.BuildsData);
            }

            var updateResponse = await client.PostAsJsonAsync(
                "/anonymous/teams",
                CreateRequest(anonymousId, "CM 42", "build-2", 2, "uma-2"));
            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var saved = await db.AnonymousTeamContributions.SingleAsync();
                Assert.Equal(2, saved.LastUpdate);
                Assert.Contains("uma-2", saved.BuildsData);
                Assert.DoesNotContain("uma-1", saved.BuildsData);
            }

            var staleDeleteResponse = await client.PostAsJsonAsync(
                "/anonymous/teams",
                new AnonymousTeamEndpoints.AnonymousTeamRequest(
                    anonymousId,
                    "CM 42",
                    new AnonymousTeamEndpoints.AnonymousEventTeam(null, null, null, 1),
                    Array.Empty<AnonymousTeamEndpoints.AnonymousUmaBuild>()));
            Assert.Equal(HttpStatusCode.OK, staleDeleteResponse.StatusCode);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var unchanged = await db.AnonymousTeamContributions.SingleAsync();
                Assert.Equal(2, unchanged.LastUpdate);
            }

            var deleteResponse = await client.PostAsJsonAsync(
                "/anonymous/teams",
                new AnonymousTeamEndpoints.AnonymousTeamRequest(
                    anonymousId,
                    "CM 42",
                    new AnonymousTeamEndpoints.AnonymousEventTeam(null, null, null, 3),
                    Array.Empty<AnonymousTeamEndpoints.AnonymousUmaBuild>()));
            Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            await using var finalScope = app.Services.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cleared = await finalDb.AnonymousTeamContributions.SingleAsync();
            Assert.Equal(3, cleared.LastUpdate);
            Assert.Equal("[]", cleared.BuildsData);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Anonymous_team_rejects_builds_that_do_not_match_the_team()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

        await using var app = builder.Build();
        app.MapAnonymousTeamEndpoints();
        await app.StartAsync();

        try
        {
            using var client = CreateClient(app);
            var response = await client.PostAsJsonAsync(
                "/anonymous/teams",
                CreateRequest(Guid.NewGuid().ToString(), "CM 42", "build-1", 1, "uma-1") with
                {
                    Team = new AnonymousTeamEndpoints.AnonymousEventTeam(
                        "different-build",
                        null,
                        null,
                        1)
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.AnonymousTeamContributions.ToListAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static AnonymousTeamEndpoints.AnonymousTeamRequest CreateRequest(
        string anonymousId,
        string eventName,
        string buildId,
        long lastUpdate,
        string outfitId) =>
        new(
            anonymousId,
            eventName,
            new AnonymousTeamEndpoints.AnonymousEventTeam(
                buildId,
                null,
                null,
                lastUpdate),
            new[]
            {
                new AnonymousTeamEndpoints.AnonymousUmaBuild(
                    buildId,
                    "standard",
                    outfitId,
                    "Senkou",
                    1200,
                    800,
                    900,
                    400,
                    500,
                    new[] { "skill-1" },
                    Array.Empty<AnonymousTeamEndpoints.AnonymousSupportCard>())
            });
}
