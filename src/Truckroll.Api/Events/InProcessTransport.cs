using System.Text.Json;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Events;

/// <summary>
/// Delivers events to the handlers in this process: the dispatch board's hub and the knowledge
/// index. What v0.1 did straight from the endpoints, now only for changes that committed.
/// </summary>
/// <remarks>
/// Handlers are best-effort here, as they were in v0.1: one that fails is logged and the others
/// still run, and the event counts as sent. A dropped board update heals on the next refresh; it
/// isn't worth holding every later event back for.
/// </remarks>
public sealed class InProcessTransport(IEnumerable<IJobChangeHandler> handlers, ILogger<InProcessTransport> logger) : IEventTransport
{
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var change = JsonSerializer.Deserialize<JobChanged>(message.Payload, OutboxJobEvents.Json)
            ?? throw new InvalidOperationException($"Outbox event {message.EventId} has an empty payload.");

        logger.LogInformation("Job {Number} {Change} by {ChangedBy}: now {Status}", change.Number, change.Change, change.ChangedBy, change.Status);

        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(change, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "A job change handler failed for {Number}", change.Number);
            }
        }
    }
}
