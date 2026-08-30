using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.UpdateAiProvider;
using AskLucy.Application.Ai.Queries.GetAdminAiProviders;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// The platform default decides which provider serves every request that has no user preference
/// behind it — location intent classification, memory extraction, background jobs. Until now no
/// UI could set it, so every provider sat at null and DefaultProviderResolver fell through to
/// "first enabled provider in display-name order". In production that silently routed location
/// resolution to Anthropic while the operator's chat ran on OpenAI, and stayed that way after
/// Anthropic's credit ran out.
/// </summary>
public sealed class PlatformDefaultModelTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();

    private static AIProvider EnabledProvider(string key, string displayName)
    {
        var provider = AIProvider.Create(key, displayName, "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        return provider;
    }

    private static AIModel AvailableModel(Guid providerId, string key)
        => AIModel.Create(
            providerId, key, key, 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

    [Fact]
    public async Task Handle_ShouldFlagTheProviderThatActuallyServes_NotSimplyTheFirstAlphabetically()
    {
        // Anthropic sorts before OpenAI, so the bare alphabetical fallback would pick it. Giving
        // only OpenAI a default model is precisely how an administrator overrides that, and the
        // page has to agree with the resolver about the outcome.
        var anthropic = EnabledProvider("anthropic", "Anthropic");
        var openai = EnabledProvider("openai", "OpenAI");
        var gpt = AvailableModel(openai.Id, "gpt-4.1");
        openai.SetDefaultModel(gpt.Id, "test");

        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { anthropic, openai });
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { anthropic, openai });
        _models.GetByIdAsync(gpt.Id, Arg.Any<CancellationToken>()).Returns(gpt);

        var handler = new GetAdminAiProvidersQueryHandler(
            _providers, Substitute.For<IProviderHealthFreshnessPolicy>(),
            new DefaultProviderResolver(_providers, _models));

        var result = await handler.Handle(new GetAdminAiProvidersQuery(), TestContext.Current.CancellationToken);

        result.Single(p => p.ProviderKey == "openai").IsEffectivePlatformDefault.Should().BeTrue();
        result.Single(p => p.ProviderKey == "anthropic").IsEffectivePlatformDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ShouldReportNoDefault_WhenNoEnabledProviderHasAnAvailableModel()
    {
        // The documented zero-providers state. This screen must render it, not fail on it.
        var anthropic = EnabledProvider("anthropic", "Anthropic");
        _providers.ListAllAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { anthropic });
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { anthropic });
        _models.ListAvailableByProviderIdAsync(anthropic.Id, Arg.Any<CancellationToken>()).Returns(new List<AIModel>());

        var handler = new GetAdminAiProvidersQueryHandler(
            _providers, Substitute.For<IProviderHealthFreshnessPolicy>(),
            new DefaultProviderResolver(_providers, _models));

        var result = await handler.Handle(new GetAdminAiProvidersQuery(), TestContext.Current.CancellationToken);

        result.Should().OnlyContain(p => !p.IsEffectivePlatformDefault);
    }

    [Fact]
    public async Task Validator_ShouldReject_AModelBelongingToAnotherProvider()
    {
        // Accepting it would store a default the resolver skips, handing the platform default
        // back to whichever provider sorts first — the accident this setting exists to end.
        var openai = EnabledProvider("openai", "OpenAI");
        var foreignModel = AvailableModel(Guid.NewGuid(), "claude-3-5-haiku");
        _models.GetByIdAsync(foreignModel.Id, Arg.Any<CancellationToken>()).Returns(foreignModel);

        var result = await new UpdateAiProviderCommandValidator(_models).ValidateAsync(
            new UpdateAiProviderCommand(openai.Id, null, foreignModel.Id),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "defaultModelId");
    }

    [Fact]
    public async Task Validator_ShouldAllowClearing_WithoutInspectingAnyModel()
    {
        var result = await new UpdateAiProviderCommandValidator(_models).ValidateAsync(
            new UpdateAiProviderCommand(Guid.NewGuid(), null, null, ClearDefaultModel: true),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        await _models.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
