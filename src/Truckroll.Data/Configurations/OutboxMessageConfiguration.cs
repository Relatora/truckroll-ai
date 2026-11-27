using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> message)
    {
        message.Property(m => m.Type).HasMaxLength(40);
        message.Property(m => m.Payload).HasColumnType("json").IsLongText();
        message.Property(m => m.LastError).HasMaxLength(500);

        message.HasIndex(m => m.EventId).IsUnique();

        // The relay's one query: the oldest unsent rows. Sent rows sort out of the way (NULL first
        // in MySQL, so the waiting ones are at the front of the index).
        message.HasIndex(m => new { m.PublishedAt, m.Id });
    }
}
