using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Infrastructure.Data.Admin;

public static class StatsSnapshot
{
    public static async Task CaptureAsync(
        AppDbContext db,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var userCount = await db.Users.LongCountAsync(cancellationToken);
        var buildCounts = await db.UmaBuilds
            .GroupBy(build => build.Event)
            .Select(group => new { Event = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);
        var storedTeams = await db.Teams
            .AsNoTracking()
            .Select(team => new { team.Event, team.UserId, team.Data })
            .ToListAsync(cancellationToken);
        var selectedTeams = storedTeams
            .Where(team => HasSelectedUma(team.Data))
            .ToArray();
        var teamCounts = selectedTeams
            .GroupBy(team => team.Event)
            .Select(group => new { Event = group.Key, Count = group.LongCount() })
            .ToArray();
        var eventUserEntries = await db.UmaBuilds
            .Select(build => new { build.Event, build.UserId })
            .Union(db.Results.Select(result => new { result.Event, result.UserId }))
            .ToListAsync(cancellationToken);
        var usersByEvent = eventUserEntries
            .Concat(selectedTeams.Select(team => new { team.Event, team.UserId }))
            .GroupBy(entry => entry.Event)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.UserId).Distinct(StringComparer.Ordinal).LongCount());

        var knownEvents = await db.DailyStats
            .Where(stat => stat.Event != string.Empty)
            .Select(stat => stat.Event)
            .Distinct()
            .ToListAsync(cancellationToken);
        var eventNames = knownEvents
            .Concat(buildCounts.Select(count => count.Event))
            .Concat(teamCounts.Select(count => count.Event))
            .Concat(usersByEvent.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var buildsByEvent = buildCounts.ToDictionary(count => count.Event, count => count.Count);
        var teamsByEvent = teamCounts.ToDictionary(count => count.Event, count => count.Count);

        var existingForDate = await db.DailyStats
            .Where(stat => stat.Date == date)
            .ToListAsync(cancellationToken);
        var existingByEvent = existingForDate.ToDictionary(stat => stat.Event);

        Upsert(
            string.Empty,
            userCount,
            buildCounts.Sum(count => count.Count),
            teamCounts.Sum(count => count.Count));

        foreach (var eventName in eventNames)
            Upsert(
                eventName,
                usersByEvent.GetValueOrDefault(eventName),
                buildsByEvent.GetValueOrDefault(eventName),
                teamsByEvent.GetValueOrDefault(eventName));

        var removedEvents = existingForDate
            .Where(stat => stat.Event != string.Empty && !eventNames.Contains(stat.Event))
            .ToArray();
        db.DailyStats.RemoveRange(removedEvents);

        await db.SaveChangesAsync(cancellationToken);

        void Upsert(string eventName, long users, long builds, long teams)
        {
            if (existingByEvent.TryGetValue(eventName, out var existing))
            {
                existing.UserCount = users;
                existing.BuildCount = builds;
                existing.TeamCount = teams;
                return;
            }

            db.DailyStats.Add(new DailyStat
            {
                Date = date,
                Event = eventName,
                UserCount = users,
                BuildCount = builds,
                TeamCount = teams
            });
        }
    }

    private static bool HasSelectedUma(string data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object &&
               new[] { "uma1", "uma2", "uma3" }.Any(slot =>
                   root.TryGetProperty(slot, out var selection) &&
                   selection.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(selection.GetString()));
    }
}
