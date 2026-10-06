using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Memory;
using NSubstitute;

namespace AskLucy.Infrastructure.Tests.Memory;

/// <summary>T079 (specs/067) — <see cref="MemoryNotifier"/> publishes through <see cref="INotificationPublisher"/> only.</summary>
public sealed class MemoryNotifierTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private MemoryNotifier CreateSut() => new(_publisher);

    [Theory]
    [InlineData(MemoryNotificationEventType.AutoCreated, NotificationTypeKeys.MemoryAutoCreated)]
    [InlineData(MemoryNotificationEventType.AutoApproved, NotificationTypeKeys.MemoryAutoApproved)]
    [InlineData(MemoryNotificationEventType.ConflictNeedsConfirmation, NotificationTypeKeys.MemoryConflictConfirmationNeeded)]
    public async Task NotifyAsync_ShouldPublishTheMappedCatalogKey_ForTheOwningUser(MemoryNotificationEventType eventType, string expectedType)
    {
        var sut = CreateSut();
        var memoryId = Guid.CreateVersion7();

        await sut.NotifyAsync("user-1", memoryId, eventType, "A new memory was recorded.", TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == expectedType &&
            ((NotificationRecipient.User)r.Recipient).UserId == "user-1" &&
            r.Variables["memorySummary"] == "A new memory was recorded." &&
            r.RelatedItem == new RelatedItem("Memory", memoryId.ToString()) &&
            r.EventKey == $"memory:{memoryId}:{eventType}"));
    }

    [Fact]
    public async Task NotifyAsync_ShouldOmitRelatedItem_WhenMemoryIdIsNull()
    {
        var sut = CreateSut();

        await sut.NotifyAsync("user-2", null, MemoryNotificationEventType.AutoCreated, "Summary.", TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.RelatedItem == null && r.EventKey == $"memory:none:{MemoryNotificationEventType.AutoCreated}"));
    }
}
