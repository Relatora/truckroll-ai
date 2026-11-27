using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Knowledge;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Calls;

/// <summary>The result of analysing a call: a proposal for a person to review, or why there isn't one.</summary>
public sealed record AnalysisOutcome(CallProposal? Proposal, string? Summary, string? FollowUpSubject, string? FollowUpBody, List<string> Problems);

/// <summary>
/// Reads a call transcript with the model and turns its answer into a proposal a CSR can approve:
/// masked going in, validated coming out, matched and safety-checked by code.
/// </summary>
public sealed partial class CallAnalyser(IChatClient chat, TruckrollDbContext db, CompanyClock clock, ILogger<CallAnalyser> logger)
{
    /// <summary>One try, plus one retry with the problems spelled out. Then a person takes over.</summary>
    public const int MaxAttempts = 2;

    public async Task<AnalysisOutcome> AnalyseAsync(CallRecord call, AccessScope scope, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var vault = new PiiVault();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Rules(today, call.Branch)),
            new(ChatRole.User, $"Transcript:\n{vault.Mask(call.Transcript)}"),
        };
        // Temperature 0: the same call should get the same reading, and the eval should be repeatable.
        var options = new ChatOptions { Temperature = 0f, MaxOutputTokens = 900 };

        CallAnalysis? analysis = null;
        var problems = new List<string>();
        var attempts = 0;
        while (attempts < MaxAttempts)
        {
            attempts++;
            ChatResponse<CallAnalysis> response;
            try
            {
                response = await chat.GetResponseAsync<CallAnalysis>(messages, options, cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Call analysis failed to reach the model");
                return Failed(["The model isn't reachable right now. Try again in a minute."]);
            }

            problems = response.TryGetResult(out var answer) ? answer.Problems(today) : ["The answer wasn't valid JSON for the schema."];
            if (problems.Count == 0)
            {
                analysis = answer;
                break;
            }

            logger.LogWarning("Call analysis attempt {Attempt} had problems: {Problems}", attempts, string.Join(" ", problems));
            // Show the model its own answer and exactly what to fix: small models fix a named
            // field far more reliably than they redo a whole answer.
            messages.Add(new ChatMessage(ChatRole.Assistant, response.Text));
            messages.Add(new ChatMessage(ChatRole.User, $"That answer had problems:\n- {string.Join("\n- ", problems)}\nAnswer again with the same schema, fixing only those."));
        }

        if (analysis is null)
        {
            return Failed(problems);
        }

        var proposal = await ProposeAsync(analysis, call, scope, vault, attempts, cancellationToken);
        return new AnalysisOutcome(
            proposal,
            vault.Restore(analysis.Summary!.Trim()),
            vault.Restore(analysis.FollowUpSubject!.Trim()),
            SignedOff(vault.Restore(analysis.FollowUpBody!.Trim()), call.Branch),
            []);
    }

    private static AnalysisOutcome Failed(List<string> problems) => new(null, null, null, null, problems);

    /// <summary>
    /// The model's reading, checked against the CRM: who the caller is, which unit, and the one
    /// rule no model gets to bend (a gas smell is always an emergency).
    /// </summary>
    private async Task<CallProposal> ProposeAsync(CallAnalysis analysis, CallRecord call, AccessScope scope, PiiVault vault, int attempts, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var callerName = vault.Restore(analysis.CallerName!.Trim());
        var street = analysis.Street is null ? null : vault.Restore(analysis.Street.Trim());

        // Customer: by last name in the call's branch, then by street when more than one fits.
        var lastName = callerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        var customers = await scope.Customers(db).AsNoTracking()
            .Where(c => c.BranchId == call.BranchId && c.LastName == lastName)
            .Include(c => c.Properties).ThenInclude(p => p.Equipment)
            .ToListAsync(cancellationToken);
        var candidates = customers
            .SelectMany(c => c.Properties.Select(p => new CustomerOption(
                c.Id, c.FullName, p.Id, p.ShortAddress,
                p.Equipment.Select(e => new EquipmentOption(e.Id, e.Kind, $"{e.Brand} {e.Model}")).ToList())))
            .ToList();

        // Several people with that last name: the street decides, then the full name, then who
        // owns the kind of unit the call is about. Anything still ambiguous goes to the CSR.
        var onStreet = candidates.Where(c => SameStreet(c.Address, street)).ToList();
        var byFullName = candidates.Where(c => string.Equals(c.Name, callerName, StringComparison.OrdinalIgnoreCase)).ToList();
        var withUnit = candidates.Where(c => c.Equipment.Any(e => e.Kind == analysis.ParsedEquipment)).ToList();
        var match = onStreet.Count == 1 ? onStreet[0]
            : candidates.Count == 1 ? candidates[0]
            : onStreet.Count == 0 && byFullName.Count == 1 ? byFullName[0]
            : onStreet.Count == 0 && withUnit.Count == 1 ? withUnit[0]
            : null;
        if (match is not null)
        {
            notes.Add($"Matched to {match.Name} at {match.Address} by {(onStreet.Count == 1 ? "name and street" : byFullName.Count == 1 ? "full name" : "name")}.");
        }
        else if (candidates.Count > 1)
        {
            notes.Add($"{candidates.Count} homes match the name {lastName}. Pick the right one.");
        }
        else
        {
            notes.Add("No customer with that name in this branch: likely a new caller.");
        }

        // Equipment: the matched home's unit of that kind. Tank and tankless are both "the water
        // heater" to a caller, so either counts when the exact kind isn't there.
        var kind = analysis.ParsedEquipment;
        var unit = match?.Equipment.FirstOrDefault(e => e.Kind == kind)
            ?? match?.Equipment.FirstOrDefault(e => IsWaterHeater(e.Kind) && kind is { } k && IsWaterHeater(k));

        // Safety: policy P-103 says a gas smell or CO alarm is never booked below Emergency. Code
        // checks the transcript itself, so a model that misses it can't lower the priority.
        var priority = analysis.ParsedPriority!.Value;
        var safety = analysis.SafetyConcern || SafetyWords().IsMatch(CallerSide(call.Transcript));
        if (safety && priority != JobPriority.Emergency)
        {
            notes.Add($"Raised from {priority} to Emergency: gas smell or CO alarm (policy P-103).");
            priority = JobPriority.Emergency;
        }

        if (safety)
        {
            notes.Add("Safety call: confirm the utility or fire department has made the home safe before the visit.");
        }

        return new CallProposal(
            callerName,
            street,
            vault.Emails.FirstOrDefault(),
            analysis.NewCustomer || (match is null && candidates.Count == 0),
            match,
            candidates,
            analysis.ParsedType!.Value,
            priority,
            kind,
            unit?.Id,
            // A tune-up has no symptoms to report; the summary stands in.
            vault.Restore((string.IsNullOrWhiteSpace(analysis.Symptoms) ? analysis.Summary! : analysis.Symptoms).Trim()),
            analysis.DateFor(clock.Today),
            analysis.WindowStartHour,
            safety,
            notes,
            chat.GetService<ChatClientMetadata>()?.DefaultModelId,
            attempts);
    }

    /// <summary>"14 Maple St" and "14 Maple Street", or "Birch Cres" when the caller gave no number.</summary>
    internal static bool SameStreet(string address, string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return false;
        }

        var said = Words().Matches(spoken.ToLowerInvariant()).Select(m => m.Value).ToList();
        var have = Words().Matches(address.ToLowerInvariant()).Select(m => m.Value).ToList();
        var number = said.FirstOrDefault(w => w.All(char.IsAsciiDigit));
        var name = said.FirstOrDefault(w => !w.All(char.IsAsciiDigit));
        return name is not null && have.Contains(name) && (number is null || have.Contains(number));
    }

    /// <summary>
    /// What the caller said. The office asks "is there any smell of gas?" on every no-heat call,
    /// so the safety check listens to the caller's lines only (or everything, if the transcript
    /// has no speaker labels).
    /// </summary>
    internal static string CallerSide(string transcript)
    {
        var lines = transcript.Split('\n');
        var callerLines = lines.Where(l => l.TrimStart().StartsWith("Caller:", StringComparison.OrdinalIgnoreCase)).ToList();
        return callerLines.Count > 0 ? string.Join('\n', callerLines) : transcript;
    }

    private static bool IsWaterHeater(EquipmentKind kind) => kind is EquipmentKind.WaterHeater or EquipmentKind.TanklessWaterHeater;

    /// <summary>
    /// The instructions. The field rules mirror the company's policies (P-101 for priority, P-106
    /// for the follow-up) so the draft reads like a CSR who knows the handbook wrote it.
    /// </summary>
    /// <summary>Policy P-106: every follow-up signs off with the branch and its number.</summary>
    internal static string SignedOff(string body, Branch branch)
    {
        var signOff = $"Kestrel Home Comfort, {branch.Name}, {branch.Phone}";
        return body.Contains(branch.Phone, StringComparison.Ordinal) ? body : $"{body.TrimEnd()}\n\n{signOff}";
    }

    private static string Rules(DateOnly today, Branch branch) =>
        $"""
        You turn a phone call to Kestrel Home Comfort (heating, cooling and plumbing, Ontario) into a record for a customer service rep to review.
        Today is {today.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)}. The call was to the {branch.Name} branch.

        Fill every field from the transcript only:
        - summary: two or three sentences on what the caller needs.
        - callerName: the caller's full name as they said it.
        - street: house number and street as said, like "14 Maple St", or null if not given.
        - newCustomer: true only if the caller says they aren't a customer yet.
        - jobType: Repair when something is broken, leaking, noisy, not working or showing an error code; Maintenance only for a planned tune-up or service with nothing wrong; Installation; or Estimate for a quote on new equipment. Never None. For example: pressure dropping, an error code, a leak or no heat is a Repair; a yearly furnace tune-up is Maintenance.
        - priority: Emergency for no heat with a baby, senior or medically vulnerable person, no heat below 0 C, any gas smell or carbon monoxide alarm, or an active leak. Urgent if it should be within two days. Otherwise Normal.
        - equipment: the one unit the visit is for (for an estimate, the equipment being quoted), as Furnace, AirConditioner, HeatPump, Boiler, WaterHeater (tank), TanklessWaterHeater, Humidifier, AirExchanger, or None. Mention other units in symptoms.
        - symptoms: a one-line ticket summary in the caller's own words (for a tune-up, what they asked for).
        - windowDay: the day agreed or promised for the visit, in words: today, tomorrow, or a weekday name like Wednesday ("we'll send someone today" is today). Null if no day was settled ("next week", "we'll confirm by email").
        - windowStartHour: 8, 10, 12 or 14 for the two-hour window the CSR offered and the caller accepted (8 to 10 is 8, noon to two is 12, two to four is 14). Null if no window was agreed, even if the caller said a preference.
        - followUpSubject and followUpBody: an email to the caller. Thank them by name, restate the problem in their words, confirm the agreed window or say we'll call to book, acknowledge a repeat problem plainly, mention what their membership covers only if the CSR said so on the call. Sign off "Kestrel Home Comfort, {branch.Name}, {branch.Phone}". Under 120 words, no marketing.
        - safetyConcern: true only if the caller reports a gas smell or a carbon monoxide alarm. The CSR asking about gas doesn't count.

        Keep tokens like [phone 1] or [email 1] exactly as written. Never invent details that weren't said.
        """;

    [GeneratedRegex(@"\b(smell\w*\s+(of\s+)?gas|gas\s+smell|carbon\s+monoxide|CO\s+(alarm|detector))\b", RegexOptions.IgnoreCase)]
    private static partial Regex SafetyWords();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
