using AskLucy.Domain.Common;
using AskLucy.Domain.SiteAnalysis;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.SiteAnalysis;

public sealed class SiteAnalysisTests
{
    private const string UserId = "user-1";
    private static readonly Guid UserChatId = Guid.NewGuid();

    private static Domain.SiteAnalysis.SiteAnalysis CreateAnalysis(int expectedResultCount = 1) =>
        Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Al Barsha South", 25.09, 55.20, boundaryGeoJson: null, expectedResultCount, UserId);

    [Fact]
    public void Create_ShouldStartRunning_WithNoResults()
    {
        var analysis = CreateAnalysis();

        analysis.Status.Should().Be(SiteAnalysisStatus.Running);
        analysis.Results.Should().BeEmpty();
        analysis.ClosingOutcomeReportedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldThrow_WhenExpectedResultCountIsZero()
    {
        var act = () => Domain.SiteAnalysis.SiteAnalysis.Create(UserId, UserChatId, "Site", 0, 0, null, 0, UserId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void AddResult_ShouldThrow_WhenTheSameSpecialistAlreadySettled()
    {
        var analysis = CreateAnalysis(expectedResultCount: 2);
        analysis.AddResult(SiteAnalysisType.SchematicImage, """{"blocks":[]}""", "openai:x", SiteAnalysisConfidenceLevel.Medium, null, UserId);

        var act = () => analysis.AddResult(SiteAnalysisType.SchematicImage, """{"blocks":[]}""", "openai:x", SiteAnalysisConfidenceLevel.Medium, null, UserId);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void TryClaimClosingOutcome_ShouldReturnTrue_OnlyOnce()
    {
        var analysis = CreateAnalysis();

        var firstClaim = analysis.TryClaimClosingOutcome(UserId);
        var secondClaim = analysis.TryClaimClosingOutcome(UserId);

        firstClaim.Should().BeTrue();
        secondClaim.Should().BeFalse();
        analysis.ClosingOutcomeReportedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Complete_ShouldSetStatusAndCompletedAtUtc()
    {
        var analysis = CreateAnalysis();

        analysis.Complete(UserId);

        analysis.Status.Should().Be(SiteAnalysisStatus.Completed);
        analysis.CompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Fail_ShouldSetStatusAndCompletedAtUtc()
    {
        var analysis = CreateAnalysis();

        analysis.Fail(UserId);

        analysis.Status.Should().Be(SiteAnalysisStatus.Failed);
        analysis.CompletedAtUtc.Should().NotBeNull();
    }
}

public sealed class SiteAnalysisResultTests
{
    private static readonly Guid SiteAnalysisId = Guid.NewGuid();

    [Fact]
    public void Completed_ShouldSetContentAndProvenance()
    {
        var result = SiteAnalysisResult.Completed(
            SiteAnalysisId, SiteAnalysisType.SchematicImage, """{"blocks":[]}""", "openai:gpt-image-1",
            SiteAnalysisConfidenceLevel.Medium, Guid.NewGuid(), "actor");

        result.Status.Should().Be(SiteAnalysisResultStatus.Completed);
        result.ContentJson.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Completed_ShouldThrow_WhenContentIsBlank()
    {
        var act = () => SiteAnalysisResult.Completed(
            SiteAnalysisId, SiteAnalysisType.SchematicImage, "", "openai:gpt-image-1", SiteAnalysisConfidenceLevel.Medium, null, "actor");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Failed_ShouldSetFailureReason_AndNoContent()
    {
        var result = SiteAnalysisResult.Failed(SiteAnalysisId, SiteAnalysisType.SchematicImage, "provider unavailable", "actor");

        result.Status.Should().Be(SiteAnalysisResultStatus.Failed);
        result.FailureReason.Should().Be("provider unavailable");
        result.ContentJson.Should().BeNull();
    }

    [Fact]
    public void Rejected_ShouldSetFailureReason_AndNoContent()
    {
        var result = SiteAnalysisResult.Rejected(SiteAnalysisId, SiteAnalysisType.SchematicImage, "missing data source", "actor");

        result.Status.Should().Be(SiteAnalysisResultStatus.Rejected);
        result.FailureReason.Should().Be("missing data source");
    }

    [Fact]
    public void Failed_ShouldThrow_WhenFailureReasonIsBlank()
    {
        var act = () => SiteAnalysisResult.Failed(SiteAnalysisId, SiteAnalysisType.SchematicImage, "  ", "actor");

        act.Should().Throw<DomainRuleViolationException>();
    }
}
