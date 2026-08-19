using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowNode"/> — child of <see cref="WorkflowVersion"/>, immutable once created.</summary>
public sealed class WorkflowNodeConfiguration : IEntityTypeConfiguration<WorkflowNode>
{
    public void Configure(EntityTypeBuilder<WorkflowNode> builder)
    {
        builder.ToTable("WorkflowNodes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.NodeKey).IsRequired().HasMaxLength(200);
        builder.Property(n => n.NodeType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Name).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Description).HasMaxLength(1000);
        builder.Property(n => n.InputSchemaJson).IsRequired();
        builder.Property(n => n.OutputSchemaJson).IsRequired();
        builder.Property(n => n.ConfigurationJson).IsRequired();
        builder.Property(n => n.RequiredPermissionsJson).IsRequired();
        builder.Property(n => n.TimeoutSeconds);
        builder.Property(n => n.RetryPolicyJson);
        builder.Property(n => n.ApprovalPolicy).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.IdempotencyKeyExpression).HasMaxLength(1000);
        builder.Property(n => n.CompensatingNodeId);
        builder.Property(n => n.CanvasX).IsRequired();
        builder.Property(n => n.CanvasY).IsRequired();

        builder.Property(n => n.CreatedBy);
        builder.Property(n => n.RowVersion).IsRowVersion();

        builder.HasIndex(n => new { n.WorkflowVersionId, n.NodeKey }).IsUnique();
        builder.HasIndex(n => n.NodeType);

        builder.HasOne<WorkflowNode>()
            .WithMany()
            .HasForeignKey(n => n.CompensatingNodeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
