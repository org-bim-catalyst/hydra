using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/005-multi-provider-ai-engine T084 — a saved default whose provider has since been disabled falls back with `IsPlatformDefault: true` (FR-018), rather than returning a now-invalid pair.</summary>
public sealed class DefaultProviderResolverTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly DefaultProviderResolver _resolver;

    public DefaultProviderResolverTests()
    {
        _resolver = new DefaultProviderResolver(_providers, _models);
    }

    private static AIModel MakeModel(Guid providerId) => AIModel.Create(
        providerId, "gpt-4.1", "GPT-4.1", 128000, 16384,
        new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

    [Fact]
    public async Task ResolveAsync_ShouldUseTheSavedPreference_WhenItsProviderAndModelAreStillValid()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = MakeModel(provider.Id);

        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _models.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);

        var preference = UserAiPreference.Create("user-1", "user-1");
        preference.SetDefaults(provider.Id, model.Id, null, "user-1");

        var result = await _resolver.ResolveAsync(preference, CancellationToken.None);

        result.ProviderId.Should().Be(provider.Id);
        result.ModelId.Should().Be(model.Id);
        result.IsPlatformDefault.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBackWithANotice_WhenTheSavedProviderIsNoLongerEnabled()
    {
        var disabledProvider = AIProvider.Create("openai", "OpenAI", "test");
        var disabledModel = MakeModel(disabledProvider.Id);
        _providers.GetByIdAsync(disabledProvider.Id, Arg.Any<CancellationToken>()).Returns(disabledProvider);
        _models.GetByIdAsync(disabledModel.Id, Arg.Any<CancellationToken>()).Returns(disabledModel);

        var fallbackProvider = AIProvider.Create("anthropic", "Anthropic", "test");
        fallbackProvider.SetCredential("ciphertext", "test");
        fallbackProvider.Enable("test");
        var fallbackModel = MakeModel(fallbackProvider.Id);
        fallbackProvider.SetDefaultModel(fallbackModel.Id, "test");

        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns([fallbackProvider]);
        _models.GetByIdAsync(fallbackModel.Id, Arg.Any<CancellationToken>()).Returns(fallbackModel);

        var preference = UserAiPreference.Create("user-1", "user-1");
        preference.SetDefaults(disabledProvider.Id, disabledModel.Id, null, "user-1");

        var result = await _resolver.ResolveAsync(preference, CancellationToken.None);

        result.ProviderId.Should().Be(fallbackProvider.Id);
        result.ModelId.Should().Be(fallbackModel.Id);
        result.IsPlatformDefault.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_ShouldFallBackToTheFirstAvailableModel_WhenNoEnabledProviderHasADefaultModelId()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = MakeModel(provider.Id);

        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns([provider]);
        _models.ListAvailableByProviderIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns([model]);

        var result = await _resolver.ResolveAsync(preference: null, CancellationToken.None);

        result.ProviderId.Should().Be(provider.Id);
        result.ModelId.Should().Be(model.Id);
        result.IsPlatformDefault.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_ShouldThrow_WhenNoEnabledProviderHasAnyAvailableModel()
    {
        _providers.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns([]);

        var act = () => _resolver.ResolveAsync(preference: null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
