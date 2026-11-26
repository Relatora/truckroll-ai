using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Tests.Features;

/// <summary>
/// The dispatch board and assigning work. These tests leave Priya Raman's emergency alone (other
/// tests expect it waiting) and assign other jobs from Guelph's day instead.
/// </summary>
public sealed class DispatchTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }

    /// <summary>
    /// A waiting Guelph job (not Priya's) and a Guelph technician who could legally take it,
    /// either free in its window or, with <paramref name="busy"/>, already booked in it. Tests that
    /// assign pass their own <paramref name="hour"/>, so parallel tests never reach for the same job.
    /// </summary>
    private Task<(int JobId, int TechnicianId)?> FindAsync(bool busy, int? hour = null) => WithDbAsync<(int, int)?>(async db =>
    {
        var jobs = await db.Jobs
            .Where(j => j.Branch.Code == "GUE" && j.Status == JobStatus.Scheduled && j.TechnicianId == null && j.Customer.LastName != "Raman")
            .Select(j => new { j.Id, j.WindowStart, j.WindowEnd, j.RequiredCertification })
            .ToListAsync(Ct);
        var technicians = await db.Technicians.Where(t => t.Branch.Code == "GUE").ToListAsync(Ct);

        foreach (var job in jobs.Where(j => hour is null || TimeZoneInfo.ConvertTime(j.WindowStart, DemoDataGenerator.LocalZone).Hour == hour))
        {
            foreach (var technician in technicians.Where(t => (t.Certifications & job.RequiredCertification) == job.RequiredCertification))
            {
                var clash = await Schedules.FindClashAsync(db, technician.Id, job.WindowStart, job.WindowEnd, job.Id, Ct);
                if ((clash is not null) == busy)
                {
                    return (job.Id, technician.Id);
                }
            }
        }

        return null;
    });

    [Fact]
    public async Task The_board_shows_the_branch_day_with_the_emergency_waiting()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);

        var board = (await alex.GetFromJsonAsync<Board>("/api/dispatch/board", JsonOptions.Api, Ct))!;

        Assert.Equal("GUE", board.Branch.Code);
        Assert.True(board.CanDispatch);
        Assert.Contains(board.Technicians, t => t.Name == "Nadia Kowalczyk");
        Assert.Contains(board.Jobs, j => j.Priority == JobPriority.Emergency && j.TechnicianId is null && j.Street == "14 Maple St");
    }

    [Fact]
    public async Task No_licence_no_job()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        var (emergency, tyler) = await WithDbAsync(async db => (
            await db.Jobs.Where(j => j.Property.Street == "14 Maple St" && j.Priority == JobPriority.Emergency && j.Status == JobStatus.Scheduled).Select(j => j.Id).SingleAsync(Ct),
            await db.Technicians.Where(t => t.FirstName == "Tyler").Select(t => t.Id).SingleAsync(Ct)));

        var response = await alex.PostAsJsonAsync("/api/dispatch/assign", new AssignRequest(emergency, tyler), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("G2 gas licence", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assigning_and_unassigning_moves_a_job_between_lanes()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        var (jobId, technicianId) = (await FindAsync(busy: false, hour: 12))!.Value;

        var assigned = await alex.PostAsJsonAsync("/api/dispatch/assign", new AssignRequest(jobId, technicianId), Ct);
        var unassigned = await alex.PostAsJsonAsync("/api/dispatch/unassign", new UnassignRequest(jobId), Ct);

        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Equal(JobStatus.Dispatched, (await assigned.Content.ReadFromJsonAsync<BoardJob>(JsonOptions.Api, Ct))!.Status);
        var back = (await unassigned.Content.ReadFromJsonAsync<BoardJob>(JsonOptions.Api, Ct))!;
        Assert.Equal(JobStatus.Scheduled, back.Status);
        Assert.Null(back.TechnicianId);
    }

    [Fact]
    public async Task A_technician_is_never_in_two_places_at_once()
    {
        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        var found = await FindAsync(busy: true);
        Assert.NotNull(found);

        var response = await alex.PostAsJsonAsync("/api/dispatch/assign", new AssignRequest(found.Value.JobId, found.Value.TechnicianId), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already has", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_service_can_see_the_board_but_not_assign()
    {
        using var dana = await factory.SignedInAsAsync(SignedInClients.GuelphCsr);
        var (jobId, technicianId) = (await FindAsync(busy: false))!.Value;

        var board = (await dana.GetFromJsonAsync<Board>("/api/dispatch/board", JsonOptions.Api, Ct))!;
        var response = await dana.PostAsJsonAsync("/api/dispatch/assign", new AssignRequest(jobId, technicianId), Ct);

        Assert.False(board.CanDispatch);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Everyone_watching_the_branch_hears_about_a_change_and_nobody_else_can_listen()
    {
        var cookie = await SessionCookieAsync(SignedInClients.GuelphDispatcher);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, DispatchHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["Cookie"] = cookie;
            })
            .Build();
        var (jobId, technicianId) = (await FindAsync(busy: false, hour: 14))!.Value;
        var heard = new TaskCompletionSource<JobChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Other tests change Guelph jobs at the same time; wait for this one.
        hub.On<JobChanged>("jobChanged", change =>
        {
            if (change.JobId == jobId)
            {
                heard.TrySetResult(change);
            }
        });
        await hub.StartAsync(Ct);
        await hub.InvokeAsync("JoinBranch", "GUE", Ct);

        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinBranch", "KIT", Ct));

        using var alex = await factory.SignedInAsAsync(SignedInClients.GuelphDispatcher);
        (await alex.PostAsJsonAsync("/api/dispatch/assign", new AssignRequest(jobId, technicianId), Ct)).EnsureSuccessStatusCode();

        var change = await heard.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(jobId, change.JobId);
        Assert.StartsWith("assigned to", change.Change, StringComparison.Ordinal);

        await alex.PostAsJsonAsync("/api/dispatch/unassign", new UnassignRequest(jobId), Ct);
    }

    private async Task<string> SessionCookieAsync(string email)
    {
        using var client = factory.CreateClient(new() { HandleCookies = false });
        var response = await client.PostAsJsonAsync("/api/auth/sign-in", new { email, password = DemoSeeder.DemoPassword }, Ct);
        response.EnsureSuccessStatusCode();
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }
}
