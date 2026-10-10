using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UmaPlanner.Infrastructure.Data.Event;

public sealed class Summary
{
    public int UserCount { get; init; }
    public SortedDictionary<string, int> Outfits { get; init; } = [];
    public SortedDictionary<string, int> Skills { get; init; } = [];
    public IReadOnlyList<RunningStyleSummary> RunningStyles { get; init; } = [];
    public SortedDictionary<string, int> RunningStyleCombinations { get; init; } = [];
    public SortedDictionary<string, int> SupportCards { get; init; } = [];
}

public sealed class RunningStyleSummary
{
    public required string Style { get; init; }
    public int Count { get; init; }
    public SortedDictionary<string, int> Outfits { get; init; } = [];
    public SortedDictionary<string, int> Skills { get; init; } = [];
    public SortedDictionary<string, int> SupportCards { get; init; } = [];
    public SortedDictionary<string, double> AverageStats { get; init; } = [];
}

public sealed class SummaryDocument
{
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("nextUpdate")]
    public required DateTimeOffset NextUpdate { get; init; }

    [JsonPropertyName("data")]
    public required Summary Data { get; init; }
}

internal static class SummarySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static (SummaryDocument Document, string Json) Create(
        Summary summary,
        DateTimeOffset nextUpdate)
    {
        var hashInput = JsonSerializer.Serialize(new { NextUpdate = nextUpdate, Data = summary }, Options);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
        var document = new SummaryDocument
        {
            Sha256 = sha,
            NextUpdate = nextUpdate,
            Data = summary
        };
        return (document, JsonSerializer.Serialize(document, Options));
    }

    public static string? ReadSha256(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("sha256", out var sha)
                ? sha.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
