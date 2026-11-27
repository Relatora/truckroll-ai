// The whole local stack, described in one place.
//
// Read top to bottom, this is the architecture diagram: three containers (MySQL for records,
// Redis for retrieval and cache, Kafka for job events), the ASP.NET Core API that depends on
// them, and the Next.js app that talks to the API. Aspire starts them in dependency order,
// injects connection strings, and streams every resource's logs, traces and metrics into its
// dashboard.

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
    .WithDataVolume("truckroll-redis")
    // The image's own entrypoint loads the Query Engine module, but Aspire starts redis-server
    // with its own command line (for the password and TLS), which skips it. Ask for it by name.
    .WithModule(RedisModules.Search);

// Kafka carries job events to whoever needs them: the dispatch board, the search index and the
// notifications drafter, each as its own consumer group (docs/adr/009). The native image is
// Apache Kafka compiled ahead of time: it starts in about a second and idles in a fraction of
// the memory of the JVM broker, which matters on a laptop already running MySQL, Redis and a
// model.
//
// No data volume, on purpose: locally the broker starts empty each run. MySQL's outbox is the
// durable record, and every event waits there until Kafka has it, so nothing is lost. (The image
// also runs as an unprivileged user that can't write to a fresh Docker volume.)
var kafka = builder.AddKafka("kafka")
    .WithImage("apache/kafka-native", "4.4.0")
    // Aspire sets the listeners; the Apache image also needs to be told it's a single node that
    // is both broker and controller (KRaft, no ZooKeeper). The Confluent image Aspire defaults to
    // has these baked in; this one reads them from the environment.
    .WithEnvironment("KAFKA_LOG_DIRS", "/tmp/kafka-logs")
    .WithEnvironment("KAFKA_NODE_ID", "1")
    .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
    .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:29093")
    .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
    .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "PLAINTEXT")
    // One node, so one copy of the internal topics that track consumer groups' places.
    .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
    .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
    .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
    // Don't wait three seconds for more group members that will never come.
    .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0");

// Ollama runs on the host, not in a container, so it can use the GPU. Its address comes from
// configuration (ConnectionStrings:ollama in appsettings.json), which keeps a machine without
// a GPU free to point it somewhere else.
var ollama = builder.AddConnectionString("ollama");

var api = builder.AddDotnetProject("api", "../Truckroll.Api/Truckroll.Api.csproj")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithReference(kafka).WaitFor(kafka)
    .WithEnvironment("Events__Transport", "Kafka")
    .WithReference(ollama)
    .WithHttpHealthCheck("/health")
    // A dashboard button that rebuilds the demo data around today (the board is date-relative).
    .WithHttpCommand(
        path: "/api/dev/reset-demo",
        displayName: "Reset demo data",
        commandOptions: new HttpCommandOptions
        {
            Method = HttpMethod.Post,
            Description = "Drop the database and re-seed Kestrel Home Comfort for today.",
            ConfirmationMessage = "Wipe the database and re-seed the demo data?",
            IconName = "ArrowCounterclockwise",
        })
    // Rebuilds the Redis search index from MySQL: every manual section, unit and job, embedded.
    .WithHttpCommand(
        path: "/api/dev/reindex",
        displayName: "Rebuild knowledge index",
        commandOptions: new HttpCommandOptions
        {
            Method = HttpMethod.Post,
            Description = "Re-embed every document, unit and job into the Redis search index.",
            IconName = "DatabaseSearch",
        });

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
