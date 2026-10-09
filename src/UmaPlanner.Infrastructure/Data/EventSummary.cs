using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UmaPlanner.Infrastructure.Data;

public sealed class EventSummary
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

public sealed class EventSummaryDocument
{
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("data")]
    public required EventSummary Data { get; init; }
}

internal static class EventSummarySerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static (EventSummaryDocument Document, string Json) Create(EventSummary summary)
    {
        var dataJson = JsonSerializer.Serialize(summary, Options);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataJson))).ToLowerInvariant();
        var document = new EventSummaryDocument { Sha256 = sha, Data = summary };
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
