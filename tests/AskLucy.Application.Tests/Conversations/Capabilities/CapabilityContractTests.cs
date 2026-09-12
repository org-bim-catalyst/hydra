using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Locations;
using AskLucy.Application.Panels;
using AskLucy.Application.SiteBoundaries;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Capabilities;

/// <summary>
/// specs/045 T031 — the rules every conversation capability must satisfy, asserted once over all
/// of them.
///
/// <para>
/// A shared theory rather than per-capability duplicates, because the point is that a
/// <b>future</b> capability cannot skip them. Written per-class, these checks would be copied for
/// the first few and quietly omitted for the seventh; written here, adding a capability to
/// <see cref="AllCapabilities"/> is what makes it testable, and forgetting to do that is the only
/// way to evade them.
/// </para>
/// </summary>
public sealed class CapabilityContractTests
{
    /// <summary>
    /// Constructed with substitutes rather than resolved from the container: these assertions are
    /// about declared metadata and pure predicates, none of which touches a dependency, so a real
    /// DI graph would add setup cost and a failure mode without adding coverage.
    /// </summary>
    public static TheoryData<string, IConversationCapability> AllCapabilities()
    {
        var data = new TheoryData<string, IConversationCapability>();
        foreach (var capability in Build())
        {
            data.Add(capability.Name, capability);
        }

        return data;
    }

