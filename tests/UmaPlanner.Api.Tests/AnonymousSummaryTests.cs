using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Infrastructure.Data.Event;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class AnonymousSummaryTests
{
    [Fact]
    public async Task Anonymous_contributions_are_included_in_event_summaries()
    {
        var databaseName = Guid.NewGuid().ToString();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"umaplanner-summary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "UmaPlanner.slnx"), "");

        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            await using var serviceProvider = services.BuildServiceProvider();
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AnonymousTeamContributions.Add(new AnonymousTeamContribution
                {
                    AnonymousId = Guid.NewGuid().ToString(),
                    Event = "CM anonymous only",
                    TeamData = """{"uma1":"build-1","uma2":null,"uma3":null,"lastUpdate":1}""",
                    BuildsData =
                        """
                        [{
                          "id":"build-1",
                          "outfitId":"outfit-1",
                          "strategy":"Senkou",
                          "skills":["skill-1"],
                          "supportCards":[{"supportCardId":1234}],
                          "speed":1200,
                          "stamina":800,
                          "power":900,
                          "guts":400,
                          "wisdom":500
                        }]
                        """,
                    LastUpdate = 1,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EventSummary:IntervalMinutes"] = "10",
                    ["EventSummary:WriteLocalJson"] = "true"
                })
                .Build();
            var environment = new TestHostEnvironment(outputDirectory);
            var service = new SummaryService(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                environment,
                configuration,
                NullLogger<SummaryService>.Instance);

            await service.SummarizeAsync(CancellationToken.None);

            var summaryPath = Path.Combine(
                outputDirectory,
                "event-summary-CM%20anonymous%20only.json");
            using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
            var data = summary.RootElement.GetProperty("data");
            Assert.Equal(1, data.GetProperty("userCount").GetInt32());
            Assert.Equal(1, data.GetProperty("outfits").GetProperty("outfit-1").GetInt32());
            Assert.Equal(1, data.GetProperty("skills").GetProperty("skill-1").GetInt32());
            Assert.Equal(1, data.GetProperty("supportCards").GetProperty("1234").GetInt32());
            var runningStyle = data.GetProperty("runningStyles")[0];
            Assert.Equal("Senkou", runningStyle.GetProperty("style").GetString());
            Assert.Equal(1, runningStyle.GetProperty("count").GetInt32());
            Assert.Equal(
                1200,
                runningStyle.GetProperty("averageStats").GetProperty("speed").GetDouble());
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "UmaPlanner.Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
