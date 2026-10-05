using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
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
    IAmazonS3 storage,
    R2StorageOptions options,
    ILogger<EventSummaryService> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await SummarizeAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var nextHour = new DateTimeOffset(
                now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(1);
            await Task.Delay(nextHour - now, stoppingToken);

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
                        Add(summary.RunningStyles, data.RunningStyle);
                        foreach (var skill in data.Skills)
                            Add(summary.Skills, skill);
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

            await WriteIfChangedAsync(eventTeams.Key, summary, cancellationToken);
        }
    }

    private async Task WriteIfChangedAsync(
        string eventName,
        EventSummary summary,
        CancellationToken cancellationToken)
    {
        var (document, json) = EventSummarySerializer.Create(summary);
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
        data = new BuildData(outfit, strategy, skillValues, cards);
        return true;
    }

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
        IReadOnlyList<string> SupportCards);
}
