using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Agents;

/// <summary>EF Core mapping for <see cref="AgentExecution"/> — aggregate root for the runtime bounded context; never hard-deleted (FR-050 audit trail), so no query filter on <c>DeletedAtUtc</c> is needed (it is never set for this entity).</summary>
public sealed class AgentExecutionConfiguration : IEntityTypeConfiguration<AgentExecution>
{
    public void Configure(EntityTypeBuilder<AgentExecution> builder)
    {
        builder.ToTable("AgentExecutions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.RunByUserId).IsRequired();
        builder.Property(e => e.Objective).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.IsTestExecution).IsRequired();
        builder.Property(e => e.ConversationIntegrationMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.PlanJson);
        builder.Property(e => e.FinalOutputJson);
        builder.Property(e => e.FinalOutputText);
        builder.Property(e => e.TerminationReason).HasMaxLength(2000);

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.RunByUserId);
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.RunByUserId, e.Status });

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AgentVersion>()
            .WithMany()
            .HasForeignKey(e => e.AgentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.RunByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UserChat>()
            .WithMany()
            .HasForeignKey(e => e.UserChatId)
            .OnDelete(DeleteBehavior.SetNull);

        // Steps/Events/Approvals/Errors are children of this aggregate — reachable only via
        // these navigations (backed by private fields), mirrors Prompt.Versions. Cascade, since
        // child rows have no independent meaning outside their execution (data-model.md).
        builder.HasMany(e => e.Steps)
            .WithOne()
            .HasForeignKey(s => s.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Events)
            .WithOne()
            .HasForeignKey(ev => ev.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Approvals)
            .WithOne()
            .HasForeignKey(a => a.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Errors)
            .WithOne()
            .HasForeignKey(er => er.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Errors).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(e => e.Usage)
            .WithOne()
            .HasForeignKey<AgentExecutionUsage>(u => u.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Cost)
            .WithOne()
            .HasForeignKey<AgentExecutionCost>(c => c.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
