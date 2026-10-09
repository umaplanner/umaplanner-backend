using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public sealed class R2StorageOptions
{
    public string AccountId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "auto";
}

public sealed class EventSummaryService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<EventSummaryService> logger,
    IAmazonS3? storage = null,
    R2StorageOptions? options = null) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = GetInterval();
        await SummarizeAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = GetInterval();
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);

            try
            {
                await SummarizeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to summarize event data.");
            }
        }
    }

    private TimeSpan GetInterval()
    {
        var intervalMinutes = configuration.GetValue("EventSummary:IntervalMinutes", 10);
        if (intervalMinutes <= 0)
        {
            throw new InvalidOperationException(
                "EventSummary:IntervalMinutes must be greater than zero.");
        }

        return TimeSpan.FromMinutes(intervalMinutes);
    }

    public async Task SummarizeAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teams = await db.Teams.AsNoTracking().ToListAsync(cancellationToken);
        var builds = await db.UmaBuilds
            .AsNoTracking()
            .Where(build => build.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var buildsByKey = builds.ToDictionary(build => (build.UserId, build.Event, build.Id));

        foreach (var eventTeams in teams.GroupBy(team => team.Event, StringComparer.Ordinal))
        {
            var summary = new EventSummary
            {
                UserCount = eventTeams
                    .Select(team => team.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            };
            var runningStyles = new SortedDictionary<string, RunningStyleAccumulator>(StringComparer.Ordinal);
            foreach (var team in eventTeams)
            {
                using var teamDocument = Parse(team.Data, "team", team.UserId, team.Event);
                if (teamDocument is null)
                    continue;

                var teamBuilds = new List<BuildData>();
                foreach (var slot in new[] { "uma1", "uma2", "uma3" })
                {
                    if (!teamDocument.RootElement.TryGetProperty(slot, out var id) ||
                        id.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(id.GetString()) ||
                        !buildsByKey.TryGetValue((team.UserId, team.Event, id.GetString()!), out var build))
                        continue;

                    using var buildDocument = Parse(build.Data, "build", team.UserId, team.Event);
                    if (buildDocument is not null && TryReadBuild(buildDocument.RootElement, out var data))
                    {
                        teamBuilds.Add(data);
                        Add(summary.Outfits, data.OutfitId);
                        if (!runningStyles.TryGetValue(data.RunningStyle, out var style))
                        {
                            style = new RunningStyleAccumulator(data.RunningStyle);
                            runningStyles.Add(data.RunningStyle, style);
                        }
                        style.Add(data);
                        if (!data.IsPlan)
                        {
                            foreach (var skill in data.Skills)
                                Add(summary.Skills, skill);
                        }
                        foreach (var card in data.SupportCards)
                            Add(summary.SupportCards, card);
                    }
                }

                if (teamBuilds.Count == 3)
                {
                    var combination = string.Join(",", teamBuilds
                        .Select(build => build.RunningStyle)
                        .Order(StringComparer.Ordinal));
                    Add(summary.RunningStyleCombinations, combination);
                }
            }

            await WriteIfChangedAsync(eventTeams.Key, new EventSummary
            {
                UserCount = summary.UserCount,
                Outfits = summary.Outfits,
                Skills = summary.Skills,
                RunningStyles = runningStyles.Values.Select(style => style.ToSummary()).ToArray(),
                RunningStyleCombinations = summary.RunningStyleCombinations,
                SupportCards = summary.SupportCards
            }, cancellationToken);
        }
    }

    private async Task WriteIfChangedAsync(
        string eventName,
        EventSummary summary,
        CancellationToken cancellationToken)
    {
        var (document, json) = EventSummarySerializer.Create(summary);
        if (environment.IsDevelopment())
        {
            if (!configuration.GetValue("EventSummary:WriteLocalJson", true))
            {
                logger.LogDebug("Local event summary output is disabled.");
                return;
            }

            var repositoryRoot = FindRepositoryRoot(environment.ContentRootPath);
            var localPath = Path.Combine(
                repositoryRoot,
                $"event-summary-{Uri.EscapeDataString(eventName)}.json");
            string? existingLocalSha = null;
            if (File.Exists(localPath))
            {
                existingLocalSha = EventSummarySerializer.ReadSha256(
                    await File.ReadAllTextAsync(localPath, cancellationToken));
            }

            if (string.Equals(existingLocalSha, document.Sha256, StringComparison.Ordinal))
            {
                logger.LogDebug("Event summary unchanged for {Event}.", eventName);
                return;
            }

            await File.WriteAllTextAsync(localPath, json, cancellationToken);
            logger.LogInformation("Wrote event summary for {Event} to {Path}.", eventName, localPath);
            return;
        }

        if (storage is null || options is null)
            throw new InvalidOperationException("R2 storage is required outside Development.");

        var key = $"data/overview/{eventName}.json";
        string? existingSha = null;
        try
        {
            var response = await storage.GetObjectAsync(new GetObjectRequest
            {
                BucketName = options.BucketName,
                Key = key
            }, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            existingSha = EventSummarySerializer.ReadSha256(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        if (string.Equals(existingSha, document.Sha256, StringComparison.Ordinal))
        {
            logger.LogDebug("Event summary unchanged for {Event}.", eventName);
            return;
        }

        await storage.PutObjectAsync(new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = key,
            ContentType = "application/json",
            InputStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)),
            DisableDefaultChecksumValidation = true,
            DisablePayloadSigning = true
        }, cancellationToken);
        logger.LogInformation("Updated event summary for {Event}.", eventName);
    }

    private JsonDocument? Parse(string data, string kind, string userId, string eventName)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Skipping malformed {Kind} for user {UserId}, event {Event}.",
                kind, userId, eventName);
            return null;
        }
    }

    private static bool TryReadBuild(JsonElement root, out BuildData data)
    {
        data = default!;
        if (!TryString(root, "outfitId", out var outfit) ||
            !TryString(root, "strategy", out var strategy) ||
            !root.TryGetProperty("skills", out var skills) || skills.ValueKind != JsonValueKind.Array)
            return false;
        var isPlan = root.TryGetProperty("build-type", out var buildType) &&
                     buildType.ValueKind == JsonValueKind.String &&
                     string.Equals(buildType.GetString(), "plan", StringComparison.OrdinalIgnoreCase);

        var skillValues = skills.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!)
            .ToArray();
        var cards = root.TryGetProperty("supportCards", out var supportCards) &&
                    supportCards.ValueKind == JsonValueKind.Array
            ? supportCards.EnumerateArray()
                .Where(card => card.ValueKind == JsonValueKind.Object &&
                    card.TryGetProperty("support_card_id", out var id) &&
                    id.ValueKind == JsonValueKind.Number)
                .Select(card => card.GetProperty("support_card_id").GetRawText())
                .ToArray()
            : [];
        var stats = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var stat in new[] { "speed", "stamina", "power", "guts", "wisdom" })
        {
            if (root.TryGetProperty(stat, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var statValue) &&
                double.IsFinite(statValue))
            {
                stats.Add(stat, statValue);
            }
        }

        data = new BuildData(outfit, NormalizeRunningStyle(strategy), skillValues, cards, stats, isPlan);
        return true;
    }

    private static string FindRepositoryRoot(string contentRootPath)
    {
        for (var directory = new DirectoryInfo(contentRootPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UmaPlanner.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find UmaPlanner.slnx above the content root '{contentRootPath}'.");
    }

    private static string NormalizeRunningStyle(string strategy) =>
        strategy.Trim() switch
        {
            "Oonige" or "Runaway" => "Oonige",
            var value => value
        };

    private static bool TryString(JsonElement root, string property, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(property, out var element) ||
            element.ValueKind != JsonValueKind.String)
            return false;

        var candidate = element.GetString();
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        value = candidate;
        return true;
    }

    private static void Add(SortedDictionary<string, int> counts, string value) =>
        counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;

    private sealed record BuildData(
        string OutfitId,
        string RunningStyle,
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> SupportCards,
        IReadOnlyDictionary<string, double> Stats,
        bool IsPlan);

    private sealed class RunningStyleAccumulator(string style)
    {
        private readonly SortedDictionary<string, int> outfits = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> skills = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, int> supportCards = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (double Sum, int Count)> stats = new(StringComparer.Ordinal);
        private int count;

        public void Add(BuildData build)
        {
            count++;
            EventSummaryService.Add(outfits, build.OutfitId);
            foreach (var card in build.SupportCards)
                EventSummaryService.Add(supportCards, card);
            if (build.IsPlan)
                return;

            foreach (var skill in build.Skills)
                EventSummaryService.Add(skills, skill);
            foreach (var (stat, value) in build.Stats)
            {
                var current = stats.TryGetValue(stat, out var aggregate) ? aggregate : (0d, 0);
                stats[stat] = (current.Item1 + value, current.Item2 + 1);
            }
        }

        public RunningStyleSummary ToSummary() => new()
        {
            Style = style,
            Count = count,
            Outfits = outfits,
            Skills = skills,
            SupportCards = supportCards,
            AverageStats = new SortedDictionary<string, double>(
                stats.ToDictionary(pair => pair.Key, pair => pair.Value.Sum / pair.Value.Count),
                StringComparer.Ordinal)
        };
    }
}
