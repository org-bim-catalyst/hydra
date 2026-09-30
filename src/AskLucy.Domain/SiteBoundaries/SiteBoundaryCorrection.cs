using System.Globalization;
using System.Text;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/079 — a user's hand-edited outline for one site, kept per user and reused in every chat of
/// theirs that shows that site. The single source of truth for the edit: a chat's own stored
/// boundary keeps the outline Lucy found and only links here (research D5).
/// </summary>
public sealed class SiteBoundaryCorrection : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public string SiteName { get; private set; } = string.Empty;

    public string NormalizedSiteName { get; private set; } = string.Empty;

    /// <summary>Place identity: the found outline's centre.</summary>
    public double FoundCentroidLatitude { get; private set; }

    public double FoundCentroidLongitude { get; private set; }

    /// <summary>The first ring is the one holding the site; the rest are separate building rings.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> EditedRings { get; private set; } = [];

    /// <summary>Union area of <see cref="EditedRings"/>, overlap counted once; computed by the server.</summary>
    public double AreaSquareMeters { get; private set; }

    public FoundSiteBoundarySnapshot FoundSnapshot { get; private set; } = null!;

    /// <summary>The building choice <see cref="EditedRings"/> covers.</summary>
    public IReadOnlyList<SiteBoundaryMember> Members { get; private set; } = [];

    /// <summary>New on every save or membership change; the optimistic-concurrency token clients send back.</summary>
    public Guid Revision { get; private set; }

    private SiteBoundaryCorrection()
    {
        // Required by EF Core materialization.
    }

    public static SiteBoundaryCorrection Create(
        string userId, string siteName, double foundCentroidLatitude, double foundCentroidLongitude,
        FoundSiteBoundarySnapshot foundSnapshot, IReadOnlyList<IReadOnlyList<GeoPoint>> editedRings,
        double areaSquareMeters, IReadOnlyList<SiteBoundaryMember> members, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A site correction must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(siteName))
        {
            throw new DomainRuleViolationException("A site correction must name a site.");
        }

        ArgumentNullException.ThrowIfNull(foundSnapshot);
        EnsureShape(editedRings, areaSquareMeters);

        return new SiteBoundaryCorrection
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SiteName = siteName.Trim(),
            NormalizedSiteName = NormalizeSiteName(siteName),
            FoundCentroidLatitude = foundCentroidLatitude,
            FoundCentroidLongitude = foundCentroidLongitude,
            FoundSnapshot = foundSnapshot,
            EditedRings = editedRings,
            AreaSquareMeters = areaSquareMeters,
            Members = members,
            Revision = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void ReplaceRings(IReadOnlyList<IReadOnlyList<GeoPoint>> editedRings, double areaSquareMeters, string actor)
    {
        EnsureShape(editedRings, areaSquareMeters);
        EditedRings = editedRings;
        AreaSquareMeters = areaSquareMeters;
        Touch(actor);
    }

    /// <summary>
    /// A building choice changed on a hand-edited outline (research D9). Also re-bases
    /// <see cref="FoundSnapshot"/> to the recomposed found outline, so a reset from any chat
    /// restores the found outline with the building choice in force.
    /// </summary>
    public void ApplyMembership(
        IReadOnlyList<IReadOnlyList<GeoPoint>> editedRings, double areaSquareMeters,
        IReadOnlyList<SiteBoundaryMember> members, FoundSiteBoundarySnapshot foundSnapshot, string actor)
    {
        EnsureShape(editedRings, areaSquareMeters);
        ArgumentNullException.ThrowIfNull(foundSnapshot);
        EditedRings = editedRings;
        AreaSquareMeters = areaSquareMeters;
        Members = members;
        FoundSnapshot = foundSnapshot;
        Touch(actor);
    }

    /// <summary>Soft delete, used by reset. Every chat still linked to this row then shows its found outline.</summary>
    public void Delete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
        Revision = Guid.NewGuid();
    }

    /// <summary>Lower-case invariant, diacritics folded, whitespace collapsed (research D5).</summary>
    public static string NormalizeSiteName(string siteName)
    {
        ArgumentNullException.ThrowIfNull(siteName);

        var decomposed = siteName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            previousWasSpace = false;
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().TrimEnd();
    }

    private void Touch(string actor)
    {
        Revision = Guid.NewGuid();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    private static void EnsureShape(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, double areaSquareMeters)
    {
        if (rings is null || rings.Count == 0)
        {
            throw new DomainRuleViolationException("A site correction needs at least one ring.");
        }

        if (rings.Any(ring => ring is null || ring.Count < 3))
        {
            throw new DomainRuleViolationException("Every ring of a site correction needs at least 3 corners.");
        }

        if (!(areaSquareMeters > 0))
        {
            throw new DomainRuleViolationException("A site correction must have an area greater than zero.");
        }
    }
}
