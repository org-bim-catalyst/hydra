using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.PublishSystemAnnouncement;

/// <summary>
/// contracts/admin-notifications-api.md POST /notifications/announcements (M). Announcements are immutable once published:
/// there is no edit and no delete, because this is never a marketing channel (FR-004a).
/// </summary>
public sealed record PublishSystemAnnouncementCommand(
    AnnouncementKind Kind,
    string Title,
    string Message,
    AnnouncementAudience Audience,
    IReadOnlyList<string>? TargetRoleIds,
    bool IsCritical,
    DateTime? EndsAtUtc) : IRequest<PublishedAnnouncementDto>;
