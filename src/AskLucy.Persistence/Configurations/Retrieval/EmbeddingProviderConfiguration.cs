using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Retrieval;

public sealed class EmbeddingProviderConfiguration : IEntityTypeConfiguration<EmbeddingProvider>
{
    public void Configure(EntityTypeBuilder<EmbeddingProvider> builder)
    {
        builder.ToTable("EmbeddingProviders");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Vendor).IsRequired().HasMaxLength(100);
        builder.Property(p => p.ModelKey).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Dimensionality).IsRequired();
        builder.Property(p => p.HostingType).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(p => p.IsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        builder.HasIndex(p => p.HostingType);
        builder.HasIndex(p => new { p.HostingType, p.IsDefault });
    }
}
