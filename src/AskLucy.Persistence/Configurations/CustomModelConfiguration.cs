using AskLucy.Domain.CustomModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="CustomModel"/> (specs/072 data-model.md).</summary>
public sealed class CustomModelConfiguration : IEntityTypeConfiguration<CustomModel>
{
    public const string NameIndex = "UX_CustomModels_Name";
    public const string RepositoryAvailableIndex = "UX_CustomModels_Repository_Available";
    public const string DestinationInProgressIndex = "UX_CustomModels_Destination_InProgress";

    // Explicit rather than inherited from the database default, so every uniqueness check and
    // lookup on these columns ignores case whatever collation the host database was created with.
    private const string CaseInsensitiveCollation = "SQL_Latin1_General_CP1_CI_AS";

    public void Configure(EntityTypeBuilder<CustomModel> builder)
    {
        builder.ToTable("CustomModels");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Name).IsRequired().HasMaxLength(CustomModel.MaxNameLength).UseCollation(CaseInsensitiveCollation);
        builder.Property(m => m.RepositoryId).IsRequired().HasMaxLength(CustomModel.MaxRepositoryIdLength).UseCollation(CaseInsensitiveCollation);
        builder.Property(m => m.Revision).IsRequired().HasMaxLength(CustomModel.MaxRevisionLength);
        builder.Property(m => m.ResolvedCommitSha).HasMaxLength(40).IsFixedLength();
        builder.Property(m => m.SourceUrl).IsRequired().HasMaxLength(CustomModel.MaxSourceUrlLength);
        builder.Property(m => m.Destination).IsRequired().HasMaxLength(DeploymentDestination.MaxLength).UseCollation(CaseInsensitiveCollation);

        builder.Property(m => m.DeploymentState).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Availability).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.FailureKind).HasConversion<string>().HasMaxLength(40);
        builder.Property(m => m.FailureReason).HasMaxLength(CustomModel.MaxFailureReasonLength);

        builder.Property(m => m.CurrentFilePath).HasMaxLength(CustomModel.MaxCurrentFilePathLength);
        builder.Property(m => m.SubmittedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(m => m.CancelledByUserId).HasMaxLength(450);
        builder.Property(m => m.BackgroundJobId).HasMaxLength(CustomModel.MaxBackgroundJobIdLength);

        builder.Property(m => m.CreatedBy).IsRequired();
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasQueryFilter(m => m.DeletedAtUtc == null);

        // A removed model frees its name for reuse (FR-032).
        builder.HasIndex(m => m.Name)
            .IsUnique()
            .HasFilter("[DeletedAtUtc] IS NULL")
            .HasDatabaseName(NameIndex);

        // At most one Available model per repository (FR-034) — the last line of defence behind
        // the handler's own check, which two concurrent admins could both pass.
        builder.HasIndex(m => m.RepositoryId)
            .IsUnique()
            .HasFilter("[Availability] = 'Available' AND [DeletedAtUtc] IS NULL")
            .HasDatabaseName(RepositoryAvailableIndex);

        // Exact-match backstop for FR-015. Overlap on a segment boundary (Models/a vs Models/a/b)
        // can't be expressed as an index, so the handler checks that; this catches the race on
        // the identical-destination case.
        builder.HasIndex(m => m.Destination)
            .IsUnique()
            .HasFilter("[IsInProgress] = 1")
            .HasDatabaseName(DestinationInProgressIndex);

        builder.HasIndex(m => new { m.RepositoryId, m.DeploymentState });
        builder.HasIndex(m => m.CreatedAtUtc);

        // No navigation on purpose: the job inserts overwrite rows one at a time through the
        // repository, so the aggregate never carries (or re-saves) the collection.
        builder.HasMany<CustomModelOverwrittenFile>()
            .WithOne()
            .HasForeignKey(f => f.CustomModelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
