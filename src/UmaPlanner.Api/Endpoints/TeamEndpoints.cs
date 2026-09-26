using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this WebApplication app)
    {
        app.MapGet("/teams", async (
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var teams = await db.Teams
                .AsNoTracking()
                .Where(team => team.UserId == userId)
                .OrderBy(team => team.Event)
                .ToListAsync(cancellationToken);

            var response = teams.Select(team =>
                JsonDocument.Parse(team.Data).RootElement.Clone());

            return Results.Ok(response);
        });

        app.MapPost("/teams", async (
            IReadOnlyList<TeamRequest>? requests,
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
                return Results.BadRequest(new { message = "A JSON array of teams is required." });
            }

            var normalizedRequests = new List<(string Event, string Data, long LastUpdate)>();
            foreach (var request in requests)
            {
                if (request is null ||
                    string.IsNullOrWhiteSpace(request.Event) ||
                    request.LastUpdate is null ||
                    request.LastUpdate < 0)
                {
                    return Results.BadRequest(new
                    {
                        message = "Each team requires an event and a non-negative numeric lastUpdate."
                    });
                }

                normalizedRequests.Add((
                    request.Event.Trim(),
                    JsonSerializer.Serialize(request, JsonSerializerOptions.Web),
                    request.LastUpdate.Value));
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

            var existing = await db.Teams
                .Where(team => team.UserId == userId && events.Contains(team.Event))
                .ToDictionaryAsync(team => team.Event, cancellationToken);

            foreach (var request in uniqueRequests)
            {
                if (existing.TryGetValue(request.Event, out var team))
                {
                    if (request.LastUpdate > GetLastUpdate(team.Data))
                    {
                        team.Data = request.Data;
                    }
                }
                else
                {
                    db.Teams.Add(new UserTeam
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

    public sealed record TeamRequest(
        string? Event,
        string? Uma1,
        string? Uma2,
        string? Uma3,
        long? LastUpdate);

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
