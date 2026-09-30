using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using NSubstitute;

namespace AskLucy.Application.Tests.SiteBoundaries;

/// <summary>A <see cref="SiteBoundaryCorrectionMatcher"/> over a user who has corrected nothing, for tests unrelated to reuse.</summary>
internal static class TestCorrectionMatcher
{
    public static SiteBoundaryCorrectionMatcher None()
    {
        var repository = Substitute.For<ISiteBoundaryCorrectionRepository>();
        repository.FindCandidatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SiteBoundaryCorrection>>([]));
        return new SiteBoundaryCorrectionMatcher(repository);
    }
}
