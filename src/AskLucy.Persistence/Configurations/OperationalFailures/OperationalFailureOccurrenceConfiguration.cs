using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.OperationalFailures;

/// <summary>
/// EF Core mapping for <see cref="OperationalFailureOccurrence"/> — append-only; retention and
/// erasure are its only writers after insert. The ids are references only, with no foreign keys,
/// so a deleted chat or workflow never takes the failure record with it (FR-012).
/// </summary>
public sealed class OperationalFailureOccurrenceConfiguration : IEntityTypeConfiguration<OperationalFailureOccurrence>
{
    public void Configure(EntityTypeBuilder<OperationalFailureOccurrence> builder)
    {
        builder.ToTable("OperationalFailureOccurrences");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Severity).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(o => o.Engine).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(o => o.Operation).HasMaxLength(120).IsRequired();
        builder.Property(o => o.ProviderName).HasMaxLength(100);
        builder.Property(o => o.Model).HasMaxLength(200);
        builder.Property(o => o.Reason).HasMaxLength(500).IsRequired();
        builder.Property(o => o.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(o => o.UserId).HasMaxLength(450);
        builder.Property(o => o.JobId).HasMaxLength(100);
        builder.Property(o => o.SourceIp).HasMaxLength(45);

        builder.Property(o => o.CreatedBy).IsRequired();
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasIndex(o => new { o.IncidentId, o.OccurredAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Occurrences_Incident_OccurredAt");

        builder.HasIndex(o => o.OccurredAtUtc).HasDatabaseName("IX_Occurrences_OccurredAt");

        builder.HasIndex(o => o.UserId).HasFilter("[UserId] IS NOT NULL").HasDatabaseName("IX_Occurrences_UserId");
        builder.HasIndex(o => o.ChatId).HasFilter("[ChatId] IS NOT NULL").HasDatabaseName("IX_Occurrences_ChatId");
        builder.HasIndex(o => o.WorkflowExecutionId).HasFilter("[WorkflowExecutionId] IS NOT NULL").HasDatabaseName("IX_Occurrences_WorkflowExecutionId");
        builder.HasIndex(o => o.DocumentId).HasFilter("[DocumentId] IS NOT NULL").HasDatabaseName("IX_Occurrences_DocumentId");
    }
}
