using System.Text.Json;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations;

/// <summary>
/// specs/079 — EF Core mapping for <see cref="SiteBoundaryCorrection"/>. The ring, snapshot and
/// member lists are JSON columns, converted here so Domain stays free of any JSON reference
/// (constitution &#167;3), the same way <see cref="UserChatConfiguration"/> stores the active boundary.
/// </summary>
public sealed class SiteBoundaryCorrectionConfiguration : IEntityTypeConfiguration<SiteBoundaryCorrection>
{
    /// <summary>Enums as names, so reordering an enum never remaps stored rows.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public void Configure(EntityTypeBuilder<SiteBoundaryCorrection> builder)
    {
        builder.ToTable("SiteBoundaryCorrections");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        builder.Property(c => c.SiteName).IsRequired().HasMaxLength(500);
        builder.Property(c => c.NormalizedSiteName).IsRequired().HasMaxLength(500);
        builder.Property(c => c.FoundCentroidLatitude).IsRequired();
        builder.Property(c => c.FoundCentroidLongitude).IsRequired();
        builder.Property(c => c.AreaSquareMeters).IsRequired();
        builder.Property(c => c.Revision).IsRequired();

        builder.Property(c => c.EditedRings)
            .HasColumnName("EditedRingsJson")
            .IsRequired()
            .HasConversion(
                rings => JsonSerializer.Serialize(rings, JsonOptions),
                json => JsonSerializer.Deserialize<IReadOnlyList<IReadOnlyList<GeoPoint>>>(json, JsonOptions) ?? new List<IReadOnlyList<GeoPoint>>())
            .Metadata.SetValueComparer(JsonValueComparer<IReadOnlyList<IReadOnlyList<GeoPoint>>>());

        // specs/081 — each ring's voids. Defaults to "[]" so rows saved before voids existed read as having none.
        builder.Property(c => c.EditedVoids)
            .HasColumnName("EditedVoidsJson")
            .IsRequired()
            .HasDefaultValueSql("N'[]'")
            .HasConversion(
                voids => JsonSerializer.Serialize(voids, JsonOptions),
                json => JsonSerializer.Deserialize<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>(json, JsonOptions)
                    ?? new List<IReadOnlyList<IReadOnlyList<GeoPoint>>>())
            .Metadata.SetValueComparer(JsonValueComparer<IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>>());

        builder.Property(c => c.FoundSnapshot)
            .HasColumnName("FoundSnapshotJson")
            .IsRequired()
            .HasConversion(
                snapshot => JsonSerializer.Serialize(snapshot, JsonOptions),
                json => JsonSerializer.Deserialize<FoundSiteBoundarySnapshot>(json, JsonOptions)!)
            .Metadata.SetValueComparer(JsonValueComparer<FoundSiteBoundarySnapshot>());

        builder.Property(c => c.Members)
            .HasColumnName("MembersJson")
            .IsRequired()
            .HasConversion(
                members => JsonSerializer.Serialize(members, JsonOptions),
                json => JsonSerializer.Deserialize<IReadOnlyList<SiteBoundaryMember>>(json, JsonOptions) ?? new List<SiteBoundaryMember>())
            .Metadata.SetValueComparer(JsonValueComparer<IReadOnlyList<SiteBoundaryMember>>());

        builder.Property(c => c.CreatedBy).IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasQueryFilter(c => c.DeletedAtUtc == null);

        // The lookup path: a user's live corrections for a site name (FR-023).
        builder.HasIndex(c => new { c.UserId, c.NormalizedSiteName })
            .HasDatabaseName("IX_SiteBoundaryCorrections_UserId_NormalizedSiteName")
            .HasFilter("[DeletedAtUtc] IS NULL");
    }

    private static ValueComparer<T> JsonValueComparer<T>() => new(
        (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
        a => JsonSerializer.Serialize(a, JsonOptions).GetHashCode(StringComparison.Ordinal),
        a => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(a, JsonOptions), JsonOptions)!);
}
