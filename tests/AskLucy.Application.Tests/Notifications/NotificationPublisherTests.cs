using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

public sealed class NotificationPublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    private readonly INotificationOutboxStore _outbox = Substitute.For<INotificationOutboxStore>();
    private readonly ICorrelationIdAccessor _correlation = Substitute.For<ICorrelationIdAccessor>();
    private readonly List<NotificationOutboxEvent> _added = [];

    public NotificationPublisherTests()
    {
        _outbox.When(o => o.Add(Arg.Any<NotificationOutboxEvent>())).Do(c => _added.Add(c.Arg<NotificationOutboxEvent>()!));
        _correlation.Current.Returns("corr-1");
    }

    private NotificationPublisher Publisher => new(_outbox, _correlation, new FakeTimeProvider(Now));

    private static NotificationRequest DocumentFailed(NotificationRecipient recipient, IReadOnlyDictionary<string, string?>? variables = null) => new(
        NotificationTypeKeys.DocumentProcessingFailed,
        recipient,
        variables ?? new Dictionary<string, string?> { ["documentName"] = "Tower A.pdf", ["failureSummary"] = "OCR timed out" },
        new RelatedItem("document", "doc-1"),
        EventKey: "document:doc-1:processing-failed");

    [Fact]
    public void Publish_AddsOneOutboxEvent_WithTheRequestSerialized()
    {
        Publisher.Publish(DocumentFailed(new NotificationRecipient.User("user-1")));

        var ev = _added.Should().ContainSingle().Subject;
        ev.Type.Should().Be(NotificationTypeKeys.DocumentProcessingFailed);
        ev.Status.Should().Be(OutboxEventStatus.Pending);
        ev.EventKey.Should().Be("document:doc-1:processing-failed");
        ev.RelatedItemType.Should().Be("document");
        ev.RelatedItemId.Should().Be("doc-1");
        ev.OccurredAtUtc.Should().Be(Now.UtcDateTime);
        ev.VariablesJson.Should().Contain("Tower A.pdf");
        NotificationRecipientJson.Deserialize(ev.RecipientJson).Should().Be(new NotificationRecipient.User("user-1"));
    }

    [Fact]
    public void Publish_OnlyAddsTheEvent_TheEmittersOwnSaveCommitsIt()
    {
        Publisher.Publish(DocumentFailed(new NotificationRecipient.User("user-1")));

        // No I/O: one Add, no claim or read against the store (research R2).
        _outbox.ReceivedCalls().Should().ContainSingle(c => c.GetMethodInfo().Name == nameof(INotificationOutboxStore.Add));
    }

    [Fact]
    public void Publish_CapturesTheAmbientCorrelationId()
    {
        Publisher.Publish(DocumentFailed(new NotificationRecipient.User("user-1")));

        _added.Single().CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public void Publish_WithNoAmbientCorrelationId_FallsBackToTheCurrentTrace()
    {
        _correlation.Current.Returns((string?)null);
        using var activity = new Activity("test").Start();

        Publisher.Publish(DocumentFailed(new NotificationRecipient.User("user-1")));

        _added.Single().CorrelationId.Should().Be(activity.TraceId.ToHexString());
    }

    [Fact]
    public void Publish_WithNoCorrelationOrTrace_StillStampsAnId()
    {
        _correlation.Current.Returns((string?)null);
        Activity.Current = null;

        Publisher.Publish(DocumentFailed(new NotificationRecipient.User("user-1")));

        Guid.TryParse(_added.Single().CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public void Publish_UnknownType_Throws()
    {
        var request = DocumentFailed(new NotificationRecipient.User("user-1")) with { Type = "document.exploded" };

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<ArgumentException>().WithMessage("*document.exploded*");
        _added.Should().BeEmpty();
    }

    [Fact]
    public void Publish_TypeWithNoEmitter_Throws()
    {
        var request = new NotificationRequest(
            NotificationTypeKeys.KnowledgeBaseUpdated,
            new NotificationRecipient.User("user-1"),
            new Dictionary<string, string?>(),
            new RelatedItem("knowledge-base", "kb-1"));

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Publish_UndeclaredVariable_Throws()
    {
        var request = DocumentFailed(new NotificationRecipient.User("user-1"), new Dictionary<string, string?> { ["password"] = "x" });

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<ArgumentException>().WithMessage("*password*");
    }

    [Fact]
    public void Publish_StandardVariable_Throws_BecauseTheHubFillsIt()
    {
        var request = DocumentFailed(new NotificationRecipient.User("user-1"), new Dictionary<string, string?> { ["actionUrl"] = "https://evil.example" });

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<ArgumentException>().WithMessage("*actionUrl*");
    }

    [Fact]
    public void Publish_MoreThan100Users_Throws()
    {
        var ids = Enumerable.Range(0, NotificationRecipient.MaxUsers + 1).Select(i => $"user-{i}").ToList();

        Publisher.Invoking(p => p.Publish(DocumentFailed(new NotificationRecipient.Users(ids)))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Publish_Exactly100Users_IsAccepted()
    {
        var ids = Enumerable.Range(0, NotificationRecipient.MaxUsers).Select(i => $"user-{i}").ToList();

        Publisher.Publish(DocumentFailed(new NotificationRecipient.Users(ids)));

        _added.Should().ContainSingle();
    }

    [Fact]
    public void Publish_AudienceForANonAnnouncementType_Throws()
    {
        var request = DocumentFailed(new NotificationRecipient.Audience(AllActiveUsers: true, []));

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<ArgumentException>().WithMessage("*announcement*");
    }

    [Fact]
    public void Publish_ItemAccessTypeWithoutARelatedItem_Throws()
    {
        var request = DocumentFailed(new NotificationRecipient.User("user-1")) with { RelatedItem = null };

        Publisher.Invoking(p => p.Publish(request)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Publish_BlankUserId_Throws(string userId)
    {
        Publisher.Invoking(p => p.Publish(DocumentFailed(new NotificationRecipient.User(userId)))).Should().Throw<ArgumentException>();
    }
}