    private static IReadOnlyList<IConversationCapability> Build() =>
    [
        new ResolveLocationCapability(Substitute.For<ILocationResolutionService>()),
        new ResolveSiteBoundaryCapability(Substitute.For<IBoundaryResolutionService>(), Substitute.For<IUserChatRepository>()),
        new AdjustViewerFocusCapability(),
        new SearchKnowledgeBaseCapability(Substitute.For<IRagService>(), Substitute.For<IConversationKnowledgeBaseRepository>()),
        new SearchMemoryCapability(Substitute.For<IMemoryService>()),
        new PresentPanelContentCapability(Substitute.For<IPanelNotifier>()),
        new OpenLivePanelCapability(Substitute.For<IPanelNotifier>()),
    ];

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void Key_ShouldBeStableSnakeCase(string key, IConversationCapability capability)
    {
        // The key is persisted on offers and echoed back on selection, so it is a contract with
        // stored data, not just an identifier. Restricting the shape keeps a rename visible.
        key.Should().MatchRegex("^[a-z][a-z0-9_]*$");
        capability.Name.Should().Be(key);
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void IndexEntry_ShouldCarryBothHalves(string key, IConversationCapability capability)
    {
        // Anthropic's skill guidance: a description must say what it does AND when to use it.
        // "What it does" alone gives the deciding model nothing to match a request against, which
        // is the most common reason routing picks the wrong tool.
        capability.Description.Should().NotBeNullOrWhiteSpace($"{key} must say what it does");
        capability.WhenToUse.Should().NotBeNullOrWhiteSpace($"{key} must say when to use it");
        capability.WhenToUse.Length.Should().BeLessThanOrEqualTo(300);
        capability.ArgumentHint.Should().NotBeNullOrWhiteSpace();
        capability.ArgumentHint.Length.Should().BeLessThanOrEqualTo(80);
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void WhenToUse_ShouldBeThirdPersonAndCarryTriggerTerms(string key, IConversationCapability capability)
    {
        var text = capability.WhenToUse;

        // First and second person read as marketing copy to a router and, per the guidance,
        // measurably hurt discovery — the description is injected into a system prompt, so a
        // shifting point of view muddles who is being described.
        text.Should().NotContainAny(["I can", "I'll", "you can use this", "You can use this"]);

        // "Use when …" plus at least two concrete triggers. A capability that cannot name two
        // situations it applies to has not been described specifically enough to route on.
        text.Should().StartWith("Use when", $"{key} should describe the situation, not the mechanism");
        CountTriggerTerms(text).Should().BeGreaterThanOrEqualTo(2,
            $"{key} must name the words a user actually says");
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void UserFacingText_ShouldRespectLengthLimits(string key, IConversationCapability capability)
    {
        capability.Label.Should().NotBeNullOrWhiteSpace().And.HaveLength(capability.Label.Length);
        capability.Label.Length.Should().BeLessThanOrEqualTo(80, $"{key}'s label is spoken aloud");
        capability.OfferDescription.Should().NotBeNullOrWhiteSpace();
        capability.OfferDescription.Length.Should().BeLessThanOrEqualTo(160);
        capability.AcknowledgementTemplate.Should().NotBeNullOrWhiteSpace(
            $"{key} must have wording for the beat shown before it runs, which is never model-generated");
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void UsageGuidance_ShouldExistAndStayShort(string key, IConversationCapability capability)
    {
        // Tier 2: loaded only when this capability is chosen, but once loaded it competes with the
        // conversation for context. A cap keeps "document what the model cannot know" honest.
        capability.UsageGuidance.Should().NotBeNullOrWhiteSpace();
        capability.UsageGuidance.Length.Should().BeLessThanOrEqualTo(800, $"{key}'s Tier 2 guidance is competing with conversation history");
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void Schemas_ShouldBeValidJson(string key, IConversationCapability capability)
    {
        // Tier 3. Never shown to a model — which is precisely why it must be machine-valid: the
        // grounder validates arguments against it, and a malformed schema would silently pass
        // everything through.
        var act = () =>
        {
            using var _ = JsonDocument.Parse(capability.InputSchemaJson);
            using var __ = JsonDocument.Parse(capability.OutputSchemaJson);
        };

        act.Should().NotThrow($"{key}'s schemas back the grounding guarantee");
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public async Task ExecuteAsync_ShouldReturnFailure_RatherThanThrow_OnInvalidInput(string key, IConversationCapability capability)
    {
        // Constitution §2.VIII, at the capability boundary. A capability that throws into the turn
        // takes the whole turn with it; one that returns a failure lets the orchestrator report it
        // and keep the results already delivered.
        var context = new AgentToolExecutionContext(
            Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var emptyInput = JsonDocument.Parse("{}");

        var result = await capability.ExecuteAsync(context, emptyInput, CancellationToken.None);

        result.Succeeded.Should().BeFalse($"{key} was given no arguments");
        result.FailureReason.Should().NotBeNullOrWhiteSpace($"{key} must say why it could not run");
    }

    [Theory]
    [MemberData(nameof(AllCapabilities))]
    public void IsOfferable_ShouldNeverExceedIsAvailable(string key, IConversationCapability capability)
    {
        // The whole point of the split (FR-025b): offerable is strictly stronger. A capability
        // that is offerable while unavailable would put a row on the card that cannot run.
        foreach (var context in RepresentativeContexts())
        {
            if (capability.IsOfferable(context, TurnOutcome.None))
            {
                capability.IsAvailable(context).Should().BeTrue(
                    $"{key} claims to be offerable in a state where it cannot run");
            }
        }
    }

    /// <summary>
    /// The five capabilities that exist to be invoked, never advertised. Pinned as an explicit
    /// list because it is a deliberate product decision rather than an accident of the
    /// predicates: an offer that lists everything possible is a menu, and a menu shown after
    /// every message is wallpaper.
    /// </summary>
    [Theory]
    [InlineData(ResolveLocationCapability.CapabilityKey)]
    [InlineData(ResolveSiteBoundaryCapability.CapabilityKey)]
    [InlineData(AdjustViewerFocusCapability.CapabilityKey)]
    [InlineData(SearchMemoryCapability.CapabilityKey)]
    public void NeverOfferedCapabilities_ShouldStayUnofferable(string key)
    {
        var capability = Build().Single(c => c.Name == key);

        foreach (var context in RepresentativeContexts())
        {
            foreach (var outcome in RepresentativeOutcomes())
            {
                capability.IsOfferable(context, outcome).Should().BeFalse(
                    $"{key} is reached by asking or through a flow, never by being suggested");
            }
        }
    }

    private static IEnumerable<TurnContext> RepresentativeContexts()
    {
        yield return TurnContext.Empty("user-1", Guid.NewGuid());

        yield return TurnContext.Empty("user-1", Guid.NewGuid()) with
        {
            ActiveLocation = new Domain.Chats.ActiveSiteLocation(25.156, 55.2218, "Al Safa Park 2", 0.9),
        };

        yield return TurnContext.Empty("user-1", Guid.NewGuid()) with
        {
            AttachedKnowledgeBaseIds = [Guid.NewGuid()],
            IsMemoryAvailable = true,
        };
    }

    private static IEnumerable<TurnOutcome> RepresentativeOutcomes()
    {
        yield return TurnOutcome.None;
        yield return new TurnOutcome([ResolveLocationCapability.CapabilityKey], true, [], false);
        yield return new TurnOutcome([], false, [], true);
    }

    private static int CountTriggerTerms(string whenToUse)
    {
        string[] triggers =
        [
            "show", "see", "find", "locate", "navigate", "where", "take me", "centre", "center",
            "zoom", "closer", "pull back", "outline", "highlight", "extent", "measure",
            "search", "ask", "document", "standard", "polic", "knowledge", "remember",
            "preference", "recall", "chart", "table", "summar", "compar",
        ];

        return triggers.Count(t => whenToUse.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
