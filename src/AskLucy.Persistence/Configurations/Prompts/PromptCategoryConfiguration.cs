using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Prompts;

/// <summary>EF Core mapping for <see cref="PromptCategory"/> — mirrors <c>KnowledgeBaseCategoryConfiguration</c> (research.md Decision 6).</summary>
public sealed class PromptCategoryConfiguration : IEntityTypeConfiguration<PromptCategory>
{
    public void Configure(EntityTypeBuilder<PromptCategory> builder)
    {
        builder.ToTable("PromptCategories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        builder.HasIndex(c => c.OwnerId);

        // Predefined (OwnerId = null) rows are NOT seeded via HasData() here — verified (tasks.md
        // T025/E1) that KnowledgeBaseCategory's predefined rows are instead seeded via a
        // hand-written migrationBuilder.InsertData(...) call directly inside the migration's Up()
        // method (see 20260804044614_AddKnowledgeBaseManagement.cs). The AddPromptLibrary migration
        // mirrors that exact approach instead of HasData().
    }
}
