using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Features.Notifications;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Events;

/// <summary>The notifications subscriber: one draft per assignment, however often the event arrives.</summary>
public sealed class NotificationTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Assigning_a_job_drafts_a_message_for_the_customer()
    {
        var (jobId, technicianId) = await WaitingJobAndLicensedTechnicianAsync();
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var assigned = await alex.PostAsJsonAsync("/api/dispatch/assign", new { jobId, technicianId }, Ct);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        // Drafted by the subscriber after the commit, so it may take a moment.
        List<CustomerUpdate>? updates = null;
        for (var tries = 0; tries < 50 && updates is not { Count: > 0 }; tries++)
        {
            await Task.Delay(100, Ct);
            updates = await alex.GetFromJsonAsync<List<CustomerUpdate>>($"/api/jobs/{jobId}/customer-updates", JsonOptions.Api, Ct);
        }

        var update = Assert.Single(updates!);
        Assert.Equal("technician-assigned", update.Kind);
        Assert.Contains("is booked", update.Subject);
        Assert.Contains("Kestrel Home Comfort", update.Body);
        Assert.DoesNotContain("..", update.Body); // "2 p.m." must never meet a full stop.
    }

    [Fact]
    public async Task The_same_event_delivered_twice_drafts_one_message()
    {
        var (jobId, technicianId) = await WaitingJobAndLicensedTechnicianAsync();
        await WithDbAsync(async db =>
        {
            var job = await db.Jobs.SingleAsync(j => j.Id == jobId, Ct);
            job.TechnicianId = technicianId;
            job.Status = JobStatus.Dispatched;
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        var drafter = new TechnicianAssignedDrafter(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<TechnicianAssignedDrafter>.Instance);
        var change = new JobChanged(jobId, "J-?", 1, JobStatus.Dispatched, technicianId, "assigned to someone", "Test", DateTimeOffset.UtcNow) { EventId = Guid.NewGuid() };

        // At least once means this can happen, including two at the same moment.
        await drafter.HandleAsync(change, Ct);
        await Task.WhenAll(drafter.HandleAsync(change, Ct), drafter.HandleAsync(change, Ct));

        Assert.Equal(1, await WithDbAsync(db => db.CustomerNotifications.CountAsync(n => n.EventId == change.EventId, Ct)));
    }

    [Fact]
    public async Task Only_people_who_can_see_the_job_see_its_drafts()
    {
        var kitchenerJob = await WithDbAsync(db => db.Jobs.Where(j => j.Branch.Code == "KIT").Select(j => j.Id).FirstAsync(Ct));
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var response = await alex.GetAsync($"/api/jobs/{kitchenerJob}/customer-updates", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A Guelph job still waiting, on a free day, and a Guelph technician who holds its licence:
    /// booked fresh, so other tests' assignments can't collide with it.
    /// </summary>
    private async Task<(int JobId, int TechnicianId)> WaitingJobAndLicensedTechnicianAsync()
    {
        var (propertyId, technicianId) = await WithDbAsync(async db =>
        {
            var property = await db.Properties.Where(p => p.Customer.Branch.Code == "GUE").OrderBy(p => p.Id).Skip(7).FirstAsync(Ct);
            var tech = await db.Technicians.Where(t => t.Branch.Code == "GUE" && t.IsActive).OrderBy(t => t.Id).FirstAsync(Ct);
            return (property.Id, tech.Id);
        });
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        // A day far enough out that no seeded job is in the window, with a random hour so two
        // runs of these tests don't book the same technician into the same slot.
        var day = TruckrollApiFactory.DemoToday.AddDays(40 + Random.Shared.Next(200));
        var booked = await alex.PostAsJsonAsync("/api/jobs", new
        {
            propertyId,
            type = "Maintenance",
            priority = "Normal",
            date = day,
            windowStartHour = 8 + (2 * Random.Shared.Next(4)),
            summary = "Annual tune-up (notification test).",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var job = (await booked.Content.ReadFromJsonAsync<JobDetail>(JsonOptions.Api, Ct))!;
        return (job.Id, technicianId);
    }

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }
}
