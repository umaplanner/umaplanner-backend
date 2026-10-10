using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Infrastructure.Data.Admin;

public sealed class StatsSnapshotPollingService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<StatsSnapshotPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasSnapshots = await db.DailyStats
                .AnyAsync(stat => stat.Event == string.Empty, stoppingToken);
            if (!hasSnapshots)
            {
                var date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
                await StatsSnapshot.CaptureAsync(db, date, stoppingToken);
                logger.LogInformation("Captured initial admin statistics for {Date}", date);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to capture initial admin statistics");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var todayMidnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            var nextMidnight = now == todayMidnight
                ? todayMidnight
                : todayMidnight.AddDays(1);

            if (now < nextMidnight)
            {
                try
                {
                    await Task.Delay(nextMidnight - now, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var snapshotDate = DateOnly.FromDateTime(nextMidnight.UtcDateTime);
                await StatsSnapshot.CaptureAsync(db, snapshotDate, stoppingToken);
                logger.LogInformation("Captured admin statistics for {Date}", snapshotDate);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to capture daily admin statistics");
            }
        }
    }
}
