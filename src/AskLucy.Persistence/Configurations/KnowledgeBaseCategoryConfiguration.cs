using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

public sealed class KnowledgeBaseCategoryConfiguration : IEntityTypeConfiguration<KnowledgeBaseCategory>
{
    public void Configure(EntityTypeBuilder<KnowledgeBaseCategory> builder)
    {
        builder.ToTable("KnowledgeBaseCategories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.OwnerId);
    }
}
