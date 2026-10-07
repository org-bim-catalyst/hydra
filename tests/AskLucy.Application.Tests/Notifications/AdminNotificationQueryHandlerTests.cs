using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Queries.GetNotificationChannels;
using AskLucy.Application.Notifications.Queries.GetNotificationDeliveries;
using AskLucy.Application.Notifications.Queries.GetNotificationDelivery;
using AskLucy.Application.Notifications.Queries.GetNotificationStatistics;
using AskLucy.Application.Notifications.Queries.GetSystemAnnouncements;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>specs/067 US6 — what the admin queries return and refuse: the 90-day range, masking, the retry verdict and the safe summaries.</summary>
public sealed class AdminNotificationQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly INotificationAdminRepository _repository = Substitute.For<INotificationAdminRepository>();
    private readonly FakeTimeProvider _time = new(Now);

    private static NotificationStatisticsData Data(int emailSent = 0, int emailFailed = 0, DateTime? oldestDue = null) => new(
        Created: 10, Sent: 5, Failed: 1, DeadLettered: 0, Ambiguous: 0, Retries: 2, EmailSent: emailSent, EmailFailed: emailFailed,
        AverageLatencyMs: 1200, P95LatencyMs: 3000, OutboxPending: 1, DeliveriesDue: 2, OldestDueAtUtc: oldestDue, UnreadNotifications: 4,
        ByCategory: [new CategoryCountRow(NotificationCategory.Workflow, 10, 1)], Series: [new SeriesRow(Now.UtcDateTime, 10, 5, 1)]);

    // ---- statistics ----

    [Fact]
    public async Task Statistics_DefaultToTheLastSevenDays_WithHourlyBucketsOnlyForTwoDaysOrLess()
    {
        _repository.GetStatisticsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Data());
        var handler = new GetNotificationStatisticsQueryHandler(_repository, _time);

        await handler.Handle(new GetNotificationStatisticsQuery(), TestContext.Current.CancellationToken);
        await handler.Handle(new GetNotificationStatisticsQuery(Now.UtcDateTime.AddHours(-48), Now.UtcDateTime), TestContext.Current.CancellationToken);
        await handler.Handle(new GetNotificationStatisticsQuery(Now.UtcDateTime.AddHours(-49), Now.UtcDateTime), TestContext.Current.CancellationToken);

        await _repository.Received(1).GetStatisticsAsync(Now.UtcDateTime.AddDays(-7), Now.UtcDateTime, false, Now.UtcDateTime, Arg.Any<CancellationToken>());
        await _repository.Received(1).GetStatisticsAsync(Now.UtcDateTime.AddHours(-48), Now.UtcDateTime, true, Now.UtcDateTime, Arg.Any<CancellationToken>());
        await _repository.Received(1).GetStatisticsAsync(Now.UtcDateTime.AddHours(-49), Now.UtcDateTime, false, Now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Statistics_RefuseARangeOfMoreThanNinetyDays_AndOneThatEndsBeforeItStarts()
    {
        var handler = new GetNotificationStatisticsQueryHandler(_repository, _time);
        var to = Now.UtcDateTime;

        var tooLong = () => handler.Handle(new GetNotificationStatisticsQuery(to.AddDays(-91), to), TestContext.Current.CancellationToken);
        var ninety = () => handler.Handle(new GetNotificationStatisticsQuery(to.AddDays(-90), to), TestContext.Current.CancellationToken);
        var backwards = () => handler.Handle(new GetNotificationStatisticsQuery(to, to.AddDays(-1)), TestContext.Current.CancellationToken);
        var loneFrom = () => handler.Handle(new GetNotificationStatisticsQuery(to.AddDays(-100), null), TestContext.Current.CancellationToken);
        _repository.GetStatisticsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Data());

        await tooLong.Should().ThrowAsync<FluentValidation.ValidationException>();
        await backwards.Should().ThrowAsync<FluentValidation.ValidationException>();
        await loneFrom.Should().ThrowAsync<FluentValidation.ValidationException>();
        await ninety.Should().NotThrowAsync("ninety days is the maximum, not beyond it");
    }

    [Fact]
    public void TheStatisticsValidator_AppliesTheSameNinetyDayRule_WithoutTheHandler()
    {
        var validator = new GetNotificationStatisticsQueryValidator();
        var to = Now.UtcDateTime;

        validator.Validate(new GetNotificationStatisticsQuery(to.AddDays(-91), to)).IsValid.Should().BeFalse();
        validator.Validate(new GetNotificationStatisticsQuery(to.AddDays(-90), to)).IsValid.Should().BeTrue();
        validator.Validate(new GetNotificationStatisticsQuery(to, to)).IsValid.Should().BeFalse();
        validator.Validate(new GetNotificationStatisticsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Statistics_ComputeTheEmailSuccessRateAndTheBacklogAge()
    {
        _repository.GetStatisticsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Data(emailSent: 989, emailFailed: 11, oldestDue: Now.UtcDateTime.AddSeconds(-90)));

        var dto = await new GetNotificationStatisticsQueryHandler(_repository, _time).Handle(new GetNotificationStatisticsQuery(), TestContext.Current.CancellationToken);

        dto.EmailSuccessRate.Should().Be(0.989);
        dto.Backlog.Should().Be(new NotificationBacklogDto(1, 2, 90));
        dto.AverageDeliveryLatencyMs.Should().Be(1200);
    }

    [Fact]
    public async Task Statistics_HaveNoSuccessRate_WhenNoEmailWasAttempted()
    {
        _repository.GetStatisticsAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Data());

        var dto = await new GetNotificationStatisticsQueryHandler(_repository, _time).Handle(new GetNotificationStatisticsQuery(), TestContext.Current.CancellationToken);

        dto.EmailSuccessRate.Should().BeNull("no email is not a success rate of zero");
        dto.Backlog.OldestDueAgeSeconds.Should().Be(0);
    }

    [Fact]
    public void TheDashboardQueries_AreAuditedViews_OfTheirOwnResource()
    {
        new GetNotificationStatisticsQuery().AuditAction.Should().Be(NotificationAuditAction.StatisticsViewed);
        new GetNotificationChannelsQuery().AuditAction.Should().Be(NotificationAuditAction.ChannelsViewed);
        new GetNotificationDeliveriesQuery().AuditAction.Should().Be(NotificationAuditAction.DeliveriesViewed);
        var id = Guid.NewGuid();
        var one = new GetNotificationDeliveryQuery(id);
        one.AuditAction.Should().Be(NotificationAuditAction.DeliveryViewed);
        one.AuditTargetId.Should().Be(id.ToString());
    }

    // ---- masking ----

    [Theory]
    [InlineData("layla@bimcatalyst.com", "l•••@bimcatalyst.com")]
    [InlineData("x@y.io", "x•••@y.io")]
    [InlineData("not-an-address", "•••")]
    [InlineData("@nolocal.com", "•••")]
    public void AnAddress_ShowsTheFirstLetterAndTheDomain_Only(string address, string expected) =>
        AdminAddressMask.Mask(address).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoAddress_IsNoMask(string? address) => AdminAddressMask.Mask(address).Should().BeNull();

    [Theory]
    [InlineData("Layla Hassan", "L. H.")]
    [InlineData("layla", "L.")]
    [InlineData("  Ada   King  Lovelace ", "A. K. L.")]
    public void ANameIsShownAsInitials(string name, string expected) => AdminAddressMask.Initials(name).Should().Be(expected);

    // ---- deliveries ----

    private static AdminDeliveryRow Row(
        DeliveryStatus status = DeliveryStatus.DeadLettered,
        RecipientKind kind = RecipientKind.User,
        NotificationCategory category = NotificationCategory.Workflow,
        bool notificationDeleted = false,
        DateTime? notificationExpires = null,
        bool recipientDeleted = false) => new(
        Guid.NewGuid(), Guid.NewGuid(), NotificationTypeKeys.WorkflowExecutionFailed, category, NotificationChannel.Email, status,
        DeliveryFailureKind.RetryLimitReached, "Rejected.", "451 4.3.0", 5, Now.UtcDateTime, null, kind, "user-1", "Layla Hassan", "layla@bimcatalyst.com",
        recipientDeleted, "corr-1", "Workflow failed", "en", null, Now.UtcDateTime.AddDays(-1), notificationDeleted, notificationExpires, null);

    [Fact]
    public async Task Deliveries_DefaultToFailedAndDeadLettered_AndTheLimitIsPassedThrough()
    {
        _repository.ListDeliveriesAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AdminDeliveryRow>)[Row()], (string?)"next"));

        var page = await new GetNotificationDeliveriesQueryHandler(_repository, _time).Handle(new GetNotificationDeliveriesQuery(Limit: 25), TestContext.Current.CancellationToken);

        page.NextCursor.Should().Be("next");
        await _repository.Received(1).ListDeliveriesAsync(
            Arg.Is<AdminDeliveryFilter>(f => f != null && f.Statuses.SequenceEqual(new[] { DeliveryStatus.Failed, DeliveryStatus.DeadLettered })), null, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADelivery_ShowsAMaskedAddress_AndInitials_AndIsRetryable()
    {
        _repository.ListDeliveriesAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AdminDeliveryRow>)[Row()], (string?)null));

        var item = (await new GetNotificationDeliveriesQueryHandler(_repository, _time).Handle(new GetNotificationDeliveriesQuery(), TestContext.Current.CancellationToken)).Items.Single();

        item.Recipient.Should().Be(new AdminRecipientDto(RecipientKind.User, "user-1", "L. H.", "l•••@bimcatalyst.com"));
        item.Retryable.Should().BeTrue();
        item.NotRetryableReason.Should().BeNull();
    }

    [Fact]
    public async Task TheSupportMailbox_NeverHasAnAddress()
    {
        _repository.ListDeliveriesAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AdminDeliveryRow>)[Row(kind: RecipientKind.SupportMailbox)], (string?)null));

        var item = (await new GetNotificationDeliveriesQueryHandler(_repository, _time).Handle(new GetNotificationDeliveriesQuery(), TestContext.Current.CancellationToken)).Items.Single();

        item.Recipient.Should().Be(new AdminRecipientDto(RecipientKind.SupportMailbox, null, null, null));
    }

    public static TheoryData<DeliveryRetryRefusal> Refusals() => new() { DeliveryRetryRefusal.NotFailed, DeliveryRetryRefusal.NotificationDeleted, DeliveryRetryRefusal.NotificationExpired, DeliveryRetryRefusal.RecipientDeleted };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ADeliveryThatCantBeRetried_SaysWhy(DeliveryRetryRefusal reason)
    {
        var row = reason switch
        {
            DeliveryRetryRefusal.NotFailed => Row(status: DeliveryStatus.Sent),
            DeliveryRetryRefusal.NotificationDeleted => Row(notificationDeleted: true),
            DeliveryRetryRefusal.NotificationExpired => Row(notificationExpires: Now.UtcDateTime.AddMinutes(-1)),
            _ => Row(recipientDeleted: true),
        };
        _repository.ListDeliveriesAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AdminDeliveryRow>)[row], (string?)null));

        var item = (await new GetNotificationDeliveriesQueryHandler(_repository, _time).Handle(new GetNotificationDeliveriesQuery(), TestContext.Current.CancellationToken)).Items.Single();

        item.Retryable.Should().BeFalse();
        item.NotRetryableReason.Should().Be(reason);
    }

    [Theory]
    [InlineData(NotificationCategory.Security)]
    [InlineData(NotificationCategory.Account)]
    public async Task TheDetailOfASecurityOrAccountDelivery_ShowsNoTitle(NotificationCategory category)
    {
        var row = Row(category: category);
        _repository.GetDeliveryAsync(row.DeliveryId, Arg.Any<CancellationToken>()).Returns(row);

        var detail = await new GetNotificationDeliveryQueryHandler(_repository, _time).Handle(new GetNotificationDeliveryQuery(row.DeliveryId), TestContext.Current.CancellationToken);

        detail.Notification.Title.Should().BeNull();
    }

    [Fact]
    public async Task TheDetailOfAnOrdinaryDelivery_ShowsItsTitle()
    {
        var row = Row();
        _repository.GetDeliveryAsync(row.DeliveryId, Arg.Any<CancellationToken>()).Returns(row);

        var detail = await new GetNotificationDeliveryQueryHandler(_repository, _time).Handle(new GetNotificationDeliveryQuery(row.DeliveryId), TestContext.Current.CancellationToken);

        detail.Notification.Title.Should().Be("Workflow failed");
    }

    [Fact]
    public async Task ADeliveryThatDoesNotExist_IsNotFound()
    {
        _repository.GetDeliveryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((AdminDeliveryRow?)null);

        var act = () => new GetNotificationDeliveryQueryHandler(_repository, _time).Handle(new GetNotificationDeliveryQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void TheDeliveriesLimit_IsOneToTwoHundred(int limit, bool valid) =>
        new GetNotificationDeliveriesQueryValidator().Validate(new GetNotificationDeliveriesQuery(Limit: limit)).IsValid.Should().Be(valid);

    // ---- channels ----

    [Fact]
    public async Task Channels_ReportHealthFromTheCachedChecks_AndTheSendLimit_AndNothingElse()
    {
        var registry = Substitute.For<INotificationChannelRegistry>();
        registry.AvailableChannels.Returns(new HashSet<NotificationChannel> { NotificationChannel.InApp });
        var health = Substitute.For<INotificationChannelHealthReader>();
        health.GetAsync(NotificationChannel.Email, Arg.Any<CancellationToken>()).Returns(new NotificationChannelHealth("Degraded", Now.UtcDateTime, "The mail server probe failed (TimeoutException)."));
        health.GetAsync(NotificationChannel.InApp, Arg.Any<CancellationToken>()).Returns(new NotificationChannelHealth("Healthy", Now.UtcDateTime, null));
        var options = Microsoft.Extensions.Options.Options.Create(new NotificationsOptions { Email = new NotificationEmailOptions { MaxPerMinute = 45 } });

        var channels = await new GetNotificationChannelsQueryHandler(registry, health, options).Handle(new GetNotificationChannelsQuery(), TestContext.Current.CancellationToken);

        channels.Should().HaveCount(2);
        var email = channels.Single(c => c.Channel == NotificationChannel.Email);
        email.Should().Be(new NotificationChannelDto(NotificationChannel.Email, Enabled: false, "SMTP", "Degraded", Now.UtcDateTime, "The mail server probe failed (TimeoutException).", 45));
        channels.Single(c => c.Channel == NotificationChannel.InApp).Should().Be(
            new NotificationChannelDto(NotificationChannel.InApp, Enabled: true, "SignalR", "Healthy", Now.UtcDateTime, null, null));
    }

    // ---- announcements ----

    [Fact]
    public async Task Announcements_ShowTheirFanOutStatus_AndWhoPublishedThem()
    {
        var published = Now.UtcDateTime;
        AdminAnnouncementRow Row(bool done, string? name) => new(
            Guid.NewGuid(), AnnouncementKind.Maintenance, "Maintenance", AnnouncementAudience.Roles, ["role-1"], [new AdminRoleName("role-1", "Engineers")], true, null, published, "admin-1", name, done ? 12 : null, done, 1, 10, 1);
        _repository.ListAnnouncementsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<AdminAnnouncementRow>)[Row(done: true, "Ada Lovelace"), Row(done: false, null)], (string?)"c2"));

        var page = await new GetSystemAnnouncementsQueryHandler(_repository).Handle(new GetSystemAnnouncementsQuery(), TestContext.Current.CancellationToken);

        page.NextCursor.Should().Be("c2");
        page.Items[0].FanOutStatus.Should().Be("Completed");
        page.Items[0].PublishedBy.Should().Be("Ada Lovelace");
        page.Items[0].TargetRoles.Should().ContainSingle().Which.Should().Be(new AdminAnnouncementRoleDto("role-1", "Engineers"));
        page.Items[1].FanOutStatus.Should().Be("InProgress");
        page.Items[1].PublishedBy.Should().Be("admin-1", "an account with no name falls back to its id");
    }
}
