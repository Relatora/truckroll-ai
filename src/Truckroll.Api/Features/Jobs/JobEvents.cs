using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Jobs;

/// <summary>Something happened to a job: booked, assigned, started, finished, cancelled, noted.</summary>
public sealed record JobChanged(int JobId, string Number, int BranchId, JobStatus Status, int? TechnicianId, string Change, string ChangedBy, DateTimeOffset At);

/// <summary>
/// Where job changes go after they're saved. Today: in-process subscribers (the log, and the
/// dispatch board's SignalR hub from #9). In v0.2 the same events go to Kafka through an outbox,
/// so other services hear about them reliably.
/// </summary>
public interface IJobEvents
{
    Task PublishAsync(JobChanged change, CancellationToken cancellationToken);
}

/// <summary>A subscriber to job changes. Register as many as needed; each gets every event.</summary>
public interface IJobChangeHandler
{
    Task HandleAsync(JobChanged change, CancellationToken cancellationToken);
}

/// <summary>Fans each event out to every registered handler, in process.</summary>
public sealed class InProcessJobEvents(IEnumerable<IJobChangeHandler> handlers, ILogger<InProcessJobEvents> logger) : IJobEvents
{
    public async Task PublishAsync(JobChanged change, CancellationToken cancellationToken)
    {
        logger.LogInformation("Job {Number} {Change} by {ChangedBy}: now {Status}", change.Number, change.Change, change.ChangedBy, change.Status);

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(change, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failing subscriber (say, a dropped hub connection) must never undo a saved job.
                logger.LogWarning(exception, "A job change handler failed for {Number}", change.Number);
            }
        }
    }
}
