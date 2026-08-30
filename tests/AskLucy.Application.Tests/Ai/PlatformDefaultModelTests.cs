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
/// A provider's default model is what any capability assigned to that provider runs on, so a
/// default naming another provider's model — or one that is not Available — stores a setting the
/// resolver silently skips. Rejected at the boundary instead.
/// <para>
/// The "effective platform default" this file also used to cover is gone: an administrator now
/// assigns a provider per capability explicitly, so surfacing an implicit alphabetical winner
/// only described a fallback nobody configures against.
/// </para>
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
