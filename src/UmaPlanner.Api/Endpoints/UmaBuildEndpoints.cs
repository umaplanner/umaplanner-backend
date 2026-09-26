using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class UmaBuildEndpoints
{
    public static void MapUmaBuildEndpoints(this WebApplication app)
    {
        app.MapGet("/builds", async (
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            await RemoveExpiredBuildsAsync(db, cancellationToken);

            var builds = await db.UmaBuilds
                .AsNoTracking()
                .Where(build => build.UserId == userId)
                .OrderBy(build => build.Event)
                .ThenBy(build => build.Id)
                .ToListAsync(cancellationToken);

            var response = builds.Select(build => new UmaBuildResponse(
                build.Event,
                build.Id,
                JsonDocument.Parse(build.Data).RootElement.Clone(),
                build.DeletedAt));

            return Results.Ok(response);
        });

        app.MapPost("/builds", async (
            IReadOnlyList<UmaBuildRequest>? requests,
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            await RemoveExpiredBuildsAsync(db, cancellationToken);

            if (requests is null)
            {
                return Results.BadRequest(new { message = "A JSON array of builds is required." });
            }

            var normalizedRequests = new List<(string Event, string Id, string Data, long LastUpdate)>();
            foreach (var request in requests)
            {
                if (request is null ||
                    string.IsNullOrWhiteSpace(request.Event) ||
                    string.IsNullOrWhiteSpace(request.Id) ||
                    request.Data.ValueKind != JsonValueKind.Object ||
                    !TryGetLastUpdate(request.Data, out var lastUpdate))
                {
                    return Results.BadRequest(new
                    {
                        message = "Each build requires event, id, an object-valued data property, and a numeric lastUpdate."
                    });
                }

                normalizedRequests.Add((
                    request.Event.Trim(),
                    request.Id.Trim(),
                    request.Data.GetRawText(),
                    lastUpdate));
            }

            if (!await db.Users.AnyAsync(user => user.Id == userId, cancellationToken))
            {
                return Results.Unauthorized();
            }

            var uniqueRequests = normalizedRequests
                .GroupBy(request => (request.Event, request.Id))
                .Select(group => group.MaxBy(request => request.LastUpdate)!)
                .ToArray();
            var events = uniqueRequests.Select(request => request.Event).Distinct().ToArray();
            var ids = uniqueRequests.Select(request => request.Id).Distinct().ToArray();

            var existing = await db.UmaBuilds
                .Where(build =>
                    build.UserId == userId &&
                    events.Contains(build.Event) &&
                    ids.Contains(build.Id))
                .ToDictionaryAsync(
                    build => (build.Event, build.Id),
                    cancellationToken);

            foreach (var request in uniqueRequests)
            {
                var key = (request.Event, request.Id);

                if (existing.TryGetValue(key, out var build))
                {
                    if (request.LastUpdate > GetLastUpdate(build.Data))
                    {
                        build.Data = request.Data;
                        build.DeletedAt = null;
                    }
                }
                else
                {
                    db.UmaBuilds.Add(new UserUmaBuild
                    {
                        UserId = userId,
                        Event = request.Event,
                        Id = request.Id,
                        Data = request.Data
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { saved = uniqueRequests.Length });
        });

        app.MapDelete("/builds/delete", async (
            [FromBody] BuildDeleteRequest? request,
            HttpContext context,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            await RemoveExpiredBuildsAsync(db, cancellationToken);

            if (request is null ||
                string.IsNullOrWhiteSpace(request.Event) ||
                string.IsNullOrWhiteSpace(request.Id))
            {
                return Results.BadRequest(new
                {
                    message = "Event and build id are required."
                });
            }

            var eventName = request.Event.Trim();
            var buildId = request.Id.Trim();
            var build = await db.UmaBuilds.SingleOrDefaultAsync(
                existing =>
                    existing.UserId == userId &&
                    existing.Event.ToLower() == eventName.ToLower() &&
                    existing.Id.ToLower() == buildId.ToLower(),
                cancellationToken);

            if (build is null)
            {
                return Results.NotFound(new { message = "Build not found." });
            }

            build.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });
    }

    public sealed record UmaBuildRequest(string? Event, string? Id, JsonElement Data);

    public sealed record BuildDeleteRequest(string? Event, string? Id);

    public sealed record UmaBuildResponse(
        string Event,
        string Id,
        JsonElement Data,
        DateTimeOffset? DeletedAt);

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

    private static Task<int> RemoveExpiredBuildsAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
        return db.UmaBuilds
            .Where(build => build.DeletedAt != null && build.DeletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
