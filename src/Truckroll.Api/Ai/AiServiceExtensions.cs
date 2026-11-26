using System.Data.Common;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace Truckroll.Api.Ai;

/// <summary>
/// Registers the two model services every AI feature uses: <see cref="IChatClient"/> and
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>. Feature code asks for those interfaces and
/// never learns which provider is behind them.
/// </summary>
/// <remarks>
/// <para>
/// Each service is two layers. At the bottom is the provider's own client (Ollama or Claude),
/// registered as a keyed service under <see cref="ProviderKey"/>. On top of it is a pipeline that
/// every call goes through: OpenTelemetry (a span per call with the model, token counts and
/// latency, visible in the Aspire dashboard) and then logging.
/// </para>
/// <para>
/// The split is what makes the tests honest. They replace only the keyed provider client with a
/// fake, so the pipeline they exercise is the production one.
/// </para>
/// </remarks>
public static class AiServiceExtensions
{
    /// <summary>The key the provider clients are registered under, below the pipeline.</summary>
    public const string ProviderKey = "ai-provider";

    private const string OllamaHttpKey = "ollama";

    public static IHostApplicationBuilder AddTruckrollAi(this IHostApplicationBuilder builder)
    {
        // Checked at startup, not on the first question: choosing Claude without a key should stop
        // the API with a message that says how to fix it.
        builder.Services.AddOptions<AiOptions>()
            .BindConfiguration(AiOptions.Section)
            .Validate(
                ai => ai.Provider != AiProvider.Claude || !string.IsNullOrWhiteSpace(ai.Claude.ApiKey),
                "AI:Provider is Claude, but AI:Claude:ApiKey isn't set. Run: dotnet user-secrets --project src/Truckroll.Api set \"AI:Claude:ApiKey\" <key>")
            .Validate(ai => ai.EmbeddingDimensions > 0, "AI:EmbeddingDimensions must be positive.")
            .ValidateOnStart();

        // Ollama's address is an Aspire connection string ("Endpoint=http://localhost:11434").
        // One HttpClient for it, with a timeout long enough for a model that's still loading into
        // memory, or a long streamed answer. The resilience handler that service defaults add to
        // factory-made clients gives up after 30 seconds, which is why this one isn't from the factory.
        var ollamaEndpoint = OllamaEndpoint(builder.Configuration);
        builder.Services.AddKeyedSingleton(OllamaHttpKey, (_, _) =>
            new HttpClient { BaseAddress = ollamaEndpoint, Timeout = TimeSpan.FromMinutes(5) });

        builder.Services.AddKeyedSingleton<IChatClient>(ProviderKey, (services, _) =>
        {
            var ai = services.GetRequiredService<IOptions<AiOptions>>().Value;
            return ai.Provider switch
            {
                AiProvider.Claude => new AnthropicClient { ApiKey = ai.Claude.ApiKey! }
                    .AsIChatClient(ai.Claude.Model, ai.Claude.MaxOutputTokens),
                _ => new OllamaApiClient(services.GetRequiredKeyedService<HttpClient>(OllamaHttpKey), ai.ChatModel),
            };
        });

        // Embeddings stay on Ollama whichever provider answers chat. Customer text never leaves the
        // machine to be embedded, and switching chat providers never means re-indexing.
        builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(ProviderKey, (services, _) =>
        {
            var ai = services.GetRequiredService<IOptions<AiOptions>>().Value;
            return new OllamaApiClient(services.GetRequiredKeyedService<HttpClient>(OllamaHttpKey), ai.EmbeddingModel);
        });

        // The pipelines. Prompts and answers stay out of the spans (sensitive data off): they can
        // hold customer details, and traces are kept longer and seen by more people than the CRM.
        builder.Services.AddChatClient(services => services.GetRequiredKeyedService<IChatClient>(ProviderKey))
            .UseOpenTelemetry(configure: telemetry => telemetry.EnableSensitiveData = false)
            .UseLogging();

        builder.Services.AddEmbeddingGenerator(services => services.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(ProviderKey))
            .UseOpenTelemetry(configure: telemetry => telemetry.EnableSensitiveData = false)
            .UseLogging();

        return builder;
    }

    /// <summary>Reads <c>ConnectionStrings:ollama</c>, defaulting to Ollama's standard local port.</summary>
    internal static Uri OllamaEndpoint(IConfiguration configuration)
    {
        var connection = new DbConnectionStringBuilder { ConnectionString = configuration.GetConnectionString("ollama") ?? "" };
        return connection.TryGetValue("Endpoint", out var endpoint) && endpoint is string text
            ? new Uri(text)
            : new Uri("http://localhost:11434");
    }
}
