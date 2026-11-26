using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Truckroll.Api.Access;
using Truckroll.Api.Features.Jobs;
using Truckroll.Data;

namespace Truckroll.Api.Features.Dispatch;

/// <summary>
/// The dispatch board's live channel. Clients join their branch's group and hear about every job
/// change there, so two dispatchers never work from different versions of the day.
/// </summary>
/// <remarks>
/// It carries notices ("J-22248 was assigned"), not data. Clients refetch the board, which keeps
/// one source of truth (the API) and the same access rules as every other read.
/// </remarks>
[Authorize]
public sealed class DispatchHub(TruckrollDbContext db) : Hub
{
    public const string Path = "/hubs/dispatch";

    public static string GroupFor(int branchId) => $"branch-{branchId}";

    /// <summary>Start hearing a branch's changes. Refused for a branch outside the caller's scope.</summary>
    public async Task JoinBranch(string code)
    {
        var scope = AccessScope.FromPrincipal(Context.User!);
        var branchId = await scope.Branches(db).Where(b => b.Code == code).Select(b => (int?)b.Id).SingleOrDefaultAsync();
        if (branchId is null)
        {
            throw new HubException($"No branch {code} in your scope.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(branchId.Value));
    }

    public Task LeaveBranch(int branchId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(branchId));
}

/// <summary>Forwards every job change to the branch's group on the hub.</summary>
public sealed class DispatchHubBroadcaster(IHubContext<DispatchHub> hub) : IJobChangeHandler
{
    public Task HandleAsync(JobChanged change, CancellationToken cancellationToken) =>
        hub.Clients.Group(DispatchHub.GroupFor(change.BranchId)).SendAsync("jobChanged", change, cancellationToken);
}
