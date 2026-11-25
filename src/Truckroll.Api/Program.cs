using Truckroll.Api.Features.Status;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Aspire client integrations: each registers the client, a health check and its telemetry.
// The connection names match the resources in Truckroll.AppHost.
builder.AddMySqlDataSource("truckroll");
builder.AddRedisClient("redis");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Every endpoint lives under /api, which is the prefix the Next.js app rewrites to us.
var api = app.MapGroup("/api");
api.MapStatusEndpoints();

app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point, public so integration tests can boot it with WebApplicationFactory.</summary>
public partial class Program;
