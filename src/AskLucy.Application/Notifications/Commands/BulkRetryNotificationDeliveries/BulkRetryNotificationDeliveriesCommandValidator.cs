using AskLucy.Domain.Notifications;
using FluentValidation;

namespace AskLucy.Application.Notifications.Commands.BulkRetryNotificationDeliveries;

public sealed class BulkRetryNotificationDeliveriesCommandValidator : AbstractValidator<BulkRetryNotificationDeliveriesCommand>
{
    public BulkRetryNotificationDeliveriesCommandValidator()
    {
        RuleFor(c => c).Must(c => (c.DeliveryIds is not null) ^ (c.Filter is not null))
            .WithName("deliveryIds").WithMessage("Send either 'deliveryIds' or 'filter', not both and not neither.");

        When(c => c.DeliveryIds is not null, () =>
        {
            RuleFor(c => c.DeliveryIds!).Must(ids => ids.Count is >= 1 and <= BulkRetryNotificationDeliveriesCommand.MaxIds)
                .WithName("deliveryIds").WithMessage($"Send between 1 and {BulkRetryNotificationDeliveriesCommand.MaxIds} delivery ids.");
            RuleForEach(c => c.DeliveryIds!).NotEmpty().WithName("deliveryIds");
        });

        When(c => c.Filter is not null, () =>
        {
            RuleForEach(c => c.Filter!.Statuses).Must(s => s is DeliveryStatus.Failed or DeliveryStatus.DeadLettered)
                .WithName("filter.status").WithMessage("Only failed and dead-lettered deliveries can be retried.");
            RuleFor(c => c.Filter!).Must(f => f.FromUtc is null || f.ToUtc is null || f.FromUtc <= f.ToUtc)
                .WithName("filter.from").WithMessage("'from' must not be after 'to'.");
        });
    }
}
