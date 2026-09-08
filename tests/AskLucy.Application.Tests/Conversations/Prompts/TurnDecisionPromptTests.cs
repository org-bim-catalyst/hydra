using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Prompts;

/// <summary>
/// specs/045 T033 — the decide prompt, asserted without a model call (constitution §9: prompts
/// are "versioned artifacts… testable in isolation from the model call").
///
/// <para>
/// The load-bearing assertion here is <see cref="Build_ShouldNeverIncludeAnInputSchema"/>. The
/// grounding guarantee rests on the model never seeing the schema its arguments are validated
/// against — if a schema ever leaked into this prompt, the validation would quietly become a
/// formality the model could shape itself around, and nothing else in the system would notice.
/// </para>
/// </summary>
public sealed class TurnDecisionPromptTests
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

    [Fact]
    public void Build_ShouldListEveryAvailableCapabilityKey()
    {
        var prompt = TurnDecisionPrompt.Build([Location, Knowledge]);

        prompt.Should().Contain("resolve_location").And.Contain("search_knowledge_base");
        prompt.Should().Contain("the place name", "the argument hint is how the model knows what to supply");
    }

    [Fact]
    public void Build_ShouldCarryBothHalvesOfEachIndexEntry()
    {
        var prompt = TurnDecisionPrompt.Build([Location]);

        prompt.Should().Contain(Location.Description, "what it does");
        prompt.Should().Contain(Location.WhenToUse, "when to use it — the half a router actually matches on");
    }

    [Fact]
    public void Build_ShouldNeverIncludeAnInputSchema()
    {
        var prompt = TurnDecisionPrompt.Build([Location, Knowledge]);

        // Tier 3 stays server-side (research.md D13). These are the shapes a JSON Schema would
        // bring with it; none belongs in a prompt.
        prompt.Should().NotContain("InputSchemaJson");
        prompt.Should().NotContain("\"properties\"");
        prompt.Should().NotContain("\"required\"");
        prompt.Should().NotContain("minLength");
    }

    [Fact]
    public void Build_ShouldDescribeAllThreeIntents()
    {
        var prompt = TurnDecisionPrompt.Build([Location]);

        prompt.Should().Contain("\"answer\"").And.Contain("\"act\"").And.Contain("\"suggest\"");
    }

    [Fact]
    public void Build_ShouldStateTheBorderlineRule_AndWhyItIsAsymmetric()
    {
        var prompt = TurnDecisionPrompt.Build([Location]);

        // Stating the cost is what makes a model apply the rule rather than read it as a
        // stylistic preference, so the justification is part of the prompt's contract.
        prompt.Should().Contain("choose \"suggest\"");
        prompt.Should().Contain("one click");
        prompt.Should().Contain("uninvited");
    }

    [Fact]
    public void Build_ShouldForbidInventingACapability()
    {
        var prompt = TurnDecisionPrompt.Build([Location]);

        prompt.Should().Contain("Never invent a capability");
        prompt.Should().Contain("any other key is discarded");
    }

    [Fact]
    public void Build_ShouldInstructAnswerOnly_WhenNothingIsAvailable()
    {
        var prompt = TurnDecisionPrompt.Build([]);

        prompt.Should().Contain("No capabilities are available");
        prompt.Should().NotContain("Available capabilities.");
    }

    [Fact]
    public void Version_ShouldBePinned()
    {
        // A revision is a new class, never an edit: the wording is what routing benchmarks are
        // measured against, so changing it silently would invalidate every prior measurement.
        TurnDecisionPrompt.Version.Should().Be("v1");
    }
}
