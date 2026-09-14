using UmaPlanner.Core.Interfaces;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi();

var mdbPath = builder.Configuration["MdbPath"]
              ?? Environment.GetEnvironmentVariable("UMAPLANNER_MDB_PATH")
              ?? throw new InvalidOperationException("MdbPath configuration is required.");

builder.Services.AddSingleton<IUmaService>(_ => new UmaService(mdbPath));

builder.Services.AddSingleton<RaceEventCache>();
builder.Services.AddHostedService<UmaRaceSheetPollingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

// Endpoint import
app.MapUmaEndpoints();
app.MapRaceEventEndpoints();


app.Run();
