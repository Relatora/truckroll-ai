using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Features;

/// <summary>
/// Jobs through the real API. Bookings use Sandra Kowalski's home, so they don't disturb Priya
/// Raman's history, which other tests count on.
/// </summary>
public sealed class JobTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }

    private Task<(int PropertyId, int FurnaceId)> SandrasHomeAsync() => WithDbAsync(async db =>
    {
        var property = await db.Properties.Include(p => p.Equipment).FirstAsync(p => p.Street == "140 Riverbend Rd", Ct);
        return (property.Id, property.Equipment.First(e => e.Kind == EquipmentKind.Furnace).Id);
    });

    private static object Booking(int propertyId, int? equipmentId, DateOnly date, string summary = "Furnace is making a banging noise at start-up") =>
        new { propertyId, equipmentId, type = "Repair", priority = "Normal", date = date.ToString("O"), windowStartHour = 10, summary };

    [Fact]
    public async Task Todays_list_holds_only_todays_jobs_in_the_callers_branch_in_time_order()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var page = (await alex.GetFromJsonAsync<JobPage>("/api/jobs?day=today&pageSize=100", JsonOptions.Api, Ct))!;

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, j => Assert.Equal("GUE", j.BranchCode));
        Assert.Equal(page.Items.Select(j => j.WindowStart).Order(), page.Items.Select(j => j.WindowStart));
        Assert.Contains(page.Items, j => j.Priority == JobPriority.Emergency && j.Technician is null && j.Address == "14 Maple St, Guelph");
    }

    [Fact]
    public async Task Booking_sets_the_licence_from_the_equipment_not_from_the_client()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var (propertyId, furnaceId) = await SandrasHomeAsync();

        var response = await dana.PostAsJsonAsync("/api/jobs", Booking(propertyId, furnaceId, TruckrollApiFactory.DemoToday.AddDays(2)), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<JobDetail>(JsonOptions.Api, Ct))!;
        Assert.StartsWith("J-", job.Number, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Scheduled, job.Status);
        Assert.Equal(Certifications.GasG2, job.RequiredCertification);
        Assert.Equal(TechnicianSkills.Furnaces, job.RequiredSkill);
        Assert.Equal(TimeSpan.FromHours(2), job.WindowEnd - job.WindowStart);
    }

    [Fact]
    public async Task Two_bookings_never_share_a_number()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var (propertyId, furnaceId) = await SandrasHomeAsync();
        var date = TruckrollApiFactory.DemoToday.AddDays(3);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => dana.PostAsJsonAsync("/api/jobs", Booking(propertyId, furnaceId, date), Ct)));
        var numbers = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JobDetail>(JsonOptions.Api, Ct))!.Number));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(5, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Bookings_are_checked_before_anything_is_saved()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var (propertyId, furnaceId) = await SandrasHomeAsync();
        var kitchenerProperty = await WithDbAsync(db => db.Properties.Where(p => p.Street == "88 Birch Cres").Select(p => p.Id).FirstAsync(Ct));

        var pastDay = await dana.PostAsJsonAsync("/api/jobs", Booking(propertyId, furnaceId, TruckrollApiFactory.DemoToday.AddDays(-1)), Ct);
        var otherBranch = await dana.PostAsJsonAsync("/api/jobs", Booking(kitchenerProperty, null, TruckrollApiFactory.DemoToday.AddDays(1)), Ct);
        var noSummary = await dana.PostAsJsonAsync("/api/jobs", Booking(propertyId, furnaceId, TruckrollApiFactory.DemoToday.AddDays(1), summary: " "), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, pastDay.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherBranch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noSummary.StatusCode);
        Assert.Contains("propertyId", await otherBranch.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_technician_cannot_book()
    {
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        var (propertyId, furnaceId) = await SandrasHomeAsync();

        var response = await nadia.PostAsJsonAsync("/api/jobs", Booking(propertyId, furnaceId, TruckrollApiFactory.DemoToday.AddDays(1)), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_technician_starts_and_completes_their_own_job_and_must_say_what_they_did()
    {
        var jobId = await WithDbAsync(db => db.Jobs
            .Where(j => j.Technician!.FirstName == "Nadia" && j.Status == JobStatus.Dispatched)
            .OrderBy(j => j.WindowStart)
            .Select(j => j.Id)
            .FirstAsync(Ct));
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);

        var started = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/status", new { status = "InProgress" }, Ct);
        var withoutResolution = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/status", new { status = "Completed" }, Ct);
        var completed = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/status", new { status = "Completed", resolution = "Replaced the flame sensor.", invoiceTotal = 189.50m, firstTimeFix = true }, Ct);
        var reopened = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/status", new { status = "InProgress" }, Ct);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutResolution.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reopened.StatusCode);

        var job = (await completed.Content.ReadFromJsonAsync<JobDetail>(JsonOptions.Api, Ct))!;
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(job.CompletedAt);
        Assert.Empty(job.NextStatuses);
    }

    [Fact]
    public async Task A_technician_cannot_touch_someone_elses_job()
    {
        var othersJob = await WithDbAsync(db => db.Jobs
            .Where(j => j.Technician != null && j.Technician.FirstName != "Nadia" && j.Status == JobStatus.Dispatched)
            .Select(j => j.Id)
            .FirstAsync(Ct));
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);

        var response = await nadia.PostAsJsonAsync($"/api/jobs/{othersJob}/status", new { status = "InProgress" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Notes_are_signed_by_whoever_wrote_them()
    {
        var jobId = await WithDbAsync(db => db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").Select(j => j.Id).FirstAsync(Ct));
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);

        var response = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/notes", new { body = "Customer asked about a humidifier for the winter." }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = (await response.Content.ReadFromJsonAsync<JobNoteItem>(JsonOptions.Api, Ct))!;
        Assert.Equal("Nadia Kowalczyk", note.Author);
        Assert.Equal(NoteSource.Technician, note.Source);
    }

    [Fact]
    public async Task A_failing_subscriber_never_stops_the_others_or_the_save()
    {
        var recorder = new RecordingHandler();
        var events = new InProcessJobEvents([new ThrowingHandler(), recorder], NullLogger<InProcessJobEvents>.Instance);

        await events.PublishAsync(new JobChanged(1, "J-1", 1, JobStatus.Scheduled, null, "booked", "Test", DateTimeOffset.UtcNow), Ct);

        Assert.Single(recorder.Seen);
    }

    private sealed class ThrowingHandler : IJobChangeHandler
    {
        public Task HandleAsync(JobChanged change, CancellationToken cancellationToken) => throw new InvalidOperationException("Hub down");
    }

    private sealed class RecordingHandler : IJobChangeHandler
    {
        public List<JobChanged> Seen { get; } = [];

        public Task HandleAsync(JobChanged change, CancellationToken cancellationToken)
        {
            Seen.Add(change);
            return Task.CompletedTask;
        }
    }
}
