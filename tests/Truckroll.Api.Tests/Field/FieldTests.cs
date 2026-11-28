using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Features.Field;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Field;

/// <summary>
/// The technician app's API end to end: sign-in, the day, and offline changes arriving late,
/// twice, or after the office moved the job (#22, docs/adr/011).
/// </summary>
public sealed class FieldTests(TruckrollApiFactory factory, FieldTests.NadiasPhone phone) : IClassFixture<FieldTests.NadiasPhone>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// One sign-in for the whole class. A technician can hold ten live tokens, and every test
    /// signing in would use them up.
    /// </summary>
    public sealed class NadiasPhone
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string? _token;

        public async Task<HttpClient> ClientAsync(TruckrollApiFactory factory)
        {
            await _gate.WaitAsync(Ct);
            try
            {
                _token ??= (await SignInAsync(factory.CreateClient(), SignedInClients.GuelphTechnician))!.Token;
            }
            finally
            {
                _gate.Release();
            }

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return client;
        }
    }

    private Task<HttpClient> NadiaAsync() => phone.ClientAsync(factory);

    private static async Task<FieldSignInResponse?> SignInAsync(HttpClient client, string email, string password = DemoSeeder.DemoPassword)
    {
        var response = await client.PostAsJsonAsync("/api/field/sign-in", new FieldSignInRequest(email, password, "Test phone"), Ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<FieldSignInResponse>(JsonOptions.Api, Ct) : null;
    }

    // ------------------------------------------------------------------ sign-in and access

    [Fact]
    public async Task Only_a_technician_gets_a_field_token()
    {
        // The wrong password goes to a technician of its own, not Nadia. A failed attempt bumps
        // the user's lockout counter; the next correct sign-in resets it, and when several tests
        // sign Nadia in at that moment, all but one lose Identity's concurrency check and get a
        // 401 (found by the gate, #50).
        const string email = "field-wrong-password@kestrel.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                await users.CreateAsync(new AppUser { UserName = email, Email = email, DisplayName = "Wrong Password", Role = UserRole.Technician }, "correct-horse-battery");
            }
        }

        using var client = factory.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/field/sign-in", new FieldSignInRequest(email, "not-the-password", null), Ct);
        var dispatcher = await client.PostAsJsonAsync("/api/field/sign-in", new FieldSignInRequest(SignedInClients.GuelphDispatcher, DemoSeeder.DemoPassword, null), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dispatcher.StatusCode);
    }

    [Fact]
    public async Task The_field_api_takes_a_token_not_a_cookie_and_the_token_opens_nothing_else()
    {
        using var cookie = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        using var token = await NadiaAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cookie.GetAsync("/api/field/day", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await token.GetAsync("/api/field/day", Ct)).StatusCode);
        // The web app's endpoints stay cookie-only.
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/customers", Ct)).StatusCode);
    }

    // ------------------------------------------------------------------ the day

    [Fact]
    public async Task The_day_has_her_jobs_and_the_history_at_each_address_whoever_did_it()
    {
        var (jobId, _) = await NewJobForNadiaAsync("day", withHistoryBy: "Tyler");
        using var nadia = await NadiaAsync();

        var day = (await nadia.GetFromJsonAsync<FieldDay>("/api/field/day", JsonOptions.Api, Ct))!;

        Assert.Equal(TruckrollApiFactory.DemoToday, day.Date);
        Assert.Equal("Nadia Kowalczyk", day.Technician);
        var job = Assert.Single(day.Jobs, j => j.Id == jobId);
        Assert.Equal("555-0142", job.Customer.Phone);
        Assert.Equal("Side door; dog is friendly.", job.Place.AccessNotes);
        Assert.Equal("Calderon", job.Equipment?.Brand);

        // Tyler's earlier visit comes down too: it's the address's history, not Nadia's.
        var visit = Assert.Single(job.History);
        Assert.StartsWith("Tyler", visit.Technician, StringComparison.Ordinal);
        Assert.Equal("Replaced the ignitor.", visit.Resolution);

        // Every job on the day is hers.
        var hers = await WithDbAsync(db => db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").Select(j => j.Id).ToListAsync(Ct));
        Assert.All(day.Jobs, j => Assert.Contains(j.Id, hers));
    }

    // ------------------------------------------------------------------ sync

    [Fact]
    public async Task A_job_closed_offline_and_synced_twice_completes_once()
    {
        var (jobId, _) = await NewJobForNadiaAsync("twice");
        using var nadia = await NadiaAsync();
        var finishedAt = new DateTimeOffset(2026, 11, 25, 15, 10, 0, TimeSpan.Zero);
        var batch = new FieldSyncRequest(
        [
            new(Guid.NewGuid(), jobId, "start", finishedAt.AddMinutes(-50), null, null, null),
            new(Guid.NewGuid(), jobId, "note", finishedAt.AddMinutes(-20), "Heat exchanger looks fine.", null, null),
            new(Guid.NewGuid(), jobId, "complete", finishedAt, null, "Cleaned the flame sensor; three good cycles.", true),
        ]);

        // The phone sends, loses the connection before the answer arrives, and sends again.
        var first = await SyncAsync(nadia, batch);
        var second = await SyncAsync(nadia, batch);

        Assert.All(first.Results, r => Assert.Equal("Applied", r.Outcome));
        Assert.Equal(first.Results, second.Results);

        var (job, notes, events) = await WithDbAsync(async db => (
            await db.Jobs.SingleAsync(j => j.Id == jobId, Ct),
            await db.JobNotes.CountAsync(n => n.JobId == jobId && n.Body == "Heat exchanger looks fine.", Ct),
            await db.OutboxMessages.Where(m => m.JobId == jobId).OrderBy(m => m.Id).Select(m => m.Type).ToListAsync(Ct)));
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal("Cleaned the flame sensor; three good cycles.", job.Resolution);
        Assert.Equal(finishedAt, job.CompletedAt); // When she finished, not when she found signal.
        Assert.Equal(1, notes);
        Assert.Equal(["job.started", "job.noted", "job.completed"], events);
    }

    [Fact]
    public async Task A_job_reassigned_while_offline_keeps_her_resolution_as_a_note()
    {
        var (jobId, number) = await NewJobForNadiaAsync("moved");
        using var nadia = await NadiaAsync();

        // While she's in the basement, the office gives the job to Tyler.
        var tyler = await WithDbAsync(async db =>
        {
            var tech = await db.Technicians.SingleAsync(t => t.FirstName == "Tyler" && t.Branch.Code == "GUE", Ct);
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId, Ct);
            job.TechnicianId = tech.Id;
            await db.SaveChangesAsync(Ct);
            return tech;
        });

        var result = Assert.Single((await SyncAsync(nadia, new FieldSyncRequest(
        [
            new(Guid.NewGuid(), jobId, "complete", DateTimeOffset.UtcNow.AddMinutes(-5), null, "Swapped the pressure switch.", true),
        ]))).Results);

        Assert.Equal("Rejected", result.Outcome);
        Assert.Equal($"{number} was given to {tyler.FullName} while you were offline. Your resolution was saved as a note on the job.", result.Explanation);

        var (job, note) = await WithDbAsync(async db => (
            await db.Jobs.SingleAsync(j => j.Id == jobId, Ct),
            await db.JobNotes.SingleOrDefaultAsync(n => n.JobId == jobId && n.Body.Contains("Swapped the pressure switch."), Ct)));
        Assert.Equal(JobStatus.Dispatched, job.Status); // The office's decision stands.
        Assert.NotNull(note);
        Assert.Equal(NoteSource.Technician, note.Source);
    }

    [Fact]
    public async Task A_change_id_from_another_account_is_refused()
    {
        var (jobId, _) = await NewJobForNadiaAsync("theirs");
        using var nadia = await NadiaAsync();
        var id = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.FieldChanges.Add(new FieldChange { Id = id, UserId = "someone-else", JobId = jobId, Kind = "note", Outcome = FieldChangeOutcome.Applied, ReceivedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync(Ct);
        });

        var result = Assert.Single((await SyncAsync(nadia, new FieldSyncRequest([new(id, jobId, "note", DateTimeOffset.UtcNow, "Hi", null, null)]))).Results);

        Assert.Equal("Invalid", result.Outcome);
    }

    [Fact]
    public async Task A_completion_without_a_resolution_is_turned_back()
    {
        var (jobId, _) = await NewJobForNadiaAsync("empty");
        using var nadia = await NadiaAsync();

        var result = Assert.Single((await SyncAsync(nadia, new FieldSyncRequest([new(Guid.NewGuid(), jobId, "complete", DateTimeOffset.UtcNow, null, "  ", true)]))).Results);

        Assert.Equal("Invalid", result.Outcome);
        Assert.Equal(JobStatus.Dispatched, await WithDbAsync(db => db.Jobs.Where(j => j.Id == jobId).Select(j => j.Status).SingleAsync(Ct)));
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<FieldSyncResponse> SyncAsync(HttpClient client, FieldSyncRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/field/sync", request, JsonOptions.Api, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FieldSyncResponse>(JsonOptions.Api, Ct))!;
    }

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }

    /// <summary>
    /// A fresh Guelph customer with a job dispatched to Nadia on the demo day, so each test owns
    /// its job and none touches the demo's anchor customers. Optionally with an earlier,
    /// completed visit at the same address by someone else.
    /// </summary>
    private Task<(int JobId, string Number)> NewJobForNadiaAsync(string suffix, string? withHistoryBy = null) => WithDbAsync(async db =>
    {
        var branch = await db.Branches.SingleAsync(b => b.Code == "GUE", Ct);
        var nadia = await db.Technicians.SingleAsync(t => t.FirstName == "Nadia" && t.BranchId == branch.Id, Ct);
        var unique = $"{suffix}{Guid.NewGuid():N}"[..20];

        var customer = new Customer
        {
            BranchId = branch.Id,
            FirstName = "Field",
            LastName = $"Test{unique}",
            Email = $"field.{unique}@example.test",
            Phone = "555-0142",
            Source = CustomerSource.Referral,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var property = new Property
        {
            Customer = customer,
            Street = $"{unique} Field Rd",
            City = "Guelph",
            PostalCode = "N1H 1A1",
            Latitude = 43.55,
            Longitude = -80.25,
            Kind = PropertyKind.Detached,
            YearBuilt = 2001,
            AccessNotes = "Side door; dog is friendly.",
        };
        var furnace = new Equipment
        {
            Property = property,
            Kind = EquipmentKind.Furnace,
            Brand = "Calderon",
            Model = "CAL-80-96",
            SerialNumber = $"SN-{unique}",
            InstalledOn = new DateOnly(2019, 10, 3),
            InstalledByUs = true,
        };

        var start = new DateTimeOffset(2026, 11, 25, 14, 0, 0, TimeSpan.Zero);
        var number = $"J-F{Random.Shared.Next(100_000, 999_999)}";
        var job = NewJob(number, branch.Id, customer, property, furnace, start, JobStatus.Dispatched, nadia.Id);
        db.Add(job);

        if (withHistoryBy is not null)
        {
            var other = await db.Technicians.SingleAsync(t => t.FirstName == withHistoryBy && t.BranchId == branch.Id, Ct);
            var earlier = NewJob($"J-H{Random.Shared.Next(100_000, 999_999)}", branch.Id, customer, property, furnace, start.AddMonths(-8), JobStatus.Completed, other.Id);
            earlier.Resolution = "Replaced the ignitor.";
            earlier.CompletedAt = start.AddMonths(-8).AddHours(1);
            db.Add(earlier);
        }

        await db.SaveChangesAsync(Ct);
        return (job.Id, number);
    });

    private static Job NewJob(string number, int branchId, Customer customer, Property property, Equipment equipment, DateTimeOffset start, JobStatus status, int technicianId) => new()
    {
        Number = number,
        BranchId = branchId,
        Customer = customer,
        Property = property,
        Equipment = equipment,
        Type = JobType.Repair,
        Priority = JobPriority.Normal,
        Status = status,
        RequiredSkill = TechnicianSkills.Furnaces,
        RequiredCertification = Certifications.GasG2,
        WindowStart = start,
        WindowEnd = start.AddHours(2),
        EstimatedMinutes = 90,
        TechnicianId = technicianId,
        Summary = "Furnace short-cycles.",
        CreatedAt = start.AddDays(-1),
        DispatchedAt = start.AddHours(-2),
    };
}
