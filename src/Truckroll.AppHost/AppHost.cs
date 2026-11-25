// The whole local stack, described in one place.
//
// Read top to bottom, this is the architecture diagram: two containers (MySQL for records,
// Redis for retrieval and cache), the ASP.NET Core API that depends on both, and the Next.js
// app that talks to the API. Aspire starts them in dependency order, injects connection
// strings, and streams every resource's logs, traces and metrics into its dashboard.

var builder = DistributedApplication.CreateBuilder(args);

// MySQL 8.4 is the current LTS line. The data volume keeps the seeded demo data between runs,
// so restarting the AppHost doesn't mean re-seeding.
var mysql = builder.AddMySql("mysql")
    .WithImageTag("8.4")
    .WithDataVolume("truckroll-mysql");

var database = mysql.AddDatabase("truckroll");

// Redis 8 ships the Query Engine (vector, full-text and tag search) in the open-source image,
// which is what retrieval needs. Pinned, because Aspire's default tag moves between releases.
var redis = builder.AddRedis("redis")
    .WithImageTag("8.12")
    .WithDataVolume("truckroll-redis");

// Ollama runs on the host, not in a container, so it can use the GPU. Its address comes from
// configuration (ConnectionStrings:ollama in appsettings.json), which keeps a machine without
// a GPU free to point it somewhere else.
var ollama = builder.AddConnectionString("ollama");

var api = builder.AddDotnetProject("api", "../Truckroll.Api/Truckroll.Api.csproj")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithReference(ollama)
    .WithHttpHealthCheck("/health");

// Next.js rewrites /api and /hubs to API_URL, so the browser only ever sees one origin and
// the sign-in cookie needs no CORS rules (ADR-006).
// A fixed port (3000) so bookmarks, Playwright and the demo script don't chase a new port
// on every run.
builder.AddNextJsApp("web", "../../web")
    .WithEndpoint("http", endpoint => endpoint.Port = 3000)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithReference(api).WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
