using System.Globalization;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Calls;

/// <summary>
/// What the model returns for a call. Every field is a string or a primitive on purpose: the
/// model's answer is untrusted input, so it's parsed and checked here, field by field, rather
/// than trusted to deserialize into enums and dates.
/// </summary>
public sealed record CallAnalysis(
    string? Summary,
    string? CallerName,
    string? Street,
    bool NewCustomer,
    string? JobType,
    string? Priority,
    string? Equipment,
    string? Symptoms,
    string? WindowDay,
    int? WindowStartHour,
    string? FollowUpSubject,
    string? FollowUpBody,
    bool SafetyConcern)
{
    public const int MaxFollowUpLength = 1500;

    /// <summary>What's wrong with this answer, in words the model can act on. Empty when it's usable.</summary>
    public List<string> Problems(DateOnly today)
    {
        var problems = new List<string>();
        Require(Summary, "summary", problems);
        Require(CallerName, "callerName", problems);
        Require(FollowUpSubject, "followUpSubject", problems);
        Require(FollowUpBody, "followUpBody", problems);

        if (ParsedType is null)
        {
            problems.Add($"jobType must be one of {string.Join(", ", Enum.GetNames<JobType>())}, not \"{JobType}\".");
        }

        if (ParsedPriority is null)
        {
            problems.Add($"priority must be one of {string.Join(", ", Enum.GetNames<JobPriority>())}, not \"{Priority}\".");
        }

        if (!string.IsNullOrWhiteSpace(Equipment) && Equipment != "None" && ParsedEquipment is null)
        {
            problems.Add($"equipment must be one of {string.Join(", ", Enum.GetNames<EquipmentKind>())} or None, not \"{Equipment}\".");
        }

        if (!string.IsNullOrWhiteSpace(WindowDay) && DateFor(today) is null)
        {
            problems.Add($"windowDay must be today, tomorrow, a weekday name like Wednesday, or null; not \"{WindowDay}\".");
        }

        if (WindowStartHour is { } hour && !JobEndpoints.WindowStarts.Contains(hour))
        {
            problems.Add($"windowStartHour must be 8, 10, 12, 14 or null, not {hour}.");
        }

        if (FollowUpBody?.Length > MaxFollowUpLength)
        {
            problems.Add("followUpBody is too long; keep it under 120 words.");
        }

        return problems;
    }

    public JobType? ParsedType => Enum.TryParse<JobType>(JobType, ignoreCase: true, out var t) && Enum.IsDefined(t) ? t : null;

    public JobPriority? ParsedPriority => Enum.TryParse<JobPriority>(Priority, ignoreCase: true, out var p) && Enum.IsDefined(p) ? p : null;

    public EquipmentKind? ParsedEquipment => Enum.TryParse<EquipmentKind>(Equipment, ignoreCase: true, out var e) && Enum.IsDefined(e) ? e : null;

    /// <summary>
    /// The agreed day as a date. The model says the day in words and code does the calendar:
    /// asked for dates, qwen2.5:7b turned "Friday" into a Monday even with the week listed. A
    /// weekday means the next one after today ("Friday" said on a Friday is next week's).
    /// </summary>
    public DateOnly? DateFor(DateOnly today)
    {
        var day = WindowDay?.Trim().ToLowerInvariant();
        switch (day)
        {
            case null or "":
                return null;
            case "today":
                return today;
            case "tomorrow":
                return today.AddDays(1);
        }

        if (int.TryParse(day, out _) || !Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var weekday) || !Enum.IsDefined(weekday))
        {
            return null;
        }

        var ahead = ((int)weekday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(ahead == 0 ? 7 : ahead);
    }

    private static void Require(string? value, string name, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{name} is missing.");
        }
    }
}

/// <summary>A home that might be the caller's, as the CSR sees it in the review form.</summary>
public sealed record CustomerOption(int CustomerId, string Name, int PropertyId, string Address, List<EquipmentOption> Equipment);

public sealed record EquipmentOption(int Id, EquipmentKind Kind, string Label);

/// <summary>
/// The reviewed-and-checked proposal for a call: the model's reading, with code's matching,
/// safety rule and validation applied. Stored as JSON on the call until a person decides.
/// </summary>
/// <param name="Notes">What code changed or wants a person to know ("Raised to Emergency: gas smell").</param>
public sealed record CallProposal(
    string CallerName,
    string? Street,
    string? CallerEmail,
    bool NewCustomer,
    CustomerOption? Match,
    List<CustomerOption> Candidates,
    JobType Type,
    JobPriority Priority,
    EquipmentKind? Equipment,
    int? EquipmentId,
    string Symptoms,
    DateOnly? Date,
    int? WindowStartHour,
    bool SafetyConcern,
    List<string> Notes,
    string? Model,
    int Attempts);
