using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetSystemAnnouncements;

public sealed class GetSystemAnnouncementsQueryHandler(INotificationAdminRepository repository)
    : IRequestHandler<GetSystemAnnouncementsQuery, AdminPage<AdminAnnouncementDto>>
{
    public async Task<AdminPage<AdminAnnouncementDto>> Handle(GetSystemAnnouncementsQuery request, CancellationToken cancellationToken)
    {
        var (rows, next) = await repository.ListAnnouncementsAsync(request.Cursor, request.Limit, cancellationToken);
        return new AdminPage<AdminAnnouncementDto>(
            [.. rows.Select(r => new AdminAnnouncementDto(
                r.Id,
                r.Kind,
                r.Title,
                r.Audience,
                [.. r.TargetRoles.Select(role => new AdminAnnouncementRoleDto(role.Id, role.Name))],
                r.IsCritical,
                r.EndsAtUtc,
                r.PublishedAtUtc,
                r.PublishedByDisplayName ?? r.PublishedByUserId,
                r.RecipientCount,
                r.FanOutCompleted ? "Completed" : "InProgress",
                r.EmailQueued,
                r.EmailSent,
                r.EmailExpired))],
            next);
    }
}
