using AskLucy.Application.Prompts;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Prompts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Application.Tests.Prompts;

public sealed class PromptCapabilityCheckerTests
{
    private static readonly AIModelCapabilities NoCapabilities = new(false, false, false, false, false, false, false, false, false);
    private static readonly AIModelCapabilities AllCapabilities = new(true, true, true, true, true, true, true, true, true);

    [Fact]
    public void GetUnmetRequirements_ShouldBeEmpty_WhenNoCapabilitiesAreRequired()
    {
        PromptCapabilityChecker.GetUnmetRequirements(PromptCapabilityRequirements.None, NoCapabilities).Should().BeEmpty();
    }

    [Fact]
    public void GetUnmetRequirements_ShouldListEachUnsupportedRequiredFlag()
    {
        var required = new PromptCapabilityRequirements(
            RequiresStreaming: false, RequiresVision: true, RequiresFunctionCalling: false, RequiresJsonMode: true,
            RequiresReasoning: false, RequiresEmbeddings: false, RequiresImageInput: false, RequiresImageOutput: false, RequiresAudio: false);

        var unmet = PromptCapabilityChecker.GetUnmetRequirements(required, NoCapabilities);

        unmet.Should().BeEquivalentTo(["vision", "JSON mode"]);
    }

    [Fact]
    public void GetUnmetRequirements_ShouldBeEmpty_WhenModelSupportsEveryRequiredCapability()
    {
        var required = new PromptCapabilityRequirements(
            RequiresStreaming: true, RequiresVision: true, RequiresFunctionCalling: false, RequiresJsonMode: false,
            RequiresReasoning: false, RequiresEmbeddings: false, RequiresImageInput: false, RequiresImageOutput: false, RequiresAudio: false);

        PromptCapabilityChecker.GetUnmetRequirements(required, AllCapabilities).Should().BeEmpty();
    }

    [Fact]
    public void IsCompatible_ShouldReflectWhetherAnyRequirementIsUnmet()
    {
        var required = new PromptCapabilityRequirements(
            RequiresStreaming: false, RequiresVision: false, RequiresFunctionCalling: false, RequiresJsonMode: true,
            RequiresReasoning: false, RequiresEmbeddings: false, RequiresImageInput: false, RequiresImageOutput: false, RequiresAudio: false);

        PromptCapabilityChecker.IsCompatible(required, NoCapabilities).Should().BeFalse();
        PromptCapabilityChecker.IsCompatible(required, AllCapabilities).Should().BeTrue();
    }
}
