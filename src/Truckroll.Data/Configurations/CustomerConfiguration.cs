using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> customer)
    {
        customer.Ignore(c => c.FullName);
        customer.Property(c => c.Phone).HasMaxLength(20);

        // The customer list sorts by name within a branch; caller ID looks up by phone.
        customer.HasIndex(c => new { c.BranchId, c.LastName, c.FirstName });
        customer.HasIndex(c => c.Phone);
        customer.HasIndex(c => c.Email);

        customer.HasOne(c => c.Branch)
            .WithMany(b => b.Customers)
            .HasForeignKey(c => c.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        customer.HasOne(c => c.Membership)
            .WithOne(m => m.Customer)
            .HasForeignKey<Membership>(m => m.CustomerId);
    }
}

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> property)
    {
        property.Ignore(p => p.ShortAddress);
        property.Property(p => p.PostalCode).HasMaxLength(7);
        property.Property(p => p.AccessNotes).HasMaxLength(500);

        // "What did we install at 14 Maple St?" starts with finding the street.
        property.HasIndex(p => new { p.City, p.Street });

        property.HasOne(p => p.Customer)
            .WithMany(c => c.Properties)
            .HasForeignKey(p => p.CustomerId);
    }
}

internal sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> equipment)
    {
        equipment.Property(e => e.Model).HasMaxLength(40);
        equipment.Property(e => e.SerialNumber).HasMaxLength(40);

        equipment.HasIndex(e => e.SerialNumber).IsUnique();
        equipment.HasIndex(e => e.Model);

        equipment.HasOne(e => e.Property)
            .WithMany(p => p.Equipment)
            .HasForeignKey(e => e.PropertyId);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> membership)
    {
        // The dashboard counts renewals coming due.
        membership.HasIndex(m => new { m.Status, m.RenewsOn });
    }
}
