using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Api.Endpoints;

public static class DiscordAuthEndpoints
{
    private const string StateKey = "discord_oauth_state";
    private const string ReturnUrlKey = "discord_oauth_return_url";
    internal const string UserSessionKey = "authenticated_user_id";
    private const string DiscordAuthorizeUrl = "https://discord.com/oauth2/authorize";
    private const string DiscordTokenUrl = "https://discord.com/api/oauth2/token";
    private const string DiscordUserUrl = "https://discord.com/api/users/@me";

    public static void MapDiscordAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/discord", (
            string? returnUrl,
            HttpContext context,
            IConfiguration configuration) =>
        {
            var clientId = configuration["Discord:ClientId"];
            var redirectUri = configuration["Discord:RedirectUri"];

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Problem("Discord OAuth is not configured.");
            }

            if (!IsValidReturnUrl(returnUrl))
            {
                return Results.BadRequest(new
                {
                    message = "A valid returnUrl is required."
                });
            }

            var state = Guid.NewGuid().ToString("N");
            context.Session.SetString(StateKey, state);
            context.Session.SetString(ReturnUrlKey, returnUrl!);

            var authorizationUrl = QueryHelpers.AddQueryString(
                DiscordAuthorizeUrl,
                new Dictionary<string, string?>
                {
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code",
                    ["scope"] = "identify",
                    ["state"] = state
                });

            return Results.Redirect(authorizationUrl);
        });

        app.MapGet("/auth/discord/callback", async (
            string? code,
            string? state,
            string? error,
            HttpContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return Results.BadRequest(new { message = $"Discord login failed: {error}" });
            }

            var expectedState = context.Session.GetString(StateKey);
            var returnUrl = context.Session.GetString(ReturnUrlKey);
            context.Session.Remove(StateKey);
            context.Session.Remove(ReturnUrlKey);

            if (string.IsNullOrWhiteSpace(code) ||
                !string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Invalid Discord OAuth callback." });
            }

            var clientId = configuration["Discord:ClientId"];
            var clientSecret = configuration["Discord:ClientSecret"];
            var redirectUri = configuration["Discord:RedirectUri"];

            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret) ||
                string.IsNullOrWhiteSpace(redirectUri))
            {
                return Results.Problem("Discord OAuth is not configured.");
            }

            var client = httpClientFactory.CreateClient();
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, DiscordTokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri
                })
            };

            using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Results.Problem("Discord rejected the authorization code.");
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<DiscordToken>(
                cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
            {
                return Results.Problem("Discord returned no access token.");
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, DiscordUserUrl);
            userRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

            using var userResponse = await client.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                return Results.Problem("Discord profile lookup failed.");
            }

            var profile = await userResponse.Content.ReadFromJsonAsync<DiscordProfile>(
                cancellationToken);
            if (string.IsNullOrWhiteSpace(profile?.Id) ||
                string.IsNullOrWhiteSpace(profile.Username))
            {
                return Results.Problem("Discord returned an incomplete profile.");
            }

            var user = await db.Users.SingleOrDefaultAsync(
                existing => existing.DiscordId == profile.Id,
                cancellationToken);

            if (user is null)
            {
                user = new UserOptions
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DiscordId = profile.Id,
                    Username = profile.Username,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O")
                };
                db.Users.Add(user);
            }
            else
            {
                user.Username = profile.Username;
            }

            user.AvatarUrl = string.IsNullOrWhiteSpace(profile.Avatar)
                ? null
                : $"https://cdn.discordapp.com/avatars/{profile.Id}/{profile.Avatar}.png";

            await db.SaveChangesAsync(cancellationToken);
            context.Session.SetString(UserSessionKey, user.Id);

            if (!IsValidReturnUrl(returnUrl))
            {
                return Results.Problem(
                    "The OAuth return URL is missing.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Redirect(returnUrl!);
        });

        app.MapGet("/auth/logout", (string? returnUrl, HttpContext context) =>
        {
            context.Session.Clear();

            return IsValidReturnUrl(returnUrl)
                ? Results.Redirect(returnUrl!)
                : Results.Redirect("/");
        });
    }

    private static bool IsValidReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) &&
               Uri.TryCreate(returnUrl, UriKind.Absolute, out var target) &&
               (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps);
    }

    private sealed record DiscordToken(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record DiscordProfile(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("avatar")] string? Avatar);
}
