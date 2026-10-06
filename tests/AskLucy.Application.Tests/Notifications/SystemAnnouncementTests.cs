using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Commands.PublishSystemAnnouncement;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>
/// T154 (specs/067 US6, FR-004a) — publishing an announcement: validation, the one event it publishes, its audit row and its estimates.
/// The fan-out itself (batches of 500, resuming from the cursor, email only when critical) is covered in <see cref="OutboxDispatchServiceTests"/>.
/// </summary>
public sealed class SystemAnnouncementTests
{
    private const string AdminId = "admin-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly ISystemAnnouncementRepository _announcements = Substitute.For<ISystemAnnouncementRepository>();
    private readonly INotificationRecipientDirectory _directory = Substitute.For<INotificationRecipientDirectory>();
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly INotificationAuditWriter _audit = Substitute.For<INotificationAuditWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _time = new(Now);

    public SystemAnnouncementTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _directory.CountActiveAsync(Arg.Any<IReadOnlyCollection<string>?>(), false, Arg.Any<CancellationToken>()).Returns(1234);
        _directory.CountActiveAsync(Arg.Any<IReadOnlyCollection<string>?>(), true, Arg.Any<CancellationToken>()).Returns(1200);
        _announcements.GetExistingRoleIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<string>>().Where(id => id.StartsWith("role", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
    }

    private PublishSystemAnnouncementCommandHandler Handler(int maxPerMinute = 60) => new(
        _announcements, _directory, _publisher, _audit, _unitOfWork, _currentUser,
        Microsoft.Extensions.Options.Options.Create(new NotificationsOptions { Email = new NotificationEmailOptions { MaxPerMinute = maxPerMinute } }), _time);

    private PublishSystemAnnouncementCommandValidator Validator() => new(_time);

    private static PublishSystemAnnouncementCommand Command(
        string title = "Scheduled maintenance",
        string message = "Ask Lucy will be unavailable 22:00-23:00 UTC.",
        AnnouncementAudience audience = AnnouncementAudience.AllActiveUsers,
        IReadOnlyList<string>? roles = null,
        bool critical = false,
        DateTime? endsAtUtc = null) =>
        new(AnnouncementKind.Maintenance, title, message, audience, roles, critical, endsAtUtc);

    // ---- validation ----

    [Fact]
    public void ACompleteAnnouncement_IsValid() =>
        Validator().Validate(Command(endsAtUtc: Now.UtcDateTime.AddDays(1))).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ATitleIsRequired(string title) => Validator().Validate(Command(title: title)).IsValid.Should().BeFalse();

    [Fact]
    public void TheTitleMayBe150Characters_NotMore()
    {
        Validator().Validate(Command(title: new string('a', 150))).IsValid.Should().BeTrue();
        Validator().Validate(Command(title: new string('a', 151))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void TheMessageMayBe2000Characters_NotMore()
    {
        Validator().Validate(Command(message: new string('a', 2000))).IsValid.Should().BeTrue();
        Validator().Validate(Command(message: new string('a', 2001))).IsValid.Should().BeFalse();
        Validator().Validate(Command(message: "")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Visit https://example.com now")]
    [InlineData("Go to www.example.com")]
    [InlineData("<b>Bold</b> news")]
    [InlineData("<script>alert(1)</script>")]
    public void LinksAndHtmlAreRefused_InTheTitleAndTheMessage(string text)
    {
        Validator().Validate(Command(title: text)).IsValid.Should().BeFalse();
        Validator().Validate(Command(message: text)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ARoleTargetedAnnouncement_NeedsAtLeastOneRole()
    {
        Validator().Validate(Command(audience: AnnouncementAudience.Roles, roles: null)).IsValid.Should().BeFalse();
        Validator().Validate(Command(audience: AnnouncementAudience.Roles, roles: [])).IsValid.Should().BeFalse();
        Validator().Validate(Command(audience: AnnouncementAudience.Roles, roles: ["role-1"])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AnAnnouncementToEveryone_CantAlsoNameRoles() =>
        Validator().Validate(Command(audience: AnnouncementAudience.AllActiveUsers, roles: ["role-1"])).IsValid.Should().BeFalse();

    [Fact]
    public void TheEndTimeMustBeInTheFuture()
    {
        Validator().Validate(Command(endsAtUtc: Now.UtcDateTime.AddMinutes(1))).IsValid.Should().BeTrue();
        Validator().Validate(Command(endsAtUtc: Now.UtcDateTime.AddMinutes(-1))).IsValid.Should().BeFalse();
        Validator().Validate(Command(endsAtUtc: Now.UtcDateTime)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void AnUnknownKindOrAudience_IsRefused()
    {
        Validator().Validate(Command() with { Kind = (AnnouncementKind)99 }).IsValid.Should().BeFalse();
        Validator().Validate(Command() with { Audience = (AnnouncementAudience)99 }).IsValid.Should().BeFalse();
    }

    // ---- publishing ----

    [Fact]
    public async Task Publishing_SavesTheAnnouncementAndOneEvent_InOneSave_AndAuditsIt()
    {
        SystemAnnouncement? saved = null;
        _announcements.When(a => a.Add(Arg.Any<SystemAnnouncement>())).Do(call => saved = call.Arg<SystemAnnouncement>());

        var result = await Handler().Handle(Command(critical: true, endsAtUtc: Now.UtcDateTime.AddDays(1)), TestContext.Current.CancellationToken);

        saved.Should().NotBeNull();
        result.Id.Should().Be(saved!.Id);
        saved.PublishedByUserId.Should().Be(AdminId);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.SystemAnnouncementPublished &&
            r.Recipient is NotificationRecipient.Audience && ((NotificationRecipient.Audience)r.Recipient).AllActiveUsers &&
            r.EventKey == AnnouncementKeys.EventKey(saved.Id) &&
            r.RelatedItem == new RelatedItem(AnnouncementKeys.RelatedItemType, saved.Id.ToString(), null) &&
            r.Variables["announcementTitle"] == "Scheduled maintenance" &&
            r.Variables["isCritical"] == "true" &&
            r.Variables["endsAtUtc"] != null));
        _audit.Received(1).Write(
            NotificationAuditAction.AnnouncementPublished, nameof(SystemAnnouncement), saved.Id.ToString(), NotificationAuditOutcome.Succeeded,
            Arg.Any<object?>(), Arg.Any<string?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARoleTargetedAnnouncement_CarriesItsRolesInTheAudience()
    {
        await Handler().Handle(Command(audience: AnnouncementAudience.Roles, roles: ["role-a", "role-b"]), TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            ((NotificationRecipient.Audience)r!.Recipient).AllActiveUsers == false &&
            ((NotificationRecipient.Audience)r.Recipient).RoleIds.SequenceEqual(new[] { "role-a", "role-b" })));
    }

    [Fact]
    public async Task ARoleThatDoesNotExist_IsRefused_AndNothingIsPublishedOrSaved()
    {
        var act = () => Handler().Handle(Command(audience: AnnouncementAudience.Roles, roles: ["role-a", "ghost"]), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ValidationException>();
        _publisher.DidNotReceiveWithAnyArgs().Publish(default!);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACriticalAnnouncement_EstimatesTheEmailMinutes_FromTheSendLimit()
    {
        // 1,200 verified addresses at 60 a minute is 20 minutes.
        var result = await Handler(maxPerMinute: 60).Handle(Command(critical: true), TestContext.Current.CancellationToken);

        result.EstimatedRecipients.Should().Be(1234);
        result.EmailEstimatedMinutes.Should().Be(20);
    }

    [Fact]
    public async Task AnAnnouncementThatIsNotCritical_SendsNoEmail_SoItEstimatesNoMinutes()
    {
        var result = await Handler().Handle(Command(critical: false), TestContext.Current.CancellationToken);

        result.EmailEstimatedMinutes.Should().Be(0);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_CantPublish()
    {
        _currentUser.UserId.Returns((string?)null);

        var act = () => Handler().Handle(Command(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
