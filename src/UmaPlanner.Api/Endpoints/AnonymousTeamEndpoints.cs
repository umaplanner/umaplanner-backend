using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class AnonymousTeamEndpoints
{
    private const int MaxBuildsPerTeam = 3;
    private const int MaxEventLength = 128;
    private const int MaxBuildIdLength = 128;

    public static void MapAnonymousTeamEndpoints(this WebApplication app)
    {
        app.MapPost("/anonymous/teams", async (
            AnonymousTeamRequest? request,
            AppDbContext db,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            if (request is null ||
                !Guid.TryParse(request.AnonymousId, out var anonymousId) ||
                string.IsNullOrWhiteSpace(request.Event) ||
                request.Event.Trim().Length > MaxEventLength ||
                request.Team is null ||
                request.Team.LastUpdate is < 0 or null ||
                request.Builds is null ||
                request.Builds.Count > MaxBuildsPerTeam)
            {
                return Results.BadRequest(new
                {
                    message = "A valid anonymous id, event, team, and up to three builds are required."
                });
            }

            var eventName = request.Event.Trim();
            var team = request.Team;
            var selectedBuildIds = new[] { team.Uma1, team.Uma2, team.Uma3 }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToArray();
            if (selectedBuildIds.Any(id => id.Length > MaxBuildIdLength) ||
                selectedBuildIds.Length != selectedBuildIds.Distinct(StringComparer.Ordinal).Count() ||
                request.Builds.Any(build =>
                    build is null ||
                    string.IsNullOrWhiteSpace(build.Id) ||
                    build.Id.Length > MaxBuildIdLength ||
                    string.IsNullOrWhiteSpace(build.OutfitId) ||
                    build.OutfitId.Length > MaxBuildIdLength ||
                    build.BuildType is not null &&
                        build.BuildType is not ("standard" or "plan") ||
                    string.IsNullOrWhiteSpace(build.Strategy) ||
                    build.Strategy.Length > 64 ||
                    build.Skills is null ||
                    !IsValidBuildNumber(build.Speed) ||
                    !IsValidBuildNumber(build.Stamina) ||
                    !IsValidBuildNumber(build.Power) ||
                    !IsValidBuildNumber(build.Guts) ||
                    !IsValidBuildNumber(build.Wisdom) ||
                    build.Skills.Count > 500 ||
                    build.Skills.Any(skill =>
                        string.IsNullOrWhiteSpace(skill) || skill.Length > 128) ||
                    build.SupportCards is { Count: > 20 } ||
                    build.SupportCards?.Any(card =>
                        card is null ||
                        card.Position < 0 ||
                        card.SupportCardId < 0 ||
                        card.LimitBreakCount is < 0 or > 4) == true) ||
                request.Builds.Select(build => build.Id).Distinct(StringComparer.Ordinal).Count() !=
                    request.Builds.Count ||
                !selectedBuildIds.Order(StringComparer.Ordinal).SequenceEqual(
                    request.Builds.Select(build => build.Id!).Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                return Results.BadRequest(new
                {
                    message = "Team build ids must match the supplied builds, with valid build data."
                });
            }

            var normalizedAnonymousId = anonymousId.ToString("D");
            var existing = await db.AnonymousTeamContributions.SingleOrDefaultAsync(
                contribution =>
                    contribution.AnonymousId == normalizedAnonymousId &&
                    contribution.Event == eventName,
                cancellationToken);

            var lastUpdate = team.LastUpdate!.Value;
            if (existing is not null && lastUpdate < existing.LastUpdate)
            {
                    return Results.Ok(new { saved = 0, stale = true });
            }

            if (selectedBuildIds.Length == 0)
            {
                if (existing is not null)
                {
                    existing.TeamData = JsonSerializer.Serialize(team, JsonSerializerOptions.Web);
                    existing.BuildsData = "[]";
                    existing.LastUpdate = lastUpdate;
                    existing.UpdatedAt = timeProvider.GetUtcNow();
                    await db.SaveChangesAsync(cancellationToken);
                }

                return Results.Ok(new { saved = 0, cleared = existing is null ? 0 : 1 });
            }

            var contribution = existing ?? new AnonymousTeamContribution
            {
                AnonymousId = normalizedAnonymousId,
                Event = eventName
            };
            contribution.TeamData = JsonSerializer.Serialize(team, JsonSerializerOptions.Web);
            contribution.BuildsData = JsonSerializer.Serialize(request.Builds, JsonSerializerOptions.Web);
            contribution.LastUpdate = lastUpdate;
            contribution.UpdatedAt = timeProvider.GetUtcNow();

            if (existing is null)
            {
                db.AnonymousTeamContributions.Add(contribution);
            }
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new { saved = 1 });
        })
        .WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
    }

    public sealed record AnonymousTeamRequest(
        string? AnonymousId,
        string? Event,
        AnonymousEventTeam? Team,
        IReadOnlyList<AnonymousUmaBuild>? Builds);

    public sealed record AnonymousEventTeam(
        string? Uma1,
        string? Uma2,
        string? Uma3,
        long? LastUpdate);

    public sealed record AnonymousUmaBuild(
        string? Id,
        [property: JsonPropertyName("build-type")] string? BuildType,
        string? OutfitId,
        string? Strategy,
        double Speed,
        double Stamina,
        double Power,
        double Guts,
        double Wisdom,
        IReadOnlyList<string>? Skills,
        IReadOnlyList<AnonymousSupportCard>? SupportCards);

    public sealed record AnonymousSupportCard(
        int Position,
        int SupportCardId,
        int LimitBreakCount);

    private static bool IsValidBuildNumber(double value) =>
        double.IsFinite(value) && value is >= 0 and <= 10000;
}
