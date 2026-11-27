using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Truckroll.Data.Domain;

namespace Truckroll.Data.Configurations;

internal sealed class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> token)
    {
        token.Property(t => t.Name).HasMaxLength(60);
        token.Property(t => t.Prefix).HasMaxLength(12);
        token.Property(t => t.Hash).HasMaxLength(64).IsFixedLength();

        // Every MCP request finds its token by hash, so that's the index that matters.
        token.HasIndex(t => t.Hash).IsUnique();

        // A deleted account takes its tokens with it.
        token.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
