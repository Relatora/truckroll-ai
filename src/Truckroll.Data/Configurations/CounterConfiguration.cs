using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class CounterConfiguration : IEntityTypeConfiguration<Counter>
{
    public void Configure(EntityTypeBuilder<Counter> counter)
    {
        counter.HasKey(c => c.Name);
        counter.Property(c => c.Name).HasMaxLength(40);
    }
}
