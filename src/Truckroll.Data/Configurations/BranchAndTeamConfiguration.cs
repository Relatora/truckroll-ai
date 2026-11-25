using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> branch)
    {
        branch.Property(b => b.Code).HasMaxLength(8);
        branch.HasIndex(b => b.Code).IsUnique();
    }
}

internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> technician)
    {
        technician.Property(t => t.Color).HasMaxLength(9);
        technician.Ignore(t => t.FullName);

        technician.HasOne(t => t.Branch)
            .WithMany(b => b.Technicians)
            .HasForeignKey(t => t.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
