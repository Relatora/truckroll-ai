using System.Threading.Channels;
using Truckroll.Api.Features.Jobs;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// The to-do list for the index: jobs that changed and full rebuilds that were asked for.
/// Subscribes to job events, so a new note is searchable seconds after it's saved.
/// </summary>
/// <remarks>
/// One queue and one worker means index writes never race: a rebuild and a job's re-index can't
/// interleave and leave the job's old passages behind. The request that saved the job never waits
/// for the model either; it only drops a message here.
/// </remarks>
public sealed class KnowledgeQueue : IJobChangeHandler
{
    private readonly Channel<IndexRequest> _channel = Channel.CreateUnbounded<IndexRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the startup check has run (built, already current, or failed and logged).</summary>
    public Task Ready => _ready.Task;

    internal ChannelReader<IndexRequest> Reader => _channel.Reader;

    public Task HandleAsync(JobChanged change, CancellationToken cancellationToken)
    {
        _channel.Writer.TryWrite(new IndexRequest.ReindexJob(change.JobId));
        return Task.CompletedTask;
    }

    /// <summary>Queues a full rebuild and completes with its report.</summary>
    public Task<IndexReport> RebuildAsync()
    {
        var done = new TaskCompletionSource<IndexReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        _channel.Writer.TryWrite(new IndexRequest.Rebuild(done));
        return done.Task;
    }

    /// <summary>Queues a full rebuild without waiting for it (after a demo reset, say).</summary>
    public void RequestRebuild() => _channel.Writer.TryWrite(new IndexRequest.Rebuild(null));

    internal void MarkReady() => _ready.TrySetResult();
}

internal abstract record IndexRequest
{
    public sealed record ReindexJob(int JobId) : IndexRequest;

    public sealed record Rebuild(TaskCompletionSource<IndexReport>? Done) : IndexRequest;
}

/// <summary>
/// Keeps the index in step with MySQL. At startup it rebuilds the index if it's missing or was
/// built differently (another model, width or schema version); after that it works through the
/// <see cref="KnowledgeQueue"/>.
/// </summary>
public sealed class KnowledgeSyncService(
    KnowledgeQueue queue,
    PassageIndex index,
    IServiceScopeFactory scopes,
    ILogger<KnowledgeSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await index.IsCurrentAsync())
            {
                logger.LogInformation("The knowledge index is missing or out of date; building it");
                await RunAsync(new IndexRequest.Rebuild(null), stoppingToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Most likely Ollama isn't running. The CRM works without the index; the assistant
            // just has nothing to cite until a rebuild succeeds.
            logger.LogWarning(exception, "Couldn't build the knowledge index. Retrieval is empty until a rebuild succeeds (POST /api/dev/reindex)");
        }
        finally
        {
            queue.MarkReady();
        }

        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAsync(request, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Knowledge index update failed: {Request}", request);
                (request as IndexRequest.Rebuild)?.Done?.TrySetException(exception);
            }
        }
    }

    private async Task RunAsync(IndexRequest request, CancellationToken cancellationToken)
    {
        // A scope per request: the indexer uses the DbContext, which mustn't outlive one unit of work.
        await using var scope = scopes.CreateAsyncScope();
        var indexer = scope.ServiceProvider.GetRequiredService<KnowledgeIndexer>();
        switch (request)
        {
            case IndexRequest.ReindexJob job:
                await indexer.ReindexJobAsync(job.JobId, cancellationToken);
                break;
            case IndexRequest.Rebuild rebuild:
                var report = await indexer.RebuildAsync(cancellationToken);
                rebuild.Done?.TrySetResult(report);
                break;
        }
    }
}

/// <summary>Registers the index, the indexer and the sync worker.</summary>
public static class KnowledgeServiceExtensions
{
    public static IServiceCollection AddTruckrollKnowledge(this IServiceCollection services)
    {
        services.AddSingleton<PassageIndex>();
        services.AddScoped<KnowledgeIndexer>();
        services.AddSingleton<KnowledgeQueue>();
        services.AddSingleton<IJobChangeHandler>(provider => provider.GetRequiredService<KnowledgeQueue>());
        services.AddHostedService<KnowledgeSyncService>();
        return services;
    }
}
