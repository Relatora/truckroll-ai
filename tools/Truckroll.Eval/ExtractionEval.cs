using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Truckroll.Eval;

/// <summary>
/// The extraction eval: real calls through the real model, scored field by field against what a
/// careful CSR would record. Needs the stack running, because the point is the real model.
/// </summary>
internal static class ExtractionEval
{
    /// <summary>The bar from issue #15: at least this share of fields right.</summary>
    public const double FieldBar = 0.90;

    public static readonly string[] Fields = ["type", "priority", "equipment", "customer", "day", "hour", "safety"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Result(Case Case, Dictionary<string, (bool Right, string Got)> Fields, int Attempts, double Seconds, string? Error);

    public static async Task<List<string>> RunAsync(string root, HttpClient api, bool write)
    {
        var file = JsonSerializer.Deserialize<CaseFile>(await File.ReadAllTextAsync(Path.Combine(root, "evals", "extraction.json")), Json)
            ?? throw new InvalidOperationException("evals/extraction.json is empty.");

        // The calls in the inbox, with their transcripts, to find the seed ones by a phrase.
        var inbox = await api.GetFromJsonAsync<List<JsonElement>>("/api/calls", Json) ?? [];
        var transcripts = new Dictionary<int, string>();
        foreach (var call in inbox)
        {
            var id = call.GetProperty("id").GetInt32();
            transcripts[id] = (await api.GetFromJsonAsync<JsonElement>($"/api/calls/{id}", Json)).GetProperty("transcript").GetString()!;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var results = new List<Result>();
        foreach (var c in file.Cases)
        {
            int id;
            var pasted = c.Transcript is not null;
            if (pasted)
            {
                var created = await api.PostAsJsonAsync("/api/calls", new { branchCode = c.Branch, transcript = c.Transcript });
                created.EnsureSuccessStatusCode();
                id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
            }
            else
            {
                id = transcripts.FirstOrDefault(t => t.Value.Contains(c.Find!, StringComparison.Ordinal)).Key;
                if (id == 0)
                {
                    throw new InvalidOperationException($"No call in the inbox contains \"{c.Find}\". Reset the demo data and run again.");
                }
            }

            var clock = Stopwatch.StartNew();
            var response = await api.PostAsync($"/api/calls/{id}/analyse", null);
            var seconds = clock.Elapsed.TotalSeconds;
            Result result;
            if (!response.IsSuccessStatusCode)
            {
                result = new Result(c, Fields.ToDictionary(f => f, _ => (false, "no answer")), 0, seconds, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }
            else
            {
                var proposal = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("proposal");
                result = Score(c, proposal, today, seconds);
            }

            results.Add(result);
            Console.WriteLine($"{result.Fields.Count(f => f.Value.Right)}/{Fields.Length}  {c.Id}{(result.Error is null ? "" : $"  ({result.Error})")}");

            // Pasted calls were only for measuring: close them so the inbox stays the demo's.
            if (pasted)
            {
                await api.PostAsync($"/api/calls/{id}/dismiss", null);
            }
        }

        var right = results.Sum(r => r.Fields.Count(f => f.Value.Right));
        var total = results.Count * Fields.Length;
        var accuracy = right / (double)total;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"\nextraction field accuracy {accuracy:0.00} ({right}/{total})"));

        if (write)
        {
            var path = Path.Combine(root, "evals", "extraction-results.md");
            await File.WriteAllTextAsync(path, Markdown(results, accuracy, right, total, today));
            Console.WriteLine($"Wrote {Path.GetRelativePath(Environment.CurrentDirectory, path)}");
        }

        return accuracy >= FieldBar ? [] : [string.Create(CultureInfo.InvariantCulture, $"extraction field accuracy is {accuracy:0.00}, below {FieldBar:0.00}")];
    }

    private static Result Score(Case c, JsonElement p, DateOnly today, double seconds)
    {
        var e = c.Expect;
        string Str(string name) => p.GetProperty(name).ValueKind == JsonValueKind.Null ? "null" : p.GetProperty(name).ToString();

        var match = p.GetProperty("match");
        var customer = match.ValueKind == JsonValueKind.Null
            ? (p.GetProperty("newCustomer").GetBoolean() ? "new" : "none")
            : match.GetProperty("name").GetString()!.Split(' ')[^1];
        var customerRight = e.Customer switch
        {
            // A new caller and an unknown one both end up as "add them": either is right for "new".
            "new" => customer is "new" or "none",
            "none" => customer is "new" or "none",
            var last => customer == last,
        };

        var date = Str("date") == "null" ? (DateOnly?)null : DateOnly.Parse(Str("date"), CultureInfo.InvariantCulture);
        var dayRight = e.Day switch
        {
            null => date is null,
            "today" => date == today,
            "tomorrow" => date == today.AddDays(1),
            var weekday => date is { } d && d.DayOfWeek.ToString() == weekday && d > today.AddDays(-1) && d <= today.AddDays(7),
        };

        var hour = Str("windowStartHour");
        var fields = new Dictionary<string, (bool, string)>
        {
            ["type"] = (e.Type.Contains(Str("type")), Str("type")),
            ["priority"] = (e.Priority.Contains(Str("priority")), Str("priority")),
            ["equipment"] = (e.Equipment.Contains(Str("equipment") == "null" ? "None" : Str("equipment")), Str("equipment")),
            ["customer"] = (customerRight, customer),
            ["day"] = (dayRight, date?.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null"),
            ["hour"] = (e.Hour is null ? hour == "null" : hour == e.Hour.Value.ToString(CultureInfo.InvariantCulture), hour),
            ["safety"] = (p.GetProperty("safetyConcern").GetBoolean() == e.Safety, p.GetProperty("safetyConcern").GetBoolean() ? "yes" : "no"),
        };

        return new Result(c, fields, p.GetProperty("attempts").GetInt32(), seconds, null);
    }

    private static string Markdown(List<Result> results, double accuracy, int right, int total, DateOnly today)
    {
        var page = new StringBuilder();
        void Line(string text = "") => page.Append(text).Append('\n');

        Line("# Extraction eval results");
        Line();
        Line("<!-- Generated by `dotnet run --project tools/Truckroll.Eval -- --suite extraction --write`. Don't edit by hand: change evals/extraction.json or the code, and run it again. -->");
        Line();
        Line(string.Create(CultureInfo.InvariantCulture, $"Ran on {today:yyyy-MM-dd} ({today.DayOfWeek}) against the running stack's model, over {results.Count} calls from [extraction.json](extraction.json), {ExtractionEval.Fields.Length} fields each. Each proposal is what a CSR would see: the model's reading after code's matching and safety rule."));
        Line();
        Line(accuracy >= FieldBar
            ? string.Create(CultureInfo.InvariantCulture, $"**Bar met:** {right} of {total} fields right ({accuracy * 100:0}%; the bar is {FieldBar * 100:0}%).")
            : string.Create(CultureInfo.InvariantCulture, $"**Bar missed:** {right} of {total} fields right ({accuracy * 100:0}%; the bar is {FieldBar * 100:0}%)."));
        Line();
        Line("## By field");
        Line();
        Line("| Field | Right |");
        Line("| ----- | ----: |");
        foreach (var field in Fields)
        {
            Line(string.Create(CultureInfo.InvariantCulture, $"| {field} | {results.Count(r => r.Fields[field].Right)}/{results.Count} |"));
        }

        Line();
        Line("## By call");
        Line();
        Line("A wrong field shows what the proposal said, struck through.");
        Line();
        Line($"| Call | {string.Join(" | ", Fields)} | Tries | Seconds |");
        Line($"| ---- | {string.Join(" | ", Fields.Select(_ => "---"))} | ---: | ---: |");
        foreach (var r in results)
        {
            var cells = Fields.Select(f => r.Fields[f].Right ? "✓" : $"~~{r.Fields[f].Got}~~");
            Line(string.Create(CultureInfo.InvariantCulture, $"| {r.Case.Id} | {string.Join(" | ", cells)} | {r.Attempts} | {r.Seconds:0.0} |"));
        }

        return page.ToString();
    }

    public sealed record CaseFile(string About, List<Case> Cases);

    public sealed record Case(string Id, string? Find, string? Branch, string? Transcript, Expected Expect);

    public sealed record Expected(List<string> Type, List<string> Priority, List<string> Equipment, string Customer, string? Day, int? Hour, bool Safety);
}
