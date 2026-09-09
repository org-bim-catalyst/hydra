using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/045 T032 — the two rules that belong to a capability rather than to the catalog:
/// <b>precondition</b> (is the state it needs present?) and <b>non-redundancy</b> (would running
/// it produce anything the user does not already have?).
///
/// <para>
/// Each precondition is falsified independently. A capability that gates on two things and is
/// tested only with both missing would pass while checking neither.
/// </para>
/// </summary>
public sealed class CapabilityAvailabilityTests
{
    private static readonly ActiveSiteLocation AlSafaPark =
        new(25.156, 55.2218, "Al Safa Park 2", 0.9);

    private static TurnContext Base() => TurnContext.Empty("user-1", Guid.NewGuid());

    // ---- resolve_location: unconditional, and deliberately never offered. ----

    [Fact]
    public void ResolveLocation_ShouldBeAvailable_EvenWithNoState()
    {
        var capability = new ResolveLocationCapability(Substitute.For<ILocationResolutionService>());

        capability.IsAvailable(Base()).Should().BeTrue(
            "finding a place needs nothing to already be on screen");
    }

    [Fact]
    public void ResolveLocation_ShouldNeverBeOffered_DespiteAlwaysBeingAvailable()
    {
        var capability = new ResolveLocationCapability(Substitute.For<ILocationResolutionService>());

        // The canonical case for splitting the two predicates. Gating the offer on availability
        // alone would have put "Find a place" on the card after every single turn.
        capability.IsOfferable(Base(), TurnOutcome.None).Should().BeFalse();
    }

    // ---- resolve_site_boundary: precondition AND non-redundancy, falsified separately. ----

    [Fact]
    public void ResolveSiteBoundary_ShouldBeUnavailable_WhenNoLocationIsActive()
    {
        var capability = new ResolveSiteBoundaryCapability(Substitute.For<IBoundaryResolutionService>(), Substitute.For<IUserChatRepository>());

        capability.IsAvailable(Base()).Should().BeFalse(
            "there is nothing to outline without a confirmed place");
    }

    [Fact]
    public void ResolveSiteBoundary_ShouldBeAvailable_WhenALocationIsActiveAndUnoutlined()
    {
        var capability = new ResolveSiteBoundaryCapability(Substitute.For<IBoundaryResolutionService>(), Substitute.For<IUserChatRepository>());
        var context = Base() with { ActiveLocation = AlSafaPark };

        capability.IsAvailable(context).Should().BeTrue();
    }

    [Fact]
    public void ResolveSiteBoundary_ShouldBeUnavailable_WhenThatSiteIsAlreadyOutlined()
    {
        var capability = new ResolveSiteBoundaryCapability(Substitute.For<IBoundaryResolutionService>(), Substitute.For<IUserChatRepository>());
        var context = Base() with
        {
            ActiveLocation = AlSafaPark,
            ActiveBoundary = BoundaryFor("Al Safa Park 2"),
        };

        // Non-redundancy, and the reason it matters here more than anywhere else: re-running this
        // costs tens of seconds and an external lookup to produce a result already on screen.
        capability.IsAvailable(context).Should().BeFalse();
    }

    [Fact]
    public void ResolveSiteBoundary_ShouldBeAvailable_WhenTheOutlinedSiteIsADifferentOne()
    {
        var capability = new ResolveSiteBoundaryCapability(Substitute.For<IBoundaryResolutionService>(), Substitute.For<IUserChatRepository>());
        var context = Base() with
        {
            ActiveLocation = AlSafaPark,
            ActiveBoundary = BoundaryFor("Zabeel Park"),
        };

        capability.IsAvailable(context).Should().BeTrue(
            "the active outline describes a different site, so this one is still unoutlined");
    }

    // ---- adjust_viewer_focus: the split-brain guard. ----

