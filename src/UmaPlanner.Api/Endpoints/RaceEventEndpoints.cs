using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class RaceEventEndpoints
{
    public static void MapRaceEventEndpoints(this WebApplication app)
    {
        app.MapGet("/races", async (RaceEventCache cache) =>
            await cache.GetAllAsync()
        );
    }
}
