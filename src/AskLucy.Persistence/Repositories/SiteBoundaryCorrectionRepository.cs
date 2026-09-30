using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/079 — a save race on <c>RowVersion</c> surfaces as <c>DbUpdateConcurrencyException</c>,
/// which <c>ProblemDetailsMiddleware</c> already maps to 409; nothing is translated here.
/// </summary>
public sealed class SiteBoundaryCorrectionRepository(AskLucyDbContext dbContext) : ISiteBoundaryCorrectionRepository
{
    public void Add(SiteBoundaryCorrection correction) => dbContext.SiteBoundaryCorrections.Add(correction);

    public Task<SiteBoundaryCorrection?> GetByIdAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
        dbContext.SiteBoundaryCorrections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<SiteBoundaryCorrection>> FindCandidatesAsync(
        string userId, string normalizedSiteName, CancellationToken cancellationToken = default) =>
        await dbContext.SiteBoundaryCorrections
            .Where(c => c.UserId == userId && c.NormalizedSiteName == normalizedSiteName)
            .ToListAsync(cancellationToken);
}
