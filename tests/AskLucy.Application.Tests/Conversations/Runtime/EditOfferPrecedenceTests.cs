using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/079 contracts/edit-and-reset-capabilities.md, offer precedence rows 2-4: when the edit
/// offer is due. (Row 1, the reset offer, arrives with User Story 3.) The membership offer itself
/// is decided ahead of this by <see cref="SiteBoundaryMembershipOffer"/>, so what is asserted here
/// is what happens when it does not apply.
/// </summary>
public sealed class EditOfferPrecedenceTests
{
    private static readonly IReadOnlyList<GeoPoint> Ring =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1560, 55.2210)];

    private static TurnOutcome Invoked(params string[] keys) => new(keys, false, [], false);

    private static ConfirmedSiteBoundaryData Confirmed(
        BoundaryConfidenceLevel level = BoundaryConfidenceLevel.Medium, SiteBoundarySource source = SiteBoundarySource.OsmBoundary) =>
        new("Muscat Grand Mall", 25.1555, 55.2215, Ring, 48_860, 0.7, level, source, "x", []);

    private static TurnContext ContextWith(ActiveSiteBoundary? boundary) =>
        new("user-1", Guid.NewGuid(), null, boundary, [], false, true, 0, new HashSet<AgentToolPermission>(), null);

    private static ActiveSiteBoundary Active(SiteBoundarySource source, BoundaryConfidenceLevel level) =>
        new("Muscat Grand Mall", 25.1555, 55.2215, Ring, 48_860, 0.7, level, source, "x");

    [Fact]
    public void ADisplayedResolutionWithNoBuildingQuestion_GetsTheEditOfferAtTheConfirmedConfidence()
    {
        var level = ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(BoundaryConfidenceLevel.Low), ContextWith(null));

        level.Should().Be(BoundaryConfidenceLevel.Low);
    }

    [Fact]
    public void AnAnsweredBuildingChoice_GetsTheEditOfferToo()
    {
        var level = ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(SetSiteBoundaryMembersCapability.CapabilityKey), Confirmed(BoundaryConfidenceLevel.High), ContextWith(null));

        level.Should().Be(BoundaryConfidenceLevel.High);
    }

    [Fact]
    public void AKeptChoice_HasNoBoundaryPayload_SoItUsesTheOutlineInForce()
    {
        var context = ContextWith(Active(SiteBoundarySource.OsmBoundary, BoundaryConfidenceLevel.Medium));

        var level = ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(SetSiteBoundaryMembersCapability.CapabilityKey), confirmedBoundary: null, context);

        level.Should().Be(BoundaryConfidenceLevel.Medium);
    }

    [Fact]
    public void AnOutlineThatIsAlreadyHandEdited_IsNeverOfferedAnEditAgain()
    {
        var context = ContextWith(Active(SiteBoundarySource.UserCorrected, BoundaryConfidenceLevel.High));

        ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(SetSiteBoundaryMembersCapability.CapabilityKey), confirmedBoundary: null, context).Should().BeNull();
        ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(source: SiteBoundarySource.UserCorrected), ContextWith(null))
            .Should().BeNull();
    }

    [Fact]
    public void AFreshResolution_IsJudgedByItsOwnPayload_NotByTheHandEditedOutlineTheTurnStartedWith()
    {
        var staleHandEdited = ContextWith(Active(SiteBoundarySource.UserCorrected, BoundaryConfidenceLevel.High));

        var level = ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(ResolveSiteBoundaryCapability.CapabilityKey), Confirmed(BoundaryConfidenceLevel.Medium), staleHandEdited);

        level.Should().Be(BoundaryConfidenceLevel.Medium);
    }

    [Fact]
    public void AnyOtherTurn_GetsNoEditOffer()
    {
        var context = ContextWith(Active(SiteBoundarySource.OsmBoundary, BoundaryConfidenceLevel.Medium));

        ConversationTurnOrchestrator.EditOfferLevel(TurnOutcome.None, null, context).Should().BeNull();
        ConversationTurnOrchestrator.EditOfferLevel(Invoked("open_solar_analysis"), null, context).Should().BeNull();
    }

    [Fact]
    public void ATurnWithNoOutlineAtAll_GetsNoEditOffer() =>
        ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(SetSiteBoundaryMembersCapability.CapabilityKey), confirmedBoundary: null, ContextWith(null)).Should().BeNull();
}
