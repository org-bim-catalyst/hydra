using AskLucy.Domain.Agents;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="Agent"/> — persistence mapping lives entirely here, never as attributes on the Domain entity (constitution &#167;3).</summary>
public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("Agents");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.OwnerId).IsRequired();
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.AgentType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.PreArchiveStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.OutputFormat).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.PublishedVersionNumber);

        builder.OwnsOne(a => a.Instructions, instructions =>
        {
            instructions.Property(i => i.SystemInstructions).HasColumnName("SystemInstructions");
            instructions.Property(i => i.Objectives).HasColumnName("Objectives");
            instructions.Property(i => i.Constraints).HasColumnName("Constraints");
            instructions.Property(i => i.BehavioralRules).HasColumnName("BehavioralRules");
            instructions.Property(i => i.OutputRequirements).HasColumnName("OutputRequirements");
            instructions.Property(i => i.ToolUsageRules).HasColumnName("ToolUsageRules");
            instructions.Property(i => i.SafetyRules).HasColumnName("SafetyRules");
        });
        builder.Navigation(a => a.Instructions).IsRequired();

        builder.OwnsOne(a => a.ExecutionPolicy, policy =>
        {
            policy.Property(p => p.MaxSteps).HasColumnName("MaxSteps");
            policy.Property(p => p.MaxExecutionDurationSeconds).HasColumnName("MaxExecutionDurationSeconds");
            policy.Property(p => p.MaxTokens).HasColumnName("MaxTokens");
            policy.Property(p => p.MaxCost).HasColumnName("MaxCost").HasColumnType("decimal(10,4)");
            policy.Property(p => p.MaxToolCalls).HasColumnName("MaxToolCalls");
            policy.Property(p => p.MaxRetries).HasColumnName("MaxRetries");
        });
        builder.Navigation(a => a.ExecutionPolicy).IsRequired();

        builder.Property(a => a.CreatedBy).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasQueryFilter(a => a.DeletedAtUtc == null);

        builder.HasIndex(a => a.OwnerId);
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.AgentType);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tools/KnowledgeBases/Versions are children of this aggregate — reachable only via
        // these navigations (backed by private fields), mirrors Prompt.Tags/Prompt.Versions.
        // Versions use Restrict (data-model.md Delete Behavior) — a soft-deleted agent's
        // published versions outlive it for audit purposes; Tools/KnowledgeBases cascade since
        // they are draft-only configuration with no independent audit value.
        builder.HasMany(a => a.Tools)
            .WithOne()
            .HasForeignKey(t => t.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Tools).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.KnowledgeBases)
            .WithOne()
            .HasForeignKey(k => k.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.KnowledgeBases).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.Versions)
            .WithOne()
            .HasForeignKey(v => v.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(a => a.MemoryPolicy)
            .WithOne()
            .HasForeignKey<AgentMemoryPolicy>(m => m.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
