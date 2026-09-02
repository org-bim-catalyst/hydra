using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/008-ai-model-catalog-management T016 — research.md Decision 1's matching rule,
/// covering all four catalog-status × vendor-listing combinations.
/// </summary>
public sealed class GetProviderModelSyncDiffQueryHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly AIProvider _provider;
    private readonly GetProviderModelSyncDiffQueryHandler _handler;

    private static readonly AIModelCapabilities Capabilities = new(true, true, true, true, false, false, true, false, false);

    public GetProviderModelSyncDiffQueryHandlerTests()
    {
        _provider = AIProvider.Create("openai", "OpenAI", "test");
        _providers.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _resolver.Resolve("openai").Returns(_aiProvider);
        _handler = new GetProviderModelSyncDiffQueryHandler(_providers, _models, _resolver);
    }

    private AIModel MakeCatalogModel(string modelKey, AIModelStatus status)
    {
        var model = AIModel.Create(_provider.Id, modelKey, modelKey.ToUpperInvariant(), 128000, 16384, Capabilities, null, null, "test");
        model.SetStatus(status, "test");
        return model;
    }

    [Fact]
    public async Task Handle_ShouldProposeAsAdded_WhenVendorListsAModelNotInTheCatalogAtAll()
    {
        _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns([]);
        _aiProvider.ListAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns([new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities)]);

        var result = await _handler.Handle(new GetProviderModelSyncDiffQuery(_provider.Id), CancellationToken.None);

        result.Added.Should().ContainSingle(m => m.ModelKey == "gpt-5");
        result.RemovedFromVendor.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldProposeAsRemovedFromVendor_WhenAnAvailableCatalogModelIsNoLongerListed()
    {
        var available = MakeCatalogModel("gpt-3.5", AIModelStatus.Available);
        _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns([available]);
        _aiProvider.ListAvailableModelsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.Handle(new GetProviderModelSyncDiffQuery(_provider.Id), CancellationToken.None);

        result.RemovedFromVendor.Should().ContainSingle(m => m.Id == available.Id);
        result.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldProposeNeither_WhenADeprecatedCatalogModelIsStillListedByTheVendor()
    {
        // Regression case for the first clarification: never re-propose a deliberately-deprecated model.
        var deprecated = MakeCatalogModel("gpt-3.5", AIModelStatus.Deprecated);
        _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns([deprecated]);
        _aiProvider.ListAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns([new ProviderModelInfo("gpt-3.5", "GPT-3.5", 16000, 4096, Capabilities)]);

        var result = await _handler.Handle(new GetProviderModelSyncDiffQuery(_provider.Id), CancellationToken.None);

        result.Added.Should().BeEmpty();
        result.RemovedFromVendor.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldProposeNeither_WhenAnUnavailableCatalogModelIsAlsoNoLongerListed()
    {
        // FR-006's explicit exclusion: an already-non-Available model is never surfaced on
        // the removed side either — re-flagging it would be redundant noise.
        var unavailable = MakeCatalogModel("gpt-3.5", AIModelStatus.Unavailable);
        _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns([unavailable]);
        _aiProvider.ListAvailableModelsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.Handle(new GetProviderModelSyncDiffQuery(_provider.Id), CancellationToken.None);

        result.Added.Should().BeEmpty();
        result.RemovedFromVendor.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCarryAbsentTokenLimitsThrough_AsNull()
    {
        // specs/043 FR-029: the diff an administrator reviews must show absence as absence, so
        // the apply step can accept it rather than rejecting a fabricated 0.
        _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns([]);
        _aiProvider.ListAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns([new ProviderModelInfo("gpt-4-turbo", "gpt-4-turbo", null, null, Capabilities)]);

        var diff = await _handler.Handle(new GetProviderModelSyncDiffQuery(_provider.Id), CancellationToken.None);

        diff.Added.Should().ContainSingle()
            .Which.Should().Match<ProviderModelInfo>(m => m.ContextWindowTokens == null && m.MaxOutputTokens == null);
    }
}
