using UmaPlanner.Infrastructure.Data.Event;

namespace UmaPlanner.Api.Endpoints;

public static class RaceEventEndpoints
{
    public static void MapRaceEventEndpoints(this WebApplication app)
    {
        app.MapGet("/races", async (Cache cache) =>
            await cache.GetAllAsync()
        );
    }
}
