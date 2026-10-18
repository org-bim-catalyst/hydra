using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/069 FR-014/FR-015 research D3: redirect a retired model to its replacement chain, or fail loudly.</summary>
public sealed class ExecutableModelResolverTests
{
    private static readonly AIModelCapabilities Caps = new(true, false, false, false, false, false, false, false, false);

    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IModelDeprecationRepository _deprecations = Substitute.For<IModelDeprecationRepository>();
    private readonly ExecutableModelResolver _resolver;

    public ExecutableModelResolverTests() => _resolver = new ExecutableModelResolver(_models, _deprecations);

    private static AIModel Model(string key, AIModelStatus status = AIModelStatus.Available)
    {
        var model = AIModel.Create(Guid.CreateVersion7(), key, key, 128_000, 4096, Caps, null, null, "test");
        if (status == AIModelStatus.Deprecated)
        {
            model.Deprecate("test");
        }
        else if (status != AIModelStatus.Available)
        {
            model.SetStatus(status, "test");
        }

        return model;
    }

    private void RetiredTo(AIModel retired, AIModel? replacement)
    {
        var record = ModelDeprecation.Create(Guid.CreateVersion7(), retired.Id, replacement?.Id, ReplacementSource.Proposed, "admin", DateTime.UtcNow);
        _deprecations.GetByModelIdAsync(retired.Id, Arg.Any<CancellationToken>()).Returns(record);
    }

    [Fact]
    public async Task AvailableModel_ResolvesToItself()
    {
        var model = Model("gpt-5");

        var result = await _resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        result.Model.Should().BeSameAs(model);
        result.RetiredModelId.Should().BeNull();
    }

    [Fact]
    public async Task UnavailableModel_IsNotRedirected()
    {
        var model = Model("gpt-5", AIModelStatus.Unavailable);

        var result = await _resolver.ResolveAsync(model, TestContext.Current.CancellationToken);

        result.Model.Should().BeSameAs(model);
    }

    [Fact]
    public async Task DeprecatedModel_ResolvesToItsAvailableReplacement()
    {
        var retired = Model("gpt-4-turbo", AIModelStatus.Deprecated);
        var replacement = Model("gpt-5");
        RetiredTo(retired, replacement);
        _models.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);

        var result = await _resolver.ResolveAsync(retired, TestContext.Current.CancellationToken);

        result.Model.Should().BeSameAs(replacement);
        result.RetiredModelId.Should().Be(retired.Id);
    }

    [Fact]
    public async Task ChainedDeprecation_FollowsTheChainToTheAvailableModel()
    {
        var a = Model("a", AIModelStatus.Deprecated);
        var b = Model("b", AIModelStatus.Deprecated);
        var c = Model("c");
        RetiredTo(a, b);
        RetiredTo(b, c);
        _models.GetByIdAsync(b.Id, Arg.Any<CancellationToken>()).Returns(b);
        _models.GetByIdAsync(c.Id, Arg.Any<CancellationToken>()).Returns(c);

        var result = await _resolver.ResolveAsync(a, TestContext.Current.CancellationToken);

        result.Model.Should().BeSameAs(c);
        result.RetiredModelId.Should().Be(a.Id);
    }

    [Fact]
    public async Task DeprecatedModelWithNoReplacement_Throws()
    {
        var retired = Model("gpt-4-turbo", AIModelStatus.Deprecated);
        RetiredTo(retired, null);

        var act = () => _resolver.ResolveAsync(retired, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<ModelRetiredException>()).Which;
        ex.UserSafeMessage.Should().Contain("gpt-4-turbo").And.Contain("Choose another model");
    }

    [Fact]
    public async Task ACycle_ThrowsInsteadOfLooping()
    {
        var a = Model("a", AIModelStatus.Deprecated);
        var b = Model("b", AIModelStatus.Deprecated);
        RetiredTo(a, b);
        RetiredTo(b, a);
        _models.GetByIdAsync(b.Id, Arg.Any<CancellationToken>()).Returns(b);
        _models.GetByIdAsync(a.Id, Arg.Any<CancellationToken>()).Returns(a);

        var act = () => _resolver.ResolveAsync(a, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ModelRetiredException>();
    }
}
