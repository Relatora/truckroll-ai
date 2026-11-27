// The retrieval eval: asks the running stack every question in evals/retrieval.json, in every
// search mode, and reports where the right passage came back.
//
//   dotnet run --project tools/Truckroll.Eval              print the summary, check the bars
//   dotnet run --project tools/Truckroll.Eval -- --write   also regenerate evals/results.md
//   --url http://localhost:3000                            where the web app (or API) is
//
// Exits 1 when a bar from issue #12 is missed, so it can gate a change to retrieval.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Truckroll.Eval;

const int ResultsPerQuestion = 10;

var write = args.Contains("--write");
var target = args.SkipWhile(a => a != "--url").Skip(1).FirstOrDefault() ?? "http://localhost:3000";
var root = FindRepoRoot();

var file = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(root, "evals", "retrieval.json")), EvalJson.Default.EvalFile)
    ?? throw new InvalidOperationException("evals/retrieval.json is empty.");

using var api = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
{
    BaseAddress = new Uri(target),
    // A cold embedding model takes a while to load on the first question.
    Timeout = TimeSpan.FromMinutes(5),
};

// The owner sees every branch, so the eval measures ranking, not access rules (those have tests).
// These are the demo's public credentials (see the README), not a secret.
var signIn = await api.PostAsJsonAsync("/api/auth/sign-in", new { email = "morgan@kestrel.test", password = "truckroll-demo" });
signIn.EnsureSuccessStatusCode();

var placeholders = new Placeholders(api);
var results = new List<CaseResult>();
foreach (var evalCase in file.Cases)
{
    var question = await placeholders.ResolveAsync(evalCase.Question);
    var expectations = new List<Expectation>();
    foreach (var e in evalCase.Expect)
    {
        expectations.Add(e with { Id = e.Id is null ? null : await placeholders.ResolveAsync(e.Id) });
    }

    var resolved = evalCase with { Expect = expectations };
    var ranks = new Dictionary<string, int?>();
    var times = new Dictionary<string, double>();
    foreach (var mode in Report.Modes)
    {
        var clock = Stopwatch.StartNew();
        var hits = await api.GetFromJsonAsync(
            $"/api/knowledge/search?q={Uri.EscapeDataString(question)}&limit={ResultsPerQuestion}&mode={mode}",
            EvalJson.Default.ListHit) ?? [];
        times[mode] = clock.Elapsed.TotalMilliseconds;
        var index = hits.FindIndex(resolved.IsAnsweredBy);
        ranks[mode] = index < 0 ? null : index + 1;
    }

    results.Add(new CaseResult(resolved, question, ranks, times));
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{(ranks["hybrid"] is { } r ? $"#{r}" : "miss"),5}  {evalCase.Id}"));
}

Console.WriteLine();
foreach (var mode in Report.Modes)
{
    var m = Report.For(results, mode);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{mode,-10} hit@1 {m.HitAt1:0.00}  hit@5 {m.HitAt5:0.00}  MRR {m.Mrr:0.00}  median {m.MedianMilliseconds:0} ms"));
}

if (write)
{
    var path = Path.Combine(root, "evals", "results.md");
    await File.WriteAllTextAsync(path, Report.Markdown(results, DateTimeOffset.UtcNow, target));
    Console.WriteLine($"\nWrote {Path.GetRelativePath(Environment.CurrentDirectory, path)}");
}

var failures = Report.CheckBars(results);
foreach (var failure in failures)
{
    Console.Error.WriteLine($"Bar missed: {failure}");
}

return failures.Count == 0 ? 0 : 1;

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Truckroll.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("Run this from inside the repository.");
}
