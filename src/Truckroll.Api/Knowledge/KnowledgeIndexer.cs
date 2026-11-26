using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Truckroll.Api.Access;
using Truckroll.Api.Ai;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// Reads records from MySQL, cuts them into masked passages, embeds them and writes them to the
/// index. MySQL stays the source of truth: the index can always be thrown away and rebuilt.
/// </summary>
/// <remarks>
/// It reads with <see cref="AccessScope.System"/>, because the index has to hold everything. Who
/// may see a passage is decided when someone searches, against their own scope (#12).
/// </remarks>
public sealed class KnowledgeIndexer(
    TruckrollDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    PassageIndex index,
    IOptions<AiOptions> options,
    ILogger<KnowledgeIndexer> logger)
{
    /// <summary>Texts per embedding request. Big enough to keep the GPU busy, small enough to retry.</summary>
    private const int EmbeddingBatch = 64;

    /// <summary>Jobs read from MySQL at a time, so a large history never sits in memory at once.</summary>
    private const int JobPage = 250;

    private static readonly AccessScope Scope = AccessScope.System;

    /// <summary>Drops the index and builds it again from every document, unit and job.</summary>
    public async Task<IndexReport> RebuildAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        await index.RecreateAsync();

        var documents = await Scope.Documents(db).AsNoTracking().ToListAsync(cancellationToken);
        var passages = await WriteAsync(documents.Select(d => (PassageBuilder.RecordFor(d), PassageBuilder.FromDocument(d).ToList())), cancellationToken);

        var equipment = await Scope.Equipment(db).AsNoTracking()
            .Include(e => e.Property).ThenInclude(p => p.Customer)
            .ToListAsync(cancellationToken);
        passages += await WriteAsync(equipment.Select(e => (PassageBuilder.RecordFor(e), new List<Passage> { PassageBuilder.FromEquipment(e) })), cancellationToken);

        var jobs = 0;
        var lastId = 0;
        while (true)
        {
            var page = await JobsWithText().Where(j => j.Id > lastId).OrderBy(j => j.Id).Take(JobPage).ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            passages += await WriteAsync(page.Select(j => (PassageBuilder.RecordFor(j), PassageBuilder.FromJob(j).ToList())), cancellationToken);
            jobs += page.Count;
            lastId = page[^1].Id;
        }

        var report = new IndexReport(documents.Count, equipment.Count, jobs, passages, Math.Round(clock.Elapsed.TotalSeconds, 1));
        await index.MarkBuiltAsync(report);
        logger.LogInformation(
            "Knowledge index built: {Passages} passages from {Documents} documents, {Equipment} units and {Jobs} jobs in {Seconds}s",
            report.Passages, report.Documents, report.Equipment, report.Jobs, report.Seconds);
        return report;
    }

    /// <summary>Re-indexes one job after it changed, or removes it if it's gone.</summary>
    public async Task ReindexJobAsync(int jobId, CancellationToken cancellationToken)
    {
        var job = await JobsWithText().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null)
        {
            await index.ReplaceAsync($"job:{jobId}", []);
            return;
        }

        var passages = await WriteAsync([(PassageBuilder.RecordFor(job), PassageBuilder.FromJob(job).ToList())], cancellationToken);
        logger.LogDebug("Re-indexed {Number} as {Passages} passages", job.Number, passages);
    }

    private IQueryable<Job> JobsWithText() =>
        Scope.Jobs(db).AsNoTracking().AsSplitQuery()
            .Include(j => j.Customer)
            .Include(j => j.Property)
            .Include(j => j.Equipment)
            .Include(j => j.Technician)
            .Include(j => j.Notes);

    /// <summary>
    /// Embeds records' passages in batches that cross record boundaries (one job rarely fills a
    /// batch), then replaces each record in the index. Returns how many passages were written.
    /// </summary>
    private async Task<int> WriteAsync(IEnumerable<(string Record, List<Passage> Passages)> records, CancellationToken cancellationToken)
    {
        var written = 0;
        var batch = new List<(string Record, List<Passage> Passages)>();
        foreach (var record in records)
        {
            batch.Add(record);
            if (batch.Sum(r => r.Passages.Count) >= EmbeddingBatch)
            {
                written += await FlushAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            written += await FlushAsync(batch, cancellationToken);
        }

        return written;
    }

    private async Task<int> FlushAsync(List<(string Record, List<Passage> Passages)> batch, CancellationToken cancellationToken)
    {
        var prefix = options.Value.EmbeddingDocumentPrefix;
        var texts = batch.SelectMany(r => r.Passages).Select(p => prefix + p.EmbeddingText).ToList();
        var vectors = texts.Count == 0 ? [] : await embedder.GenerateAsync(texts, cancellationToken: cancellationToken);

        var next = 0;
        foreach (var (record, passages) in batch)
        {
            var withVectors = passages.Select(p => (p, vectors[next++].Vector.ToArray())).ToList();
            await index.ReplaceAsync(record, withVectors);
        }

        return texts.Count;
    }
}
