using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Api.Endpoints;
using UmaPlanner.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfiguredCors(builder.Configuration, builder.Environment);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = builder.Environment.IsDevelopment()
        ? SameSiteMode.Lax
        : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddSingleton<RaceEventCache>();
builder.Services.AddHostedService<UmaRaceSheetPollingService>();

builder.Services.AddDbContext<AppDbContext>(options =>
options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
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
app.MapUmaBuildEndpoints();

app.Run();
