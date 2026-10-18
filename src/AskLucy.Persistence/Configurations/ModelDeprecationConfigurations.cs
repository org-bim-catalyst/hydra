using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>specs/069 data-model: EF Core mappings for the model deprecation aggregate and its children.</summary>
public sealed class ModelDeprecationBatchConfiguration : IEntityTypeConfiguration<ModelDeprecationBatch>
{
    public void Configure(EntityTypeBuilder<ModelDeprecationBatch> builder)
    {
        builder.ToTable("ModelDeprecationBatches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(b => b.UserImpactStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.ConfirmedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(b => b.CreatedBy).IsRequired();
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasIndex(b => new { b.UserImpactStatus, b.ConfirmedAtUtc });

        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(b => b.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Deprecations)
            .WithOne()
            .HasForeignKey(d => d.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Deprecations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasQueryFilter(b => b.DeletedAtUtc == null);
    }
}

public sealed class ModelDeprecationConfiguration : IEntityTypeConfiguration<ModelDeprecation>
{
    public void Configure(EntityTypeBuilder<ModelDeprecation> builder)
    {
        builder.ToTable("ModelDeprecations");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Reason).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(d => d.ReplacementSource).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.ValidationStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(d => d.ValidatedByUserId).HasMaxLength(450);
        builder.Property(d => d.CreatedBy).IsRequired();
        builder.Property(d => d.RowVersion).IsRowVersion();

        // A model is deprecated once, so the record is unique per model.
        builder.HasIndex(d => d.ModelId).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");
        builder.HasIndex(d => d.ReplacementModelId);

        // Restrict: deleting a model that carries a deprecation record must be an explicit decision.
        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(d => d.ModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(d => d.ReplacementModelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(d => d.DefaultChanges)
            .WithOne()
            .HasForeignKey(c => c.DeprecationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(d => d.DefaultChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasQueryFilter(d => d.DeletedAtUtc == null);
    }
}

public sealed class PlatformDefaultChangeConfiguration : IEntityTypeConfiguration<PlatformDefaultChange>
{
    public void Configure(EntityTypeBuilder<PlatformDefaultChange> builder)
    {
        builder.ToTable("PlatformDefaultChanges");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.TargetKind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.Capability).HasConversion<string>().HasMaxLength(40);
        builder.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.FlagReason).HasConversion<string>().HasMaxLength(40);
        builder.Property(c => c.ResolvedByUserId).HasMaxLength(450);
        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        // Open attention flags, read by the AI Providers page.
        builder.HasIndex(c => new { c.Outcome, c.ResolvedAtUtc });

        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(c => c.PreviousModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AIModel>()
            .WithMany()
            .HasForeignKey(c => c.NewModelId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);
    }
}

public sealed class ItemSwitchConfiguration : IEntityTypeConfiguration<ItemSwitch>
{
    public void Configure(EntityTypeBuilder<ItemSwitch> builder)
    {
        builder.ToTable("ItemSwitches");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.OwnerUserId).HasMaxLength(450).IsRequired();
        builder.Property(s => s.ItemKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.NodeId).HasMaxLength(100);
        builder.Property(s => s.ItemLabel).HasMaxLength(200).IsRequired();
        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.BatchId, s.OwnerUserId });

        // Cascade from the batch only. A second cascade path from the deprecation would be
        // rejected by SQL Server (error 1785), so this edge is Restrict.
        builder.HasOne<ModelDeprecation>()
            .WithMany()
            .HasForeignKey(s => s.DeprecationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ModelDeprecationBatch>()
            .WithMany()
            .HasForeignKey(s => s.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(s => s.DeletedAtUtc == null);
    }
}
