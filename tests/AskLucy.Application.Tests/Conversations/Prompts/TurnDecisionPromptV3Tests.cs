using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Prompts;

/// <summary>
/// specs/045 FR-036 / constitution §9 — the decide prompt the runtime actually uses, asserted
/// without a model call. <see cref="TurnDecisionPromptV2Tests"/> keeps covering v2, which is
/// retained unreferenced so a routing benchmark taken against it stays reproducible.
///
/// <para>
/// Every invariant the earlier versions carry is re-asserted here rather than inherited: a
/// versioned prompt is a copy, and a copy is exactly where a guarantee gets dropped by hand.
/// </para>
/// </summary>
public sealed class TurnDecisionPromptV3Tests
{
    private static readonly CapabilityIndexEntry Location = new(
        "resolve_location",
        "Resolves a named real-world place to confirmed coordinates.",
        "Use when the user asks to see, find or navigate to a named place.",
        "the place name");

    private static readonly CapabilityIndexEntry Analysis = new(
        "request_site_analysis",
        "Starts a multi-specialist analysis of a site.",
        "Use when the user asks for a site analysis, for the site in the viewer or a named site.",
        "site: the name the user asked for; omit only for 'this site'; or lat/long");

    // ---- the change that justifies the version ----

    /// <summary>
    /// The reported defect: "Run a site analysis for Al Safa Park 2" started an analysis for the
    /// site already on screen, because the decide step supplied no place argument and the
    /// capability's documented fallback is the active site. The rule is stated once, for every
    /// capability that takes a place, and names the only wordings that mean the active site.
    /// </summary>
    [Fact]
    public void Build_ShouldPassANamedPlaceThrough_TheCapabilitysPlaceArgument()
    {
        var prompt = TurnDecisionPromptV3.Build([Analysis]);

        prompt.Should().Contain("pass that name in the capability's place argument");
        prompt.Should().Contain("Never substitute the site already on screen");
        prompt.Should().Contain("\"this site\"").And.Contain("\"here\"");
        prompt.Should().Contain("a message naming no place at all means the active site");
    }

    [Fact]
    public void Build_ShouldStillCarryTheArgumentHint_ThatNamesThePlace()
    {
        // The hint is where the model learns what to supply; the rule alone is not enough.
        TurnDecisionPromptV3.Build([Analysis])
            .Should().Contain("site: the name the user asked for");
    }

    [Fact]
    public void Version_ShouldBePinned_AndDistinctFromV2()
    {
        TurnDecisionPromptV3.Version.Should().Be("v3");
        TurnDecisionPromptV3.Version.Should().NotBe(TurnDecisionPromptV2.Version);
    }

    [Fact]
    public void V2_ShouldBeLeftExactlyAsItWas()
    {
        // The point of a new class rather than an edit. If this ever fails, a measurement taken
        // against v2 has been invalidated retroactively and silently.
        TurnDecisionPromptV2.Build([Analysis])
            .Should().NotContain("Never substitute the site already on screen");
        TurnDecisionPromptV2.Version.Should().Be("v2");
    }

    // ---- the invariants carried over from v2, re-asserted on the copy ----

    [Fact]
    public void Build_ShouldNeverIncludeAnInputSchema()
    {
        var prompt = TurnDecisionPromptV3.Build([Location, Analysis]);

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
        var prompt = TurnDecisionPromptV3.Build([Location, Analysis]);

        prompt.Should().Contain("resolve_location").And.Contain("request_site_analysis");
        prompt.Should().Contain(Location.Description, "what it does");
        prompt.Should().Contain(Location.WhenToUse, "when to use it — the half a router actually matches on");
        prompt.Should().Contain("the place name", "the argument hint is how the model knows what to supply");
    }

    [Fact]
    public void Build_ShouldDescribeAllThreeIntents()
    {
        var prompt = TurnDecisionPromptV3.Build([Location]);

        prompt.Should().Contain("\"answer\"").And.Contain("\"act\"").And.Contain("\"suggest\"");
    }

    [Fact]
    public void Build_ShouldStateTheBorderlineRule_AndWhyItIsAsymmetric()
    {
        var prompt = TurnDecisionPromptV3.Build([Location]);

        prompt.Should().Contain("choose \"suggest\"");
        prompt.Should().Contain("one click");
        prompt.Should().Contain("uninvited");
    }

    [Fact]
    public void Build_ShouldForbidInventingACapability()
    {
        var prompt = TurnDecisionPromptV3.Build([Location]);

        prompt.Should().Contain("Never invent a capability");
        prompt.Should().Contain("any other key is discarded");
    }

    [Fact]
    public void Build_ShouldInstructAnswerOnly_WhenNothingIsAvailable()
    {
        var prompt = TurnDecisionPromptV3.Build([]);

        prompt.Should().Contain("Nothing is available this turn");
        prompt.Should().NotContain("Available capabilities.");
    }

    [Fact]
    public void Build_ShouldListAvailableFlows_AndPreferThemOverASingleCapability()
    {
        var flow = new CapabilityIndexEntry(
            "locate_a_place", "Finds a place, focuses the viewer, and outlines the site — one job, three steps.",
            "Use when the user asks to see, find or navigate to a named place.", "the place name");

        var prompt = TurnDecisionPromptV3.Build([Location], [flow]);

        prompt.Should().Contain("locate_a_place");
        prompt.Should().Contain("flowKey");
        prompt.Should().Contain("throughStepIndex");
    }

    [Fact]
    public void Build_ShouldNotMentionFlows_WhenNoneAreAvailable()
    {
        TurnDecisionPromptV3.Build([Location]).Should().NotContain("Available flows");
    }

    [Fact]
    public void Build_WithNoRecentOutcomes_ShouldBeIdenticalToOmittingThemEntirely()
    {
        TurnDecisionPromptV3.Build([Location, Analysis], null, RecentTurnOutcomeSummary.Empty)
            .Should().Be(TurnDecisionPromptV3.Build([Location, Analysis]));

        TurnDecisionPromptV3.Build([Location], [], null)
            .Should().Be(TurnDecisionPromptV3.Build([Location], []));
    }

    [Fact]
    public void Build_WithARecentFailure_ShouldNotLeakArgumentsToTheModel()
    {
        var summary = RecentTurnOutcomeSummary.From([RecordedTurnOutcome.Acted(
            [ActionAttempt.Failure("Capability", "resolve_location", "Al Safa Park 2", """{"secretQuery":"x"}""", "unavailable")],
            DateTimeOffset.UtcNow)]);

        TurnDecisionPromptV3.Build([Location], null, summary).Should().NotContain("secretQuery");
    }
}
