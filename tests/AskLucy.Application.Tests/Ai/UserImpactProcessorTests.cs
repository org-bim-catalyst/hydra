using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Deprecation;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>specs/069 FR-014/FR-017 research D4: switch items, one notice per owner, resume safely.</summary>
public sealed class UserImpactProcessorTests
{
    private static readonly AIModelCapabilities Caps = new(true, false, false, false, false, false, false, false, false);

    private readonly IModelDeprecationRepository _deprecations = Substitute.For<IModelDeprecationRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IModelReferenceStore _references = Substitute.For<IModelReferenceStore>();
    private readonly INotificationPublisher _notifications = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UserImpactProcessor _processor;

    public UserImpactProcessorTests() =>
        _processor = new UserImpactProcessor(_deprecations, _models, _references, _notifications, _unitOfWork);

    private (ModelDeprecationBatch Batch, AIModel Retired, AIModel Replacement) SetupBatch(UserImpactStatus status = UserImpactStatus.Pending)
    {
        var providerId = Guid.CreateVersion7();
        var retired = AIModel.Create(providerId, "gpt-4-turbo", "GPT-4 Turbo", 128_000, 4096, Caps, null, null, "test");
        var replacement = AIModel.Create(providerId, "gpt-5", "GPT-5", 128_000, 4096, Caps, null, null, "test");
        retired.Deprecate("admin");

        var batch = ModelDeprecationBatch.Deprecate(providerId, "admin", DateTime.UtcNow);
        batch.Add(ModelDeprecation.Create(batch.Id, retired.Id, replacement.Id, ReplacementSource.Proposed, "admin", DateTime.UtcNow));
        if (status == UserImpactStatus.Completed)
        {
            batch.MarkUserImpactCompleted(0, DateTime.UtcNow);
        }

        _deprecations.GetBatchAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _models.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        _references.ListOwnerIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(["user-1"]);
        _references.SwitchAsync(batch.Id, Arg.Any<IReadOnlyDictionary<Guid, RetiredModelTarget>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new OwnerSwitchResult("user-1",
                [
                    new OwnerSwitchItem(retired.Id, "GPT-4 Turbo", replacement.Id, "GPT-5", "My chat", ModelImpactKind.Conversation),
                    new OwnerSwitchItem(retired.Id, "GPT-4 Turbo", replacement.Id, "GPT-5", "My agent", ModelImpactKind.Agent),
                ]),
            ]);
        return (batch, retired, replacement);
    }

    [Fact]
    public async Task ProcessAsync_SwitchesItems_SendsOneNoticePerOwner_AndCompletes()
    {
        var (batch, _, _) = SetupBatch();

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        _notifications.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            r.Type == NotificationTypeKeys.SystemAiModelDeprecated
            && r.Recipient is NotificationRecipient.User
            && r.Variables["modelNames"] == "GPT-4 Turbo"
            && r.Variables["replacementSummary"] == "GPT-5"
            && r.Variables["itemSummary"]!.Contains("1 conversation")
            && r.Variables["itemSummary"]!.Contains("1 agent")));
        batch.UserImpactStatus.Should().Be(UserImpactStatus.Completed);
        batch.NotifiedUserCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_IsANoOp_WhenTheBatchAlreadyCompleted()
    {
        var (batch, _, _) = SetupBatch(UserImpactStatus.Completed);

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        await _references.DidNotReceiveWithAnyArgs().SwitchAsync(default, default!, default!, default!, default);
        _notifications.DidNotReceiveWithAnyArgs().Publish(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task ProcessAsync_DoesNotSwitchAnything_WhenNoDeprecationHasAReplacement()
    {
        var providerId = Guid.CreateVersion7();
        var retired = AIModel.Create(providerId, "gpt-4", "GPT-4", 128_000, 4096, Caps, null, null, "test");
        retired.Deprecate("admin");
        var batch = ModelDeprecationBatch.Deprecate(providerId, "admin", DateTime.UtcNow);
        batch.Add(ModelDeprecation.Create(batch.Id, retired.Id, null, ReplacementSource.None, "admin", DateTime.UtcNow));
        _deprecations.GetBatchAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        await _processor.ProcessAsync(batch.Id, TestContext.Current.CancellationToken);

        await _references.DidNotReceiveWithAnyArgs().SwitchAsync(default, default!, default!, default!, default);
        batch.UserImpactStatus.Should().Be(UserImpactStatus.Completed);
        batch.NotifiedUserCount.Should().Be(0);
    }
}
