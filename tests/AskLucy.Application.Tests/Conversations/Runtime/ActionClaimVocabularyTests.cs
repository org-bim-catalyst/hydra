using System.Reflection;
using System.Runtime.CompilerServices;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/068 T029 — binds the claim patterns to the capability catalog.
///
/// <para>
/// Hand-written patterns are this feature's main residual risk, and the risk is not that today's
/// are wrong: it is that a capability added next year keeps working while its claims quietly stop
/// being checked, with nothing failing to say so. These tests are what makes that impossible.
/// </para>
/// </summary>
public sealed class ActionClaimVocabularyTests
{
    private static IReadOnlyList<Type> RegisteredCapabilityTypes() =>
        [.. typeof(IConversationCapability).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IConversationCapability).IsAssignableFrom(t))
            .Where(t => t.GetField("CapabilityKey", BindingFlags.Public | BindingFlags.Static) is not null)];

    [Fact]
    public void EveryRegisteredCapability_ShouldContributeToTheVocabulary()
    {
        var registeredKeys = RegisteredCapabilityTypes()
            .Select(t => (string)t.GetField("CapabilityKey", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!);

        // An MCP tool has no CapabilityKey constant and is deliberately out of scope: its vocabulary
        // is whatever a third-party server happens to expose, so there is no closed set to generate
        // patterns from. Unnamed claims in those turns fall back to "did anything succeed at all".
        ActionClaimVocabulary.Capabilities.Select(c => c.Key).Should().BeEquivalentTo(registeredKeys);
    }

    [Fact]
    public void EveryCapabilityLabel_ShouldMatchTheOneTheCapabilityItselfExposes()
    {
        foreach (var type in RegisteredCapabilityTypes())
        {
            var key = (string)type.GetField("CapabilityKey", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

            // Every Label here is an expression-bodied literal, so an uninitialized instance reads it
            // correctly without constructing the capability's whole dependency graph. That is the
            // point: the vocabulary must track the real label, not a copy of it that can drift.
            var label = (string)type.GetProperty(nameof(IConversationCapability.Label))!
                .GetValue(RuntimeHelpers.GetUninitializedObject(type))!;

            var terms = ActionClaimVocabulary.Capabilities.Single(c => c.Key == key);
            var labelWords = label.Split([' ', '&'], StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant());

            terms.Verbs.Should().NotBeEmpty();
            labelWords.Skip(1).Where(w => w.Length > 4)
                .Should().OnlyContain(w => terms.Nouns.Contains(w) || terms.Nouns.Contains(w.TrimEnd('s')),
                    "the vocabulary for {0} must be derived from its own label \"{1}\"", key, label);
        }
    }

    [Fact]
    public void EveryStandaloneVerb_ShouldBeOneTheCatalogGenerates()
    {
        // The standalone set is a filter over the generated vocabulary, never a second hand-written
        // one. A verb here that no capability produces would be a pattern with no owner.
        ActionClaimVocabulary.StandaloneVerbs.Should().BeSubsetOf(ActionClaimVocabulary.AllVerbs);
    }

    [Theory]
    [InlineData("I've shown you Al Safa Park 2.")]
    [InlineData("I have highlighted the site boundary.")]
    [InlineData("I've loaded the model into the viewer.")]
    [InlineData("I searched your knowledge bases for that.")]
    [InlineData("I've zoomed in on the plot.")]
    public void ACompletedFirstPersonAction_ShouldReadAsAClaim(string sentence) =>
        ActionClaimVocabulary.IsActionClaim(sentence).Should().BeTrue();

    [Theory]
    [InlineData("I can show you that if you'd like.")]
    [InlineData("I'll highlight the site boundary next.")]
    [InlineData("Would you like me to open the viewer?")]
    [InlineData("The boundary was highlighted in the original survey.")]
    [InlineData("I found that the report contradicts itself.")]
    [InlineData("Al Safa Park 2 covers roughly 64 hectares.")]
    public void EverythingElse_ShouldNot(string sentence) =>
        // The last two matter most. "found" and "ran" are generated verbs, but each has an everyday
        // sense; treating those as claims would make the gate correct ordinary conversation, which
        // is a worse product than the defect it fixes.
        ActionClaimVocabulary.IsActionClaim(sentence).Should().BeFalse();

    [Fact]
    public void AClaimNamingACapability_ShouldResolveToThatCapabilityAlone()
    {
        ActionClaimVocabulary.CapabilitiesNamedIn("I've highlighted the site boundary.")
            .Should().ContainSingle().Which.Should().Be(ResolveSiteBoundaryCapability.CapabilityKey);
    }

    [Fact]
    public void AClaimNamingNothingInParticular_ShouldResolveToNoCapability()
    {
        // "I've shown you Al Safa Park 2" — the reported sentence. It claims plenty and names
        // nothing, which is why the gate falls back to asking whether the turn achieved anything.
        ActionClaimVocabulary.CapabilitiesNamedIn("I've shown you Al Safa Park 2.").Should().BeEmpty();
    }
}
