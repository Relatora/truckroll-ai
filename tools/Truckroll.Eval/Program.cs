// The evals.
//
//   dotnet run --project tools/Truckroll.Eval                          every suite, check the bars
//   dotnet run --project tools/Truckroll.Eval -- --write               also regenerate the results pages
//   dotnet run --project tools/Truckroll.Eval -- --suite dispatch      one suite: retrieval, dispatch or extraction
//   --url http://localhost:3000                                        where the stack is (retrieval and extraction)
//
// Retrieval asks the running stack every question in evals/retrieval.json and writes
// evals/results.md. Dispatch runs evals/dispatch.json through the scorer offline and writes
// evals/dispatch-results.md. Extraction analyses evals/extraction.json's calls with the running
// stack's model and writes evals/extraction-results.md. Exits 1 when a bar is missed.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Truckroll.Eval;

// A named entry point: the tool references the API project, whose own Program would clash with
// a top-level one.
internal static class EvalProgram
{
    private const int ResultsPerQuestion = 10;

    public static async Task<int> Main(string[] args)
    {
        var write = args.Contains("--write");
        var suite = args.SkipWhile(a => a != "--suite").Skip(1).FirstOrDefault() ?? "all";
        var target = args.SkipWhile(a => a != "--url").Skip(1).FirstOrDefault() ?? "http://localhost:3000";
        var root = FindRepoRoot();

        var failures = new List<string>();
        if (suite is "all" or "dispatch")
        {
            Console.WriteLine("Dispatch\n");
            failures.AddRange(await DispatchEval.RunAsync(root, write));
            Console.WriteLine();
        }

        if (suite is "all" or "retrieval")
        {
            Console.WriteLine("Retrieval\n");
            failures.AddRange(await RetrievalAsync(root, target, write));
        }

        if (suite is "all" or "extraction")
        {
            Console.WriteLine("\nExtraction\n");
            using var api = await SignedInAsync(target);
            failures.AddRange(await ExtractionEval.RunAsync(root, api, write));
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine($"Bar missed: {failure}");
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task<List<string>> RetrievalAsync(string root, string target, bool write)
    {
        var file = JsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(root, "evals", "retrieval.json")), EvalJson.Default.EvalFile)
            ?? throw new InvalidOperationException("evals/retrieval.json is empty.");

        using var api = await SignedInAsync(target);

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
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{(ranks["hybrid"] is { } r ? $"#{r}" : "miss"),5}  {evalCase.Id}"));
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

        return Report.CheckBars(results);
    }

    /// <summary>
    /// A client signed in as the owner, who sees every branch, so the evals measure the AI, not
    /// access rules (those have tests). These are the demo's public credentials, not a secret.
    /// </summary>
    private static async Task<HttpClient> SignedInAsync(string target)
    {
        var api = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() })
        {
            BaseAddress = new Uri(target),
            // A cold model takes a while to load on the first request.
            Timeout = TimeSpan.FromMinutes(5),
        };

        var signIn = await api.PostAsJsonAsync("/api/auth/sign-in", new { email = "morgan@kestrel.test", password = "truckroll-demo" });
        signIn.EnsureSuccessStatusCode();
        return api;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Truckroll.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Run this from inside the repository.");
    }
}
