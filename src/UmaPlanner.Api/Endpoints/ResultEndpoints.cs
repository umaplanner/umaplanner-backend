using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class ResultEndpoints
{
    public static void MapResultEndpoints(this WebApplication app)
    {
        app.MapGet("/results", async (
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var results = await db.Results
                .AsNoTracking()
                .Where(result => result.UserId == userId)
                .OrderBy(result => result.Event)
                .ToListAsync(cancellationToken);

            var response = results.Select(result => new ResultResponse(
                result.Event,
                JsonDocument.Parse(result.Data).RootElement.Clone()));

            return Results.Ok(response);
        });

        app.MapGet("/results/{eventName}", async (
            [FromRoute] string eventName,
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var result = await db.Results
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entry => entry.UserId == userId && entry.Event == eventName.Trim(),
                    cancellationToken);
            if (result is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ResultResponse(
                result.Event,
                JsonDocument.Parse(result.Data).RootElement.Clone()));
        });

        app.MapPost("/results", async (
            IReadOnlyList<ResultRequest>? requests,
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            if (requests is null)
            {
                return Results.BadRequest(new { message = "A JSON array of results is required." });
            }

            var normalizedRequests = new List<(string Event, string Data, long LastUpdate)>();
            foreach (var request in requests)
            {
                if (request is null ||
                    string.IsNullOrWhiteSpace(request.Event) ||
                    request.Data.ValueKind != JsonValueKind.Object ||
                    !TryGetLastUpdate(request.Data, out var lastUpdate))
                {
                    return Results.BadRequest(new
                    {
                        message = "Each result requires an event, an object-valued data property, and a non-negative numeric lastUpdate."
                    });
                }

                normalizedRequests.Add((
                    request.Event.Trim(),
                    request.Data.GetRawText(),
                    lastUpdate));
            }

            if (!await db.Users.AnyAsync(user => user.Id == userId, cancellationToken))
            {
                return Results.Unauthorized();
            }

            var uniqueRequests = normalizedRequests
                .GroupBy(request => request.Event)
                .Select(group => group.MaxBy(request => request.LastUpdate)!)
                .ToArray();
            var events = uniqueRequests.Select(request => request.Event).ToArray();
            var existingResults = await db.Results
                .Where(result => result.UserId == userId && events.Contains(result.Event))
                .ToDictionaryAsync(result => result.Event, cancellationToken);

            foreach (var request in uniqueRequests)
            {
                if (existingResults.TryGetValue(request.Event, out var result))
                {
                    if (request.LastUpdate >= GetLastUpdate(result.Data) &&
                        !JsonNode.DeepEquals(
                            JsonNode.Parse(result.Data),
                            JsonNode.Parse(request.Data)))
                    {
                        result.Data = request.Data;
                    }
                }
                else
                {
                    db.Results.Add(new UserResult
                    {
                        UserId = userId,
                        Event = request.Event,
                        Data = request.Data
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { saved = uniqueRequests.Length });
        });
    }

    public sealed record ResultRequest(string? Event, JsonElement Data);

    public sealed record ResultResponse(string Event, JsonElement Data);

    private static bool TryGetLastUpdate(JsonElement data, out long lastUpdate)
    {
        lastUpdate = 0;
        return data.TryGetProperty("lastUpdate", out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out lastUpdate) &&
               lastUpdate >= 0;
    }

    private static long GetLastUpdate(string data)
    {
        using var document = JsonDocument.Parse(data);
        return TryGetLastUpdate(document.RootElement, out var lastUpdate)
            ? lastUpdate
            : 0;
    }
}
