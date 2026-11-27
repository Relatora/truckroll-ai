using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Truckroll.Api.Events;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Tests.Infrastructure;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Events;

/// <summary>
/// The outbox's promise: an event exists exactly when its change committed, and the relay hands
/// events on in order, retrying rather than skipping (docs/adr/005).
/// </summary>
public sealed class OutboxTests(TruckrollApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_change_and_its_event_are_saved_together_then_relayed()
    {
        var jobId = await WithDbAsync(db => db.Jobs.Where(j => j.Technician!.FirstName == "Nadia").Select(j => j.Id).FirstAsync(Ct));
        using var nadia = await factory.SignedInAsAsync(SignedInClients.GuelphTechnician);
        var before = await WithDbAsync(db => db.OutboxMessages.MaxAsync(m => (long?)m.Id, Ct)) ?? 0;

        var response = await nadia.PostAsJsonAsync($"/api/jobs/{jobId}/notes", new { body = "Filter changed, customer has spares in the garage." }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var events = await WithDbAsync(db => db.OutboxMessages.Where(m => m.Id > before && m.JobId == jobId).ToListAsync(Ct));
        var noted = Assert.Single(events);
        Assert.Equal("job.noted", noted.Type);
        Assert.Equal("Nadia Kowalczyk", JsonSerializer.Deserialize<JobChanged>(noted.Payload, OutboxJobEvents.Json)!.ChangedBy);

        // The relay picks it up after the commit, without waiting for its poll.
        var published = await EventuallyAsync(() => WithDbAsync(db => db.OutboxMessages.AnyAsync(m => m.Id == noted.Id && m.PublishedAt != null, Ct)));
        Assert.True(published, "The relay never marked the event as sent.");
    }

    [Fact]
    public async Task A_rolled_back_change_leaves_no_event()
    {
        // A job id no seeded job has, so this event can't be confused with a real one.
        const int phantomJob = 987_654;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            var events = scope.ServiceProvider.GetRequiredService<IJobEvents>();

            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            await events.PublishAsync(Change(phantomJob, "booked"), Ct);
            await transaction.RollbackAsync(Ct);
        }

        Assert.False(await WithDbAsync(db => db.OutboxMessages.AnyAsync(m => m.JobId == phantomJob, Ct)));
    }

    [Fact]
    public async Task Publishing_outside_a_transaction_fails_loudly()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var events = scope.ServiceProvider.GetRequiredService<IJobEvents>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => events.PublishAsync(Change(1, "booked"), Ct));

        Assert.Contains("inside the transaction", error.Message);
    }

    [Theory]
    [InlineData("booked", "job.booked")]
    [InlineData("assigned to Nadia Kowalczyk", "job.assigned")]
    [InlineData("unassigned", "job.unassigned")]
    [InlineData("inprogress", "job.started")]
    [InlineData("completed", "job.completed")]
    [InlineData("noted (MCP draft)", "job.noted")]
    public void Events_get_stable_names_whatever_the_wording(string change, string type) =>
        Assert.Equal(type, Change(1, change).Type);

    [Fact]
    public async Task A_failing_subscriber_never_stops_the_others()
    {
        var recorder = new RecordingHandler();
        var transport = new InProcessTransport([new ThrowingHandler(), recorder], NullLogger<InProcessTransport>.Instance);

        await transport.SendAsync(Message(1, Change(1, "booked")), Ct);

        Assert.Single(recorder.Seen);
    }

    /// <summary>
    /// The relay on a database of its own, so the API's own relay (which runs for every test)
    /// can't send these rows first.
    /// </summary>
    [Fact]
    public async Task The_relay_sends_in_order_stops_at_a_failure_and_resumes_where_it_stopped()
    {
        await using var services = new ServiceCollection()
            .AddDbContext<TruckrollDbContext>(options => options.UseMySql(factory.ConnectionStringFor("relay_tests"), TruckrollDbContext.ServerVersion))
            .AddMetrics()
            .AddSingleton<EventMetrics>()
            .BuildServiceProvider();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
            await db.Database.EnsureDeletedAsync(Ct);
            await db.Database.EnsureCreatedAsync(Ct);
            db.OutboxMessages.AddRange(Message(0, Change(1, "booked")), Message(0, Change(1, "assigned to Nadia Kowalczyk")), Message(0, Change(1, "unassigned")));
            await db.SaveChangesAsync(Ct);
        }

        // The broker is down for the second event, once.
        var transport = new FlakyTransport(failOnSend: 2);
        var relay = new OutboxRelay(services.GetRequiredService<IServiceScopeFactory>(), transport, new OutboxSignal(), services.GetRequiredService<EventMetrics>(), TimeProvider.System, NullLogger<OutboxRelay>.Instance);

        await Assert.ThrowsAsync<IOException>(() => relay.RelayBatchAsync(Ct));

        Assert.Equal(["job.booked"], transport.Delivered);
        await using (var scope = services.CreateAsyncScope())
        {
            var rows = await scope.ServiceProvider.GetRequiredService<TruckrollDbContext>().OutboxMessages.OrderBy(m => m.Id).ToListAsync(Ct);
            Assert.NotNull(rows[0].PublishedAt);
            Assert.Null(rows[1].PublishedAt);
            Assert.Equal(1, rows[1].Attempts);
            Assert.Equal("Broker unavailable", rows[1].LastError);
            Assert.Null(rows[2].PublishedAt); // Never sent ahead of the one that failed.
        }

        // Back up: the rest go, in order, and nothing is sent twice.
        Assert.Equal(2, await relay.RelayBatchAsync(Ct));
        Assert.Equal(["job.booked", "job.assigned", "job.unassigned"], transport.Delivered);
        Assert.Equal(0, await relay.RelayBatchAsync(Ct));
    }

    private static JobChanged Change(int jobId, string change) =>
        new(jobId, $"J-{jobId}", 1, JobStatus.Scheduled, null, change, "Test", DateTimeOffset.UtcNow);

    private static OutboxMessage Message(long id, JobChanged change) => new()
    {
        Id = id,
        EventId = Guid.NewGuid(),
        Type = change.Type,
        JobId = change.JobId,
        Payload = JsonSerializer.Serialize(change, OutboxJobEvents.Json),
        OccurredAt = change.At,
    };

    private async Task<T> WithDbAsync<T>(Func<TruckrollDbContext, Task<T>> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TruckrollDbContext>());
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var tries = 0; tries < 50; tries++)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100, Ct);
        }

        return false;
    }

    private sealed class FlakyTransport(int failOnSend) : IEventTransport
    {
        private int _sends;

        public List<string> Delivered { get; } = [];

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (++_sends == failOnSend)
            {
                throw new IOException("Broker unavailable");
            }

            Delivered.Add(message.Type);
            return Task.CompletedTask;
        }
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
