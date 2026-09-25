using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.OperationalFailures;

/// <summary>
/// EF Core mapping for <see cref="OperationalFailureIncident"/> (specs/074 data-model). No
/// soft-delete query filter: this is operational telemetry that retention hard-deletes (FR-029).
/// </summary>
public sealed class OperationalFailureIncidentConfiguration : IEntityTypeConfiguration<OperationalFailureIncident>
{
    public void Configure(EntityTypeBuilder<OperationalFailureIncident> builder)
    {
        builder.ToTable("OperationalFailureIncidents");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.GroupingKey).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(i => i.RootCauseKey).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(i => i.Engine).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(i => i.Operation).HasMaxLength(120).IsRequired();
        builder.Property(i => i.ProviderName).HasMaxLength(100);
        builder.Property(i => i.Model).HasMaxLength(200);
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(i => i.SubjectType).HasMaxLength(40);
        builder.Property(i => i.SubjectLabel).HasMaxLength(200);
        builder.Property(i => i.HighestSeverity).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(i => i.LatestReason).HasMaxLength(500).IsRequired();
        builder.Property(i => i.LatestCorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(i => i.TriageState).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(i => i.AcknowledgedByUserId).HasMaxLength(450);
        builder.Property(i => i.ResolvedByUserId).HasMaxLength(450);
        builder.Property(i => i.ResolutionNote).HasMaxLength(OperationalFailureIncident.MaxResolutionNoteLength);

        builder.Property(i => i.CreatedBy).IsRequired();
        builder.Property(i => i.RowVersion).IsRowVersion();

        // Exactly one unresolved incident per key (research D3); resolved ones fall outside, so a
        // recurrence opens a new row.
        builder.HasIndex(i => i.GroupingKey)
            .IsUnique()
            .HasFilter("[TriageState] <> N'Resolved'")
            .HasDatabaseName("UX_Incidents_GroupingKey_Unresolved");

        builder.HasIndex(i => new { i.TriageState, i.LastSeenUtc })
            .IsDescending(false, true)
            .IncludeProperties(i => new { i.HighestSeverity, i.Engine, i.ProviderName, i.Kind })
            .HasDatabaseName("IX_Incidents_State_LastSeen");

        builder.HasIndex(i => i.LastSeenUtc)
            .IsDescending()
            .HasDatabaseName("IX_Incidents_LastSeen");

        builder.HasIndex(i => new { i.RootCauseKey, i.TriageState })
            .HasDatabaseName("IX_Incidents_RootCause");

        builder.HasIndex(i => new { i.HighestSeverity, i.TriageState, i.RootCauseKey })
            .HasFilter("[TriageState] = N'Open'")
            .HasDatabaseName("IX_Incidents_Badge");

        builder.HasMany<OperationalFailureOccurrence>()
            .WithOne()
            .HasForeignKey(o => o.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany<IncidentParticipant>()
            .WithOne()
            .HasForeignKey(p => p.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
