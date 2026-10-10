using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Infrastructure.Data.Admin;
using UmaPlanner.Infrastructure.Data.Event;
using UmaPlanner.Api.Endpoints;
using UmaPlanner.Api;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Amazon.S3;

var builder = WebApplication.CreateBuilder(args);
var authSessionLifetime = TimeSpan.FromDays(400);

builder.Services.AddConfiguredCors(builder.Configuration, builder.Environment);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddDistributedPostgresCache(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;
    options.SchemaName = "public";
    options.TableName = "distributed_cache";
    options.CreateIfNotExists = true;
});
builder.Services.AddSession(options =>
{
    options.IdleTimeout = authSessionLifetime;
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.MaxAge = authSessionLifetime;
    options.Cookie.SameSite = builder.Environment.IsDevelopment()
        ? SameSiteMode.Lax
        : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddSingleton<Cache>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHostedService<RaceSheetPollingService>();
builder.Services.AddHostedService<StatsSnapshotPollingService>();

if (builder.Environment.IsProduction())
{
    var r2Options = builder.Configuration.GetSection("R2Storage").Get<R2StorageOptions>()
        ?? new R2StorageOptions();
    r2Options.AccountId = FirstConfigured(r2Options.AccountId, "R2_ACCOUNT_ID");
    r2Options.Endpoint = FirstConfigured(r2Options.Endpoint, "R2_ENDPOINT");
    r2Options.AccessKeyId = FirstConfigured(r2Options.AccessKeyId, "R2_ACCESS_KEY_ID");
    r2Options.SecretAccessKey = FirstConfigured(r2Options.SecretAccessKey, "R2_SECRET_ACCESS_KEY");
    r2Options.BucketName = FirstConfigured(
        r2Options.BucketName,
        "R2_BUCKET_NAME",
        "R2_BUCKET");
    r2Options.Region = FirstConfigured(r2Options.Region, "R2_REGION");

    if ((string.IsNullOrWhiteSpace(r2Options.Endpoint) &&
         string.IsNullOrWhiteSpace(r2Options.AccountId)) ||
        string.IsNullOrWhiteSpace(r2Options.AccessKeyId) ||
        string.IsNullOrWhiteSpace(r2Options.SecretAccessKey) ||
        string.IsNullOrWhiteSpace(r2Options.BucketName))
    {
        throw new InvalidOperationException(
            "R2Storage:Endpoint or AccountId, AccessKeyId, SecretAccessKey, and BucketName are required.");
    }

    if (!string.IsNullOrWhiteSpace(r2Options.AccountId))
    {
        r2Options.Endpoint = $"https://{r2Options.AccountId}.r2.cloudflarestorage.com";
    }

    if (!r2Options.Endpoint.Contains("://", StringComparison.Ordinal))
    {
        r2Options.Endpoint = $"https://{r2Options.Endpoint}";
    }

    if (!Uri.TryCreate(r2Options.Endpoint, UriKind.Absolute, out var r2Endpoint) ||
        (r2Endpoint.Scheme != Uri.UriSchemeHttp && r2Endpoint.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException(
            "R2Storage:Endpoint must be an HTTP or HTTPS URL.");
    }

    builder.Services.AddSingleton(r2Options);
    builder.Services.AddSingleton<IAmazonS3>(_ =>
        new AmazonS3Client(
            r2Options.AccessKeyId,
            r2Options.SecretAccessKey,
            new AmazonS3Config
            {
                ServiceURL = r2Options.Endpoint,
                AuthenticationRegion = r2Options.Region
            }));
}

if (builder.Environment.IsDevelopment() || builder.Environment.IsProduction())
{
    builder.Services.AddHostedService<SummaryService>();
}

static string FirstConfigured(string current, params string[] environmentNames)
{
    if (!string.IsNullOrWhiteSpace(current))
        return current;

    foreach (var environmentName in environmentNames)
    {
        var value = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }

    return current;
}

builder.Services.AddDbContext<AppDbContext>(options =>
options.UseNpgsql(
    builder.Configuration.GetConnectionString("DefaultConnection"),
    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("UmaPlanner");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await db.UmaBuilds
        .Where(build =>
            build.DeletedAt != null &&
            build.DeletedAt < DateTimeOffset.UtcNow.AddDays(-14))
        .ExecuteDeleteAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("ConfiguredCors");
app.UseSession();

app.UseHttpsRedirection();

app.MapRaceEventEndpoints();

app.MapDiscordAuthEndpoints();
app.MapUserEndpoints();
app.MapAdminEndpoints();
app.MapUmaBuildEndpoints();
app.MapTeamEndpoints();
app.MapResultEndpoints();

app.Run();
