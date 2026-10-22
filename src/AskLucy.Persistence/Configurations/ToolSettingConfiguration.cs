using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="ToolSetting"/>, following <see cref="AiCapabilitySettingConfiguration"/>'s conventions.</summary>
public sealed class ToolSettingConfiguration : IEntityTypeConfiguration<ToolSetting>
{
    public void Configure(EntityTypeBuilder<ToolSetting> builder)
    {
        builder.ToTable("ToolSettings");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ToolName).HasMaxLength(ToolSetting.MaxToolNameLength).IsRequired();
        builder.Property(s => s.Key).HasMaxLength(ToolSetting.MaxKeyLength).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(ToolSetting.MaxValueLength).IsRequired();

        builder.Property(s => s.CreatedBy).IsRequired();
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => new { s.ToolName, s.Key }).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");

        builder.HasQueryFilter(s => s.DeletedAtUtc == null);
    }
}
