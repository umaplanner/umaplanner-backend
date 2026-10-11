using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Infrastructure.Data.Admin;

public static class StatsSnapshot
{
    public static async Task RefreshResultCountsAsync(
        AppDbContext db,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var resultRows = await db.Results
            .AsNoTracking()
            .Select(result => new { result.Event, result.Data })
            .ToListAsync(cancellationToken);
        var resultCounts = resultRows
            .GroupBy(result => result.Event)
            .Select(group => new ResultEventCount(
                group.Key,
                group.LongCount(),
                group.Sum(result => CountTickets(result.Data))))
            .ToArray();
        var currentStats = await db.DailyStats
            .Where(stat => stat.Date == date)
            .ToListAsync(cancellationToken);
        var currentStatsByEvent = currentStats.ToDictionary(stat => stat.Event);

        foreach (var stat in currentStats)
        {
            stat.ResultCount = 0;
            stat.TicketCount = 0;
        }

        Upsert(
            string.Empty,
            resultRows.LongCount(),
            resultCounts.Sum(count => count.Tickets));
        foreach (var resultCount in resultCounts)
            Upsert(resultCount.Event, resultCount.Results, resultCount.Tickets);

        await db.SaveChangesAsync(cancellationToken);

        void Upsert(string eventName, long results, long tickets)
        {
            if (currentStatsByEvent.TryGetValue(eventName, out var stat))
            {
                stat.ResultCount = results;
                stat.TicketCount = tickets;
                return;
            }

            stat = new DailyStat
            {
                Date = date,
                Event = eventName,
                ResultCount = results,
                TicketCount = tickets
            };
            currentStatsByEvent.Add(eventName, stat);
            db.DailyStats.Add(stat);
        }
    }

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
        var resultRows = await db.Results
            .AsNoTracking()
            .Select(result => new { result.Event, result.Data })
            .ToListAsync(cancellationToken);
        var resultCounts = resultRows
            .GroupBy(result => result.Event)
            .Select(group => new
            {
                Event = group.Key,
                ResultCount = group.LongCount(),
                TicketCount = group.Sum(result => CountTickets(result.Data))
            })
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
            .Concat(resultCounts.Select(count => count.Event))
            .Concat(usersByEvent.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var buildsByEvent = buildCounts.ToDictionary(count => count.Event, count => count.Count);
        var teamsByEvent = teamCounts.ToDictionary(count => count.Event, count => count.Count);
        var resultsByEvent = resultCounts.ToDictionary(count => count.Event, count => count.ResultCount);
        var ticketsByEvent = resultCounts.ToDictionary(count => count.Event, count => count.TicketCount);

        var existingForDate = await db.DailyStats
            .Where(stat => stat.Date == date)
            .ToListAsync(cancellationToken);
        var existingByEvent = existingForDate.ToDictionary(stat => stat.Event);

        Upsert(
            string.Empty,
            userCount,
            buildCounts.Sum(count => count.Count),
            teamCounts.Sum(count => count.Count),
            resultRows.LongCount(),
            resultCounts.Sum(count => count.TicketCount));

        foreach (var eventName in eventNames)
            Upsert(
                eventName,
                usersByEvent.GetValueOrDefault(eventName),
                buildsByEvent.GetValueOrDefault(eventName),
                teamsByEvent.GetValueOrDefault(eventName),
                resultsByEvent.GetValueOrDefault(eventName),
                ticketsByEvent.GetValueOrDefault(eventName));

        var removedEvents = existingForDate
            .Where(stat => stat.Event != string.Empty && !eventNames.Contains(stat.Event))
            .ToArray();
        db.DailyStats.RemoveRange(removedEvents);

        await db.SaveChangesAsync(cancellationToken);

        void Upsert(
            string eventName,
            long users,
            long builds,
            long teams,
            long results,
            long tickets)
        {
            if (existingByEvent.TryGetValue(eventName, out var existing))
            {
                existing.UserCount = users;
                existing.BuildCount = builds;
                existing.TeamCount = teams;
                existing.ResultCount = results;
                existing.TicketCount = tickets;
                return;
            }

            db.DailyStats.Add(new DailyStat
            {
                Date = date,
                Event = eventName,
                UserCount = users,
                BuildCount = builds,
                TeamCount = teams,
                ResultCount = results,
                TicketCount = tickets
            });
        }
    }

    private static long CountTickets(string data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return 0;

        return root.EnumerateObject()
            .Where(property => IsTicketCountProperty(property.Name))
            .Where(property => property.Value.ValueKind == JsonValueKind.Object)
            .Sum(property => property.Value.EnumerateObject().LongCount());
    }

    private static bool IsTicketCountProperty(string name)
    {
        const string roundPrefix = "round";
        const string daySeparator = "Day";
        if (!name.StartsWith(roundPrefix, StringComparison.Ordinal))
            return false;

        var separatorIndex = name.IndexOf(daySeparator, roundPrefix.Length, StringComparison.Ordinal);
        return separatorIndex > roundPrefix.Length &&
               separatorIndex + daySeparator.Length < name.Length &&
               IsAsciiDigits(name.AsSpan(roundPrefix.Length, separatorIndex - roundPrefix.Length)) &&
               IsAsciiDigits(name.AsSpan(separatorIndex + daySeparator.Length));
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return value.Length > 0;
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

    private sealed record ResultEventCount(string Event, long Results, long Tickets);
}
