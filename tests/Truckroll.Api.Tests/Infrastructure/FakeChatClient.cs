using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Truckroll.Api.Tests.Infrastructure;

/// <summary>
/// A chat model for tests: it answers from a script, records every request, and streams its
/// answers word by word the way a real model does. Tests never call a real model.
/// </summary>
/// <remarks>
/// One instance serves the whole test run, and test classes run in parallel. So a test scripts a
/// reply for prompts containing something only it sends (a job number, a marker string), rather
/// than "the next request", which another test could take.
/// </remarks>
public sealed partial class FakeChatClient : IChatClient
{
    public const string ModelId = "fake-chat";

    private readonly Lock _gate = new();
    private readonly List<(Func<string, bool> When, string Reply)> _script = [];
    private readonly ChatClientMetadata _metadata = new("fake", defaultModelId: ModelId);

    /// <summary>Every request so far, oldest first.</summary>
    public ConcurrentQueue<FakeChatRequest> Requests { get; } = new();

    /// <summary>Answer <paramref name="reply"/> whenever the prompt matches. The latest rule wins.</summary>
    public void Reply(Func<string, bool> when, string reply)
    {
        lock (_gate)
        {
            _script.Add((when, reply));
        }
    }

    /// <summary>Answer <paramref name="reply"/> whenever the prompt contains <paramref name="marker"/>.</summary>
    public void Reply(string marker, string reply) => Reply(prompt => prompt.Contains(marker, StringComparison.Ordinal), reply);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = Answer(messages, options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = ModelId,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = Words().Count(text), TotalTokenCount = 10 + Words().Count(text) },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = Answer(messages, options);
        foreach (Match word in Words().Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Yield between pieces so callers see a real stream, not one synchronous burst.
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, word.Value) { ModelId = ModelId };
        }

        yield return new ChatResponseUpdate { ModelId = ModelId, FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? _metadata
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private string Answer(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var list = messages.ToList();
        Requests.Enqueue(new FakeChatRequest(list, options));

        var prompt = string.Join("\n", list.Select(m => m.Text));
        lock (_gate)
        {
            for (var i = _script.Count - 1; i >= 0; i--)
            {
                if (_script[i].When(prompt))
                {
                    return _script[i].Reply;
                }
            }
        }

        // Unscripted: a predictable answer that names what was asked, so a test that forgot to
        // script a reply fails with a readable message instead of a null.
        return $"Fake answer to: {list.LastOrDefault(m => m.Role == ChatRole.User)?.Text}";
    }

    // A word and the whitespace after it: the pieces a streamed answer arrives in.
    [GeneratedRegex(@"\S+\s*")]
    private static partial Regex Words();
}

/// <summary>One request the fake received.</summary>
public sealed record FakeChatRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);
