using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/stats", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var user = await AdminAuthorization.GetCurrentUserAsync(
                context, db, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AdminAuthorization.IsAdmin(user, configuration))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var tableCounts = new Dictionary<string, long>
            {
                ["users"] = await db.Users.LongCountAsync(cancellationToken),
                ["user_uma_builds"] = await db.UmaBuilds.LongCountAsync(cancellationToken),
                ["user_teams"] = await db.Teams.LongCountAsync(cancellationToken),
                ["user_results"] = await db.Results.LongCountAsync(cancellationToken),
                ["DataProtectionKeys"] = await db.DataProtectionKeys.LongCountAsync(cancellationToken)
            };

            return Results.Ok(new { tableCounts });
        });

        app.MapGet("/admin/users/build-counts", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var user = await AdminAuthorization.GetCurrentUserAsync(
                context, db, cancellationToken);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (!AdminAuthorization.IsAdmin(user, configuration))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var countsByUser = await db.UmaBuilds
                .AsNoTracking()
                .GroupBy(build => build.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    Total = group.LongCount(),
                    Active = group.LongCount(build => build.DeletedAt == null),
                    Deleted = group.LongCount(build => build.DeletedAt != null)
                })
                .ToDictionaryAsync(counts => counts.UserId, cancellationToken);

            var users = await db.Users
                .AsNoTracking()
                .OrderBy(existing => existing.Username)
                .Select(existing => new
                {
                    existing.Id,
                    existing.DiscordId,
                    existing.Username
                })
                .ToListAsync(cancellationToken);

            var buildCounts = users.Select(existing =>
            {
                countsByUser.TryGetValue(existing.Id, out var counts);
                return new
                {
                    existing.Id,
                    existing.DiscordId,
                    existing.Username,
                    TotalBuildCount = counts?.Total ?? 0,
                    ActiveBuildCount = counts?.Active ?? 0,
                    DeletedBuildCount = counts?.Deleted ?? 0
                };
            });

            return Results.Ok(buildCounts);
        });

    }
}

public static class AdminAuthorization
{
    public static async Task<UserOptions?> GetCurrentUserAsync(
        HttpContext context,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await db.Users.AsNoTracking().SingleOrDefaultAsync(
            user => user.Id == userId,
            cancellationToken);
    }

    public static bool IsAdmin(UserOptions user, IConfiguration configuration)
    {
        var adminDiscordIds = configuration["Admin:AdminDiscordIds"];
        return !string.IsNullOrWhiteSpace(adminDiscordIds) &&
               adminDiscordIds.Split(
                       ',',
                       StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Contains(user.DiscordId, StringComparer.Ordinal);
    }
}
