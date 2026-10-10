using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Infrastructure.Data.Admin;

namespace UmaPlanner.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/stats", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            int? days,
            int? compareTo,
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

            if (!TryGetPeriod(days, out var periodDays))
                return Results.BadRequest(new { message = "days must be between 1 and 3650." });
            if (!IsValidComparisonOffset(compareTo))
                return Results.BadRequest(new { message = "compareTo must be between 1 and 3650 days." });

            var stats = await db.DailyStats
                .AsNoTracking()
                .Where(stat =>
                    stat.Event == string.Empty &&
                    stat.Date >= periodDays.StartDate &&
                    stat.Date <= periodDays.Today)
                .OrderBy(stat => stat.Date)
                .ToListAsync(cancellationToken);
            StatsPercentChangeResponse? comparison = null;
            if (compareTo is not null)
            {
                var latest = await db.DailyStats
                    .AsNoTracking()
                    .Where(stat => stat.Event == string.Empty)
                    .OrderByDescending(stat => stat.Date)
                    .FirstOrDefaultAsync(cancellationToken);
                var comparedDate = latest?.Date.AddDays(-compareTo.Value);
                var compared = comparedDate is null
                    ? null
                    : await db.DailyStats
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            stat => stat.Date == comparedDate.Value && stat.Event == string.Empty,
                            cancellationToken);

                comparison = CreatePercentChange(latest, compared);
            }

            return Results.Ok(new
            {
                periodDays = periodDays.Count,
                data = stats.Select(ToResponse).ToArray(),
                comparison
            });
        });

        app.MapGet("/admin/stats/max-days", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var user = await AdminAuthorization.GetCurrentUserAsync(
                context, db, cancellationToken);
            if (user is null)
                return Results.Unauthorized();

            if (!AdminAuthorization.IsAdmin(user, configuration))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var range = await db.DailyStats
                .AsNoTracking()
                .Where(stat => stat.Event == string.Empty)
                .GroupBy(stat => stat.Event)
                .Select(group => new
                {
                    EarliestDate = group.Min(stat => stat.Date),
                    LatestDate = group.Max(stat => stat.Date)
                })
                .SingleOrDefaultAsync(cancellationToken);

            return range is null
                ? Results.Ok(new { maxDays = 0, earliestDate = (DateOnly?)null, latestDate = (DateOnly?)null })
                : Results.Ok(new
                {
                    maxDays = range.LatestDate.DayNumber - range.EarliestDate.DayNumber + 1,
                    earliestDate = (DateOnly?)range.EarliestDate,
                    latestDate = (DateOnly?)range.LatestDate
                });
        });

        app.MapGet("/admin/stats/events", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            int? days,
            int? compareTo,
            [FromQuery(Name = "event")] string? eventName,
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

            if (!TryGetPeriod(days, out var periodDays))
                return Results.BadRequest(new { message = "days must be between 1 and 3650." });
            if (!IsValidComparisonOffset(compareTo))
                return Results.BadRequest(new { message = "compareTo must be between 1 and 3650 days." });

            var stats = await db.DailyStats
                .AsNoTracking()
                .Where(stat =>
                    stat.Event != string.Empty &&
                    (eventName == null || stat.Event == eventName) &&
                    stat.Date >= periodDays.StartDate &&
                    stat.Date <= periodDays.Today)
                .OrderBy(stat => stat.Date)
                .ThenBy(stat => stat.Event)
                .ToListAsync(cancellationToken);
            var events = stats
                .GroupBy(stat => stat.Event)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(ToResponse).ToArray());
            object? comparison = null;
            if (compareTo is not null)
            {
                var latest = await db.DailyStats
                    .AsNoTracking()
                    .Where(stat => stat.Event == string.Empty)
                    .OrderByDescending(stat => stat.Date)
                    .FirstOrDefaultAsync(cancellationToken);
                var comparedDate = latest?.Date.AddDays(-compareTo.Value);
                Dictionary<string, StatsPercentChangeResponse> eventComparisons = [];
                if (latest is not null)
                {
                    var hasComparedSnapshot = await db.DailyStats
                        .AnyAsync(
                            stat => stat.Date == comparedDate!.Value && stat.Event == string.Empty,
                            cancellationToken);
                    var comparisonStats = await db.DailyStats
                        .AsNoTracking()
                        .Where(stat =>
                            stat.Event != string.Empty &&
                            (eventName == null || stat.Event == eventName) &&
                            (stat.Date == latest.Date ||
                             (hasComparedSnapshot && stat.Date == comparedDate!.Value)))
                        .ToListAsync(cancellationToken);
                    var newestEvents = comparisonStats
                        .Where(stat => stat.Date == latest.Date)
                        .ToDictionary(stat => stat.Event);
                    var comparedEvents = comparisonStats
                        .Where(stat => stat.Date == comparedDate!.Value)
                        .ToDictionary(stat => stat.Event);
                    var eventNames = newestEvents.Keys
                        .Concat(comparedEvents.Keys)
                        .Distinct(StringComparer.Ordinal);

                    eventComparisons = eventNames.ToDictionary(
                        eventKey => eventKey,
                        eventKey => CreatePercentChange(
                            newestEvents.GetValueOrDefault(eventKey) ??
                                CreateZeroStat(latest.Date, eventKey),
                            hasComparedSnapshot
                                ? comparedEvents.GetValueOrDefault(eventKey) ??
                                    CreateZeroStat(comparedDate!.Value, eventKey)
                                : null));
                }

                comparison = eventName is null
                    ? eventComparisons
                    : eventComparisons.GetValueOrDefault(eventName) ??
                        CreatePercentChange(null, null);
            }

            return Results.Ok(new { periodDays = periodDays.Count, events, comparison });
        });

    }

    private static bool TryGetPeriod(
        int? days,
        out StatsPeriod period)
    {
        var count = days ?? 1;
        if (count is < 1 or > 3650)
        {
            period = default;
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        period = new StatsPeriod(count, today.AddDays(1 - count), today);
        return true;
    }

    private static bool IsValidComparisonOffset(int? compareTo) =>
        compareTo is null || compareTo is >= 1 and <= 3650;

    private static DailyStatsResponse ToResponse(DailyStat stat) =>
        new(stat.Date, stat.UserCount, stat.BuildCount, stat.TeamCount);

    private static StatsPercentChangeResponse CreatePercentChange(
        DailyStat? latest,
        DailyStat? compared)
    {
        return new StatsPercentChangeResponse(
            latest is null || compared is null
                ? null
                : PercentChange(latest.UserCount, compared.UserCount),
            latest is null || compared is null
                ? null
                : PercentChange(latest.BuildCount, compared.BuildCount),
            latest is null || compared is null
                ? null
                : PercentChange(latest.TeamCount, compared.TeamCount));
    }

    private static double? PercentChange(long latest, long comparedTo) =>
        comparedTo == 0
            ? latest == 0 ? 0 : null
            : (latest - comparedTo) / (double)comparedTo * 100;

    private static DailyStat CreateZeroStat(DateOnly date, string eventName) =>
        new() { Date = date, Event = eventName };

    private readonly record struct StatsPeriod(int Count, DateOnly StartDate, DateOnly Today);

    private sealed record DailyStatsResponse(
        DateOnly Date,
        long Users,
        long Builds,
        long Teams);

    private sealed record StatsPercentChangeResponse(
        double? UsersPercentChange,
        double? BuildsPercentChange,
        double? TeamsPercentChange);
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
