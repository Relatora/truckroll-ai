using System.Text.Json.Serialization;
using Truckroll.Api.Access;
using Truckroll.Api.Ai;
using Truckroll.Api.Common;
using Truckroll.Api.Data;
using Truckroll.Api.Events;
using Truckroll.Api.Features.Assistant;
using Truckroll.Api.Features.Auth;
using Truckroll.Api.Features.Calls;
using Truckroll.Api.Features.Branches;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Features.Dashboard;
using Truckroll.Api.Features.Dev;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Dispatch.Suggestions;
using Truckroll.Api.Features.Field;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Features.Mcp;
using Truckroll.Api.Features.Notifications;
using Truckroll.Api.Features.Tokens;
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

// Job events: written to an outbox in the same transaction as the change, then relayed through
// Kafka to the dispatch board, the knowledge index and customer notifications (docs/adr/005, 009).
builder.AddTruckrollEvents();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IJobChangeHandler, DispatchHubBroadcaster>();
builder.Services.AddScoped<SuggestionExplainer>();
builder.Services.AddScoped<CallAnalyser>();

// MCP for AI clients: streamable HTTP, stateless (no server-side session to leak between users),
// every tool running as the personal token's owner (docs/MCP.md).
builder.Services.AddMcpServer(options => options.ServerInfo = new()
{
    Name = "truckroll",
    Title = "Truckroll",
    Version = "0.1.0",
    Description = "Kestrel Home Comfort's CRM: customers, jobs, the schedule, dispatch suggestions and the knowledge base, as the token's owner.",
})
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<TruckrollTools>();
builder.Services.AddProblemDetails();

// Enums go over the wire as names ("Dispatched"), the same way they're stored, so the web app's
// types read like the domain.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// The field app's web build runs on an origin of its own (Expo's dev server, during
// development), unlike the Next.js app, which shares the API's. It sends a token, never a
// cookie, so allowing its origin exposes nothing a cookie protects. Phones send no Origin at
// all and never touch this. Only /api/field opts in, and only listed origins get through.
builder.Services.AddCors(cors => cors.AddPolicy(FieldEndpoints.CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Field:AppOrigins").Get<string[]>() ?? [])
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST")));

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
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

// The technician app: a token, not a cookie, and technicians only (docs/adr/011).
api.MapFieldEndpoints();

// CRM data: signed-in users only, and every query inside goes through AccessScope.
var crm = api.MapGroup("").RequireAuthorization();
crm.MapCustomerEndpoints();
crm.MapBranchEndpoints();
crm.MapJobEndpoints();
crm.MapDispatchEndpoints();
crm.MapSuggestionEndpoints();
crm.MapKnowledgeEndpoints();
crm.MapAssistantEndpoints();
crm.MapCallEndpoints();
crm.MapTokenEndpoints();
crm.MapDashboardEndpoints();
crm.MapNotificationEndpoints();

// Live updates for the dispatch board, behind the same cookie (DispatchHub is [Authorize]).
app.MapHub<DispatchHub>(DispatchHub.Path);

// Tokens only: a browser cookie can't drive MCP.
app.MapMcp("/mcp").RequireAuthorization(PersonalTokenHandler.SchemeName);

app.MapDefaultEndpoints();

app.Run();

/// <summary>Entry point, public so integration tests can boot it with WebApplicationFactory.</summary>
public partial class Program;
