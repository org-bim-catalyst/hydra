using AskLucy.Domain.OperationalFailures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.OperationalFailures;

/// <summary>
/// EF Core mapping for <see cref="UserContentAccessEvent"/> — the immutable content-access trail
/// (FR-016c). Never purged; <see cref="UserContentAccessEvent.IncidentId"/> is a soft reference so
/// the event outlives the incident's retention.
/// </summary>
public sealed class UserContentAccessEventConfiguration : IEntityTypeConfiguration<UserContentAccessEvent>
{
    public void Configure(EntityTypeBuilder<UserContentAccessEvent> builder)
    {
        builder.ToTable("UserContentAccessEvents");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ViewerUserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.OwnerUserId).HasMaxLength(450);
        builder.Property(e => e.ItemType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();

        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.OwnerUserId).HasFilter("[OwnerUserId] IS NOT NULL");
        builder.HasIndex(e => e.OccurredAtUtc).IsDescending();
    }
}
