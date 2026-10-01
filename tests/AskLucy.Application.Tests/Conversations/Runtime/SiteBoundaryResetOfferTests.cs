using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Conversations;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>specs/079 (US5, precedence row 1) - the offer made when Lucy reused the user's own hand-edited outline.</summary>
public sealed class SiteBoundaryResetOfferTests
{
    private static readonly IReadOnlyList<GeoPoint> Ring =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1560, 55.2210)];

    private static SuggestedAction Analysis(string key, string label) =>
        new(SuggestedActionKind.Capability, key, null, label, "d", "{}");

    private static TurnOutcome Invoked(params string[] keys) => new(keys, false, [], false);

    private static ConfirmedSiteBoundaryData Confirmed(Guid? correctionId) =>
        new("Muscat Grand Mall", 25.1555, 55.2215, Ring, 48_860, 0.7, BoundaryConfidenceLevel.High, SiteBoundarySource.UserCorrected, "x", [])
        {
            CorrectionId = correctionId,
        };

    [Fact]
    public void Build_PutsTheResetRowFirst_ThenTheAnalysisRows_AndKeepMyOutlineLast()
    {
        var offer = SiteBoundaryResetOffer.Build([Analysis("open_solar_analysis", "Show sun & shadows"), SuggestedAction.Decline("Nothing now")]);

        offer.Question.Should().Be("This is your corrected outline.");
        offer.Actions.Select(a => a.Label).Should().Equal("Reset to Lucy's outline", "Show sun & shadows", "Keep my outline");
        offer.Actions[0].Key.Should().Be(ResetSiteBoundaryCapability.CapabilityKey);
        offer.Actions[0].ArgumentsJson.Should().Be("{}");
        offer.Actions[^1].Kind.Should().Be(SuggestedActionKind.Decline);
    }

    [Fact]
    public void ResetOfferDue_WhenAResolutionReusedACorrection() =>
        ConversationTurnOrchestrator.ResetOfferDue(Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(Guid.NewGuid()))
            .Should().BeTrue();

    [Fact]
    public void ResetOfferDue_NotForAFreshlyFoundOutline() =>
        ConversationTurnOrchestrator.ResetOfferDue(Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(null))
            .Should().BeFalse();

    [Fact]
    public void ResetOfferDue_NotWhenNothingWasResolvedThisTurn() =>
        ConversationTurnOrchestrator.ResetOfferDue(Invoked(), Confirmed(Guid.NewGuid())).Should().BeFalse();

    [Fact]
    public void ResetOfferDue_NotAfterAnotherCapability_NorWithoutAConfirmedOutline()
    {
        ConversationTurnOrchestrator.ResetOfferDue(Invoked(SetSiteBoundaryMembersCapability.CapabilityKey), Confirmed(Guid.NewGuid())).Should().BeFalse();
        ConversationTurnOrchestrator.ResetOfferDue(Invoked(ResolveSiteBoundaryCapability.CapabilityKey), confirmedBoundary: null).Should().BeFalse();
    }

    [Fact]
    public void TheEditOffer_IsNeverDueForTheSameOutline()
    {
        var turnContext = new TurnContext("user-1", Guid.NewGuid(), null, null, [], false, true, 0, new HashSet<AgentToolPermission>(), null);

        ConversationTurnOrchestrator.EditOfferLevel(Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(Guid.NewGuid()), turnContext)
            .Should().BeNull();
    }
}
