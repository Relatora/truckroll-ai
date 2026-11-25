using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("truckroll")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8.12").Build();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_mysql.StartAsync(), _redis.StartAsync());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:truckroll", _mysql.GetConnectionString());
        builder.UseSetting("ConnectionStrings:redis", _redis.GetConnectionString());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_mysql.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}
