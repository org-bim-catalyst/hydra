using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AiCapabilityAssignment"/> — mirrors AIProviderConfiguration's conventions.</summary>
public sealed class AiCapabilityAssignmentConfiguration : IEntityTypeConfiguration<AiCapabilityAssignment>
{
    public void Configure(EntityTypeBuilder<AiCapabilityAssignment> builder)
    {
        builder.ToTable("AiCapabilityAssignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // Stored as a string, never an ordinal — same reasoning as HealthStatus and
        // AIModelStatus: an ordinal silently remaps if the enum is ever reordered, and this
        // column decides which provider serves a capability.
        builder.Property(a => a.Capability).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.ProviderId).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        // One provider per capability. Filtered so a soft-deleted row never blocks a new
        // assignment for the same capability.
        builder.HasIndex(a => a.Capability).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");

        // Restrict, not Cascade: removing a provider that a capability still points at is a
        // decision an administrator must make explicitly, not something that silently leaves a
        // capability unassigned and back on the alphabetical fallback.
        builder.HasOne<AIProvider>()
            .WithMany()
            .HasForeignKey(a => a.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(a => a.DeletedAtUtc == null);
    }
}
