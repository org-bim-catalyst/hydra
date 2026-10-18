using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Domain.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/069 FR-001, FR-006..FR-011 and research D1/D2/D5: deprecation, replacement and platform-default moves.</summary>
public sealed class ModelDeprecationServiceTests
{
    private static readonly AIModelCapabilities Caps = new(true, false, false, false, false, false, false, false, false);
    private static readonly AIModelCapabilities ImageCaps = new(true, false, false, false, false, false, false, true, false);

    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAiCapabilityAssignmentRepository _assignments = Substitute.For<IAiCapabilityAssignmentRepository>();
    private readonly IModelDeprecationRepository _deprecations = Substitute.For<IModelDeprecationRepository>();
    private readonly ModelDeprecationService _service;
    private ModelDeprecationBatch? _added;
    private readonly AIProvider _provider;

    public ModelDeprecationServiceTests()
    {
        _service = new ModelDeprecationService(_models, _providers, _assignments, _deprecations);
        _provider = AIProvider.Create("openai", "OpenAI", "test");
        _providers.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _assignments.ListAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _deprecations.When(d => d.Add(Arg.Any<ModelDeprecationBatch>())).Do(call => _added = call.Arg<ModelDeprecationBatch>());
    }

    private AIModel Add(string key, AIModelCapabilities? caps = null, Guid? provider = null, AIModelStatus status = AIModelStatus.Available)
    {
        var model = AIModel.Create(provider ?? _provider.Id, key, key, 128_000, 4096, caps ?? Caps, null, new ModelPricing(5m, 15m), "test");
        if (status != AIModelStatus.Available)
        {
            model.SetStatus(status, "test");
        }

        return model;
    }

    private void Catalog(params AIModel[] models) => _models.ListByProviderIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(models.ToList());

    [Fact]
    public async Task ProposedReplacement_DeprecatesTheModel_AndReassignsTheProviderDefault()
    {
        var retired = Add("gpt-4-turbo");
        var replacement = Add("gpt-5");
        Catalog(retired, replacement);
        _provider.SetDefaultModel(retired.Id, "test");

        var result = await _service.DeprecateAsync(_provider.Id,
            [new DeprecationCandidate(retired, ReplacementChoice.Proposed)], "admin-1", TestContext.Current.CancellationToken);

        retired.Status.Should().Be(AIModelStatus.Deprecated);
        result.DeprecatedModelKeys.Should().Equal("gpt-4-turbo");
        result.Failed.Should().BeEmpty();
        _provider.DefaultModelId.Should().Be(replacement.Id);
        _deprecations.Received(1).Add(Arg.Any<ModelDeprecationBatch>());
        var record = _added!.Deprecations.Single();
        record.ReplacementModelId.Should().Be(replacement.Id);
        record.ValidationStatus.Should().Be(ReplacementValidationStatus.AwaitingValidation);
    }

    [Fact]
    public async Task NoEligibleReplacement_ClearsAndFlagsTheProviderDefault()
    {
        var retired = Add("gpt-4-turbo");
        Catalog(retired);
        _provider.SetDefaultModel(retired.Id, "test");

        var result = await _service.DeprecateAsync(_provider.Id,
            [new DeprecationCandidate(retired, ReplacementChoice.Proposed)], "admin-1", TestContext.Current.CancellationToken);

        result.DeprecatedModelKeys.Should().Equal("gpt-4-turbo");
        _provider.DefaultModelId.Should().BeNull();
        _deprecations.Received(1).Add(Arg.Any<ModelDeprecationBatch>());
        var record = _added!.Deprecations.Single();
        record.ValidationStatus.Should().Be(ReplacementValidationStatus.NotApplicable);
        record.DefaultChanges.Single().Outcome.Should().Be(DefaultChangeOutcome.Flagged);
        record.DefaultChanges.Single().FlagReason.Should().Be(DefaultFlagReason.NoEligibleReplacement);
    }

    [Fact]
    public async Task ImageGenerationAssignment_IsRemovedWhenNoReplacementExists()
    {
        var retired = Add("gpt-image-1", ImageCaps);
        Catalog(retired);
        var assignment = AiCapabilityAssignment.Create(AiCapability.ImageGeneration, _provider.Id, retired.Id, "test");
        _assignments.ListAllAsync(Arg.Any<CancellationToken>()).Returns([assignment]);

        await _service.DeprecateAsync(_provider.Id, [new DeprecationCandidate(retired, ReplacementChoice.Proposed)], "admin-1", TestContext.Current.CancellationToken);

        _assignments.Received(1).Remove(assignment);
    }

    [Fact]
    public async Task AdminChosenReplacement_LackingACapability_FailsOnlyThatRow_AndLeavesTheModelAvailable()
    {
        var retired = Add("gpt-4o", new AIModelCapabilities(true, true, false, false, false, false, false, false, false));
        var textOnly = Add("gpt-3.5", Caps);
        Catalog(retired, textOnly);

        var result = await _service.DeprecateAsync(_provider.Id,
            [new DeprecationCandidate(retired, new ReplacementChoice(ReplacementChoiceKind.Model, textOnly.Id))], "admin-1", TestContext.Current.CancellationToken);

        result.DeprecatedModelKeys.Should().BeEmpty();
        result.Failed.Should().ContainSingle(f => f.ModelKey == "gpt-4o" && f.Reason.Contains("SupportsVision"));
        retired.Status.Should().Be(AIModelStatus.Available);
        _deprecations.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task ReplacementBeingDeprecatedInTheSameConfirmation_IsNeverChosen()
    {
        var first = Add("gpt-4-turbo");
        var second = Add("gpt-4");
        Catalog(first, second);

        var result = await _service.DeprecateAsync(_provider.Id,
            [
                new DeprecationCandidate(first, new ReplacementChoice(ReplacementChoiceKind.Model, second.Id)),
                new DeprecationCandidate(second, ReplacementChoice.Proposed),
            ], "admin-1", TestContext.Current.CancellationToken);

        result.Failed.Should().ContainSingle(f => f.ModelKey == "gpt-4-turbo" && f.Reason.Contains("same confirmation"));
        result.DeprecatedModelKeys.Should().Equal("gpt-4");
        first.Status.Should().Be(AIModelStatus.Available);
    }

    [Fact]
    public async Task ANonAvailableModel_FailsItsRow_WithoutChangingAnything()
    {
        var unavailable = Add("old", status: AIModelStatus.Unavailable);
        Catalog(unavailable);

        var result = await _service.DeprecateAsync(_provider.Id,
            [new DeprecationCandidate(unavailable, ReplacementChoice.Proposed)], "admin-1", TestContext.Current.CancellationToken);

        result.DeprecatedModelKeys.Should().BeEmpty();
        result.Failed.Should().ContainSingle(f => f.ModelKey == "old");
        unavailable.Status.Should().Be(AIModelStatus.Unavailable);
    }

    [Fact]
    public async Task NoCandidates_ReturnsEmptyAndWritesNoBatch()
    {
        var result = await _service.DeprecateAsync(_provider.Id, [], "admin-1", TestContext.Current.CancellationToken);

        result.DeprecatedModelKeys.Should().BeEmpty();
        _deprecations.DidNotReceiveWithAnyArgs().Add(default!);
    }
}
