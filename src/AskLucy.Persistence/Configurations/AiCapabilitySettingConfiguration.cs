using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>specs/077 — EF Core mapping for <see cref="AiCapabilitySetting"/>, following <see cref="AiCapabilityAssignmentConfiguration"/>'s conventions.</summary>
public sealed class AiCapabilitySettingConfiguration : IEntityTypeConfiguration<AiCapabilitySetting>
{
    public void Configure(EntityTypeBuilder<AiCapabilitySetting> builder)
    {
        builder.ToTable("AiCapabilitySettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // A string, never an ordinal, for the same reason as AiCapabilityAssignments.Capability.
        builder.Property(s => s.Capability).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(s => s.Key).HasMaxLength(AiCapabilitySetting.MaxKeyLength).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(AiCapabilitySetting.MaxValueLength).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.Capability, s.Key }).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");

        builder.HasQueryFilter(s => s.DeletedAtUtc == null);
    }
}
