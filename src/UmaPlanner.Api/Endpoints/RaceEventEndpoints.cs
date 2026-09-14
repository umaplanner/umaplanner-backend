using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class RaceEventEndpoints
{
    public static void MapRaceEventEndpoints(this WebApplication app)
    {
        // TODO: Evaluate if this endpoint should be removed when r2 is implemented
        app.MapGet("/races", async (RaceEventCache cache) =>
            await cache.GetAllAsync()
        );
    }
}
