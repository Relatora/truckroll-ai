using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Truckroll.Api.Features.Status;

/// <summary>
/// <c>GET /api/status</c>: what's running and whether its dependencies answer.
/// </summary>
/// <remarks>
/// The web app's footer and the onboarding guide use this to show "API connected, MySQL up,
/// Redis up" at a glance. Unlike <c>/health</c>, it's meant for people, so it returns each
/// dependency by name instead of one status code.
/// </remarks>
public static class StatusEndpoints
{
    public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/status", GetStatusAsync)
            .WithName("GetStatus")
            .WithSummary("Service version and the health of each dependency.");

        return routes;
    }

    internal static async Task<StatusResponse> GetStatusAsync(
        HealthCheckService health, IHostEnvironment environment, CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken);

        var dependencies = report.Entries
            .Where(entry => entry.Key != "self")
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new DependencyStatus(entry.Key, entry.Value.Status.ToString()))
            .ToList();

        return new StatusResponse(
            Service: "truckroll-api",
            Version: Version,
            Environment: environment.EnvironmentName,
            Status: report.Status.ToString(),
            Dependencies: dependencies);
    }

    private static readonly string Version =
        typeof(StatusEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}

/// <summary>The API's self-description, as shown in the web app's status indicator.</summary>
public sealed record StatusResponse(
    string Service,
    string Version,
    string Environment,
    string Status,
    IReadOnlyList<DependencyStatus> Dependencies);

/// <summary>One dependency (a database, a cache) and whether its health check passed.</summary>
public sealed record DependencyStatus(string Name, string Status);
