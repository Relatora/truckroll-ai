using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Truckroll.Eval;

/// <summary>
/// Resolves <c>{serial:Priya Raman/CAL-80}</c> and <c>{job:Priya Raman/CAL-80}</c> through the customer API.
/// </summary>
/// <remarks>
/// The demo data is built around the day it's seeded, so a serial number's year digits and the
/// job numbers change between resets. The questions name the customer and the model instead, and
/// this looks up today's numbers the way a person would: find the customer, open their record.
/// </remarks>
internal sealed partial class Placeholders(HttpClient api)
{
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    public async Task<string> ResolveAsync(string text)
    {
        foreach (Match match in Pattern().Matches(text))
        {
            if (!_resolved.TryGetValue(match.Value, out var value))
            {
                value = await LookUpAsync(match.Groups["kind"].Value, match.Groups["customer"].Value, match.Groups["model"].Value);
                _resolved[match.Value] = value;
            }

            text = text.Replace(match.Value, value, StringComparison.Ordinal);
        }

        return text;
    }

    private async Task<string> LookUpAsync(string kind, string fullName, string model)
    {
        var matches = await api.GetFromJsonAsync<JsonElement>($"/api/customers/search?q={Uri.EscapeDataString(fullName)}");
        // The full name, exactly: the generated customers share last names with the anchor ones.
        var customer = matches.EnumerateArray().FirstOrDefault(c => c.GetProperty("name").GetString() == fullName);
        if (customer.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"No customer named {fullName}.");
        }

        var detail = await api.GetFromJsonAsync<JsonElement>($"/api/customers/{customer.GetProperty("id").GetInt32()}");
        if (kind == "serial")
        {
            return detail.GetProperty("properties").EnumerateArray()
                .SelectMany(p => p.GetProperty("equipment").EnumerateArray())
                .Where(e => e.GetProperty("model").GetString()!.StartsWith(model, StringComparison.Ordinal))
                .Select(e => e.GetProperty("serialNumber").GetString()!)
                .FirstOrDefault() ?? throw new InvalidOperationException($"{fullName} has no {model}.");
        }

        // The most recent job on that model.
        return detail.GetProperty("timeline").EnumerateArray()
            .Where(j => j.GetProperty("equipment").GetString()?.Contains(model, StringComparison.Ordinal) == true)
            .OrderByDescending(j => j.GetProperty("windowStart").GetDateTimeOffset())
            .Select(j => j.GetProperty("number").GetString()!)
            .FirstOrDefault() ?? throw new InvalidOperationException($"{fullName} has no job on a {model}.");
    }

    [GeneratedRegex(@"\{(?<kind>serial|job):(?<customer>[A-Za-z ]+)/(?<model>[A-Z0-9-]+)\}")]
    private static partial Regex Pattern();
}
