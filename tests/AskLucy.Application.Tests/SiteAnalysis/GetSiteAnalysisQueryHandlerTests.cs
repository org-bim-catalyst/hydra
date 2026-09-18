using AskLucy.Application.Abstractions;
using AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.SiteAnalysis;

/// <summary>
/// contracts/site-analysis-api.md FR-019 — ownership is enforced here, not by the controller.
/// specs/057-site-analysis-agent tasks.md T049 (the 200-for-owner/404-for-another-user halves;
/// see <c>AskLucy.Web.Tests.SiteAnalysis.SiteAnalysesControllerTests</c> for the 401 half, which
/// is the only case this environment's Web.Tests can exercise without a second real authenticated
/// user — mirrors <c>WorkflowsControllerTests</c>'s identical split exactly).
/// </summary>
public sealed class GetSiteAnalysisQueryHandlerTests
{
    private const string OwnerId = "owner-1";
    private const string OtherUserId = "other-user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private readonly ISiteAnalysisRepository _repository = Substitute.For<ISiteAnalysisRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private GetSiteAnalysisQueryHandler BuildHandler() => new(_repository, _currentUser);

    private static Domain.SiteAnalysis.SiteAnalysis CreateOwnedAnalysis()
    {
        var analysis = Domain.SiteAnalysis.SiteAnalysis.Create(OwnerId, UserChatId, "Al Barsha South", 25.09, 55.20, null, 1, OwnerId);
        analysis.AddResult(SiteAnalysisType.SchematicImage, """{"version":1,"blocks":[]}""", "openai:x", SiteAnalysisConfidenceLevel.Medium, Guid.NewGuid(), OwnerId);
        return analysis;
    }

    [Fact]
    public async Task Handle_ShouldReturnTheAnalysis_ForItsOwner()
    {
        _currentUser.UserId.Returns(OwnerId);
        var analysis = CreateOwnedAnalysis();
        _repository.GetByIdForUserAsync(analysis.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(analysis);

        var dto = await BuildHandler().Handle(new GetSiteAnalysisQuery(analysis.Id), CancellationToken.None);

        dto.Id.Should().Be(analysis.Id);
        dto.SiteName.Should().Be("Al Barsha South");
        dto.Results.Should().ContainSingle(r => r.Status == "Completed");
    }

    [Fact]
    public async Task Handle_ShouldNotExposeFailureReason_InTheResponse()
    {
        _currentUser.UserId.Returns(OwnerId);
        var analysis = Domain.SiteAnalysis.SiteAnalysis.Create(OwnerId, UserChatId, "Al Barsha South", 25.09, 55.20, null, 1, OwnerId);
        analysis.AddFailedResult(SiteAnalysisType.SchematicImage, "provider unavailable — this must never reach the client", wasRejected: false, OwnerId);
        _repository.GetByIdForUserAsync(analysis.Id, OwnerId, Arg.Any<CancellationToken>()).Returns(analysis);

        var dto = await BuildHandler().Handle(new GetSiteAnalysisQuery(analysis.Id), CancellationToken.None);

        // FailureReason is deliberately absent from SiteAnalysisResultDetailDto's own shape — this
        // assertion is really "the DTO has no such property to leak," proven by the fact that
        // dto.Results compiles at all without one.
        dto.Results.Should().ContainSingle(r => r.Status == "Failed" && r.Content == null);
    }

    [Fact]
    public async Task Handle_ShouldThrowKeyNotFound_WhenTheAnalysisBelongsToAnotherUser()
    {
        // FR-019 — reported as a plain 404 (KeyNotFoundException maps to 404 at the Problem
        // Details boundary), never a 403 that would disclose the analysis exists at all.
        _currentUser.UserId.Returns(OtherUserId);
        var analysisId = Guid.NewGuid();
        _repository.GetByIdForUserAsync(analysisId, OtherUserId, Arg.Any<CancellationToken>()).Returns((Domain.SiteAnalysis.SiteAnalysis?)null);

        var act = () => BuildHandler().Handle(new GetSiteAnalysisQuery(analysisId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowUnauthorized_WhenThereIsNoCurrentUser()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => BuildHandler().Handle(new GetSiteAnalysisQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
