using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Truckroll.Api.Tests.Infrastructure;

namespace Truckroll.Api.Tests.Ai;

/// <summary>The model services as the running API sees them: fakes underneath, production pipeline on top.</summary>
public sealed class ModelPipelineTests(TruckrollApiFactory factory)
{
    private IChatClient Chat => factory.Services.GetRequiredService<IChatClient>();

    private IEmbeddingGenerator<string, Embedding<float>> Embedder =>
        factory.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    [Fact]
    public void Tests_talk_to_the_fake_chat_model_through_the_real_pipeline()
    {
        Assert.NotNull(Chat.GetService<OpenTelemetryChatClient>());
        Assert.NotNull(Chat.GetService<FakeChatClient>());

        // The guard: if a real provider client is ever constructed for a test, this fails.
        Assert.Null(Chat.GetService<OllamaApiClient>());
    }

    [Fact]
    public void Tests_talk_to_the_fake_embedder_through_the_real_pipeline()
    {
        Assert.NotNull(Embedder.GetService<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>());
        Assert.NotNull(Embedder.GetService<FakeEmbeddingGenerator>());
        Assert.Null(Embedder.GetService<OllamaApiClient>());
    }

    [Fact]
    public async Task A_model_call_leaves_a_span_without_the_prompt_in_it()
    {
        const string marker = "span-check-7d1f";
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Experimental.Microsoft.Extensions.AI",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await Chat.GetResponseAsync($"Customer 905-555-0142 asks about {marker}", cancellationToken: TestContext.Current.CancellationToken);

        var span = Assert.Single(spans, s => s.DisplayName == $"chat {FakeChatClient.ModelId}" && s.GetTagItem("gen_ai.usage.output_tokens") is not null
            && s.TagObjects.All(tag => tag.Value?.ToString()?.Contains(marker, StringComparison.Ordinal) != true));
        Assert.Equal(FakeChatClient.ModelId, span.GetTagItem("gen_ai.request.model"));

        // Sensitive data is off: no tag or event on any span carries the prompt.
        Assert.DoesNotContain(spans, s =>
            s.TagObjects.Any(tag => tag.Value?.ToString()?.Contains(marker, StringComparison.Ordinal) == true)
            || s.Events.Any(e => e.Tags.Any(tag => tag.Value?.ToString()?.Contains(marker, StringComparison.Ordinal) == true)));
    }

    [Fact]
    public async Task A_streamed_answer_arrives_in_pieces_and_adds_up()
    {
        const string reply = "Check the flame sensor first, then the ignitor.";
        factory.Chat.Reply("stream-check-3a9c", reply);

        var pieces = new List<string>();
        await foreach (var update in Chat.GetStreamingResponseAsync("stream-check-3a9c", cancellationToken: TestContext.Current.CancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                pieces.Add(update.Text);
            }
        }

        Assert.True(pieces.Count > 1, "A streamed answer should arrive in more than one piece.");
        Assert.Equal(reply, string.Concat(pieces));
    }

    [Fact]
    public async Task The_fake_records_what_it_was_asked()
    {
        const string marker = "record-check-51be";
        await Chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, "You are Truckroll."), new ChatMessage(ChatRole.User, marker)],
            new ChatOptions { MaxOutputTokens = 50 },
            TestContext.Current.CancellationToken);

        var request = Assert.Single(factory.Chat.Requests, r => r.Messages.Any(m => m.Text == marker));
        Assert.Equal(ChatRole.System, request.Messages[0].Role);
        Assert.Equal(50, request.Options?.MaxOutputTokens);
    }
}
