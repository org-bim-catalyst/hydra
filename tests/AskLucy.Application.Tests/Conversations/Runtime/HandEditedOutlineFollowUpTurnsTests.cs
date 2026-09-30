using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/079 SC-006 - ten follow-up turns after a hand edit (area questions, "show me the same
/// site", a building choice, analyses, unrelated talk). Across all of them Lucy is given the edited
/// area as current, is never told the found area is current, is never offered the edit or the
/// building question for that site again, and the edit is never dropped. The model's own wording is
/// not testable here; what she is GIVEN each turn is.
/// </summary>
public sealed class HandEditedOutlineFollowUpTurnsTests
{
    private const string UserId = "user-1";
    private const double FoundArea = 48_860;
    private const double EditedArea = 35_210;

    private static readonly IReadOnlyList<GeoPoint> FoundRing =
        [new(25.1560, 55.2210), new(25.1560, 55.2220), new(25.1550, 55.2220), new(25.1560, 55.2210)];

    private static readonly IReadOnlyList<GeoPoint> EditedRing =
        [new(25.1561, 55.2211), new(25.1561, 55.2221), new(25.1551, 55.2221), new(25.1561, 55.2211)];

    private readonly ISiteBoundaryCorrectionRepository _corrections = Substitute.For<ISiteBoundaryCorrectionRepository>();

    private (UserChat Chat, SiteBoundaryCorrection Correction) LinkedChat()
    {
        var chat = UserChat.Create("Chat", UserId, null, UserId);
        chat.SetActiveBoundary(
            "Muscat Grand Mall", 25.1555, 55.2215, FoundRing, FoundArea, 0.7, BoundaryConfidenceLevel.Medium,
            SiteBoundarySource.OsmBoundary, "OpenStreetMap", UserId);
        var correction = SiteBoundaryCorrection.Create(
            UserId, "Muscat Grand Mall", 25.1555, 55.2215,
            new FoundSiteBoundarySnapshot(FoundRing, [], null, FoundArea, 0.7, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap", []),
            [EditedRing], EditedArea, [], UserId);
        chat.LinkSiteBoundaryCorrection(correction.Id, UserId);
        _corrections.GetByIdAsync(correction.Id, UserId, Arg.Any<CancellationToken>()).Returns(correction);
        return (chat, correction);
    }

    private static TurnContext ContextWith(ActiveSiteBoundary? boundary) =>
        new(UserId, Guid.NewGuid(), null, boundary, [], false, true, 0, new HashSet<AgentToolPermission>(), null);

    private static TurnOutcome Invoked(params string[] keys) => new(keys, false, [], false);

    /// <summary>Each turn's kind and the capabilities it runs, as the orchestrator would report them.</summary>
    public static TheoryData<string, string[]> Turns => new()
    {
        { "How big is the site?", [] },
        { "And its area again?", [] },
        { "How sure are you about this outline?", [] },
        { "Show me the same site", [] },
        { "Include the hotel too", [SetSiteBoundaryMembersCapability.CapabilityKey] },
        { "Run a site analysis", ["request_site_analysis"] },
        { "Where does the sun go here?", ["open_solar_analysis"] },
        { "What is the weather like?", [] },
        { "Thanks", [] },
        { "Show me the site again", [] },
    };

    [Theory]
    [MemberData(nameof(Turns))]
    public async Task EveryTurn_GivesLucyTheEditedOutlineAsCurrent_AndNeverRepeatsTheEditOffer(string message, string[] invoked)
    {
        _ = message;
        var (chat, correction) = LinkedChat();
        var effective = await new EffectiveSiteBoundary(_corrections).ResolveAsync(chat, TestContext.Current.CancellationToken);

        // What Lucy is told about the site this turn.
        var note = ActiveSiteNote.Describe(effective);
        note.Should().Contain("35,210 m").And.Contain("current outline");
        note.Should().NotContain("48,860", "the found area must never read as current");

        // The outline in force is the user's, with their revision.
        effective!.IsHandEdited.Should().BeTrue();
        effective.Revision.Should().Be(correction.Revision);
        effective.Polygon.Should().BeEquivalentTo(EditedRing);

        // The edit offer is never made for a hand-edited outline, whichever capability just ran.
        ConversationTurnOrchestrator.EditOfferLevel(Invoked(invoked), null, ContextWith(effective)).Should().BeNull();

        // And nothing the turn did unlinked or altered the user's correction.
        chat.ActiveBoundary!.CorrectionId.Should().Be(correction.Id);
        correction.IsDeleted.Should().BeFalse();
        correction.EditedRings[0].Should().BeEquivalentTo(EditedRing);
    }

    [Fact]
    public async Task ReusedInAFreshChat_TheBuildingQuestionIsNotAskedForAHandEditedOutline()
    {
        var (_, correction) = LinkedChat();
        var reused = CorrectionOutline.ToConfirmed(correction);
        var withRelatedBuildings = reused with { };
        withRelatedBuildings = new(
            reused.SiteName, reused.CentroidLatitude, reused.CentroidLongitude, reused.Polygon, reused.AreaSquareMeters, reused.Confidence,
            reused.ConfidenceLevel, reused.Source, reused.SourceDetail, [])
        {
            Members =
            [
                new SiteBoundaryMember("osm_way_1", "Phase 2", SiteBoundaryMemberKind.Building, SiteBoundaryMemberRelation.Nearby, 10, EditedRing, true),
            ],
            CorrectionId = correction.Id,
        };

        // The membership offer WOULD build for these members (so it is the guard, not the data, that stops it)...
        SiteBoundaryMembershipOffer.Build(withRelatedBuildings).Should().NotBeNull();
        ConversationTurnOrchestrator.BuildingQuestionDue(Invoked(ResolveSiteBoundaryCapability.CapabilityKey), withRelatedBuildings)
            .Should().BeFalse();
        // ...whereas the same members on an outline Lucy found are asked about...
        ConversationTurnOrchestrator.BuildingQuestionDue(
            Invoked(ResolveSiteBoundaryCapability.CapabilityKey), withRelatedBuildings with { Source = SiteBoundarySource.OsmBoundary })
            .Should().BeTrue();
        // ...and the edit offer is not made either.
        ConversationTurnOrchestrator.EditOfferLevel(
            Invoked(ResolveSiteBoundaryCapability.CapabilityKey), withRelatedBuildings, ContextWith(null)).Should().BeNull();
    }
}
