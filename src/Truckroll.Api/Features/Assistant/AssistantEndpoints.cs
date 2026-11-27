using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Truckroll.Api.Access;
using Truckroll.Api.Ai;
using Truckroll.Api.Common;
using Truckroll.Api.Knowledge;

namespace Truckroll.Api.Features.Assistant;

/// <summary>
/// "Ask Truckroll": a question in, an answer out, streamed, with every claim tied to a passage the
/// asker is allowed to open.
/// </summary>
public static class AssistantEndpoints
{
    public const int SourcesPerAnswer = 6;
    public const int MaxQuestionLength = 1000;
    public const int MaxHistoryTurns = 12;
    public const int MaxTurnLength = 4000;

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/assistant/ask", Ask)
            .WithSummary("Answer a question from the knowledge index, streamed as server-sent events: sources, then deltas, then done.");
        return routes;
    }

    internal static Results<ServerSentEventsResult<object>, ValidationProblem> Ask(
        AskRequest request,
        ClaimsPrincipal user,
        KnowledgeSearch search,
        IChatClient chat,
        IOptions<AiOptions> options,
        CompanyClock clock,
        ILogger<AskRequest> logger,
        CancellationToken cancellationToken)
    {
        var history = request.History ?? [];
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            errors["question"] = ["Type a question."];
        }
        else if (request.Question.Length > MaxQuestionLength)
        {
            errors["question"] = [$"Keep the question under {MaxQuestionLength} characters."];
        }

        if (history.Count > MaxHistoryTurns || history.Any(t => t.Text.Length > MaxTurnLength || t.Role is not ("user" or "assistant")))
        {
            errors["history"] = ["The conversation is too long. Start a new one."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var answer = new Answerer(search, chat, options.Value, clock.Today, logger);
        return TypedResults.ServerSentEvents(answer.StreamAsync(request.Question.Trim(), history, AccessScope.FromPrincipal(user), cancellationToken));
    }

    /// <summary>One answer, as a sequence of events. A class so the steps read top to bottom.</summary>
    private sealed class Answerer(KnowledgeSearch search, IChatClient chat, AiOptions ai, DateOnly today, ILogger logger)
    {
        public async IAsyncEnumerable<SseItem<object>> StreamAsync(
            string rawQuestion,
            IReadOnlyList<AskTurn> history,
            AccessScope scope,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();

            // A phone number typed into the box goes no further than this line.
            var question = PiiMasker.Mask(rawQuestion);

            // 1. Sources first: the page shows what the answer will be built from while the model works.
            IReadOnlyList<SearchHit> hits = [];
            Exception? failure = null;
            try
            {
                hits = await search.SearchAsync(AssistantPrompt.SearchTextFor(question, history), scope, SourcesPerAnswer, cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failure = exception;
            }

            if (failure is not null)
            {
                logger.LogWarning(failure, "Search failed for an assistant question");
                yield return Event("error", new AnswerError("Search isn't available right now. Is the embedding model running?"));
                yield break;
            }

            yield return Event("sources", hits.Select((hit, i) => Citation.From(i + 1, hit)).ToList());

            // 2. Nothing the asker may see: say so without calling the model, which, given no
            // sources, is the surest way to get an invented answer.
            if (hits.Count == 0)
            {
                yield return Event("delta", new AnswerDelta(AssistantPrompt.NothingFound));
                yield return Event("done", new AnswerDone(null, ai.Provider.ToString(), clock.ElapsedMilliseconds, null, null, Grounded: false));
                yield break;
            }

            // 3. The answer, piece by piece.
            var messages = AssistantPrompt.Build(question, history, hits, today);
            var settings = new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 700 };
            string? model = null;
            UsageDetails? usage = null;

            await using var updates = chat.GetStreamingResponseAsync(messages, settings, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }

                    update = updates.Current;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failure = exception;
                    break;
                }

                model ??= update.ModelId;
                usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return Event("delta", new AnswerDelta(update.Text));
                }
            }

            if (failure is not null)
            {
                // The response is already a 200 stream, so the failure travels as an event.
                logger.LogWarning(failure, "The chat model failed mid-answer");
                yield return Event("error", new AnswerError("The model stopped answering. Try again in a moment."));
                yield break;
            }

            model ??= chat.GetService<ChatClientMetadata>()?.DefaultModelId;
            yield return Event("done", new AnswerDone(model, ai.Provider.ToString(), clock.ElapsedMilliseconds, usage?.InputTokenCount, usage?.OutputTokenCount, Grounded: true));
        }

        private static SseItem<object> Event(string type, object data) => new(data, type);
    }
}

/// <summary>A question, and the conversation before it (kept by the page, not the server).</summary>
public sealed record AskRequest(string Question, List<AskTurn>? History);

/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
public sealed record AskTurn(string Role, string Text)
{
    public bool IsUser => Role == "user";
}

/// <summary>One numbered source, sent before the answer. <see cref="Link"/> opens the record it came from.</summary>
public sealed record Citation(int Number, string Record, PassageSource Source, string Title, string Text, string? Link)
{
    public static Citation From(int number, SearchHit hit)
    {
        var id = hit.Record[(hit.Record.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var link = hit.Source switch
        {
            PassageSource.Job => $"/jobs/{id}",
            PassageSource.Equipment when hit.CustomerId is { } customer => $"/customers/{customer}",
            _ => null,
        };
        return new Citation(number, hit.Record, hit.Source, hit.Title, hit.Text, link);
    }
}

public sealed record AnswerDelta(string Text);

/// <param name="Grounded">False when there were no sources and the model wasn't asked.</param>
public sealed record AnswerDone(string? Model, string Provider, long Milliseconds, long? InputTokens, long? OutputTokens, bool Grounded);

public sealed record AnswerError(string Message);
