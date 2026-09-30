using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/079 — aggregate-oriented repository for <see cref="SiteBoundaryCorrection"/> (constitution
/// &#167;3), with no <c>IQueryable</c> escape hatch. Every read is scoped to the owning user, so
/// another user's correction is indistinguishable from one that does not exist (FR-026).
/// </summary>
public interface ISiteBoundaryCorrectionRepository
{
    void Add(SiteBoundaryCorrection correction);

    /// <summary>A live (not deleted) correction owned by <paramref name="userId"/>, or null.</summary>
    Task<SiteBoundaryCorrection?> GetByIdAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>The user's live corrections for a site name; the caller picks among them by place.</summary>
    Task<IReadOnlyList<SiteBoundaryCorrection>> FindCandidatesAsync(
        string userId, string normalizedSiteName, CancellationToken cancellationToken = default);
}
