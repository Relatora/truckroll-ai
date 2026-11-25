using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> job)
    {
        job.Property(j => j.Number).HasMaxLength(12);
        job.Property(j => j.Summary).HasMaxLength(1000);
        job.Property(j => j.Resolution).HasMaxLength(2000);

        job.HasIndex(j => j.Number).IsUnique();

        // The two queries the dispatch board runs all day: a branch's jobs in a time range,
        // and one technician's jobs in a time range.
        job.HasIndex(j => new { j.BranchId, j.WindowStart });
        job.HasIndex(j => new { j.TechnicianId, j.WindowStart });

        job.HasIndex(j => new { j.Status, j.WindowStart });

        job.HasOne(j => j.Branch).WithMany().HasForeignKey(j => j.BranchId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne(j => j.Customer).WithMany(c => c.Jobs).HasForeignKey(j => j.CustomerId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne(j => j.Property).WithMany(p => p.Jobs).HasForeignKey(j => j.PropertyId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne(j => j.Equipment).WithMany(e => e.Jobs).HasForeignKey(j => j.EquipmentId).OnDelete(DeleteBehavior.SetNull);
        job.HasOne(j => j.Technician).WithMany(t => t.Jobs).HasForeignKey(j => j.TechnicianId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class JobNoteConfiguration : IEntityTypeConfiguration<JobNote>
{
    public void Configure(EntityTypeBuilder<JobNote> note)
    {
        note.Property(n => n.Body).HasColumnType("text").IsLongText();

        note.HasOne(n => n.Job)
            .WithMany(j => j.Notes)
            .HasForeignKey(n => n.JobId);
    }
}

internal sealed class CallRecordConfiguration : IEntityTypeConfiguration<CallRecord>
{
    public void Configure(EntityTypeBuilder<CallRecord> call)
    {
        call.Property(c => c.Transcript).HasColumnType("text").IsLongText();
        call.Property(c => c.Summary).HasMaxLength(1000);
        call.Property(c => c.ProposedJobJson).HasColumnType("json").IsLongText();
        call.Property(c => c.DraftEmailBody).HasColumnType("text").IsLongText();

        // The calls inbox: a branch's calls by status, newest first.
        call.HasIndex(c => new { c.BranchId, c.Status, c.ReceivedAt });

        call.HasOne(c => c.Branch).WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
        call.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.SetNull);
        call.HasOne(c => c.CreatedJob).WithMany().HasForeignKey(c => c.CreatedJobId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> document)
    {
        document.Property(d => d.Code).HasMaxLength(20);
        document.Property(d => d.AppliesToModel).HasMaxLength(40);
        document.Property(d => d.Body).HasColumnType("mediumtext").IsLongText();

        document.HasIndex(d => d.Code).IsUnique();

        document.HasOne(d => d.Branch).WithMany().HasForeignKey(d => d.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
