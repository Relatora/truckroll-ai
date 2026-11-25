using System.Net.Http.Json;
using Truckroll.Api.Features.Status;
using Truckroll.Api.Tests.Infrastructure;

namespace Truckroll.Api.Tests.Features;

public sealed class StatusEndpointTests(TruckrollApiFactory factory)
{
    [Fact]
    public async Task Status_reports_mysql_and_redis_as_healthy()
    {
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<StatusResponse>("/api/status", TestContext.Current.CancellationToken);

        Assert.NotNull(status);
        Assert.Equal("Healthy", status.Status);
        Assert.Contains(status.Dependencies, d => d.Name.Contains("mysql", StringComparison.OrdinalIgnoreCase) && d.Status == "Healthy");
        Assert.Contains(status.Dependencies, d => d.Name.Contains("redis", StringComparison.OrdinalIgnoreCase) && d.Status == "Healthy");
    }
}
