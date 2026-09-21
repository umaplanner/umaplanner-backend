using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(frontendBaseUrl))
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(frontendBaseUrl)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<RaceEventCache>();
builder.Services.AddHostedService<UmaRaceSheetPollingService>();

builder.Services.AddDbContext<AppDbContext>(options =>
options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    app.MapOpenApi();
}

app.UseCors("AllowAll");
app.UseSession();

app.UseHttpsRedirection();

app.MapRaceEventEndpoints();

app.MapDiscordAuthEndpoints();
app.MapUserEndpoints();

app.Run();
