using Truckroll.Api.Access;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Features.Jobs;

/// <summary>
/// The rules of a job, in one place: what a piece of equipment requires of whoever works on it,
/// which way a job's status can move, and who may move it.
/// </summary>
public static class JobRules
{
    /// <summary>
    /// The skill and licence work on this equipment needs. Decided here, never by the client: a
    /// form bug must not be able to book gas work for someone without a gas licence.
    /// </summary>
    public static (TechnicianSkills Skill, Certifications Certification) RequirementsFor(EquipmentKind? kind, JobType type)
    {
        if (type is JobType.Installation or JobType.Estimate)
        {
            return (TechnicianSkills.Installations, kind is null ? Certifications.None : RequirementsFor(kind, JobType.Repair).Certification);
        }

        return kind switch
        {
            EquipmentKind.Furnace => (TechnicianSkills.Furnaces, Certifications.GasG2),
            EquipmentKind.Boiler => (TechnicianSkills.Boilers, Certifications.GasG2),
            EquipmentKind.WaterHeater or EquipmentKind.TanklessWaterHeater => (TechnicianSkills.WaterHeaters, Certifications.GasG2),
            EquipmentKind.AirConditioner => (TechnicianSkills.AirConditioning, Certifications.Refrigeration313A),
            EquipmentKind.HeatPump => (TechnicianSkills.HeatPumps, Certifications.Refrigeration313A),
            EquipmentKind.Humidifier or EquipmentKind.AirExchanger => (TechnicianSkills.IndoorAirQuality, Certifications.None),
            _ => (TechnicianSkills.Plumbing, Certifications.None),
        };
    }

    /// <summary>The statuses a job can move to from here. Forward only; cancel any time before done.</summary>
    public static IReadOnlyList<JobStatus> NextStatuses(JobStatus current) => current switch
    {
        JobStatus.New => [JobStatus.Scheduled, JobStatus.Cancelled],
        JobStatus.Scheduled => [JobStatus.Cancelled],
        JobStatus.Dispatched => [JobStatus.InProgress, JobStatus.Cancelled],
        JobStatus.InProgress => [JobStatus.Completed, JobStatus.Cancelled],
        _ => [],
    };

    /// <summary>
    /// Whether this person may move this job to that status. Office roles run the board; a
    /// technician starts and finishes their own jobs and nothing else.
    /// </summary>
    public static bool MayMove(AccessScope scope, Job job, JobStatus to)
    {
        if (!NextStatuses(job.Status).Contains(to))
        {
            return false;
        }

        if (scope.Role == UserRole.Technician)
        {
            return job.TechnicianId == scope.TechnicianId && to is JobStatus.InProgress or JobStatus.Completed;
        }

        return to switch
        {
            JobStatus.Cancelled or JobStatus.Scheduled => scope.CanBookJobs,
            // Assigning (Scheduled → Dispatched) happens on the dispatch board (#9), not here.
            _ => scope.CanDispatch || scope.CanBookJobs,
        };
    }
}
