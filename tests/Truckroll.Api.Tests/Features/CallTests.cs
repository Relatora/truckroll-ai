using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Features.Calls;
using Truckroll.Api.Knowledge;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Features;

/// <summary>The calls inbox end to end, with the scripted model.</summary>
public sealed class CallTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly VisitDay = TruckrollApiFactory.DemoToday.AddDays(3);

    [Fact]
    public async Task A_csr_sees_only_their_branchs_calls_and_a_technician_sees_none()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);

        var calls = await dana.GetFromJsonAsync<List<CallListItem>>("/api/calls", JsonOptions.Api, Ct);

        Assert.NotEmpty(calls!);
        Assert.All(calls!, c => Assert.Equal("GUE", c.BranchCode));
        Assert.Equal(HttpStatusCode.Forbidden, (await nadia.GetAsync("/api/calls", Ct)).StatusCode);
    }

    [Fact]
    public async Task Analysis_proposes_a_matched_job_and_creates_nothing()
    {
        const string marker = "call-propose-1a2";
        factory.Chat.Reply(marker, Analysis(caller: "Priya Raman", street: "14 Maple St", priority: "Urgent", equipment: "Furnace",
            summary: "Priya's furnace won't light again; reach her at [phone 1].", body: "Hi Priya, we'll call you at [phone 1]. Kestrel Home Comfort, Guelph"));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var jobsBefore = await JobCountAsync("Raman");
        var id = await PasteAsync(dana, $"Caller: Hi, it's Priya Raman at 14 Maple St. Text me at 519-555-0199 or priya.new@mail.test. Furnace blinks once and won't light. {marker}");

        var call = await AnalyseAsync(dana, id);

        Assert.Equal(CallStatus.AwaitingReview, call.Status);
        var proposal = call.Proposal!;
        Assert.Equal("Priya Raman", proposal.Match?.Name);
        Assert.Equal("14 Maple St, Guelph", proposal.Match?.Address);
        Assert.NotNull(proposal.EquipmentId);
        Assert.Equal("priya.new@mail.test", proposal.CallerEmail);
        Assert.Equal(1, proposal.Attempts);

        // The model saw tokens; the person sees the originals.
        var sent = string.Join('\n', Assert.Single(factory.Chat.Requests, r => r.Messages.Any(m => m.Text.Contains(marker, StringComparison.Ordinal))).Messages.Select(m => m.Text));
        Assert.Contains("[phone 1]", sent, StringComparison.Ordinal);
        Assert.Contains("[email 1]", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("519-555-0199", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("priya.new@mail.test", sent, StringComparison.Ordinal);
        Assert.Contains("519-555-0199", call.Summary, StringComparison.Ordinal);
        Assert.Contains("519-555-0199", call.FollowUpBody, StringComparison.Ordinal);

        Assert.Equal(jobsBefore, await JobCountAsync("Raman"));
        Assert.Null(call.CreatedJob);
    }

    [Fact]
    public async Task Without_a_street_the_full_name_picks_one_of_several_namesakes()
    {
        const string marker = "call-namesake-9b2";
        // Four Guelph customers are called Kowalski; the caller gave no street.
        factory.Chat.Reply(marker, Analysis(caller: "Sandra Kowalski", street: null, equipment: "TanklessWaterHeater"));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: Hi, Sandra Kowalski. My tankless thing shows E-12 again. {marker}");

        var proposal = (await AnalyseAsync(dana, id)).Proposal!;

        Assert.Equal("Sandra Kowalski", proposal.Match?.Name);
        Assert.True(proposal.Candidates.Count > 1);
        Assert.Contains(proposal.Notes, n => n.Contains("full name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_malformed_answer_gets_one_retry_with_the_problems_named()
    {
        const string marker = "call-retry-7c3";
        factory.Chat.Reply(p => p.Contains(marker, StringComparison.Ordinal) && !p.Contains("had problems", StringComparison.Ordinal),
            Analysis(caller: "Sandra Kowalski", priority: "Super urgent", hour: 9));
        factory.Chat.Reply(p => p.Contains(marker, StringComparison.Ordinal) && p.Contains("had problems", StringComparison.Ordinal),
            Analysis(caller: "Sandra Kowalski", priority: "Normal", hour: 14));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: Sandra Kowalski, Riverbend Road. Tankless shows E-12 again. {marker}");

        var call = await AnalyseAsync(dana, id);

        Assert.Equal(2, call.Proposal!.Attempts);
        Assert.Equal(JobPriority.Normal, call.Proposal.Priority);
        var retry = factory.Chat.Requests.Last(r => r.Messages.Any(m => m.Text.Contains("had problems", StringComparison.Ordinal) && m.Text.Contains("windowStartHour", StringComparison.Ordinal)));
        Assert.Contains(retry.Messages, m => m.Text.Contains("priority must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_unusable_answers_leave_the_call_for_a_person()
    {
        const string marker = "call-garbage-5e8";
        factory.Chat.Reply(marker, "I'm sorry, I can't help with that.");
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: Hello, I need someone for my furnace please. {marker}");

        var response = await dana.PostAsync($"/api/calls/{id}/analyse", null, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var call = await dana.GetFromJsonAsync<CallDetail>($"/api/calls/{id}", JsonOptions.Api, Ct);
        Assert.Equal(CallStatus.New, call!.Status);
    }

    [Fact]
    public async Task A_gas_smell_is_an_emergency_whatever_the_model_says()
    {
        const string marker = "call-gas-2b9";
        factory.Chat.Reply(marker, Analysis(caller: "Melissa Grant", priority: "Normal", equipment: "WaterHeater", safety: false));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: I think I smell gas near the water heater. Melissa Grant, 9 Kingfisher Crescent. {marker}");

        var proposal = (await AnalyseAsync(dana, id)).Proposal!;

        Assert.Equal(JobPriority.Emergency, proposal.Priority);
        Assert.True(proposal.SafetyConcern);
        Assert.Contains(proposal.Notes, n => n.Contains("P-103", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approving_books_the_job_and_files_the_follow_up_once()
    {
        // A generated customer, not an anchor (other tests count the anchors' open jobs), and one
        // from the seed (other tests add customers while this one runs).
        var customer = await WithDbAsync(db => db.Customers.Where(c => c.Branch.Code == "GUE" && c.Properties.Count == 1).OrderBy(c => c.Id).Skip(25)
            .Select(c => new { Name = c.FirstName + " " + c.LastName, c.Properties.First().Street }).FirstAsync(Ct));
        const string marker = "call-approve-4d1";
        factory.Chat.Reply(marker, Analysis(caller: customer.Name, street: customer.Street, equipment: "Furnace"));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: {customer.Name}, {customer.Street}, the furnace is acting up. {marker}");
        var proposal = (await AnalyseAsync(dana, id)).Proposal!;

        var approved = await dana.PostAsJsonAsync($"/api/calls/{id}/approve", new
        {
            propertyId = proposal.Match!.PropertyId,
            equipmentId = proposal.EquipmentId,
            type = "Repair",
            priority = "Urgent",
            date = VisitDay,
            windowStartHour = 10,
            summary = "Furnace is acting up",
            followUpSubject = "Your furnace visit",
            followUpBody = "Hi, we'll see you between 10 and noon. Kestrel Home Comfort, Guelph",
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var call = (await approved.Content.ReadFromJsonAsync<CallDetail>(JsonOptions.Api, Ct))!;
        Assert.Equal(CallStatus.Approved, call.Status);
        Assert.Equal("Dana Whitfield", call.ReviewedBy);
        Assert.Equal("Your furnace visit", call.FollowUpSubject);
        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == call.CreatedJob!.Id, Ct));
        Assert.Equal(JobStatus.Scheduled, job.Status);
        Assert.Equal(proposal.Match.PropertyId, job.PropertyId);

        var again = await dana.PostAsJsonAsync($"/api/calls/{id}/approve", new { propertyId = proposal.Match.PropertyId, type = "Repair", priority = "Urgent", date = VisitDay, windowStartHour = 10, summary = "Again", followUpSubject = "x", followUpBody = "x" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_new_caller_becomes_a_customer_with_a_home_and_a_job()
    {
        const string marker = "call-new-8f6";
        factory.Chat.Reply(marker, Analysis(caller: "Andre Fontaine-Test", street: "41 Thornbury Ave", type: "Estimate", equipment: "None", newCustomer: true));
        using var owner = await factory.SignedInAsAsync(SignedInClients.Owner);
        var id = await PasteAsync(owner, $"Caller: I'm not a customer yet, Andre Fontaine-Test, 41 Thornbury Avenue. We'd like a quote for air conditioning. {marker}", "CAM");
        var proposal = (await AnalyseAsync(owner, id)).Proposal!;
        Assert.True(proposal.NewCustomer);
        Assert.Null(proposal.Match);

        var approved = await owner.PostAsJsonAsync($"/api/calls/{id}/approve", new
        {
            newCustomer = new { firstName = "Andre", lastName = "Fontaine-Test", street = "41 Thornbury Ave", email = "andre@mail.test", source = "TruckWrap" },
            type = "Estimate",
            priority = "Normal",
            date = VisitDay,
            windowStartHour = 14,
            summary = "Estimate for central air or a heat pump",
            followUpSubject = "Your estimate",
            followUpBody = "Hi Andre, we'll confirm the day by email. Kestrel Home Comfort, Cambridge",
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var customer = await WithDbAsync(db => db.Customers.Include(c => c.Properties).Include(c => c.Branch).SingleAsync(c => c.LastName == "Fontaine-Test", Ct));
        Assert.Equal("CAM", customer.Branch.Code);
        Assert.Equal(CustomerSource.TruckWrap, customer.Source);
        Assert.Equal("41 Thornbury Ave", Assert.Single(customer.Properties).Street);
        Assert.Equal(1, await JobCountAsync("Fontaine-Test"));
    }

    [Fact]
    public async Task Dismissing_creates_nothing()
    {
        const string marker = "call-dismiss-3a7";
        factory.Chat.Reply(marker, Analysis(caller: "Wrong Number"));
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var id = await PasteAsync(dana, $"Caller: Sorry, is this the pizza place? Wrong number, never mind. {marker}");

        var dismissed = await dana.PostAsync($"/api/calls/{id}/dismiss", null, Ct);

        var call = (await dismissed.Content.ReadFromJsonAsync<CallDetail>(JsonOptions.Api, Ct))!;
        Assert.Equal(CallStatus.Dismissed, call.Status);
        Assert.Null(call.CreatedJob);
        Assert.Equal(HttpStatusCode.Conflict, (await dana.PostAsync($"/api/calls/{id}/analyse", null, Ct)).StatusCode);
    }

    // ------------------------------------------------------------------ units

    [Fact]
    public void The_vault_gives_each_detail_one_token_and_puts_them_all_back()
    {
        var vault = new PiiVault();

        var masked = vault.Mask("Call 519-555-0142 or 519-555-0142, or write andre@mail.test.");

        Assert.Equal("Call [phone 1] or [phone 1], or write [email 1].", masked);
        Assert.Equal("Reach Andre at andre@mail.test or 519-555-0142. [phone 7] stays.", vault.Restore("Reach Andre at [email 1] or [phone 1]. [phone 7] stays."));
        Assert.Equal(["andre@mail.test"], vault.Emails);
    }

    [Theory]
    [InlineData("14 Maple St, Guelph", "14 Maple Street", true)]
    [InlineData("88 Birch Cres, Kitchener", "Birch Crescent", true)]
    [InlineData("311 Birch Cres, Guelph", "88 Birch Cres", false)]
    [InlineData("14 Maple St, Guelph", null, false)]
    public void Streets_match_on_name_and_number_when_given(string address, string? spoken, bool expected)
    {
        Assert.Equal(expected, CallAnalyser.SameStreet(address, spoken));
    }

    [Fact]
    public void Validation_names_every_field_a_retry_has_to_fix()
    {
        var analysis = new CallAnalysis("Summary", "Name", null, false, "Repair", "Whenever", "Toaster", "Symptoms", "2020-01-01", 9, "Subject", "Body", false);

        var problems = analysis.Problems(new DateOnly(2026, 11, 25));

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("priority", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("equipment", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("windowDay", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("windowStartHour", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("today", "2026-11-25")]
    [InlineData("Tomorrow", "2026-11-26")]
    [InlineData("friday", "2026-11-27")]
    [InlineData("Wednesday", "2026-12-02")]
    [InlineData("Monday", "2026-11-30")]
    [InlineData(null, null)]
    [InlineData("next week", null)]
    public void The_spoken_day_becomes_a_date_by_code_not_by_the_model(string? spoken, string? expected)
    {
        // 2026-11-25 is a Wednesday: "Wednesday" said on a Wednesday means next week's.
        var analysis = new CallAnalysis("S", "N", null, false, "Repair", "Normal", "Furnace", "Sym", spoken, null, "Sub", "Body", false);

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), analysis.DateFor(new DateOnly(2026, 11, 25)));
    }

    // ------------------------------------------------------------------ helpers

    private static string Analysis(
        string caller,
        string? street = null,
        string type = "Repair",
        string priority = "Normal",
        string equipment = "Furnace",
        int? hour = 10,
        bool newCustomer = false,
        bool safety = false,
        string summary = "The caller needs a visit.",
        string body = "Hi, thanks for calling. Kestrel Home Comfort") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            summary,
            callerName = caller,
            street,
            newCustomer,
            jobType = type,
            priority,
            equipment,
            symptoms = "Not working as it should",
            windowDay = "tomorrow",
            windowStartHour = hour,
            followUpSubject = "Thanks for calling",
            followUpBody = body,
            safetyConcern = safety,
        });

    private static async Task<int> PasteAsync(HttpClient client, string transcript, string? branch = null)
    {
        var response = await client.PostAsJsonAsync("/api/calls", new { branchCode = branch, transcript = $"CSR: Kestrel Home Comfort, how can I help?\n{transcript}" }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CallDetail>(JsonOptions.Api, Ct))!.Id;
    }

    private static async Task<CallDetail> AnalyseAsync(HttpClient client, int id)
    {
        var response = await client.PostAsync($"/api/calls/{id}/analyse", null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CallDetail>(JsonOptions.Api, Ct))!;
    }

    private Task<int> JobCountAsync(string lastName) => WithDbAsync(db => db.Jobs.CountAsync(j => j.Customer.LastName == lastName, Ct));

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
