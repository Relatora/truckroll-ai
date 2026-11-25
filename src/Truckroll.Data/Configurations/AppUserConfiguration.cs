using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> user)
    {
        user.Property(u => u.DisplayName).HasMaxLength(100);

        user.HasOne(u => u.Branch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.Restrict);
        user.HasOne(u => u.Technician).WithMany().HasForeignKey(u => u.TechnicianId).OnDelete(DeleteBehavior.SetNull);
        user.HasIndex(u => u.TechnicianId).IsUnique();
    }
}
