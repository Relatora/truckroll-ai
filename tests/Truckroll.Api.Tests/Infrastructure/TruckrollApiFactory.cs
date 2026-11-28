using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Testcontainers.MySql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Truckroll.Api.Tests.Infrastructure.TruckrollApiFactory))]

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real API against real MySQL and Redis containers, once for the whole test run.
/// </summary>
/// <remarks>
/// <para>
/// Real containers, not in-memory fakes: the bugs that matter in this codebase live in SQL
/// translation, MySQL collation, and Redis query syntax, and an in-memory provider would
/// hide every one of them.
/// </para>
/// <para>
/// The image tags are pinned on purpose. Testcontainers defaults to <c>redis:7.0</c>, which has
/// no Query Engine, so vector search tests would fail for reasons unrelated to the code.
/// </para>
/// </remarks>
public sealed class TruckrollApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The day the test database's demo data is built around.</summary>
    public static readonly DateOnly DemoToday = new(2026, 11, 25);

    /// <summary>Where the field app's web build is served from in development (Expo's port).</summary>
    public const string FieldAppOrigin = "http://localhost:8081";

    // Root, so tests that need a database of their own (the seeding test) can create one.
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("truckroll")
        .WithUsername("root")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8.12").Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _redis.StartAsync());

        // Start the host now, once, so migrations and the demo seed run before any test. Left to
        // the first test that touches it, parallel test classes race to start it and seed twice.
        _ = Services;
    }

    /// <summary>A connection string to another, empty database on the same MySQL server.</summary>
    public string ConnectionStringFor(string database) =>
        new MySqlConnector.MySqlConnectionStringBuilder(_mysql.GetConnectionString())
        {
            Database = database,
            AllowUserVariables = true,
            UseAffectedRows = false,
        }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:truckroll", _mysql.GetConnectionString());
        builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());

        // The demo company is seeded into the test database too, pinned to one day, so tests can
        // sign in as the demo users and refer to anchor customers by name.
        builder.UseSetting("Demo:Today", DemoToday.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        // The field app's web build, as the AppHost allows it locally.
        builder.UseSetting("Field:AppOrigins:0", FieldAppOrigin);

        // No real models in tests. The fakes replace the provider clients, and Ollama's address
        // points at a closed port, so anything that slipped past the fakes fails fast and loudly
        // instead of quietly calling a model on the developer's machine.
        builder.UseSetting("ConnectionStrings:ollama", "Endpoint=http://127.0.0.1:9");
        builder.ConfigureTestServices(services => services.UseFakeModels());
    }

    /// <summary>The scripted chat model every test shares.</summary>
    public FakeChatClient Chat => Services.GetRequiredService<FakeChatClient>();

    /// <summary>The deterministic embedder every test shares.</summary>
    public FakeEmbeddingGenerator Embedder => Services.GetRequiredService<FakeEmbeddingGenerator>();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}
