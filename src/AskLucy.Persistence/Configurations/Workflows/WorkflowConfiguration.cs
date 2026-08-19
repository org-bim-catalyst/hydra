using AskLucy.Domain.Workflows;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="Workflow"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution &#167;3).</summary>
public sealed class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("Workflows");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.OwnerId).IsRequired();
        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).HasMaxLength(1000);
        builder.Property(w => w.WorkflowType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.PreArchiveStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(w => w.DraftDefinitionJson).IsRequired();
        builder.Property(w => w.PublishedVersionNumber);
        builder.Property(w => w.EventTriggerConfigurationJson);

        builder.Property(w => w.CreatedBy).IsRequired();
        builder.Property(w => w.RowVersion).IsRowVersion();

        builder.HasQueryFilter(w => w.DeletedAtUtc == null);

        // FR-001 — unique per owner, case-insensitive; SQL Server's default collation
        // (SQL_Latin1_General_CP1_CI_AS) is already case-insensitive, so a plain unique index
        // enforces this without a computed column.
        builder.HasIndex(w => new { w.OwnerId, w.Name }).IsUnique();
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.WorkflowType);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Versions are Restrict (data-model.md Delete Behavior), not Cascade — a soft-deleted
        // workflow's published versions are retained for audit, mirroring Agent -> AgentVersion
        // exactly (workflows-api.md's DELETE endpoint contract is the authoritative statement of
        // this; an earlier draft of data-model.md's summary table read "Cascade," corrected here).
        builder.HasMany(w => w.Versions)
            .WithOne()
            .HasForeignKey(v => v.WorkflowId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(w => w.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
