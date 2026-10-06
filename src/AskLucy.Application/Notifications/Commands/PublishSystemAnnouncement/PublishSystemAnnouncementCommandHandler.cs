using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Options;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Notifications.Commands.PublishSystemAnnouncement;

/// <summary>
/// Creates the announcement and publishes one event for it, in one save. The dispatcher fans the event out to its
/// audience in batches, so a large audience never makes this request wait (research R22).
/// </summary>
public sealed class PublishSystemAnnouncementCommandHandler(
    ISystemAnnouncementRepository announcements,
    INotificationRecipientDirectory directory,
    INotificationPublisher publisher,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    IOptions<NotificationsOptions> options,
    TimeProvider timeProvider) : IRequestHandler<PublishSystemAnnouncementCommand, PublishedAnnouncementDto>
{
    public async Task<PublishedAnnouncementDto> Handle(PublishSystemAnnouncementCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var roleIds = request.Audience == AnnouncementAudience.Roles ? (request.TargetRoleIds ?? []) : [];
        if (roleIds.Count > 0)
        {
            var existing = await announcements.GetExistingRoleIdsAsync(roleIds, cancellationToken);
            if (roleIds.FirstOrDefault(id => !existing.Contains(id)) is { } unknown)
            {
                throw new ValidationException([new ValidationFailure("targetRoleIds", $"Role '{unknown}' doesn't exist.")]);
            }
        }

        var announcement = SystemAnnouncement.Publish(
            request.Kind, request.Title, request.Message, request.Audience, roleIds, request.IsCritical, request.EndsAtUtc, adminId, now);
        announcements.Add(announcement);

        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.SystemAnnouncementPublished,
            new NotificationRecipient.Audience(request.Audience == AnnouncementAudience.AllActiveUsers, announcement.TargetRoleIds),
            new Dictionary<string, string?>
            {
                ["announcementTitle"] = announcement.Title,
                ["announcementMessage"] = announcement.Message,
                ["announcementKind"] = announcement.Kind.ToString(),
                ["endsAt"] = announcement.EndsAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                ["endsAtUtc"] = announcement.EndsAtUtc?.ToString("O", CultureInfo.InvariantCulture),
                ["isCritical"] = announcement.IsCritical ? "true" : "false",
            },
            new RelatedItem(AnnouncementKeys.RelatedItemType, announcement.Id.ToString()),
            EventKey: AnnouncementKeys.EventKey(announcement.Id)));

        audit.Write(
            NotificationAuditAction.AnnouncementPublished,
            nameof(SystemAnnouncement),
            announcement.Id.ToString(),
            NotificationAuditOutcome.Succeeded,
            new
            {
                kind = announcement.Kind.ToString(),
                audience = announcement.Audience.ToString(),
                roleIds = announcement.TargetRoleIds,
                isCritical = announcement.IsCritical,
                endsAtUtc = announcement.EndsAtUtc,
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var scope = announcement.Audience == AnnouncementAudience.Roles ? announcement.TargetRoleIds : null;
        var estimated = await directory.CountActiveAsync(scope, verifiedEmailOnly: false, cancellationToken);
        var emailMinutes = 0;
        if (announcement.IsCritical)
        {
            var emailRecipients = await directory.CountActiveAsync(scope, verifiedEmailOnly: true, cancellationToken);
            emailMinutes = (int)Math.Ceiling((double)emailRecipients / Math.Max(1, options.Value.Email.MaxPerMinute));
        }

        return new PublishedAnnouncementDto(announcement.Id, estimated, emailMinutes);
    }
}
