using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Prompts;

/// <summary>
/// specs/068 — the decide prompt the runtime actually uses, asserted without a model call
/// (constitution §9). <see cref="TurnDecisionPromptTests"/> keeps covering v1, which is retained
/// unreferenced so a routing benchmark taken against it stays reproducible.
///
/// <para>
/// Every invariant v1 carries is re-asserted here rather than inherited. A versioned prompt is a
/// copy, and a copy is exactly where a guarantee gets dropped by hand — the schema-withholding
/// rule most of all, since nothing else in the system would notice if it were.
/// </para>
/// </summary>
public sealed class TurnDecisionPromptV2Tests
{
    private static readonly CapabilityIndexEntry Location = new(
        "resolve_location",
        "Resolves a named real-world place to confirmed coordinates.",
        "Use when the user asks to see, find or navigate to a named place.",
        "the place name");

    private static readonly CapabilityIndexEntry Knowledge = new(
        "search_knowledge_base",
        "Searches attached knowledge bases.",
        "Use when the user asks what their documents say.",
        "what to search for");

    // ---- the change that justifies the version ----

    /// <summary>
    /// The reported defect. "Give me options" and "what else can you show me?" named no subject,
    /// so they matched none of v1's "suggest" examples and routed to "answer" — the one intent
    /// that suppresses the offer step outright (<see cref="OfferSuppressionRules"/>, FR-025a.1).
    /// The user asked for options and structurally could not be given any.
    /// </summary>
    [Fact]
    public void Build_ShouldRouteARequestForOptions_ToSuggest()
    {
        var prompt = TurnDecisionPromptV2.Build([Location]);

        prompt.Should().Contain("what can you do?");
        prompt.Should().Contain("give me options");
        prompt.Should().Contain("what else can you show me?");
        prompt.Should().Contain("asking what is possible is NEVER \"answer\"");
    }

    [Fact]
    public void Build_ShouldStillDescribeSuggestBySubject_NotOnlyByCapabilityQuestions()
    {
        // The broadened branch must not cost the original case: "do you know X?" is what the
        // offer step was built for and stays the primary example.
        var prompt = TurnDecisionPromptV2.Build([Location]);

        prompt.Should().Contain("do you know X?").And.Contain("what is X?");
    }

    [Fact]
    public void Version_ShouldBePinned_AndDistinctFromV1()
    {
        TurnDecisionPromptV2.Version.Should().Be("v2");
        TurnDecisionPromptV2.Version.Should().NotBe(TurnDecisionPrompt.Version);
    }

    [Fact]
    public void V1_ShouldBeLeftExactlyAsItWas()
    {
        // The point of a new class rather than an edit. If this ever fails, a measurement taken
        // against v1 has been invalidated retroactively and silently.
        TurnDecisionPrompt.Build([Location]).Should().NotContain("give me options");
        TurnDecisionPrompt.Version.Should().Be("v1");
    }

    // ---- the invariants carried over from v1, re-asserted on the copy ----

    [Fact]
    public void Build_ShouldNeverIncludeAnInputSchema()
    {
        var prompt = TurnDecisionPromptV2.Build([Location, Knowledge]);

        // Tier 3 stays server-side (research.md D13): the grounder's check is only independent
        // while the model cannot shape its arguments around the schema backing it.
        prompt.Should().NotContain("InputSchemaJson");
        prompt.Should().NotContain("\"properties\"");
        prompt.Should().NotContain("\"required\"");
        prompt.Should().NotContain("minLength");
    }

    [Fact]
    public void Build_ShouldListEveryAvailableCapabilityKey_WithBothHalvesOfItsEntry()
    {
        var prompt = TurnDecisionPromptV2.Build([Location, Knowledge]);

        prompt.Should().Contain("resolve_location").And.Contain("search_knowledge_base");
        prompt.Should().Contain(Location.Description, "what it does");
        prompt.Should().Contain(Location.WhenToUse, "when to use it — the half a router actually matches on");
        prompt.Should().Contain("the place name", "the argument hint is how the model knows what to supply");
    }

    [Fact]
    public void Build_ShouldDescribeAllThreeIntents()
    {
        var prompt = TurnDecisionPromptV2.Build([Location]);

        prompt.Should().Contain("\"answer\"").And.Contain("\"act\"").And.Contain("\"suggest\"");
    }

    [Fact]
    public void Build_ShouldStateTheBorderlineRule_AndWhyItIsAsymmetric()
    {
        var prompt = TurnDecisionPromptV2.Build([Location]);

        prompt.Should().Contain("choose \"suggest\"");
        prompt.Should().Contain("one click");
        prompt.Should().Contain("uninvited");
    }

    [Fact]
    public void Build_ShouldForbidInventingACapability()
    {
        var prompt = TurnDecisionPromptV2.Build([Location]);

        prompt.Should().Contain("Never invent a capability");
        prompt.Should().Contain("any other key is discarded");
    }

    [Fact]
    public void Build_ShouldInstructAnswerOnly_WhenNothingIsAvailable()
    {
        var prompt = TurnDecisionPromptV2.Build([]);

        prompt.Should().Contain("Nothing is available this turn");
        prompt.Should().NotContain("Available capabilities.");
    }

    [Fact]
    public void Build_ShouldListAvailableFlows_AndPreferThemOverASingleCapability()
    {
        var flow = new CapabilityIndexEntry(
            "locate_a_place", "Finds a place, focuses the viewer, and outlines the site — one job, three steps.",
            "Use when the user asks to see, find or navigate to a named place.", "the place name");

        var prompt = TurnDecisionPromptV2.Build([Location], [flow]);

        prompt.Should().Contain("locate_a_place");
        prompt.Should().Contain("flowKey");
        prompt.Should().Contain("throughStepIndex");
    }

    [Fact]
    public void Build_ShouldNotMentionFlows_WhenNoneAreAvailable()
    {
        TurnDecisionPromptV2.Build([Location]).Should().NotContain("Available flows");
    }

    [Fact]
    public void Build_WithNoRecentOutcomes_ShouldBeIdenticalToOmittingThemEntirely()
    {
        // FR-009c — a conversation with nothing recorded gets one prompt, not two similar ones,
        // so every other assertion here is about a single known variant.
        TurnDecisionPromptV2.Build([Location, Knowledge], null, RecentTurnOutcomeSummary.Empty)
            .Should().Be(TurnDecisionPromptV2.Build([Location, Knowledge]));

        TurnDecisionPromptV2.Build([Location], [], null)
            .Should().Be(TurnDecisionPromptV2.Build([Location], []));
    }

    [Fact]
    public void Build_WithARecentFailure_ShouldTellTheRouterWhatARetryRefersTo()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.Acted(
            [ActionAttempt.Failure("Capability", "resolve_location", "Al Safa Park 2", "{}", "the provider was unavailable")],
            DateTimeOffset.UtcNow)]);

        var prompt = TurnDecisionPromptV2.Build([Location], null, summary);

        prompt.Should().Contain("resolve_location")
            .And.Contain("Al Safa Park 2")
            .And.Contain("the provider was unavailable")
            .And.Contain("try again");
    }

    [Fact]
    public void Build_WithARecentFailure_ShouldNotLeakArgumentsToTheModel()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.Acted(
            [ActionAttempt.Failure("Capability", "resolve_location", "Al Safa Park 2", """{"secretQuery":"x"}""", "unavailable")],
            DateTimeOffset.UtcNow)]);

        TurnDecisionPromptV2.Build([Location], null, summary).Should().NotContain("secretQuery");
    }
}
