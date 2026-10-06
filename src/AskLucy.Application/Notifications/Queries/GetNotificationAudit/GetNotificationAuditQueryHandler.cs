using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationAudit;

public sealed class GetNotificationAuditQueryHandler(INotificationAdminRepository repository)
    : IRequestHandler<GetNotificationAuditQuery, AdminPage<AdminAuditEntryDto>>
{
    public async Task<AdminPage<AdminAuditEntryDto>> Handle(GetNotificationAuditQuery request, CancellationToken cancellationToken)
    {
        var filter = new AdminAuditFilter(request.Action, request.TargetType, request.TargetId, request.ActorUserId, request.FromUtc, request.ToUtc);
        var (rows, next) = await repository.ListAuditAsync(filter, request.Cursor, request.Limit, cancellationToken);
        return new AdminPage<AdminAuditEntryDto>(
            [.. rows.Select(r => new AdminAuditEntryDto(
                r.Id,
                r.OccurredAtUtc,
                r.ActorUserId is null ? null : new AdminAuditActorDto(r.ActorUserId, r.ActorDisplayName),
                r.Action,
                r.TargetType,
                r.TargetId,
                r.Outcome,
                r.DetailsJson,
                r.CorrelationId))],
            next);
    }
}
