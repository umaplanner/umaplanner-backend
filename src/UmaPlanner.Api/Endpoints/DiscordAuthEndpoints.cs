using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class DiscordAuthEndpoints
{
    public static void MapDiscordAuthEndpoints(this WebApplication app)
    {
        // Dummy GET endpoint
        app.MapGet("/auth", () =>
        {
            return Results.Ok(new
            {
                message = "Hello from dummy endpoint",
                timestamp = DateTime.UtcNow
            });
        });

        // Dummy POST endpoint (echoes back the body)
        app.MapPost("/auth", (MyRequest request) =>
        {
            return Results.Ok(new
            {
                received = request,
                processedAt = DateTime.UtcNow
            });
        });
    }
}


record MyRequest(string Name, int Age);
