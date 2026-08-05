using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentClassificationConfiguration : IEntityTypeConfiguration<DocumentClassification>
{
    public void Configure(EntityTypeBuilder<DocumentClassification> builder)
    {
        builder.ToTable("DocumentClassifications");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.ConfidenceScore).HasColumnType("decimal(5,4)");

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => c.DocumentId).IsUnique();
        builder.HasIndex(c => c.CategoryId);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DocumentCategory>()
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
