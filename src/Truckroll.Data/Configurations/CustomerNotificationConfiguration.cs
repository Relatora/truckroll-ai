using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class CustomerNotificationConfiguration : IEntityTypeConfiguration<CustomerNotification>
{
    public void Configure(EntityTypeBuilder<CustomerNotification> notification)
    {
        notification.Property(n => n.Kind).HasMaxLength(40);
        notification.Property(n => n.Body).HasColumnType("text").IsLongText();

        // The idempotency key: a redelivered event can't draft a second message.
        notification.HasIndex(n => n.EventId).IsUnique();

        notification.HasOne(n => n.Job).WithMany().HasForeignKey(n => n.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
