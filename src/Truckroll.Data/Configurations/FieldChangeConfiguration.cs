using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class FieldChangeConfiguration : IEntityTypeConfiguration<FieldChange>
{
    public void Configure(EntityTypeBuilder<FieldChange> change)
    {
        // The phone's id is the key: inserting the same change twice fails on it, which is the
        // last line of defence if two syncs from one phone ever race.
        change.HasKey(c => c.Id);
        change.Property(c => c.Id).ValueGeneratedNever();
        change.Property(c => c.UserId).HasMaxLength(255);
        change.Property(c => c.Kind).HasMaxLength(20);
        change.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20);
        change.Property(c => c.Explanation).HasMaxLength(500);
        change.HasIndex(c => c.JobId);
    }
}
