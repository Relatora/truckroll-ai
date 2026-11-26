using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Truckroll.Api.Ai;

namespace Truckroll.Api.Tests.Ai;

/// <summary>
/// The real registration, built without the rest of the API. Constructing a client never calls a
/// network, so these tests check what would answer, without asking it anything.
/// </summary>
public sealed class ProviderSwitchTests
{
    [Fact]
    public void Ollama_answers_chat_by_default()
    {
        using var host = Build([]);

        var chat = host.Services.GetRequiredService<IChatClient>();

        Assert.NotNull(chat.GetService<OllamaApiClient>());
        Assert.Equal("qwen2.5:7b", chat.GetService<ChatClientMetadata>()?.DefaultModelId);
        Assert.NotNull(chat.GetService<OpenTelemetryChatClient>());
    }

    [Fact]
    public void Claude_is_one_setting_away()
    {
        using var host = Build(new()
        {
            ["AI:Provider"] = "Claude",
            // A placeholder: nothing in this test sends a request.
            ["AI:Claude:ApiKey"] = "placeholder-not-a-key",
        });

        var chat = host.Services.GetRequiredService<IChatClient>();

        Assert.Null(chat.GetService<OllamaApiClient>());
        Assert.Equal("claude-sonnet-5-5", chat.GetService<ChatClientMetadata>()?.DefaultModelId);
        Assert.NotNull(chat.GetService<OpenTelemetryChatClient>());
    }

    [Fact]
    public void Embeddings_stay_local_when_Claude_answers_chat()
    {
        using var host = Build(new() { ["AI:Provider"] = "Claude", ["AI:Claude:ApiKey"] = "placeholder-not-a-key" });

        var embedder = host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        Assert.NotNull(embedder.GetService<OllamaApiClient>());
        Assert.Equal("nomic-embed-text", embedder.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void Choosing_Claude_without_a_key_stops_startup_with_the_fix()
    {
        using var host = Build(new() { ["AI:Provider"] = "Claude" });

        var error = Assert.Throws<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<AiOptions>>().Value);

        Assert.Contains("dotnet user-secrets", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Endpoint=http://gpu-box:11434", "http://gpu-box:11434/")]
    [InlineData(null, "http://localhost:11434/")]
    public void Ollama_address_comes_from_the_connection_string(string? connectionString, string expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:ollama"] = connectionString })
            .Build();

        Assert.Equal(new Uri(expected), AiServiceExtensions.OllamaEndpoint(configuration));
    }

    private static IHost Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTruckrollAi();
        return builder.Build();
    }
}
