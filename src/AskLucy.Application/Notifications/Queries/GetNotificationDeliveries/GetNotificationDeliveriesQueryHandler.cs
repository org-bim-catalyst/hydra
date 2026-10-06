using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationDeliveries;

public sealed class GetNotificationDeliveriesQueryHandler(INotificationAdminRepository repository, TimeProvider timeProvider)
    : IRequestHandler<GetNotificationDeliveriesQuery, AdminPage<AdminDeliveryDto>>
{
    public async Task<AdminPage<AdminDeliveryDto>> Handle(GetNotificationDeliveriesQuery request, CancellationToken cancellationToken)
    {
        var (rows, next) = await repository.ListDeliveriesAsync(request.ToFilter(), request.Cursor, request.Limit, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new AdminPage<AdminDeliveryDto>([.. rows.Select(r => AdminDeliveryMapper.ToDto(r, now))], next);
    }
}
