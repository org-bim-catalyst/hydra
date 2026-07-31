using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Queries.GetAdminAiModels;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/008-ai-model-catalog-management T004 — FR-001: every model for a provider, any status; pricing is null, never a fabricated zero, when unset.</summary>
public sealed class GetAdminAiModelsQueryHandlerTests
{
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly GetAdminAiModelsQueryHandler _handler;

    public GetAdminAiModelsQueryHandlerTests()
    {
        _handler = new GetAdminAiModelsQueryHandler(_providers, _models);
    }

    private static AIModelCapabilities MakeCapabilities() =>
        new(true, true, true, true, false, false, true, false, false);

    [Fact]
    public async Task Handle_ShouldReturnEveryModel_RegardlessOfStatus()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        _providers.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);

        var available = AIModel.Create(provider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384, MakeCapabilities(), null, new ModelPricing(2.5m, 10m), "test");
        var deprecated = AIModel.Create(provider.Id, "gpt-3.5", "GPT-3.5", 16000, 4096, MakeCapabilities(), null, null, "test");
        deprecated.SetStatus(AIModelStatus.Deprecated, "test");

        _models.ListByProviderIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns([available, deprecated]);

        var result = await _handler.Handle(new GetAdminAiModelsQuery(provider.Id), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(m => m.ModelKey == "gpt-4.1" && m.Status == AIModelStatus.Available && m.Pricing != null);
        result.Should().Contain(m => m.ModelKey == "gpt-3.5" && m.Status == AIModelStatus.Deprecated && m.Pricing == null);
    }

    [Fact]
    public async Task Handle_ShouldThrowKeyNotFound_WhenProviderDoesNotExist()
    {
        _providers.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AIProvider?)null);

        var act = () => _handler.Handle(new GetAdminAiModelsQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
