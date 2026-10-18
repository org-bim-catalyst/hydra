using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

public sealed class ModelDeprecationDomainTests
{
    private static readonly Guid Provider = Guid.CreateVersion7();

    private static AIModelCapabilities Caps(bool streaming = true, bool vision = false, bool reasoning = false, bool imageOutput = false) =>
        new(streaming, vision, FunctionCalling: false, JsonMode: false, reasoning, Embeddings: false, ImageInput: false, imageOutput, Audio: false);

    private static AIModel Model(
        string key,
        AIModelCapabilities? caps = null,
        int? context = null,
        ModelPricing? pricing = null,
        DateOnly? released = null,
        Guid? provider = null,
        AIModelStatus status = AIModelStatus.Available)
    {
        var model = AIModel.Create(provider ?? Provider, key, key, context, null, caps ?? Caps(), released, pricing, "admin");
        if (status != AIModelStatus.Available)
        {
            model.SetStatus(status, "admin");
        }

        return model;
    }

    [Fact]
    public void Deprecate_MovesAnAvailableModelToDeprecated()
    {
        var model = Model("a");

        model.Deprecate("admin");

        model.Status.Should().Be(AIModelStatus.Deprecated);
    }

    [Theory]
    [InlineData(AIModelStatus.Unavailable)]
    [InlineData(AIModelStatus.Deprecated)]
    public void Deprecate_RefusesAnythingButAvailable(AIModelStatus from)
    {
        var model = Model("a", status: from);

        var act = () => model.Deprecate("admin");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Rank_ReturnsOnlySameProviderAvailableModelsThatCoverEveryRetiredCapability()
    {
        var retired = Model("retired", Caps(reasoning: true));
        var sameProviderCovers = Model("covers", Caps(reasoning: true));
        var missingReasoning = Model("no-reasoning", Caps(reasoning: false));
        var otherProvider = Model("other", Caps(reasoning: true), provider: Guid.CreateVersion7());
        var unavailable = Model("unavailable", Caps(reasoning: true), status: AIModelStatus.Unavailable);

        var ranked = ReplacementModelSelector.Rank(
            retired,
            [retired, sameProviderCovers, missingReasoning, otherProvider, unavailable],
            new HashSet<Guid>());

        ranked.Select(m => m.ModelKey).Should().Equal("covers");
    }

    [Fact]
    public void Rank_ExcludesModelsInTheSameConfirmation()
    {
        var retired = Model("retired");
        var batchmate = Model("batchmate");

        var ranked = ReplacementModelSelector.Rank(retired, [batchmate], new HashSet<Guid> { batchmate.Id });

        ranked.Should().BeEmpty();
    }

    [Fact]
    public void Rank_OrdersByPriceDistanceThenPricedBeforeUnpriced()
    {
        var retired = Model("retired", pricing: new ModelPricing(10m, 30m));
        var near = Model("near", pricing: new ModelPricing(9m, 29m));
        var far = Model("far", pricing: new ModelPricing(1m, 3m));
        var unpriced = Model("unpriced");

        var ranked = ReplacementModelSelector.Rank(retired, [unpriced, far, near], new HashSet<Guid>());

        ranked.Select(m => m.ModelKey).Should().Equal("near", "far", "unpriced");
    }

    [Fact]
    public void Rank_IsDeterministicOnTies()
    {
        var retired = Model("retired");
        var b = Model("b");
        var a = Model("a");

        var ranked = ReplacementModelSelector.Rank(retired, [b, a], new HashSet<Guid>());

        ranked.Select(m => m.ModelKey).Should().Equal("a", "b");
    }

    [Fact]
    public void MissingCapabilities_ListsWhatTheCandidateLacks()
    {
        var retired = Model("retired", Caps(vision: true, reasoning: true));
        var candidate = Model("candidate", Caps(vision: true));

        ReplacementModelSelector.MissingCapabilities(retired, candidate).Should().Equal(nameof(AIModel.SupportsReasoning));
    }

    [Fact]
    public void Satisfies_RequiresImageOutputForImageGeneration()
    {
        Model("img", Caps(imageOutput: true)).Should().Match<AIModel>(m => PlatformFunctionRequirements.Satisfies(AiCapability.ImageGeneration, m));
        Model("chat", Caps(imageOutput: false)).Should().Match<AIModel>(m => !PlatformFunctionRequirements.Satisfies(AiCapability.ImageGeneration, m));
    }

    [Fact]
    public void Batch_CompletesImpactOnlyOnce()
    {
        var batch = ModelDeprecationBatch.Deprecate(Provider, "admin", DateTime.UtcNow);

        batch.MarkUserImpactCompleted(3, DateTime.UtcNow);

        batch.UserImpactStatus.Should().Be(UserImpactStatus.Completed);
        var again = () => batch.MarkUserImpactCompleted(4, DateTime.UtcNow);
        again.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Deprecation_RejectsSelfReplacement()
    {
        var id = Guid.CreateVersion7();

        var act = () => ModelDeprecation.Create(Guid.CreateVersion7(), id, id, ReplacementSource.Proposed, "admin", DateTime.UtcNow);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Deprecation_WithoutReplacementIsNotApplicable()
    {
        var deprecation = ModelDeprecation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), null, ReplacementSource.None, "admin", DateTime.UtcNow);

        deprecation.ValidationStatus.Should().Be(ReplacementValidationStatus.NotApplicable);
    }

    [Fact]
    public void Deprecation_ValidationAcceptsOnlyWhileAwaiting()
    {
        var deprecation = ModelDeprecation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), ReplacementSource.Proposed, "admin", DateTime.UtcNow);

        deprecation.Accept("admin", DateTime.UtcNow);

        deprecation.ValidationStatus.Should().Be(ReplacementValidationStatus.Accepted);
        var again = () => deprecation.ChangeReplacement(Guid.CreateVersion7(), "admin", DateTime.UtcNow);
        again.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void FlaggedDefault_CloseOnlyOnce()
    {
        var flag = PlatformDefaultChange.Flag(
            DefaultTargetKind.ProviderDefault, Provider, null, Guid.CreateVersion7(),
            DefaultFlagReason.NoEligibleReplacement, null, null, "admin", DateTime.UtcNow);
        flag.IsOpenFlag.Should().BeTrue();

        flag.Resolve(Guid.CreateVersion7(), "admin", DateTime.UtcNow);

        flag.IsOpenFlag.Should().BeFalse();
        var again = () => flag.Resolve(Guid.CreateVersion7(), "admin", DateTime.UtcNow);
        again.Should().Throw<DomainRuleViolationException>();
    }
}
