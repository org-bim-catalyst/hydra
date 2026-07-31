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
        _models.Received(1).Add(Arg.Is<AIModel>(m => m.ModelKey == "gpt-5"));
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
}
