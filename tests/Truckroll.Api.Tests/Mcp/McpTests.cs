using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Truckroll.Api.Access;
using Truckroll.Api.Features.Tokens;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Mcp;

/// <summary>
/// The MCP server through the official SDK's own client, with real personal tokens. The point of
/// most of these: a token sees exactly what its owner sees, and nothing else gets in.
/// </summary>
public sealed class McpTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------ the tools, per person

    [Fact]
    public async Task The_server_offers_the_seven_tools()
    {
        await using var mcp = await ConnectAsync(SignedInClients.Owner);

        var tools = await mcp.ListToolsAsync(cancellationToken: Ct);

        Assert.Equal(
            ["ask_knowledge_base", "create_draft_note", "get_customer_history", "get_schedule", "list_jobs", "search_customers", "suggest_technicians"],
            tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task A_technicians_list_jobs_is_only_their_own()
    {
        await using var mcp = await ConnectAsync(SignedInClients.GuelphTechnician);

        var jobs = await CallAsync(mcp, "list_jobs", new() { ["day"] = "all" });

        Assert.NotEmpty(jobs.EnumerateArray());
        Assert.All(jobs.EnumerateArray(), j => Assert.Equal("Nadia Kowalczyk", j.GetProperty("technician").GetString()));
    }

    [Fact]
    public async Task A_dispatcher_finds_only_their_branchs_customers()
    {
        await using var alex = await ConnectAsync(SignedInClients.GuelphDispatcher);
        await using var owner = await ConnectAsync(SignedInClients.Owner);

        var fromGuelph = await CallAsync(alex, "search_customers", new() { ["query"] = "Hartley" });
        var fromOwner = await CallAsync(owner, "search_customers", new() { ["query"] = "Hartley" });

        Assert.All(fromGuelph.EnumerateArray(), c => Assert.Equal("GUE", c.GetProperty("branchCode").GetString()));
        Assert.Contains(fromOwner.EnumerateArray(), c => c.GetProperty("name").GetString() == "Doug Hartley");
    }

    [Fact]
    public async Task Another_branchs_customer_history_is_refused_and_contact_details_never_leave()
    {
        var hartley = await CustomerIdAsync("Hartley", "KIT");
        var raman = await CustomerIdAsync("Raman", "GUE");
        await using var alex = await ConnectAsync(SignedInClients.GuelphDispatcher);

        var refused = await alex.CallToolAsync("get_customer_history", new Dictionary<string, object?> { ["customerId"] = hartley }, cancellationToken: Ct);
        var priya = await alex.CallToolAsync("get_customer_history", new Dictionary<string, object?> { ["customerId"] = raman }, cancellationToken: Ct);

        Assert.True(refused.IsError);
        Assert.NotEqual(true, priya.IsError);
        var text = Text(priya);
        Assert.Contains("14 Maple St", text, StringComparison.Ordinal);
        Assert.DoesNotContain("519-555-0142", text, StringComparison.Ordinal);
        Assert.DoesNotContain("priya.raman@mail.test", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_schedule_is_your_branch_and_no_other()
    {
        await using var alex = await ConnectAsync(SignedInClients.GuelphDispatcher);

        var own = await CallAsync(alex, "get_schedule", new() { ["date"] = TruckrollApiFactory.DemoToday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) });
        var other = await alex.CallToolAsync("get_schedule", new Dictionary<string, object?> { ["branch"] = "KIT" }, cancellationToken: Ct);

        Assert.Equal("Guelph", own.GetProperty("branch").GetString());
        Assert.NotEmpty(own.GetProperty("technicians").EnumerateArray());
        Assert.True(other.IsError);
    }

    [Fact]
    public async Task Suggestions_only_for_jobs_you_can_see()
    {
        var kitchenerJob = await WithDbAsync(db => db.Jobs.Where(j => j.Branch.Code == "KIT" && j.Status == JobStatus.Scheduled).Select(j => j.Id).FirstAsync(Ct));
        await using var pat = await ConnectAsync(SignedInClients.KitchenerManager);
        await using var alex = await ConnectAsync(SignedInClients.GuelphDispatcher);

        var ranked = await CallAsync(pat, "suggest_technicians", new() { ["jobId"] = kitchenerJob });
        var refused = await alex.CallToolAsync("suggest_technicians", new Dictionary<string, object?> { ["jobId"] = kitchenerJob }, cancellationToken: Ct);

        // The shortlist itself may be empty (nobody free and licensed); what matters is who may ask.
        Assert.Equal(kitchenerJob, ranked.GetProperty("jobId").GetInt32());
        Assert.True(refused.IsError);
    }

    [Fact]
    public async Task The_knowledge_base_answers_within_the_askers_scope()
    {
        await factory.Services.GetRequiredService<Api.Knowledge.KnowledgeQueue>().Ready.WaitAsync(TimeSpan.FromMinutes(2), Ct);
        await using var alex = await ConnectAsync(SignedInClients.GuelphDispatcher);

        var passages = await CallAsync(alex, "ask_knowledge_base", new() { ["question"] = "What boiler is at 88 Birch Cres?" });

        Assert.NotEmpty(passages.EnumerateArray());
        Assert.Equal(Enumerable.Range(1, passages.GetArrayLength()), passages.EnumerateArray().Select(p => p.GetProperty("number").GetInt32()));
        Assert.DoesNotContain(passages.EnumerateArray(), p => p.GetProperty("text").GetString()!.Contains("88 Birch Cres", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_draft_note_lands_on_your_own_job_labelled_and_nowhere_else()
    {
        var (mine, theirs) = await WithDbAsync(async db => (
            await db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").OrderBy(j => j.Id).Select(j => j.Id).FirstAsync(Ct),
            await db.Jobs.Where(j => j.Branch.Code == "GUE" && j.Technician != null && j.Technician.FirstName != "Nadia").Select(j => j.Id).FirstAsync(Ct)));
        await using var nadia = await ConnectAsync(SignedInClients.GuelphTechnician);

        var written = await CallAsync(nadia, "create_draft_note", new() { ["jobId"] = mine, ["text"] = "mcp-draft-6c2: bring a spare IGN-80H." });
        var refused = await nadia.CallToolAsync("create_draft_note", new Dictionary<string, object?> { ["jobId"] = theirs, ["text"] = "Should not land." }, cancellationToken: Ct);

        Assert.Equal("Nadia Kowalczyk (via MCP, draft)", written.GetProperty("author").GetString());
        var note = await WithDbAsync(db => db.JobNotes.AsNoTracking().SingleAsync(n => n.Body.StartsWith("mcp-draft-6c2"), Ct));
        Assert.Equal(NoteSource.Mcp, note.Source);
        Assert.True(refused.IsError);
        Assert.False(await WithDbAsync(db => db.JobNotes.AnyAsync(n => n.Body == "Should not land.", Ct)));
    }

    // ------------------------------------------------------------------ tokens

    [Fact]
    public async Task A_revoked_token_stops_working_on_its_next_call()
    {
        using var web = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        var created = await CreateTokenAsync(web, "Revoked laptop");
        await using var mcp = await ConnectWithAsync(created.Token);
        await CallAsync(mcp, "search_customers", new() { ["query"] = "Raman" });

        (await web.PostAsync($"/api/tokens/{created.Item.Id}/revoke", null, Ct)).EnsureSuccessStatusCode();

        await Assert.ThrowsAnyAsync<Exception>(() => mcp.CallToolAsync("search_customers", new Dictionary<string, object?> { ["query"] = "Raman" }, cancellationToken: Ct).AsTask());
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        using var web = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        var created = await CreateTokenAsync(web, "Old phone");
        await WithDbAsync(db => db.PersonalAccessTokens.Where(t => t.Id == created.Item.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));

        using var raw = Bearer(created.Token);
        var response = await raw.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cookies_cannot_drive_mcp_and_tokens_cannot_open_the_web_api()
    {
        using var web = await factory.SignedInAsAsync(SignedInClients.Owner);
        var created = await CreateTokenAsync(web, "Separation check");
        using var bearer = Bearer(created.Token);

        var cookieOnMcp = await web.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" }, Ct);
        var tokenOnApi = await bearer.GetAsync("/api/customers", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, cookieOnMcp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tokenOnApi.StatusCode);
    }

    [Fact]
    public async Task Only_the_hash_is_stored_and_the_value_is_shown_once()
    {
        using var web = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);

        var created = await CreateTokenAsync(web, "Hash check");
        var listed = await web.GetStringAsync("/api/tokens", Ct);
        var stored = await WithDbAsync(db => db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Item.Id, Ct));

        Assert.StartsWith("trk_", created.Token, StringComparison.Ordinal);
        Assert.DoesNotContain(created.Token, listed, StringComparison.Ordinal);
        Assert.Equal(PersonalTokens.Hash(created.Token), stored.Hash);
        Assert.Equal(created.Token[..8], stored.Prefix);
    }

    [Fact]
    public async Task No_one_holds_more_than_ten_live_tokens()
    {
        // A user of its own: filling a demo user's quota would starve the other tests.
        const string email = "token-quota@kestrel.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                await users.CreateAsync(new AppUser { UserName = email, Email = email, DisplayName = "Token Quota", Role = UserRole.Owner }, "correct-horse-battery");
            }
        }

        using var web = factory.CreateClient();
        (await web.PostAsJsonAsync("/api/auth/sign-in", new { email, password = "correct-horse-battery" }, Ct)).EnsureSuccessStatusCode();
        for (var i = 0; i < PersonalTokens.MaxLiveTokens; i++)
        {
            await CreateTokenAsync(web, $"Device {i}");
        }

        var eleventh = await web.PostAsJsonAsync("/api/tokens", new { name = "One too many" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<McpClient> ConnectAsync(string email)
    {
        using var web = await factory.SignedInAsAsync(email);
        return await ConnectWithAsync((await CreateTokenAsync(web, $"Test {Guid.NewGuid():N}"[..20])).Token);
    }

    private async Task<McpClient> ConnectWithAsync(string token)
    {
        var http = Bearer(token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private HttpClient Bearer(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<CreatedToken> CreateTokenAsync(HttpClient web, string name)
    {
        var response = await web.PostAsJsonAsync("/api/tokens", new { name }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedToken>(JsonOptions.Api, Ct))!;
    }

    private static async Task<JsonElement> CallAsync(McpClient mcp, string tool, Dictionary<string, object?> args)
    {
        var result = await mcp.CallToolAsync(tool, args, cancellationToken: Ct);
        Assert.True(result.IsError != true, $"{tool} failed: {Text(result)}");
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private Task<int> CustomerIdAsync(string lastName, string branch) =>
        WithDbAsync(db => db.Customers.Where(c => c.LastName == lastName && c.Branch.Code == branch && c.Email.Contains(lastName.ToLower())).Select(c => c.Id).FirstAsync(Ct));

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
