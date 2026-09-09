using UmaPlanner.Core.Interfaces;
using UmaPlanner.Infrastructure.Data;
using UmaPlanner.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var mdbPath = builder.Configuration["MdbPath"]
              ?? Environment.GetEnvironmentVariable("UMAPLANNER_MDB_PATH")
              ?? throw new InvalidOperationException("MdbPath configuration is required.");

builder.Services.AddSingleton<IUmaService>(_ => new UmaService(mdbPath));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapUmaEndpoints();

app.Run();
