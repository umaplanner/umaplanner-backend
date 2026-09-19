using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/users", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var users = await db.Users
                .ToListAsync(cancellationToken);
            return Results.Ok(users);
        });

        app.MapGet("/users/{id}", async (
            string id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var user = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

            return user is null
                ? Results.NotFound(new { message = $"User with ID {id} not found." })
                : Results.Ok(user);
        });

        app.MapPost("/users", async (
            UserOptions user,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(user.Id) ||
                string.IsNullOrWhiteSpace(user.DiscordId) ||
                string.IsNullOrWhiteSpace(user.Username))
            {
                return Results.BadRequest(new
                {
                    message = "Id, DiscordId, and Username are required."
                });
            }

            if (await db.Users.AnyAsync(existing => existing.Id == user.Id, cancellationToken))
            {
                return Results.Conflict(new { message = $"User with ID {user.Id} already exists." });
            }

            if (string.IsNullOrWhiteSpace(user.CreatedAt))
            {
                user.CreatedAt = DateTimeOffset.UtcNow.ToString("O");
            }

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/users/{user.Id}", user);
        });
    }
}
