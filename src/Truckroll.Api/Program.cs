using System.Text.Json.Serialization;
using Truckroll.Api.Access;
using Truckroll.Api.Ai;
using Truckroll.Api.Common;
using Truckroll.Api.Data;
using Truckroll.Api.Features.Assistant;
using Truckroll.Api.Features.Auth;
using Truckroll.Api.Features.Branches;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Features.Dev;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Dispatch.Suggestions;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Features.Knowledge;
using Truckroll.Api.Features.Status;
using Truckroll.Api.Knowledge;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Aspire client integrations: each registers the client, a health check and its telemetry.
// The connection names match the resources in Truckroll.AppHost.
builder.AddTruckrollData();
builder.AddRedisClient("redis");

// Chat and embedding models: Ollama by default, Claude for chat by setting (docs/adr/004).
builder.AddTruckrollAi();

// The search index in Redis, kept in step with MySQL by a background worker (docs/adr/003).
builder.Services.AddTruckrollKnowledge();

builder.Services.AddTruckrollAccess();
builder.Services.AddSingleton<CompanyClock>();
builder.Services.AddScoped<IJobEvents, InProcessJobEvents>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IJobChangeHandler, DispatchHubBroadcaster>();
builder.Services.AddScoped<SuggestionExplainer>();
builder.Services.AddProblemDetails();

// Enums go over the wire as names ("Dispatched"), the same way they're stored, so the web app's
// types read like the domain.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Every endpoint lives under /api, which is the prefix the Next.js app rewrites to us.
var api = app.MapGroup("/api");
api.MapStatusEndpoints();
api.MapAuthEndpoints();
api.MapDevEndpoints(app.Environment);

// CRM data: signed-in users only, and every query inside goes through AccessScope.
var crm = api.MapGroup("").RequireAuthorization();
crm.MapCustomerEndpoints();
crm.MapBranchEndpoints();
crm.MapJobEndpoints();
crm.MapDispatchEndpoints();
crm.MapSuggestionEndpoints();
crm.MapKnowledgeEndpoints();
crm.MapAssistantEndpoints();

// Live updates for the dispatch board, behind the same cookie (DispatchHub is [Authorize]).
app.MapHub<DispatchHub>(DispatchHub.Path);

app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point, public so integration tests can boot it with WebApplicationFactory.</summary>
public partial class Program;
