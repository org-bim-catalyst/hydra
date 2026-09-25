using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.OperationalFailures;

/// <summary>EF Core mapping for <see cref="IncidentParticipant"/> — the distinct users and sources of an incident (research D10).</summary>
public sealed class IncidentParticipantConfiguration : IEntityTypeConfiguration<IncidentParticipant>
{
    public void Configure(EntityTypeBuilder<IncidentParticipant> builder)
    {
        builder.ToTable("OperationalFailureIncidentParticipants");

        builder.HasKey(p => new { p.IncidentId, p.ParticipantType, p.ParticipantKey });

        builder.Property(p => p.ParticipantType).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(p => p.ParticipantKey).HasMaxLength(450).IsRequired();

        builder.HasIndex(p => new { p.ParticipantKey, p.ParticipantType })
            .IncludeProperties(p => p.IncidentId)
            .HasDatabaseName("IX_Participants_Key");
    }
}
