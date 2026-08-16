using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Workflows;

/// <summary>EF Core mapping for <see cref="WorkflowVariable"/> — child of <see cref="WorkflowVersion"/>, immutable once created.</summary>
public sealed class WorkflowVariableConfiguration : IEntityTypeConfiguration<WorkflowVariable>
{
    public void Configure(EntityTypeBuilder<WorkflowVariable> builder)
    {
        builder.ToTable("WorkflowVariables");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(v => v.ValueType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(v => v.DefaultValueJson);
        builder.Property(v => v.IsRequired).IsRequired();

        builder.Property(v => v.CreatedBy);
        builder.Property(v => v.RowVersion).IsRowVersion();

        builder.HasIndex(v => new { v.WorkflowVersionId, v.Name }).IsUnique();
    }
}