    [Fact]
    public void AdjustViewerFocus_ShouldBeUnavailable_WithNothingOnScreen()
    {
        var capability = new AdjustViewerFocusCapability();

        // specs/038 shipped a zoom that could fire with no map context at all; the precondition is
        // that regression written down.
        capability.IsAvailable(Base()).Should().BeFalse();
        capability.IsAvailable(Base() with { ActiveLocation = AlSafaPark }).Should().BeTrue();
    }

    // ---- search_knowledge_base: the one capability here that is genuinely offerable. ----

    [Fact]
    public void SearchKnowledgeBase_ShouldBeUnavailable_WithNoKnowledgeBaseAttached()
    {
        var capability = new SearchKnowledgeBaseCapability(Substitute.For<IRagService>(), Substitute.For<IConversationKnowledgeBaseRepository>());

        capability.IsAvailable(Base()).Should().BeFalse();
    }

    [Fact]
    public void SearchKnowledgeBase_ShouldBeOfferable_OnceTheTurnHasASubject()
    {
        var capability = new SearchKnowledgeBaseCapability(Substitute.For<IRagService>(), Substitute.For<IConversationKnowledgeBaseRepository>());
        var context = Base() with { AttachedKnowledgeBaseIds = [Guid.NewGuid()] };

        capability.IsOfferable(context, TurnOutcome.None).Should().BeFalse(
            "a turn that did nothing has produced nothing worth searching for");

        var afterLocating = new TurnOutcome([ResolveLocationCapability.CapabilityKey], true, [], false);
        capability.IsOfferable(context, afterLocating).Should().BeTrue();
    }

    [Fact]
    public void SearchKnowledgeBase_ShouldNotBeOffered_WhenItJustRan()
    {
        var capability = new SearchKnowledgeBaseCapability(Substitute.For<IRagService>(), Substitute.For<IConversationKnowledgeBaseRepository>());
        var context = Base() with { AttachedKnowledgeBaseIds = [Guid.NewGuid()] };
        var justSearched = new TurnOutcome([SearchKnowledgeBaseCapability.CapabilityKey], false, [], false);

        capability.IsOfferable(context, justSearched).Should().BeFalse(
            "an offer must never propose work the user just watched complete");
    }

    // ---- search_memory: two preconditions, falsified independently. ----

    [Fact]
    public void SearchMemory_ShouldBeUnavailable_WhenMemoryIsOff()
    {
        var capability = new SearchMemoryCapability(Substitute.For<IMemoryService>());
        var context = Base() with { IsMemoryAvailable = false };

        capability.IsAvailable(context).Should().BeFalse();
    }

    [Fact]
    public void SearchMemory_ShouldBeUnavailable_WhenThereIsNoUser()
    {
        var capability = new SearchMemoryCapability(Substitute.For<IMemoryService>());
        var context = TurnContext.Empty(userId: null) with { IsMemoryAvailable = true };

        // The second precondition on its own — memory is on, but there is nobody to remember.
        capability.IsAvailable(context).Should().BeFalse();
    }

    // ---- open_visual_panel: capacity. ----

    [Fact]
    public void OpenVisualPanel_ShouldBecomeUnavailable_AtTheConcurrentPanelCap()
    {
        var capability = new OpenVisualPanelCapability(Substitute.For<IPanelNotifier>());

        capability.IsAvailable(Base() with { OpenPanelTypeKeys = ["chart", "table"] })
            .Should().BeTrue();

        capability.IsAvailable(Base() with
        {
            OpenPanelTypeKeys = ["chart", "table", "summary", "parameters", "chart", "table"],
        }).Should().BeFalse("past the cap the framework evicts, and Lucy should not force that");
    }

    private static ActiveSiteBoundary BoundaryFor(string siteName) =>
        new(siteName, 25.156, 55.2218,
            [new GeoPoint(25.156, 55.221), new GeoPoint(25.156, 55.222), new GeoPoint(25.155, 55.222)],
            42_000, 0.85, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary,
            "OpenStreetMap (leisure=park)");
}
