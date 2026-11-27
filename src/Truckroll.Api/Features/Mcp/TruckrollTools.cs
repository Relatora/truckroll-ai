using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Truckroll.Api.Access;
using Truckroll.Api.Common;
using Truckroll.Api.Features.Customers;
using Truckroll.Api.Features.Dispatch;
using Truckroll.Api.Features.Dispatch.Suggestions;
using Truckroll.Api.Features.Jobs;
using Truckroll.Api.Knowledge;
using Truckroll.Data;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Mcp;

/// <summary>
/// What an AI client can do with Truckroll, as the person whose token it holds.
/// </summary>
/// <remarks>
/// <para>
/// Every tool reads through <see cref="AccessScope"/>, built from the token's owner, and most
/// call the very endpoint methods the web app uses. A technician's Claude sees a technician's
/// Truckroll; there's no separate, wider "API view" to get wrong.
/// </para>
/// <para>
/// Contact details are masked on the way out. The rule everywhere in Truckroll is that phone
/// numbers and emails never reach a model, and the client on the other end of this endpoint is
/// one. The person still sees them in the web app.
/// </para>
/// <para>
/// One tool writes, and only a draft: a job note labelled as written through MCP.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class TruckrollTools(AccessScope scope, TruckrollDbContext db, CompanyClock clock)
{
    public const int MaxNoteLength = 2000;

    [McpServerTool(Name = "search_customers", ReadOnly = true), Description("Find customers by name, street or phone number. Returns up to 10, with their ids for get_customer_history.")]
    public async Task<List<CustomerSearchResult>> SearchCustomersAsync(
        [Description("A name, part of a street address or a phone number, e.g. \"Raman\" or \"14 Maple\".")] string query,
        CancellationToken cancellationToken)
    {
        var found = await CustomerEndpoints.SearchAsync(query, scope, db, cancellationToken);
        return found.Value ?? [];
    }

    [McpServerTool(Name = "get_customer_history", ReadOnly = true), Description("A customer's homes, the equipment at each (model, serial, install date, warranty) and their recent jobs with technicians' notes, newest first.")]
    public async Task<CustomerHistory> GetCustomerHistoryAsync(
        [Description("The customer's id, from search_customers.")] int customerId,
        CancellationToken cancellationToken)
    {
        var result = await CustomerEndpoints.GetCustomerAsync(customerId, scope, db, clock, cancellationToken);
        if (result.Result is not Ok<CustomerDetail> { Value: { } c })
        {
            throw new McpException($"No customer {customerId} that you can see.");
        }

        return new CustomerHistory(
            c.Id,
            $"{c.FirstName} {c.LastName}",
            c.Branch.Name,
            c.Membership?.Plan.ToString(),
            c.Properties.Select(p => new HistoryHome(
                $"{p.Street}, {p.City}",
                p.Equipment.Select(e => new HistoryUnit(e.Kind.ToString(), $"{e.Brand} {e.Model}", e.SerialNumber, e.InstalledOn, e.InstalledByUs, e.WarrantyExpiresOn, e.Warranty.ToString(), e.ManualCode)).ToList())).ToList(),
            c.Timeline.Take(20).Select(j => new HistoryJob(
                j.Id, j.Number, j.Type.ToString(), j.Status.ToString(), j.WindowStart, PiiMasker.Mask(j.Summary),
                j.Resolution is null ? null : PiiMasker.Mask(j.Resolution), j.Technician, j.Equipment,
                j.Notes.Select(n => $"{n.Author}: {PiiMasker.Mask(n.Body)}").ToList())).ToList());
    }

    [McpServerTool(Name = "list_jobs", ReadOnly = true), Description("Jobs you can see, up to 50, earliest first. Technicians see only their own.")]
    public async Task<List<McpJob>> ListJobsAsync(
        [Description("today (default), upcoming, past, or all.")] string? day = null,
        [Description("open, or one status: Scheduled, Dispatched, InProgress, Completed, Cancelled. Leave empty for any.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var page = await JobEndpoints.ListAsync(new JobListQuery(day is null or "" ? "today" : day == "all" ? null : day, status, null, null, 1, 50), scope, db, clock, cancellationToken);
        return page.Value!.Items
            .Select(j => new McpJob(j.Id, j.Number, j.Type.ToString(), j.Priority.ToString(), j.Status.ToString(), j.WindowStart, j.WindowEnd, PiiMasker.Mask(j.Summary), j.Customer, j.Address, j.BranchCode, j.Technician))
            .ToList();
    }

    [McpServerTool(Name = "get_schedule", ReadOnly = true), Description("A branch's dispatch board for one day: each technician with their jobs, plus jobs still waiting for a technician.")]
    public async Task<Schedule> GetScheduleAsync(
        [Description("The day as yyyy-MM-dd. Default: today.")] string? date = null,
        [Description("A branch code (KIT, GUE, CAM, BRA, STR). Default: your own branch.")] string? branch = null,
        CancellationToken cancellationToken = default)
    {
        DateOnly? day = DateOnly.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        var result = await DispatchEndpoints.GetBoardAsync(day, branch, scope, db, clock, cancellationToken);
        if (result.Result is not Ok<Board> { Value: { } board })
        {
            throw new McpException($"No branch {branch} that you can see.");
        }

        McpJob Job(BoardJob j) => new(j.Id, j.Number, j.Type.ToString(), j.Priority.ToString(), j.Status.ToString(), j.WindowStart, j.WindowEnd, PiiMasker.Mask(j.Summary), j.Customer, j.Street, board.Branch.Code, null);
        return new Schedule(
            board.Date,
            board.Branch.Name,
            board.Technicians.Select(t => new ScheduleLane(t.Name, t.Certifications.ToString(), board.Jobs.Where(j => j.TechnicianId == t.Id).Select(Job).ToList())).ToList(),
            board.Jobs.Where(j => j.TechnicianId is null).Select(Job).ToList());
    }

    [McpServerTool(Name = "suggest_technicians", ReadOnly = true), Description("Who should go to a waiting job: the top three technicians ranked by Truckroll's rules (licence, skill, timing, distance, workload), with the facts behind each and who was ruled out. Suggests only; it doesn't assign.")]
    public async Task<SuggestionsResponse> SuggestTechniciansAsync(
        [Description("The job's id, from list_jobs or get_schedule.")] int jobId,
        CancellationToken cancellationToken)
    {
        var result = await SuggestionEndpoints.GetAsync(jobId, scope, db, cancellationToken);
        return result.Result switch
        {
            Ok<SuggestionsResponse> { Value: { } ok } => ok,
            Conflict<ProblemMessage> { Value: { } problem } => throw new McpException(problem.Detail),
            _ => throw new McpException($"No job {jobId} that you can see."),
        };
    }

    [McpServerTool(Name = "ask_knowledge_base", ReadOnly = true), Description("Search Truckroll's manuals, warranty terms, company policies, equipment records and job history. Returns up to 6 numbered passages to answer from and cite; it doesn't write the answer.")]
    public async Task<List<McpPassage>> AskKnowledgeBaseAsync(
        [Description("The question, in plain words, e.g. \"What does 2 flashes mean on a CAL-80?\"")] string question,
        KnowledgeSearch search,
        CancellationToken cancellationToken)
    {
        var hits = await search.SearchAsync(PiiMasker.Mask(question), scope, 6, cancellationToken: cancellationToken);
        return hits.Select((h, i) => new McpPassage(i + 1, h.Source.ToString(), h.Title, h.Text, h.Record)).ToList();
    }

    [McpServerTool(Name = "create_draft_note", Destructive = false, Idempotent = false), Description("Add a note to a job, labelled as an AI draft written through MCP for a person to review. The only change these tools can make.")]
    public async Task<DraftNote> CreateDraftNoteAsync(
        [Description("The job's id.")] int jobId,
        [Description("The note, as a technician or office person would write it.")] string text,
        IJobEvents events,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxNoteLength)
        {
            throw new McpException($"A note needs some text, up to {MaxNoteLength} characters.");
        }

        var job = await scope.Jobs(db).SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken)
            ?? throw new McpException($"No job {jobId} that you can see.");

        var note = new JobNote
        {
            JobId = job.Id,
            Author = $"{scope.DisplayName} (via MCP, draft)",
            Source = NoteSource.Mcp,
            Body = text.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new JobChanged(job.Id, job.Number, job.BranchId, job.Status, job.TechnicianId, "noted (MCP draft)", scope.DisplayName, DateTimeOffset.UtcNow), cancellationToken);

        return new DraftNote(note.Id, job.Number, note.Author, note.CreatedAt);
    }
}

public sealed record CustomerHistory(int Id, string Name, string Branch, string? Membership, List<HistoryHome> Homes, List<HistoryJob> RecentJobs);

public sealed record HistoryHome(string Address, List<HistoryUnit> Equipment);

public sealed record HistoryUnit(string Kind, string Model, string Serial, DateOnly InstalledOn, bool InstalledByUs, DateOnly? WarrantyEnds, string Warranty, string? ManualCode);

public sealed record HistoryJob(int Id, string Number, string Type, string Status, DateTimeOffset When, string Summary, string? Resolution, string? Technician, string? Equipment, List<string> Notes);

public sealed record McpJob(int Id, string Number, string Type, string Priority, string Status, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string Summary, string Customer, string Address, string Branch, string? Technician);

public sealed record Schedule(DateOnly Date, string Branch, List<ScheduleLane> Technicians, List<McpJob> Waiting);

public sealed record ScheduleLane(string Technician, string Licences, List<McpJob> Jobs);

public sealed record McpPassage(int Number, string Source, string Title, string Text, string Record);

public sealed record DraftNote(int Id, string Job, string Author, DateTimeOffset CreatedAt);
