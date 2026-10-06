using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationDelivery;

public sealed class GetNotificationDeliveryQueryHandler(INotificationAdminRepository repository, TimeProvider timeProvider)
    : IRequestHandler<GetNotificationDeliveryQuery, AdminDeliveryDetailDto>
{
    public async Task<AdminDeliveryDetailDto> Handle(GetNotificationDeliveryQuery request, CancellationToken cancellationToken)
    {
        var row = await repository.GetDeliveryAsync(request.DeliveryId, cancellationToken)
            ?? throw new KeyNotFoundException("Delivery not found.");
        return AdminDeliveryMapper.ToDetailDto(row, timeProvider.GetUtcNow().UtcDateTime);
    }
}
