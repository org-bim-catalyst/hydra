using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.ApplyProviderModelSync;
using AskLucy.Application.Ai.Queries.GetProviderModelSyncDiff;
using AskLucy.Domain.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/008-ai-model-catalog-management T017 — research.md Decision 2: each `added`
/// entry is created Unavailable (never Available); each `removedFromVendor` entry is
/// marked Unavailable; no row is ever deleted. specs/009-selective-model-sync-review T003
/// extends this for FR-007a/FR-007b: a stale row is skipped and reported instead of
/// rejecting the whole request, and everything that isn't stale still commits together in
/// one <see cref="IUnitOfWork.SaveChangesAsync"/> call.
/// </summary>
public sealed class ApplyProviderModelSyncCommandHandlerTests
{
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ApplyProviderModelSyncCommandHandler _handler;

    private static readonly AIModelCapabilities Capabilities = new(true, true, true, true, false, false, true, false, false);

    public ApplyProviderModelSyncCommandHandlerTests()
    {
        _currentUser.UserId.Returns("admin-1");
        _handler = new ApplyProviderModelSyncCommandHandler(
            _models, _unitOfWork, _currentUser, Substitute.For<ILogger<ApplyProviderModelSyncCommandHandler>>());

        _models.ListByProviderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Fact]
    public async Task Handle_ShouldCreateAddedModelsAsUnavailable_NeverAvailable()
    {
        var providerId = Guid.NewGuid();
        AIModel? created = null;
        _models.When(m => m.Add(Arg.Any<AIModel>())).Do(call => created = call.Arg<AIModel>());

        var command = new ApplyProviderModelSyncCommand(
            providerId,
            [new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities)],
            []);

        var result = await _handler.Handle(command, CancellationToken.None);

