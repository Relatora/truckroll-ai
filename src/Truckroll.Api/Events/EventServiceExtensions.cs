using Microsoft.Extensions.DependencyInjection.Extensions;
using Truckroll.Api.Features.Jobs;

namespace Truckroll.Api.Events;

/// <summary>Registers the outbox, its relay and the transport that delivers events (docs/adr/005).</summary>
public static class EventServiceExtensions
{
    public static IServiceCollection AddTruckrollEvents(this IServiceCollection services)
    {
        services.AddScoped<IJobEvents, OutboxJobEvents>();
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddSingleton<OutboxCommitInterceptor>();
        services.AddSingleton<IEventTransport, InProcessTransport>();
        services.AddSingleton<OutboxRelay>();
        services.AddHostedService(provider => provider.GetRequiredService<OutboxRelay>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
