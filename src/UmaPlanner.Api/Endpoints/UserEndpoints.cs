using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/users/me", async (
            HttpContext context,
            AppDbContext db,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var userId = context.Session.GetString(DiscordAuthEndpoints.UserSessionKey);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == userId, cancellationToken);

            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new CurrentUserResponse(
                    user.Id,
                    user.DiscordId,
                    user.Username,
                    user.AvatarUrl,
                    user.TrainerId,
                    user.CreatedAt,
                    AdminAuthorization.IsAdmin(user, configuration) ? true : null));
        });

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

        app.MapPut("/users/{id}/trainer-id", async (
            string id,
            TrainerIdRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var user = await db.Users
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (user is null)
            {
                return Results.NotFound(new { message = $"User with ID {id} not found." });
            }

            user.TrainerId = string.IsNullOrWhiteSpace(request.TrainerId)
                ? null
                : request.TrainerId.Trim();

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(user);
        });

        app.MapPost("/users", async (
            CreateUserRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id) ||
                string.IsNullOrWhiteSpace(request.DiscordId) ||
                string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new
                {
                    message = "Id, DiscordId, and Username are required."
                });
            }

            if (await db.Users.AnyAsync(existing => existing.Id == request.Id, cancellationToken))
            {
                return Results.Conflict(new { message = $"User with ID {request.Id} already exists." });
            }

            var user = new UserOptions
            {
                Id = request.Id,
                DiscordId = request.DiscordId,
                Username = request.Username,
                AvatarUrl = request.AvatarUrl,
                TrainerId = request.TrainerId,
                CreatedAt = string.IsNullOrWhiteSpace(request.CreatedAt)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : request.CreatedAt
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/users/{user.Id}", user);
        });
    }

    private sealed record CreateUserRequest(
        string? Id,
        string? DiscordId,
        string? Username,
        string? AvatarUrl,
        string? TrainerId,
        string? CreatedAt);

    private sealed record CurrentUserResponse(
        string Id,
        string DiscordId,
        string Username,
        string? AvatarUrl,
        string? TrainerId,
        string CreatedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool? IsAdmin);

    private sealed record TrainerIdRequest(string? TrainerId);
}
