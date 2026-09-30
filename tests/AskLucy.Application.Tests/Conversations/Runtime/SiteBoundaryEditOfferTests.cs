using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Conversations;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>specs/079 - the "want to adjust its corners?" offer made once an outline is final.</summary>
public sealed class SiteBoundaryEditOfferTests
{
    private static SuggestedAction Analysis(string key, string label) =>
        new(SuggestedActionKind.Capability, key, null, label, "d", "{}");

    [Theory]
    [InlineData(BoundaryConfidenceLevel.High, "I'm confident about this outline — want to adjust its corners?")]
    [InlineData(BoundaryConfidenceLevel.Medium, "I'm fairly sure about this outline — want to adjust its corners?")]
    [InlineData(BoundaryConfidenceLevel.Low, "I'm not sure about this outline — want to adjust its corners?")]
    public void Build_WordsTheQuestionByHowSureTheOutlineIs(BoundaryConfidenceLevel level, string question) =>
        SiteBoundaryEditOffer.Build(level, []).Question.Should().Be(question);

    [Fact]
    public void Build_PutsTheEditRowFirstAndItLooksRightLast()
    {
        var offer = SiteBoundaryEditOffer.Build(BoundaryConfidenceLevel.Medium, []);

        offer.Actions.Should().HaveCount(2);
        offer.Actions[0].Kind.Should().Be(SuggestedActionKind.Capability);
        offer.Actions[0].Key.Should().Be(EditSiteBoundaryCapability.CapabilityKey);
        offer.Actions[0].Label.Should().Be("Edit the outline");
        offer.Actions[0].ArgumentsJson.Should().Be("{}");
        offer.Actions[^1].Kind.Should().Be(SuggestedActionKind.Decline);
        offer.Actions[^1].Label.Should().Be("It looks right");
    }

    [Fact]
    public void Build_PutsTheAnalysisRowsBetweenTheEditRowAndItLooksRight()
    {
        var offer = SiteBoundaryEditOffer.Build(
            BoundaryConfidenceLevel.High,
            [Analysis("open_solar_analysis", "Show sun & shadows"), Analysis("request_site_analysis", "Run a full site analysis")]);

        offer.Actions.Select(a => a.Label).Should().Equal(
            "Edit the outline", "Show sun & shadows", "Run a full site analysis", "It looks right");
    }

    [Fact]
    public void Build_DropsTheGeneratorsOwnDeclineRow_SoThereIsExactlyOne()
    {
        var offer = SiteBoundaryEditOffer.Build(
            BoundaryConfidenceLevel.High, [Analysis("open_solar_analysis", "Show sun & shadows"), SuggestedAction.Decline("Nothing now")]);

        offer.Actions.Count(a => a.Kind == SuggestedActionKind.Decline).Should().Be(1);
        offer.Actions[^1].Label.Should().Be("It looks right");
    }

    [Fact]
    public void Build_ProducesStructurallyValidRows() =>
        SiteBoundaryEditOffer.Build(BoundaryConfidenceLevel.Low, [Analysis("open_solar_analysis", "Show sun & shadows")])
            .Actions.Should().OnlyContain(a => a.IsStructurallyValid());
}
