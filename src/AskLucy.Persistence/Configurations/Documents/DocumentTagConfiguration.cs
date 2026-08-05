using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

/// <summary>Has its own top-level `DbSet` — shared/reused across a user's documents (many-to-many, unlike per-instance `KnowledgeBaseTag`), needed for `ListTags`'s cross-document distinct query.</summary>
public sealed class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.ToTable("DocumentTags");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(50);

        builder.Property(t => t.CreatedBy).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasQueryFilter(t => t.DeletedAtUtc == null);

        builder.HasIndex(t => new { t.OwnerId, t.Name }).IsUnique();
    }
}
