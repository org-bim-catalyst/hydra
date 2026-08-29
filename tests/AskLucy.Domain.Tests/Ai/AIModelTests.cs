using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

public sealed class AIModelTests
{
    private static readonly Guid ProviderId = Guid.NewGuid();

    private static readonly AIModelCapabilities NoCapabilities =
        new(Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false,
            Reasoning: false, Embeddings: false, ImageInput: false, ImageOutput: false, Audio: false);

    [Fact]
    public void Create_ShouldStartAvailable()
    {
        var model = AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        model.Status.Should().Be(AIModelStatus.Available);
        model.IsSelectable.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldThrow_WhenContextWindowIsSuppliedAndNotPositive(int invalidContextWindow)
    {
        var act = () => AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", invalidContextWindow, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ShouldThrow_WhenMaxOutputIsSuppliedAndNotPositive(int invalidMaxOutput)
    {
        var act = () => AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, invalidMaxOutput, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_ShouldSucceed_WhenTokenLimitsAreAbsent()
    {
        // specs/043 FR-029: several vendors publish no token metadata at all - OpenAI's list
        // carries none for any model. Rejecting those rows meant they could never be added to
        // the catalog by any route, since no edit path for the figures exists either.
        var model = AIModel.Create(
            ProviderId, "gpt-4-turbo", "GPT-4 Turbo", contextWindowTokens: null, maxOutputTokens: null,
            NoCapabilities, releaseDate: null, pricing: null, actor: "admin-1");

        model.ContextWindowTokens.Should().BeNull();
        model.MaxOutputTokens.Should().BeNull();
    }

    [Fact]
    public void Create_ShouldSucceed_WhenOnlyOneTokenLimitIsAbsent()
    {
        // OpenRouter publishes a context length but no output limit - a mixed row must be
        // just as acceptable as one missing both.
        var model = AIModel.Create(
            ProviderId, "some/model", "Some Model", contextWindowTokens: 64_000, maxOutputTokens: null,
            NoCapabilities, releaseDate: null, pricing: null, actor: "admin-1");

        model.ContextWindowTokens.Should().Be(64_000);
        model.MaxOutputTokens.Should().BeNull();
    }

    [Theory]
    [InlineData(AIModelStatus.Deprecated)]
    [InlineData(AIModelStatus.Unavailable)]
    public void SetStatus_ToNonAvailable_ShouldMakeModelUnselectable(AIModelStatus status)
    {
        // Clarifications Session 2026-07-30 Q2: Deprecated and Unavailable are both
        // non-selectable, differing only in administrative meaning (FR-006/FR-007).
        var model = AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        model.SetStatus(status, "admin-1");

        model.IsSelectable.Should().BeFalse();
    }

    [Fact]
    public void SetStatus_ShouldAllowReinstatingADeprecatedModel()
    {
        var model = AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        model.SetStatus(AIModelStatus.Deprecated, "admin-1");
        model.SetStatus(AIModelStatus.Available, "admin-1");

        model.IsSelectable.Should().BeTrue();
    }

    [Fact]
    public void Pricing_ShouldBeNull_WhenNotSupplied()
    {
        // FR-022: null (not a fabricated zero) means pricing is unknown.
        var model = AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        model.Pricing.Should().BeNull();
    }

    [Fact]
    public void SetPricing_ShouldUpdatePricing()
    {
        var model = AIModel.Create(
            ProviderId, "gpt-4.1", "GPT-4.1", 128_000, 16_384, NoCapabilities,
            releaseDate: null, pricing: null, actor: "admin-1");

        model.SetPricing(new ModelPricing(2.50m, 10.00m), "admin-1");

        model.Pricing.Should().Be(new ModelPricing(2.50m, 10.00m));
    }
}
