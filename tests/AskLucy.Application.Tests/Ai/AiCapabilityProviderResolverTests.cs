using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// The administrator assigns a <b>provider</b> per capability; the model follows from that
/// provider's own default. Replaces the previous behaviour at these call sites — falling through
/// DefaultProviderResolver's last resort, "first enabled provider in display-name order" — which
/// nobody chose and which routed location intent classification to a provider whose credit had
/// run out while the operator's chat ran fine on another.
/// </summary>
public sealed class AiCapabilityProviderResolverTests
{
    private readonly IAiCapabilityAssignmentRepository _assignments = Substitute.For<IAiCapabilityAssignmentRepository>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();

    private AiCapabilityProviderResolver CreateSut() => new(
        _assignments, _providers, _models,
        new DefaultProviderResolver(_providers, _models),
        NullLogger<AiCapabilityProviderResolver>.Instance);

    private static AIModel AvailableModel(Guid providerId, string key) => AIModel.Create(
        providerId, key, key, 128000, 16384,
        new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

    private AIProvider Configured(string key, string displayName, out AIModel model)
    {
        var provider = AIProvider.Create(key, displayName, "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        model = AvailableModel(provider.Id, $"{key}-default");
        provider.SetDefaultModel(model.Id, "test");

        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        return provider;
    }

    private void Assign(AiCapability capability, Guid providerId) =>
        _assignments.GetByCapabilityAsync(capability, Arg.Any<CancellationToken>())
            .Returns(AiCapabilityAssignment.Create(capability, providerId, "test"));

    [Fact]
    public async Task ResolveAsync_ShouldUseTheAssignedProvider_AndItsOwnDefaultModel()
    {
        // Anthropic sorts first, so the old alphabetical fallback would have taken it. The whole
        // point of the assignment is that it does not.
        var anthropic = Configured("anthropic", "Anthropic", out _);
        var openai = Configured("openai", "OpenAI", out var gpt);
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { anthropic, openai });
        Assign(AiCapability.LocationIntent, openai.Id);

        var resolved = await CreateSut().ResolveAsync(AiCapability.LocationIntent, TestContext.Current.CancellationToken);

        resolved.ProviderId.Should().Be(openai.Id);
        resolved.ModelId.Should().Be(gpt.Id, "the model is never pinned per capability — it follows the provider's default");
    }

    [Fact]
    public async Task ResolveAsync_ShouldTrackTheProvidersDefaultModel_WhenItChanges()
    {
        // The reason the assignment stores only a provider: pinning a model here too would let
        // this row and the provider's own default disagree, and an administrator changing the
        // provider default would be silently ignored for every capability assigned to it.
        var openai = Configured("openai", "OpenAI", out _);
        var replacement = AvailableModel(openai.Id, "gpt-5");
        openai.SetDefaultModel(replacement.Id, "test");
        _models.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        Assign(AiCapability.BoundaryVision, openai.Id);

        var resolved = await CreateSut().ResolveAsync(AiCapability.BoundaryVision, TestContext.Current.CancellationToken);

        resolved.ModelId.Should().Be(replacement.Id);
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBackToThePlatformDefault_WhenNothingIsAssigned()
    {
        var openai = Configured("openai", "OpenAI", out var gpt);
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { openai });
        _assignments.GetByCapabilityAsync(Arg.Any<AiCapability>(), Arg.Any<CancellationToken>())
            .Returns((AiCapabilityAssignment?)null);

        var resolved = await CreateSut().ResolveAsync(AiCapability.MemoryExtraction, TestContext.Current.CancellationToken);

        resolved.ProviderId.Should().Be(openai.Id);
        resolved.ModelId.Should().Be(gpt.Id);
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBack_WhenTheAssignedProviderHasBeenDisabled()
    {
        // A capability quietly doing nothing is worse than one running on an imperfect provider,
        // so this degrades rather than throws — but AiCapabilityProviderResolver logs every
        // fallback, because silently reverting to the alphabetical rule is the failure this
        // whole design exists to end.
        var openai = Configured("openai", "OpenAI", out var gpt);
        var gemini = Configured("google-gemini", "Google Gemini", out _);
        gemini.Disable("test");
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { openai });
        Assign(AiCapability.DocumentClassification, gemini.Id);

        var resolved = await CreateSut().ResolveAsync(AiCapability.DocumentClassification, TestContext.Current.CancellationToken);

        resolved.ProviderId.Should().Be(openai.Id);
        resolved.ModelId.Should().Be(gpt.Id);
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBack_WhenTheAssignedProvidersDefaultModelIsNoLongerAvailable()
    {
        var openai = Configured("openai", "OpenAI", out var gpt);
        var gemini = Configured("google-gemini", "Google Gemini", out var geminiModel);
        geminiModel.SetStatus(AIModelStatus.Deprecated, "test");
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { openai });
        Assign(AiCapability.MemoryConflictDetection, gemini.Id);

        var resolved = await CreateSut().ResolveAsync(AiCapability.MemoryConflictDetection, TestContext.Current.CancellationToken);

        resolved.ProviderId.Should().Be(openai.Id);
        resolved.ModelId.Should().Be(gpt.Id);
    }
}