        created.Should().NotBeNull();
        created!.ModelKey.Should().Be("gpt-5");
        created.Status.Should().Be(AIModelStatus.Unavailable);
        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-5");
        result.Failed.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldMarkRemovedFromVendorModelsUnavailable_AndNeverDeleteThem()
    {
        var providerId = Guid.NewGuid();
        var existing = AIModel.Create(providerId, "gpt-3.5", "GPT-3.5", 16000, 4096, Capabilities, null, null, "test");
        _models.ListByProviderIdAsync(providerId, Arg.Any<CancellationToken>()).Returns([existing]);
        _models.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var command = new ApplyProviderModelSyncCommand(
            providerId, [], [new RemovedModelDto(existing.Id, existing.ModelKey, existing.DisplayName)]);

        var result = await _handler.Handle(command, CancellationToken.None);

        existing.Status.Should().Be(AIModelStatus.Unavailable);
        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-3.5");
        result.Failed.Should().BeEmpty();
        _models.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_ShouldApplyValidAddedEntries_AndReportStaleOnesAsFailed_InOneSaveChangesCall()
    {
        var providerId = Guid.NewGuid();
        var alreadyExists = AIModel.Create(providerId, "gpt-4-turbo", "GPT-4 Turbo", 128000, 4096, Capabilities, null, null, "test");
        _models.ListByProviderIdAsync(providerId, Arg.Any<CancellationToken>()).Returns([alreadyExists]);

        var command = new ApplyProviderModelSyncCommand(
            providerId,
            [
                new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities),
                new ProviderModelInfo("gpt-4-turbo", "GPT-4 Turbo", 128000, 4096, Capabilities),
            ],
            []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-5");
        result.Failed.Should().ContainSingle(f => f.ModelKey == "gpt-4-turbo" && f.Reason.Contains("stale"));
        _models.Received(1).Add(Arg.Is<AIModel>(m => m != null && m.ModelKey == "gpt-5"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldApplyValidRemovedFromVendorEntries_AndReportStaleOnesAsFailed()
    {
        var providerId = Guid.NewGuid();
        var belongsToProvider = AIModel.Create(providerId, "gpt-3.5", "GPT-3.5", 16000, 4096, Capabilities, null, null, "test");
        var staleId = Guid.NewGuid();
        _models.ListByProviderIdAsync(providerId, Arg.Any<CancellationToken>()).Returns([belongsToProvider]);
        _models.GetByIdAsync(belongsToProvider.Id, Arg.Any<CancellationToken>()).Returns(belongsToProvider);

        var command = new ApplyProviderModelSyncCommand(
            providerId,
            [],
            [
                new RemovedModelDto(belongsToProvider.Id, belongsToProvider.ModelKey, belongsToProvider.DisplayName),
                new RemovedModelDto(staleId, "gpt-old", "GPT Old"),
            ]);

        var result = await _handler.Handle(command, CancellationToken.None);

        belongsToProvider.Status.Should().Be(AIModelStatus.Unavailable);
        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-3.5");
        result.Failed.Should().ContainSingle(f => f.ModelKey == "gpt-old" && f.Reason.Contains("stale"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Live bug (2026-08-27): OpenAI's own ListAvailableModelsAsync always reports
    // ContextWindowTokens: 0 (that vendor's model-list endpoint carries no such metadata at
    // all), which AIModel.Create rejects with a DomainRuleViolationException — previously
    // unhandled here, aborting the ENTIRE batch (including otherwise-valid rows) instead of
    // reporting just that one row as failed, contradicting this handler's own "best-effort,
    // per row" documentation.
    [Fact]
    public async Task Handle_ShouldReportAZeroContextWindowRowAsFailed_WithoutAbortingOtherValidRowsInTheSameBatch()
    {
        var providerId = Guid.NewGuid();
        AIModel? created = null;
        _models.When(m => m.Add(Arg.Any<AIModel>())).Do(call => created = call.Arg<AIModel>());

        var command = new ApplyProviderModelSyncCommand(
            providerId,
            [
                new ProviderModelInfo("gpt-4-turbo", "GPT-4 Turbo", ContextWindowTokens: 0, MaxOutputTokens: 0, Capabilities),
                new ProviderModelInfo("gpt-5", "GPT-5", 200000, 32000, Capabilities),
            ],
            []);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-5");
        result.Failed.Should().ContainSingle(f => f.ModelKey == "gpt-4-turbo" && f.Reason.Contains("Context window"));
        created.Should().NotBeNull();
        created!.ModelKey.Should().Be("gpt-5");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldApplyEveryRow_WhenTheVendorPublishedNoTokenLimits()
    {
        // specs/043 SC-006. This is the reported defect end to end: OpenAI's list publishes no
        // token metadata for any model, the adapter substituted 0, and AIModel.Create rejected
        // it - so all ~97 rows were reported as failures on every sync, forever, with no edit
        // path to supply the figures either.
        var providerId = Guid.NewGuid();
        var added = new List<ProviderModelInfo>
        {
            new("gpt-4-turbo", "gpt-4-turbo", null, null, Capabilities),
            new("gpt-4o", "gpt-4o", null, null, Capabilities),
            new("gpt-4.1", "gpt-4.1", null, null, Capabilities),
        };

        var result = await _handler.Handle(new ApplyProviderModelSyncCommand(providerId, added, []), CancellationToken.None);

        result.AppliedModelKeys.Should().BeEquivalentTo(["gpt-4-turbo", "gpt-4o", "gpt-4.1"]);
        result.Failed.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldStillReportAGenuinelyStaleRow_WhileItsSiblingsApply()
    {
        // FR-031: per-row reporting survives - the change is only that an absent token limit is
        // no longer one of the things that makes a row fail.
        var providerId = Guid.NewGuid();
        var existing = AIModel.Create(providerId, "gpt-4o", "GPT-4o", 128_000, 16_384, Capabilities, null, null, "test");
        _models.ListByProviderIdAsync(providerId, Arg.Any<CancellationToken>()).Returns([existing]);

        var added = new List<ProviderModelInfo>
        {
            new("gpt-4o", "gpt-4o", null, null, Capabilities),      // stale: already in the catalog
            new("gpt-4-turbo", "gpt-4-turbo", null, null, Capabilities),
        };

        var result = await _handler.Handle(new ApplyProviderModelSyncCommand(providerId, added, []), CancellationToken.None);

        result.AppliedModelKeys.Should().ContainSingle().Which.Should().Be("gpt-4-turbo");
        result.Failed.Should().ContainSingle().Which.ModelKey.Should().Be("gpt-4o");
    }
}
