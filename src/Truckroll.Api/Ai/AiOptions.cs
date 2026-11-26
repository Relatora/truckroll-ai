namespace Truckroll.Api.Ai;

/// <summary>Which service answers chat requests. Embeddings are always local (docs/adr/004).</summary>
public enum AiProvider
{
    /// <summary>A model on this machine through Ollama. The default: free, private, offline.</summary>
    Ollama,

    /// <summary>Anthropic's Claude, through the official SDK. Needs <c>AI:Claude:ApiKey</c>.</summary>
    Claude,
}

/// <summary>
/// The <c>AI</c> configuration section. Switching chat to Claude is one setting:
/// <c>AI:Provider=Claude</c>, plus a key kept in user secrets.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "AI";

    public AiProvider Provider { get; set; } = AiProvider.Ollama;

    /// <summary>The Ollama chat model, used when <see cref="Provider"/> is Ollama.</summary>
    public string ChatModel { get; set; } = "qwen2.5:7b";

    /// <summary>The Ollama embedding model, used whichever provider answers chat.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// How many numbers the embedding model returns. The Redis vector index is created with this
    /// width, so a model with a different width needs a re-index, and the check endpoint says so.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 768;

    public ClaudeOptions Claude { get; set; } = new();
}

/// <summary>The <c>AI:Claude</c> section.</summary>
public sealed class ClaudeOptions
{
    /// <summary>Never in appsettings: <c>dotnet user-secrets set "AI:Claude:ApiKey" ...</c>.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-5-5";

    /// <summary>Anthropic requires a ceiling on every request; this is the default one.</summary>
    public int MaxOutputTokens { get; set; } = 2048;
}
