using Microsoft.AspNetCore.Cors.Infrastructure;

namespace UmaPlanner.Api;

internal static class CorsConfiguration
{
    public static IServiceCollection AddConfiguredCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("ConfiguredCors", policy =>
            {
                var allowedOrigins = configuration["Cors:AllowedOrigins"]?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(origin => !string.IsNullOrWhiteSpace(origin))
                    .ToArray() ?? [];
                var vercelProjectPrefix = configuration["Cors:VercelProjectPrefix"] ?? "umaplanner-";

                policy.SetIsOriginAllowed(origin =>
                {
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                        uri.Scheme != Uri.UriSchemeHttps)
                    {
                        return environment.IsDevelopment() && uri?.IsLoopback == true;
                    }

                    var isConfiguredOrigin = allowedOrigins.Contains(
                        origin,
                        StringComparer.OrdinalIgnoreCase);
                    var isAllowedVercelPreview =
                        uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) &&
                        uri.Host.StartsWith(vercelProjectPrefix, StringComparison.OrdinalIgnoreCase);

                    return isConfiguredOrigin || isAllowedVercelPreview;
                })
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
            });
        });

        return services;
    }
}
