using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Truckroll.Api.Features.Dispatch.Suggestions;

/// <summary>One sentence per technician, and whether the model or the rules wrote it.</summary>
public sealed record Explanation(int TechnicianId, string Sentence, string WrittenBy);

/// <summary>
/// Asks the model for one sentence about each of the top technicians, then fits the answer back
/// onto the ranking the code already decided. The model can't reorder, add or drop anyone.
/// </summary>
public sealed class SuggestionExplainer(IChatClient chat, ILogger<SuggestionExplainer> logger)
{
    public const int MaxSentenceLength = 280;

    private const string Rules =
        """
        You explain a dispatch decision that has already been made. You don't change it.
        For each technician, write one plain sentence (at most 30 words) for a dispatcher, built from that technician's facts: where they're coming from and how far, when they'd arrive, how many other jobs they have that day, and any later job it would make them late for. Say if this isn't their usual kind of work.
        Everyone listed holds the licence and is free in the window: never say someone can't do the job.
        Use only the facts given and copy numbers exactly. Don't compare technicians, don't mention scores or ranks, and don't invent anything.
        """;

    /// <summary>What the model must return.</summary>
    public sealed record ModelAnswer(List<ModelSentence> Sentences);

    public sealed record ModelSentence(int TechnicianId, string Sentence);

    public async Task<IReadOnlyList<Explanation>> ExplainAsync(
        IReadOnlyList<ScoredTechnician> top, IReadOnlyList<string> rulesSentences, string jobSummary, CancellationToken cancellationToken)
    {
        Dictionary<int, string> written = [];
        try
        {
            var facts = top.Select(t => new
            {
                technicianId = t.TechnicianId,
                name = t.Name,
                usuallyDoesThisWork = t.Facts.HasSkill,
                kilometres = t.Facts.Kilometres,
                comingFrom = t.Facts.ComingFrom,
                freeFrom = DispatchScorer.Clock(t.Facts.FreeFrom),
                arrives = DispatchScorer.Clock(t.Facts.Arrives),
                minutesLate = t.Facts.MinutesLate,
                otherJobsThatDay = t.Facts.JobsThatDay,
                wouldMakeThemLateFor = t.Facts.MakesLateFor,
            });

            var response = await chat.GetResponseAsync<ModelAnswer>(
                [
                    new ChatMessage(ChatRole.System, Rules),
                    // Naming the ids matters: asked only for "an entry per technician", qwen2.5:7b under a
                    // JSON schema answered with an empty list.
                    new ChatMessage(ChatRole.User,
                        $"Job: {jobSummary}\n\nTechnicians:\n{JsonSerializer.Serialize(facts)}\n\n" +
                        $"Return one entry in \"sentences\" for each of these technicianIds, in this order: {string.Join(", ", top.Select(t => t.TechnicianId))}."),
                ],
                new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 400 },
                cancellationToken: cancellationToken);

            if (response.TryGetResult(out var answer))
            {
                var allowed = top.Select(t => t.TechnicianId).ToHashSet();
                written = (answer.Sentences ?? [])
                    .Where(s => allowed.Contains(s.TechnicianId) && !string.IsNullOrWhiteSpace(s.Sentence) && s.Sentence.Length <= MaxSentenceLength)
                    .GroupBy(s => s.TechnicianId)
                    .ToDictionary(g => g.Key, g => g.First().Sentence.Trim());
            }

            if (written.Count < top.Count)
            {
                // Worth seeing when tuning the prompt; the reply holds no customer details.
                logger.LogWarning("The model's dispatch explanations were partly unusable; {Missing} fall back to rules. Reply: {Reply}",
                    top.Count - written.Count, response.Text.Length > 600 ? response.Text[..600] : response.Text);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No model, or nonsense back: the rules sentences stand on their own.
            logger.LogWarning(exception, "Couldn't get dispatch explanations from the model; using the rules sentences");
        }

        // The code's order, always. Whatever the model said comes back in this shape or not at all.
        return top.Select((t, i) => written.TryGetValue(t.TechnicianId, out var sentence)
                ? new Explanation(t.TechnicianId, sentence, "model")
                : new Explanation(t.TechnicianId, rulesSentences[i], "rules"))
            .ToList();
    }
}
