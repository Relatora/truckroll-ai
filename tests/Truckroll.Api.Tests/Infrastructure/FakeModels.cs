using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Truckroll.Api.Ai;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>Swaps the real model clients for the fakes, keeping the production pipeline on top.</summary>
public static class FakeModels
{
    /// <summary>
    /// Replaces the provider clients registered under <see cref="AiServiceExtensions.ProviderKey"/>.
    /// The unkeyed <see cref="IChatClient"/> and embedder that features use are left alone, so
    /// tests go through the same telemetry and logging layers as production.
    /// </summary>
    public static IServiceCollection UseFakeModels(this IServiceCollection services)
    {
        services.AddSingleton<FakeChatClient>();
        services.AddSingleton(provider => new FakeEmbeddingGenerator(provider.GetRequiredService<IOptions<AiOptions>>().Value.EmbeddingDimensions));

        services.RemoveAllKeyed<IChatClient>(AiServiceExtensions.ProviderKey);
        services.AddKeyedSingleton<IChatClient>(AiServiceExtensions.ProviderKey, (provider, _) => provider.GetRequiredService<FakeChatClient>());

        services.RemoveAllKeyed<IEmbeddingGenerator<string, Embedding<float>>>(AiServiceExtensions.ProviderKey);
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(AiServiceExtensions.ProviderKey, (provider, _) => provider.GetRequiredService<FakeEmbeddingGenerator>());

        return services;
    }
}
