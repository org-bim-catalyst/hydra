using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Documents;

public sealed class DocumentLanguageConfiguration : IEntityTypeConfiguration<DocumentLanguage>
{
    public void Configure(EntityTypeBuilder<DocumentLanguage> builder)
    {
        builder.ToTable("DocumentLanguages");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(l => l.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.ConfidenceScore).HasColumnType("decimal(5,4)").IsRequired();

        builder.Property(l => l.CreatedBy).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => l.DocumentId);

        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(l => l.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
